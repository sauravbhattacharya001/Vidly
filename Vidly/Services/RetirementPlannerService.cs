using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    public class RetirementPlannerService
    {
        private readonly IMovieRepository _movieRepo;
        private readonly IRentalRepository _rentalRepo;

        public RetirementPlannerService(
            IMovieRepository movieRepo,
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo)
        {
            _movieRepo = movieRepo;
            _rentalRepo = rentalRepo;
        }

        public RetirementPlan GeneratePlan()
        {
            var movies = _movieRepo.GetAll().ToList();
            var rentals = _rentalRepo.GetAll().ToList();
            var now = DateTime.Today;
            var cutoff90 = now.AddDays(-90);
            var cutoff180 = now.AddDays(-180);

            var rentalsByMovie = rentals.GroupBy(r => r.MovieId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var candidates = new List<MovieRetirementCandidate>();

            foreach (var movie in movies)
            {
                List<Rental> movieRentals;
                rentalsByMovie.TryGetValue(movie.Id, out movieRentals);
                if (movieRentals == null) movieRentals = new List<Rental>();

                var totalRentals = movieRentals.Count;
                var recentRentals = movieRentals.Count(r => r.RentalDate >= cutoff90);
                var priorRentals = movieRentals.Count(r => r.RentalDate >= cutoff180 && r.RentalDate < cutoff90);

                double declineRate = 0;
                if (priorRentals > 0)
                    declineRate = Math.Round((1.0 - (double)recentRentals / priorRentals) * 100, 1);
                else if (recentRentals == 0 && totalRentals > 0)
                    declineRate = 100;

                var lastRentalDate = movieRentals.Any()
                    ? movieRentals.Max(r => r.RentalDate)
                    : (movie.ReleaseDate ?? now.AddYears(-5));
                var daysSinceLast = (int)(now - lastRentalDate).TotalDays;

                var revenueLifetime = movieRentals.Sum(r => (double)r.TotalCost);
                var revenueRecent = movieRentals.Where(r => r.RentalDate >= cutoff90).Sum(r => (double)r.TotalCost);

                // Compute retirement score (0-100)
                double ageScore = 0;
                if (movie.ReleaseDate.HasValue)
                {
                    var ageYears = (now - movie.ReleaseDate.Value).TotalDays / 365.25;
                    ageScore = Math.Min(25, ageYears * 2);
                }

                var declineScore = Math.Min(25, declineRate * 0.25);
                var dormancyScore = Math.Min(25, daysSinceLast * 0.1);
                var lowDemandScore = recentRentals == 0 ? 25 : Math.Max(0, 25 - recentRentals * 5);

                var score = Math.Round(ageScore + declineScore + dormancyScore + lowDemandScore, 1);
                score = Math.Min(100, Math.Max(0, score));

                string urgency, action;
                if (score >= 80) { urgency = "Immediate"; action = "Archive"; }
                else if (score >= 60) { urgency = "Soon"; action = "Discount"; }
                else if (score >= 40) { urgency = "Monitor"; action = "Bundle"; }
                else { urgency = "Healthy"; action = "Keep"; }

                var reasons = new List<string>();
                if (daysSinceLast > 365) reasons.Add(string.Format("No rentals in {0} days", daysSinceLast));
                else if (daysSinceLast > 180) reasons.Add(string.Format("Last rented {0} days ago", daysSinceLast));
                if (declineRate >= 75) reasons.Add(string.Format("{0}% decline from previous quarter", declineRate));
                if (movie.ReleaseDate.HasValue)
                {
                    var yrs = (int)((now - movie.ReleaseDate.Value).TotalDays / 365.25);
                    if (yrs >= 10) reasons.Add(string.Format("Released {0} years ago", yrs));
                }
                if (recentRentals == 0 && totalRentals > 0) reasons.Add("Zero recent demand despite history");
                if (reasons.Count == 0) reasons.Add("Healthy rental activity");

                candidates.Add(new MovieRetirementCandidate
                {
                    MovieId = movie.Id,
                    MovieName = movie.Name,
                    Genre = movie.Genre.HasValue ? movie.Genre.Value.ToString() : "Unknown",
                    ReleaseDate = movie.ReleaseDate,
                    TotalRentals = totalRentals,
                    RecentRentals = recentRentals,
                    DeclineRate = declineRate,
                    DaysSinceLastRental = daysSinceLast,
                    RetirementScore = score,
                    RetirementUrgency = urgency,
                    RecommendedAction = action,
                    Reasons = reasons,
                    RevenueLifetime = Math.Round(revenueLifetime, 2),
                    RevenueRecent = Math.Round(revenueRecent, 2)
                });
            }

            candidates = candidates.OrderByDescending(c => c.RetirementScore).ToList();

            var immediate = candidates.Count(c => c.RetirementUrgency == "Immediate");
            var soon = candidates.Count(c => c.RetirementUrgency == "Soon");
            var monitor = candidates.Count(c => c.RetirementUrgency == "Monitor");
            var healthy = candidates.Count(c => c.RetirementUrgency == "Healthy");

            var genreGroups = candidates.GroupBy(c => c.Genre).Select(g =>
            {
                var retCount = g.Count(c => c.RetirementUrgency == "Immediate" || c.RetirementUrgency == "Soon");
                var avgAge = g.Where(c => c.ReleaseDate.HasValue)
                    .Select(c => (now - c.ReleaseDate.Value).TotalDays / 365.25)
                    .DefaultIfEmpty(0).Average();
                var ratio = g.Count > 0 ? (double)retCount / g.Count() : 0;
                string health;
                if (ratio >= 0.6) health = "Critical";
                else if (ratio >= 0.4) health = "Aging";
                else if (ratio >= 0.2) health = "Stable";
                else health = "Thriving";

                return new GenreRetirementBreakdown
                {
                    Genre = g.Key,
                    Total = g.Count(),
                    RetirementCandidates = retCount,
                    AverageAge = Math.Round(avgAge, 1),
                    Health = health
                };
            }).OrderByDescending(g => g.RetirementCandidates).ToList();

            var replacements = genreGroups.Where(g => g.RetirementCandidates >= 2).Select(g =>
                new ReplacementSuggestion
                {
                    Genre = g.Genre,
                    RetiringCount = g.RetirementCandidates,
                    SuggestedNewTitles = Math.Max(1, g.RetirementCandidates / 2),
                    Rationale = string.Format("{0} has {1} retiring titles — acquire fresh content to maintain genre coverage",
                        g.Genre, g.RetirementCandidates)
                }).ToList();

            var insights = new List<string>();
            if (immediate > 0)
                insights.Add(string.Format("{0} movie(s) need immediate retirement — freeing shelf space for new titles", immediate));
            var agingGenres = genreGroups.Where(g => g.Health == "Critical" || g.Health == "Aging").ToList();
            if (agingGenres.Any())
                insights.Add(string.Format("{0} genre(s) aging rapidly: {1} — consider refresh campaigns",
                    agingGenres.Count, string.Join(", ", agingGenres.Select(g => g.Genre))));
            var dormant = candidates.Count(c => c.DaysSinceLastRental > 365);
            if (dormant > 0)
                insights.Add(string.Format("{0} movie(s) haven't been rented in over a year", dormant));
            var revenueAtRisk = candidates.Where(c => c.RetirementUrgency == "Immediate" || c.RetirementUrgency == "Soon")
                .Sum(c => c.RevenueRecent);
            if (revenueAtRisk > 0)
                insights.Add(string.Format("${0:F2} recent revenue at risk from retiring titles — consider discount campaigns first", revenueAtRisk));
            if (insights.Count == 0)
                insights.Add("Catalog is healthy — no urgent retirements needed");

            var shelfSavings = movies.Count > 0 ? Math.Round((double)(immediate + soon) / movies.Count * 100, 1) : 0;

            return new RetirementPlan
            {
                GeneratedAt = DateTime.UtcNow,
                Candidates = candidates,
                Summary = new RetirementSummary
                {
                    TotalMoviesAnalyzed = movies.Count,
                    ImmediateRetirements = immediate,
                    SoonRetirements = soon,
                    MonitorList = monitor,
                    HealthyCount = healthy,
                    EstimatedShelfSpaceSavings = shelfSavings,
                    RevenueAtRisk = Math.Round(revenueAtRisk, 2),
                    GenreBreakdown = genreGroups,
                    ReplacementSuggestions = replacements
                },
                ProactiveInsights = insights
            };
        }

        public List<MovieRetirementCandidate> GetCandidatesByUrgency(string urgency)
        {
            return GeneratePlan().Candidates
                .Where(c => c.RetirementUrgency.Equals(urgency, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public List<GenreRetirementBreakdown> GetGenreHealth()
        {
            return GeneratePlan().Summary.GenreBreakdown;
        }
    }
}
