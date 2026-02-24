using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

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

            var allRentals = _rentalRepository.GetAll();
            var movieRentals = allRentals.Where(r => r.MovieId == movieId).ToList();
            var customers = _customerRepository.GetAll();
            var customerLookup = customers.ToDictionary(c => c.Id, c => c);
            var allMovies = _movieRepository.GetAll();

            return new MovieInsight
            {
                MovieId = movie.Id,
                MovieName = movie.Name,
                Genre = movie.Genre,
                Rating = movie.Rating,
                ReleaseDate = movie.ReleaseDate,
                RentalSummary = BuildRentalSummary(movieRentals),
                Revenue = BuildRevenue(movieRentals),
                CustomerDemographics = BuildDemographics(movieRentals, customerLookup),
                MonthlyTrend = BuildMonthlyTrend(movieRentals),
                PerformanceScore = ComputePerformanceScore(movieRentals, allRentals, allMovies, movie),
            };
        }

        /// <summary>
        /// Get insights for all movies, sorted by performance score descending.
        /// </summary>
        public IReadOnlyList<MovieInsight> GetAllInsights()
        {
            var allRentals = _rentalRepository.GetAll();
            var movies = _movieRepository.GetAll();
            var customers = _customerRepository.GetAll();
            var customerLookup = customers.ToDictionary(c => c.Id, c => c);

            var rentalsByMovie = new Dictionary<int, List<Rental>>();
            foreach (var r in allRentals)
            {
                if (!rentalsByMovie.ContainsKey(r.MovieId))
                    rentalsByMovie[r.MovieId] = new List<Rental>();
                rentalsByMovie[r.MovieId].Add(r);
            }

            var insights = new List<MovieInsight>();
            foreach (var movie in movies)
            {
                List<Rental> movieRentals;
                rentalsByMovie.TryGetValue(movie.Id, out movieRentals);
                if (movieRentals == null) movieRentals = new List<Rental>();

                insights.Add(new MovieInsight
                {
                    MovieId = movie.Id,
                    MovieName = movie.Name,
                    Genre = movie.Genre,
                    Rating = movie.Rating,
                    ReleaseDate = movie.ReleaseDate,
                    RentalSummary = BuildRentalSummary(movieRentals),
                    Revenue = BuildRevenue(movieRentals),
                    CustomerDemographics = BuildDemographics(movieRentals, customerLookup),
                    MonthlyTrend = BuildMonthlyTrend(movieRentals),
                    PerformanceScore = ComputePerformanceScore(movieRentals, allRentals, movies, movie),
                });
            }

            insights.Sort((a, b) => b.PerformanceScore.Overall.CompareTo(a.PerformanceScore.Overall));
            return insights;
        }

        /// <summary>
        /// Compare two movies side by side.
        /// </summary>
        public MovieInsightComparison Compare(int movieIdA, int movieIdB)
        {
            var insightA = GetInsight(movieIdA);
            var insightB = GetInsight(movieIdB);
            if (insightA == null || insightB == null) return null;

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

        // ── Internal builders (static for testability) ──

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
                if (!customerCounts.ContainsKey(r.CustomerId))
                    customerCounts[r.CustomerId] = 0;
                customerCounts[r.CustomerId]++;
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
                if (!tierCounts.ContainsKey(tier))
                    tierCounts[tier] = 0;
                tierCounts[tier]++;
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

        internal static PerformanceScore ComputePerformanceScore(
            List<Rental> movieRentals, IReadOnlyList<Rental> allRentals,
            IReadOnlyList<Movie> allMovies, Movie movie)
        {
            // Popularity score (0-100): based on rental count relative to the most-rented movie
            double popularityScore = 0;
            if (allRentals.Count > 0)
            {
                var rentalCounts = new Dictionary<int, int>();
                foreach (var r in allRentals)
                {
                    if (!rentalCounts.ContainsKey(r.MovieId))
                        rentalCounts[r.MovieId] = 0;
                    rentalCounts[r.MovieId]++;
                }
                int maxRentals = 0;
                foreach (var count in rentalCounts.Values)
                    if (count > maxRentals) maxRentals = count;

                popularityScore = maxRentals > 0
                    ? (double)movieRentals.Count / maxRentals * 100 : 0;
            }

            // Revenue score (0-100): relative to top revenue movie
            double revenueScore = 0;
            if (allRentals.Count > 0)
            {
                var revenueByMovie = new Dictionary<int, decimal>();
                foreach (var r in allRentals)
                {
                    if (!revenueByMovie.ContainsKey(r.MovieId))
                        revenueByMovie[r.MovieId] = 0;
                    revenueByMovie[r.MovieId] += r.TotalCost;
                }
                decimal maxRevenue = 0;
                foreach (var rev in revenueByMovie.Values)
                    if (rev > maxRevenue) maxRevenue = rev;

                decimal movieRevenue = 0;
                foreach (var r in movieRentals)
                    movieRevenue += r.TotalCost;

                revenueScore = maxRevenue > 0
                    ? (double)(movieRevenue / maxRevenue) * 100 : 0;
            }

            // Retention score (0-100): % of customers who rented this movie more than once
            double retentionScore = 0;
            if (movieRentals.Count > 0)
            {
                var customerCounts = new Dictionary<int, int>();
                foreach (var r in movieRentals)
                {
                    if (!customerCounts.ContainsKey(r.CustomerId))
                        customerCounts[r.CustomerId] = 0;
                    customerCounts[r.CustomerId]++;
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

        internal static string GetGrade(double score)
        {
            if (score >= 90) return "A";
            if (score >= 80) return "B";
            if (score >= 70) return "C";
            if (score >= 60) return "D";
            return "F";
        }

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

    // ── Data models ──

    public class MovieInsight
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? Genre { get; set; }
        public int? Rating { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public RentalSummary RentalSummary { get; set; }
        public RevenueBreakdown Revenue { get; set; }
        public CustomerDemographicBreakdown CustomerDemographics { get; set; }
        public List<MonthlyRentalPoint> MonthlyTrend { get; set; }
        public PerformanceScore PerformanceScore { get; set; }
    }

    public class RentalSummary
    {
        public int TotalRentals { get; set; }
        public int ActiveRentals { get; set; }
        public int ReturnedRentals { get; set; }
        public int OverdueRentals { get; set; }
        public int UniqueCustomers { get; set; }
        public int RepeatRenters { get; set; }
        public double AverageRentalDays { get; set; }
        public DateTime? FirstRentalDate { get; set; }
        public DateTime? LastRentalDate { get; set; }
    }

    public class RevenueBreakdown
    {
        public decimal TotalRevenue { get; set; }
        public decimal BaseRevenue { get; set; }
        public decimal LateFeeRevenue { get; set; }
        public decimal AverageRevenuePerRental { get; set; }
        public double LateFeePercentage { get; set; }
    }

    public class CustomerDemographicBreakdown
    {
        public Dictionary<string, int> TierDistribution { get; set; }
            = new Dictionary<string, int>();
        public int TotalUniqueCustomers { get; set; }
        public string DominantTier { get; set; }
    }

    public class MonthlyRentalPoint
    {
        public string Month { get; set; }
        public int Year { get; set; }
        public int MonthNumber { get; set; }
        public int RentalCount { get; set; }
        public decimal Revenue { get; set; }
    }

    public class PerformanceScore
    {
        public double Popularity { get; set; }
        public double Revenue { get; set; }
        public double Retention { get; set; }
        public double Rating { get; set; }
        public double Overall { get; set; }
        public string Grade { get; set; }
    }

    public class MovieInsightComparison
    {
        public MovieInsight MovieA { get; set; }
        public MovieInsight MovieB { get; set; }
        public string RevenueWinner { get; set; }
        public string PopularityWinner { get; set; }
        public string PerformanceWinner { get; set; }
        public string OverallVerdict { get; set; }
    }
}
