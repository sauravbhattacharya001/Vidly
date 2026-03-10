using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

// Data models for MovieInsightsService live in Models/MovieInsightModels.cs

namespace Vidly.Services
{
    /// <summary>
    /// Per-movie deep analytics: rental trends, customer demographics,
    /// revenue breakdown, and performance scoring.
    /// </summary>
    public class MovieInsightsService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly ICustomerRepository _customerRepository;

        public MovieInsightsService(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        /// <summary>
        /// Get comprehensive insights for a single movie.
        /// </summary>
        public MovieInsight GetInsight(int movieId)
        {
            var movie = _movieRepository.GetById(movieId);
            if (movie == null) return null;

            var ctx = LoadSharedContext();
            List<Rental> movieRentals;
            ctx.RentalsByMovie.TryGetValue(movieId, out movieRentals);
            if (movieRentals == null) movieRentals = new List<Rental>();

            return BuildInsightFromContext(movie, movieRentals, ctx);
        }

        /// <summary>
        /// Get insights for all movies, sorted by performance score descending.
        /// Pre-computes global max rental count and max revenue in one pass so
        /// that per-movie performance scoring uses O(1) lookups instead of
        /// re-scanning all rentals per movie (eliminates O(M×R×2) overhead).
        /// </summary>
        public IReadOnlyList<MovieInsight> GetAllInsights()
        {
            var ctx = LoadSharedContext();
            var movies = _movieRepository.GetAll();

            var insights = new List<MovieInsight>();
            foreach (var movie in movies)
            {
                List<Rental> movieRentals;
                ctx.RentalsByMovie.TryGetValue(movie.Id, out movieRentals);
                if (movieRentals == null) movieRentals = new List<Rental>();

                insights.Add(BuildInsightFromContext(movie, movieRentals, ctx));
            }

            insights.Sort((a, b) => b.PerformanceScore.Overall.CompareTo(a.PerformanceScore.Overall));
            return insights;
        }

        /// <summary>
        /// Compare two movies side by side. Loads shared context once
        /// so both movies use consistent global maximums for scoring.
        /// </summary>
        public MovieInsightComparison Compare(int movieIdA, int movieIdB)
        {
            var movieA = _movieRepository.GetById(movieIdA);
            var movieB = _movieRepository.GetById(movieIdB);
            if (movieA == null || movieB == null) return null;

            // Single shared context: consistent global maximums, one data load
            var ctx = LoadSharedContext();

            List<Rental> rentalsA, rentalsB;
            ctx.RentalsByMovie.TryGetValue(movieIdA, out rentalsA);
            ctx.RentalsByMovie.TryGetValue(movieIdB, out rentalsB);
            if (rentalsA == null) rentalsA = new List<Rental>();
            if (rentalsB == null) rentalsB = new List<Rental>();

            var insightA = BuildInsightFromContext(movieA, rentalsA, ctx);
            var insightB = BuildInsightFromContext(movieB, rentalsB, ctx);

            return new MovieInsightComparison
            {
                MovieA = insightA,
                MovieB = insightB,
                RevenueWinner = insightA.Revenue.TotalRevenue >= insightB.Revenue.TotalRevenue
                    ? insightA.MovieName : insightB.MovieName,
                PopularityWinner = insightA.RentalSummary.TotalRentals >= insightB.RentalSummary.TotalRentals
                    ? insightA.MovieName : insightB.MovieName,
                PerformanceWinner = insightA.PerformanceScore.Overall >= insightB.PerformanceScore.Overall
                    ? insightA.MovieName : insightB.MovieName,
                OverallVerdict = GetComparisonVerdict(insightA, insightB),
            };
        }

        // ── Shared context (loaded once, reused across methods) ──

        /// <summary>
        /// Pre-loaded shared data context to avoid redundant repository calls.
        /// GetInsight, Compare, and GetAllInsights all need the same base data;
        /// this struct lets them share a single load.
        /// </summary>
        private struct InsightContext
        {
            public Dictionary<int, List<Rental>> RentalsByMovie;
            public Dictionary<int, Customer> CustomerLookup;
            public int MaxRentalCount;
            public decimal MaxRevenue;
        }

        /// <summary>
        /// Loads all rentals, customers, and pre-computes per-movie rental groups
        /// plus global maximums in a single pass.
        /// </summary>
        private InsightContext LoadSharedContext()
        {
            var allRentals = _rentalRepository.GetAll();
            var customers = _customerRepository.GetAll();

            var rentalsByMovie = new Dictionary<int, List<Rental>>();
            foreach (var r in allRentals)
            {
                if (!rentalsByMovie.TryGetValue(r.MovieId, out var _lst1))

                {

                    _lst1 = new List<Rental>();

                    rentalsByMovie[r.MovieId] = _lst1;

                }

                _lst1.Add(r);
            }

            int maxRentalCount = 0;
            decimal maxRevenue = 0;
            foreach (var kvp in rentalsByMovie)
            {
                if (kvp.Value.Count > maxRentalCount)
                    maxRentalCount = kvp.Value.Count;

                decimal movieRevenue = 0;
                foreach (var r in kvp.Value)
                    movieRevenue += r.TotalCost;
                if (movieRevenue > maxRevenue)
                    maxRevenue = movieRevenue;
            }

            return new InsightContext
            {
                RentalsByMovie = rentalsByMovie,
                CustomerLookup = customers.ToDictionary(c => c.Id, c => c),
                MaxRentalCount = maxRentalCount,
                MaxRevenue = maxRevenue,
            };
        }

        /// <summary>
        /// Builds a MovieInsight from pre-loaded context, avoiding redundant
        /// data fetching and ensuring consistent global maximums.
        /// </summary>
        private static MovieInsight BuildInsightFromContext(
            Movie movie, List<Rental> movieRentals, InsightContext ctx)
        {
            return new MovieInsight
            {
                MovieId = movie.Id,
                MovieName = movie.Name,
                Genre = movie.Genre,
                Rating = movie.Rating,
                ReleaseDate = movie.ReleaseDate,
                RentalSummary = BuildRentalSummary(movieRentals),
                Revenue = BuildRevenue(movieRentals),
                CustomerDemographics = BuildDemographics(movieRentals, ctx.CustomerLookup),
                MonthlyTrend = BuildMonthlyTrend(movieRentals),
                PerformanceScore = ComputePerformanceScore(
                    movieRentals, movie, ctx.MaxRentalCount, ctx.MaxRevenue),
            };
        }

                // ── Internal builders (static for testability) ──

        /// <summary>
        /// Builds a rental summary from a list of rentals, including counts by status,
        /// unique/repeat customer counts, average rental duration, and date range.
        /// </summary>
        internal static RentalSummary BuildRentalSummary(List<Rental> rentals)
        {
            if (rentals == null || rentals.Count == 0)
                return new RentalSummary();

            int active = 0, returned = 0, overdue = 0;
            double totalDays = 0;
            int completedCount = 0;

            foreach (var r in rentals)
            {
                if (r.Status == RentalStatus.Active) active++;
                else if (r.Status == RentalStatus.Returned) { returned++; }
                else if (r.Status == RentalStatus.Overdue) overdue++;

                if (r.ReturnDate.HasValue)
                {
                    totalDays += (r.ReturnDate.Value - r.RentalDate).TotalDays;
                    completedCount++;
                }
            }

            // Unique customers
            var uniqueCustomers = new HashSet<int>();
            foreach (var r in rentals)
                uniqueCustomers.Add(r.CustomerId);

            // Repeat renters (rented this movie more than once)
            var customerCounts = new Dictionary<int, int>();
            foreach (var r in rentals)
            {
                customerCounts.TryGetValue(r.CustomerId, out var _c1);
                customerCounts[r.CustomerId] = _c1 + 1;
            }
            int repeatRenters = 0;
            foreach (var kvp in customerCounts)
                if (kvp.Value > 1) repeatRenters++;

            return new RentalSummary
            {
                TotalRentals = rentals.Count,
                ActiveRentals = active,
                ReturnedRentals = returned,
                OverdueRentals = overdue,
                UniqueCustomers = uniqueCustomers.Count,
                RepeatRenters = repeatRenters,
                AverageRentalDays = completedCount > 0 ? totalDays / completedCount : 0,
                FirstRentalDate = rentals.Min(r => r.RentalDate),
                LastRentalDate = rentals.Max(r => r.RentalDate),
            };
        }

        /// <summary>
        /// Computes revenue breakdown: total, base (minus late fees), late fees,
        /// average per rental, and late-fee percentage.
        /// </summary>
        internal static RevenueBreakdown BuildRevenue(List<Rental> rentals)
        {
            if (rentals == null || rentals.Count == 0)
                return new RevenueBreakdown();

            decimal totalRevenue = 0, lateFees = 0, baseRevenue = 0;

            foreach (var r in rentals)
            {
                var cost = r.TotalCost;
                totalRevenue += cost;
                lateFees += r.LateFee;
                baseRevenue += cost - r.LateFee;
            }

            return new RevenueBreakdown
            {
                TotalRevenue = totalRevenue,
                BaseRevenue = baseRevenue,
                LateFeeRevenue = lateFees,
                AverageRevenuePerRental = totalRevenue / rentals.Count,
                LateFeePercentage = totalRevenue > 0
                    ? (double)(lateFees / totalRevenue) * 100 : 0,
            };
        }

        /// <summary>
        /// Builds a customer demographic breakdown showing membership tier distribution,
        /// total unique customers, and the dominant (most common) tier.
        /// </summary>
        internal static CustomerDemographicBreakdown BuildDemographics(
            List<Rental> rentals, Dictionary<int, Customer> customerLookup)
        {
            if (rentals == null || rentals.Count == 0)
                return new CustomerDemographicBreakdown();

            var tierCounts = new Dictionary<string, int>();
            var uniqueIds = new HashSet<int>();

            foreach (var r in rentals)
            {
                if (!uniqueIds.Add(r.CustomerId)) continue;
                Customer customer;
                if (!customerLookup.TryGetValue(r.CustomerId, out customer)) continue;

                var tier = customer.MembershipType.ToString();
                tierCounts.TryGetValue(tier, out var _c2);
                tierCounts[tier] = _c2 + 1;
            }

            // Find top tier
            string topTier = null;
            int topCount = 0;
            foreach (var kvp in tierCounts)
            {
                if (kvp.Value > topCount)
                {
                    topCount = kvp.Value;
                    topTier = kvp.Key;
                }
            }

            return new CustomerDemographicBreakdown
            {
                TierDistribution = tierCounts,
                TotalUniqueCustomers = uniqueIds.Count,
                DominantTier = topTier,
            };
        }

        /// <summary>
        /// Builds a monthly trend showing rental count and revenue per calendar month.
        /// Results are sorted chronologically (yyyy-MM).
        /// </summary>
        internal static List<MonthlyRentalPoint> BuildMonthlyTrend(List<Rental> rentals)
        {
            if (rentals == null || rentals.Count == 0)
                return new List<MonthlyRentalPoint>();

            var monthGroups = new Dictionary<string, MonthlyRentalPoint>();

            foreach (var r in rentals)
            {
                var key = r.RentalDate.ToString("yyyy-MM");
                MonthlyRentalPoint point;
                if (!monthGroups.TryGetValue(key, out point))
                {
                    point = new MonthlyRentalPoint
                    {
                        Month = key,
                        Year = r.RentalDate.Year,
                        MonthNumber = r.RentalDate.Month,
                    };
                    monthGroups[key] = point;
                }
                point.RentalCount++;
                point.Revenue += r.TotalCost;
            }

            var result = new List<MonthlyRentalPoint>(monthGroups.Values);
            result.Sort((a, b) => string.Compare(a.Month, b.Month, StringComparison.Ordinal));
            return result;
        }

/// <summary>
        /// Computes a multi-factor performance score (0–100) using pre-computed
        /// global maximums. Avoids re-scanning all rentals per movie — O(movieRentals)
        /// instead of O(allRentals).
        /// </summary>
        internal static PerformanceScore ComputePerformanceScore(
            List<Rental> movieRentals, Movie movie,
            int maxRentalCount, decimal maxRevenue)
        {
            // Popularity score (0-100): rental count relative to the most-rented movie
            double popularityScore = maxRentalCount > 0
                ? (double)movieRentals.Count / maxRentalCount * 100 : 0;

            // Revenue score (0-100): relative to top revenue movie
            double revenueScore = 0;
            if (maxRevenue > 0)
            {
                decimal movieRevenue = 0;
                foreach (var r in movieRentals)
                    movieRevenue += r.TotalCost;
                revenueScore = (double)(movieRevenue / maxRevenue) * 100;
            }

            // Retention score (0-100): % of customers who rented this movie more than once
            double retentionScore = 0;
            if (movieRentals.Count > 0)
            {
                var customerCounts = new Dictionary<int, int>();
                foreach (var r in movieRentals)
                {
                    customerCounts.TryGetValue(r.CustomerId, out var _c3);
                    customerCounts[r.CustomerId] = _c3 + 1;
                }
                int repeaters = 0;
                foreach (var count in customerCounts.Values)
                    if (count > 1) repeaters++;

                retentionScore = customerCounts.Count > 0
                    ? (double)repeaters / customerCounts.Count * 100 : 0;
            }

            // Rating score (0-100): movie.Rating scaled to 100 (1=20, 2=40, ..., 5=100)
            double ratingScore = movie.Rating.HasValue ? movie.Rating.Value * 20.0 : 50.0;

            // Overall = weighted average
            double overall = popularityScore * 0.35
                           + revenueScore * 0.30
                           + retentionScore * 0.20
                           + ratingScore * 0.15;

            return new PerformanceScore
            {
                Popularity = Math.Round(popularityScore, 1),
                Revenue = Math.Round(revenueScore, 1),
                Retention = Math.Round(retentionScore, 1),
                Rating = Math.Round(ratingScore, 1),
                Overall = Math.Round(overall, 1),
                Grade = GetGrade(overall),
            };
        }

        /// <summary>Maps a numeric score (0–100) to a letter grade (A–F).</summary>
        internal static string GetGrade(double score)
        {
            if (score >= 90) return "A";
            if (score >= 80) return "B";
            if (score >= 70) return "C";
            if (score >= 60) return "D";
            return "F";
        }

        /// <summary>
        /// Compares two movie insights across revenue, popularity, and performance,
        /// returning a natural-language verdict.
        /// </summary>
        internal static string GetComparisonVerdict(MovieInsight a, MovieInsight b)
        {
            int aWins = 0, bWins = 0;
            if (a.Revenue.TotalRevenue > b.Revenue.TotalRevenue) aWins++; else if (b.Revenue.TotalRevenue > a.Revenue.TotalRevenue) bWins++;
            if (a.RentalSummary.TotalRentals > b.RentalSummary.TotalRentals) aWins++; else if (b.RentalSummary.TotalRentals > a.RentalSummary.TotalRentals) bWins++;
            if (a.PerformanceScore.Overall > b.PerformanceScore.Overall) aWins++; else if (b.PerformanceScore.Overall > a.PerformanceScore.Overall) bWins++;

            if (aWins > bWins) return a.MovieName + " outperforms " + b.MovieName + " overall.";
            if (bWins > aWins) return b.MovieName + " outperforms " + a.MovieName + " overall.";
            return a.MovieName + " and " + b.MovieName + " perform similarly.";
        }
    }
}
