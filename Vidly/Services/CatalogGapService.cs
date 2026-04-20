using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    public class CatalogGapService
    {
        private readonly IMovieRepository _movies;
        private readonly IRentalRepository _rentals;

        public CatalogGapService(IMovieRepository movies, IRentalRepository rentals)
        {
            _movies = movies;
            _rentals = rentals;
        }

        public CatalogGapDashboard GetDashboard()
        {
            var movies = _movies.GetAll();
            var rentals = _rentals.GetAll();

            var totalMovies = movies.Count;
            var totalRentals = rentals.Count;

            // Per-genre stats
            var genreGroups = Enum.GetValues(typeof(Genre)).Cast<Genre>()
                .Select(g =>
                {
                    var genreMovies = movies.Where(m => m.Genre == g).ToList();
                    var genreRentals = rentals.Where(r => genreMovies.Any(m => m.Id == r.MovieId)).ToList();
                    return new
                    {
                        Genre = g,
                        MovieCount = genreMovies.Count,
                        RentalCount = genreRentals.Count
                    };
                }).ToList();

            var genreGaps = genreGroups.Select(g =>
            {
                var catalogShare = totalMovies > 0 ? (double)g.MovieCount / totalMovies * 100 : 0;
                var demandShare = totalRentals > 0 ? (double)g.RentalCount / totalRentals * 100 : 0;
                var gapScore = demandShare - catalogShare;
                string verdict;
                if (gapScore > 5) verdict = "Underserved";
                else if (gapScore < -5) verdict = "Oversaturated";
                else verdict = "Balanced";

                return new GenreGap
                {
                    Genre = g.Genre,
                    MovieCount = g.MovieCount,
                    RentalCount = g.RentalCount,
                    DemandRatio = g.MovieCount > 0 ? (double)g.RentalCount / g.MovieCount : 0,
                    MarketSharePct = Math.Round(demandShare, 1),
                    CatalogSharePct = Math.Round(catalogShare, 1),
                    GapScore = Math.Round(gapScore, 1),
                    Verdict = verdict
                };
            }).OrderByDescending(g => g.GapScore).ToList();

            // Recommendations
            var recommendations = genreGaps
                .Where(g => g.GapScore > 0)
                .Select(g =>
                {
                    string priority;
                    if (g.GapScore > 15) priority = "High";
                    else if (g.GapScore > 5) priority = "Medium";
                    else priority = "Low";

                    var suggested = Math.Max(1, (int)Math.Ceiling(g.GapScore / 5.0));

                    return new AcquisitionRecommendation
                    {
                        Genre = g.Genre,
                        Reason = $"{g.Genre} has {g.MarketSharePct}% demand but only {g.CatalogSharePct}% catalog coverage",
                        SuggestedCount = suggested,
                        Priority = priority,
                        ExpectedImpact = Math.Round(g.GapScore * 0.8, 1),
                        MinRating = g.DemandRatio > 2 ? 4 : 3
                    };
                })
                .OrderByDescending(r => r.Priority == "High" ? 3 : r.Priority == "Medium" ? 2 : 1)
                .ToList();

            // Demand signals
            var signals = new List<DemandSignal>();

            // Genre gaps
            foreach (var gap in genreGaps.Where(g => g.Verdict == "Underserved"))
            {
                signals.Add(new DemandSignal
                {
                    Pattern = $"{gap.Genre} demand exceeds supply by {gap.GapScore}%",
                    Category = "Genre Gap",
                    Confidence = Math.Min(1.0, gap.GapScore / 30.0),
                    ActionItem = $"Add {Math.Max(1, (int)(gap.GapScore / 5))} more {gap.Genre} titles"
                });
            }

            // Rating gaps
            var highRatedRentals = rentals.Count(r =>
            {
                var m = movies.FirstOrDefault(mov => mov.Id == r.MovieId);
                return m != null && m.Rating >= 4;
            });
            var highRatedPct = totalRentals > 0 ? (double)highRatedRentals / totalRentals * 100 : 0;
            var highRatedMoviesPct = totalMovies > 0
                ? (double)movies.Count(m => m.Rating >= 4) / totalMovies * 100 : 0;
            if (highRatedPct > highRatedMoviesPct + 10)
            {
                signals.Add(new DemandSignal
                {
                    Pattern = $"High-rated movies (4-5★) account for {highRatedPct:F0}% of rentals but only {highRatedMoviesPct:F0}% of catalog",
                    Category = "Rating Gap",
                    Confidence = 0.85,
                    ActionItem = "Prioritize acquiring movies with 4+ star ratings"
                });
            }

            // Freshness gap
            var recentMovies = movies.Count(m => m.ReleaseDate.HasValue &&
                (DateTime.Today - m.ReleaseDate.Value).TotalDays <= 1825);
            var freshnessPct = totalMovies > 0 ? (double)recentMovies / totalMovies * 100 : 0;
            if (freshnessPct < 30)
            {
                signals.Add(new DemandSignal
                {
                    Pattern = $"Only {freshnessPct:F0}% of catalog released in last 5 years",
                    Category = "Freshness Gap",
                    Confidence = 0.7,
                    ActionItem = "Acquire newer releases to refresh catalog"
                });
            }

            // Stale movies
            var rentedMovieIds = new HashSet<int>(rentals.Select(r => r.MovieId));
            var staleMovies = movies.Count(m => !rentedMovieIds.Contains(m.Id));

            // Health summary
            var avgRating = movies.Where(m => m.Rating.HasValue).Select(m => m.Rating.Value)
                .DefaultIfEmpty(0).Average();

            // Shannon entropy for diversity
            var genreCounts = genreGroups.Where(g => g.MovieCount > 0)
                .Select(g => (double)g.MovieCount / totalMovies).ToList();
            var entropy = genreCounts.Count > 0
                ? -genreCounts.Sum(p => p * Math.Log(p, 2)) / Math.Log(genreCounts.Count, 2)
                : 0;

            var health = new CatalogHealthSummary
            {
                TotalMovies = totalMovies,
                TotalRentals = totalRentals,
                ActiveGenres = genreGroups.Count(g => g.MovieCount > 0),
                AvgRating = Math.Round(avgRating, 1),
                FreshnessScore = Math.Round(freshnessPct, 1),
                StaleMovies = staleMovies,
                DiversityIndex = Math.Round(entropy * 100, 1)
            };

            // Overall coverage score
            var gapPenalty = genreGaps.Where(g => g.GapScore > 0).Sum(g => g.GapScore);
            var coverageScore = Math.Max(0, Math.Min(100, 100 - gapPenalty));
            string grade;
            if (coverageScore >= 90) grade = "A";
            else if (coverageScore >= 80) grade = "B";
            else if (coverageScore >= 70) grade = "C";
            else if (coverageScore >= 60) grade = "D";
            else grade = "F";

            return new CatalogGapDashboard
            {
                OverallCoverageScore = Math.Round(coverageScore, 1),
                CoverageGrade = grade,
                GenreGaps = genreGaps,
                Recommendations = recommendations,
                UnmetDemand = signals,
                Health = health,
                AnalyzedAt = DateTime.Now
            };
        }
    }
}
