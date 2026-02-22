using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Generates comprehensive rental history and activity reports for customers.
    /// Analyzes spending patterns, genre preferences, rental frequency, and
    /// provides actionable insights like loyalty scoring and membership fit.
    /// </summary>
    public class CustomerActivityService
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly IRentalRepository _rentalRepository;

        public CustomerActivityService(
            ICustomerRepository customerRepository,
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository)
        {
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
        }

        /// <summary>
        /// Generates a full activity report for a customer.
        /// </summary>
        /// <param name="customerId">Customer ID to report on.</param>
        /// <returns>Complete activity report with stats, timeline, and insights.</returns>
        public CustomerActivityReport GetActivityReport(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new KeyNotFoundException($"Customer with Id {customerId} not found.");

            var allRentals = _rentalRepository.GetAll();
            var customerRentals = allRentals
                .Where(r => r.CustomerId == customerId)
                .OrderByDescending(r => r.RentalDate)
                .ToList();

            var allMovies = _movieRepository.GetAll();
            var movieLookup = allMovies.ToDictionary(m => m.Id);

            var report = new CustomerActivityReport
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                MembershipType = customer.MembershipType,
                MemberSince = customer.MemberSince,
                RentalHistory = customerRentals,
                Summary = BuildSummary(customerRentals),
                GenreBreakdown = BuildGenreBreakdown(customerRentals, movieLookup),
                MonthlyActivity = BuildMonthlyActivity(customerRentals),
                LoyaltyScore = CalculateLoyaltyScore(customerRentals, customer),
                Insights = GenerateInsights(customerRentals, customer, movieLookup)
            };

            return report;
        }

        /// <summary>
        /// Computes summary statistics from rental history in a single pass.
        /// </summary>
        internal static ActivitySummary BuildSummary(IList<Rental> rentals)
        {
            if (rentals == null || rentals.Count == 0)
            {
                return new ActivitySummary();
            }

            int active = 0, overdue = 0, returned = 0;
            decimal totalSpent = 0, totalLateFees = 0;
            double totalDurationDays = 0;
            int completedRentals = 0;

            foreach (var r in rentals)
            {
                switch (r.Status)
                {
                    case RentalStatus.Active: active++; break;
                    case RentalStatus.Overdue: overdue++; break;
                    case RentalStatus.Returned: returned++; break;
                }

                totalSpent += r.TotalCost;
                totalLateFees += r.LateFee;

                if (r.ReturnDate.HasValue)
                {
                    totalDurationDays += (r.ReturnDate.Value - r.RentalDate).TotalDays;
                    completedRentals++;
                }
            }

            var firstRental = rentals.Min(r => r.RentalDate);
            var lastRental = rentals.Max(r => r.RentalDate);

            return new ActivitySummary
            {
                TotalRentals = rentals.Count,
                ActiveRentals = active,
                OverdueRentals = overdue,
                ReturnedRentals = returned,
                TotalSpent = totalSpent,
                TotalLateFees = totalLateFees,
                AverageRentalDays = completedRentals > 0
                    ? Math.Round(totalDurationDays / completedRentals, 1)
                    : 0,
                AverageSpentPerRental = rentals.Count > 0
                    ? Math.Round(totalSpent / rentals.Count, 2)
                    : 0,
                FirstRentalDate = firstRental,
                LastRentalDate = lastRental,
                OnTimeReturnRate = returned > 0
                    ? Math.Round(
                        (double)rentals.Count(r =>
                            r.Status == RentalStatus.Returned && r.LateFee == 0)
                        / returned * 100, 1)
                    : 0
            };
        }

        /// <summary>
        /// Breaks down rentals by genre with count and total spent per genre.
        /// </summary>
        internal static List<GenreActivity> BuildGenreBreakdown(
            IList<Rental> rentals,
            Dictionary<int, Movie> movieLookup)
        {
            var genreStats = new Dictionary<Genre, GenreActivity>();

            foreach (var r in rentals)
            {
                if (!movieLookup.TryGetValue(r.MovieId, out var movie) || !movie.Genre.HasValue)
                    continue;

                var genre = movie.Genre.Value;
                if (!genreStats.TryGetValue(genre, out var activity))
                {
                    activity = new GenreActivity { Genre = genre };
                    genreStats[genre] = activity;
                }

                activity.RentalCount++;
                activity.TotalSpent += r.TotalCost;
            }

            var total = rentals.Count > 0 ? rentals.Count : 1;
            foreach (var activity in genreStats.Values)
            {
                activity.Percentage = Math.Round((double)activity.RentalCount / total * 100, 1);
            }

            return genreStats.Values
                .OrderByDescending(g => g.RentalCount)
                .ToList();
        }

        /// <summary>
        /// Groups rentals by month for the last 6 months.
        /// </summary>
        internal static List<MonthlyActivityEntry> BuildMonthlyActivity(IList<Rental> rentals)
        {
            var result = new List<MonthlyActivityEntry>();
            var today = DateTime.Today;

            for (int i = 5; i >= 0; i--)
            {
                var month = today.AddMonths(-i);
                var monthStart = new DateTime(month.Year, month.Month, 1);
                var monthEnd = monthStart.AddMonths(1);

                var monthRentals = rentals
                    .Where(r => r.RentalDate >= monthStart && r.RentalDate < monthEnd)
                    .ToList();

                result.Add(new MonthlyActivityEntry
                {
                    Year = monthStart.Year,
                    Month = monthStart.Month,
                    MonthName = monthStart.ToString("MMM yyyy"),
                    RentalCount = monthRentals.Count,
                    TotalSpent = monthRentals.Sum(r => r.TotalCost)
                });
            }

            return result;
        }

        /// <summary>
        /// Calculates a loyalty score (0-100) based on rental frequency,
        /// on-time returns, spending, and account age.
        /// </summary>
        internal static int CalculateLoyaltyScore(IList<Rental> rentals, Customer customer)
        {
            if (rentals == null || rentals.Count == 0)
                return 0;

            double score = 0;

            // Frequency: up to 30 points (1 point per rental, max 30)
            score += Math.Min(rentals.Count, 30);

            // On-time returns: up to 25 points
            var returned = rentals.Where(r => r.Status == RentalStatus.Returned).ToList();
            if (returned.Count > 0)
            {
                var onTimeRate = (double)returned.Count(r => r.LateFee == 0) / returned.Count;
                score += onTimeRate * 25;
            }

            // Spending: up to 20 points (1 point per $10 spent, max 20)
            var totalSpent = rentals.Sum(r => r.TotalCost);
            score += Math.Min((double)(totalSpent / 10m), 20);

            // Account age: up to 15 points (1 point per month, max 15)
            if (customer.MemberSince.HasValue)
            {
                var monthsActive = Math.Max(0,
                    (DateTime.Today.Year - customer.MemberSince.Value.Year) * 12
                    + (DateTime.Today.Month - customer.MemberSince.Value.Month));
                score += Math.Min(monthsActive, 15);
            }

            // Membership tier bonus: up to 10 points
            switch (customer.MembershipType)
            {
                case MembershipType.Platinum: score += 10; break;
                case MembershipType.Gold: score += 7; break;
                case MembershipType.Silver: score += 4; break;
                case MembershipType.Basic: score += 1; break;
            }

            return (int)Math.Min(Math.Round(score), 100);
        }

        /// <summary>
        /// Generates actionable insights based on rental patterns.
        /// </summary>
        internal static List<ActivityInsight> GenerateInsights(
            IList<Rental> rentals,
            Customer customer,
            Dictionary<int, Movie> movieLookup)
        {
            var insights = new List<ActivityInsight>();

            if (rentals.Count == 0)
            {
                insights.Add(new ActivityInsight
                {
                    Icon = "🎬",
                    Title = "No rentals yet",
                    Description = "This customer hasn't rented any movies. Suggest popular titles!",
                    Type = InsightType.Info
                });
                return insights;
            }

            // Check for overdue rentals
            var overdueCount = rentals.Count(r => r.Status == RentalStatus.Overdue);
            if (overdueCount > 0)
            {
                insights.Add(new ActivityInsight
                {
                    Icon = "⚠️",
                    Title = $"{overdueCount} overdue rental{(overdueCount > 1 ? "s" : "")}",
                    Description = "Follow up on overdue items to avoid accumulating late fees.",
                    Type = InsightType.Warning
                });
            }

            // Late fee tendency
            var returned = rentals.Where(r => r.Status == RentalStatus.Returned).ToList();
            if (returned.Count >= 3)
            {
                var lateRate = (double)returned.Count(r => r.LateFee > 0) / returned.Count;
                if (lateRate > 0.5)
                {
                    insights.Add(new ActivityInsight
                    {
                        Icon = "💸",
                        Title = "Frequent late returns",
                        Description = $"{lateRate:P0} of returns were late. Consider offering extended rental periods.",
                        Type = InsightType.Warning
                    });
                }
                else if (lateRate == 0)
                {
                    insights.Add(new ActivityInsight
                    {
                        Icon = "⭐",
                        Title = "Perfect return record",
                        Description = "This customer always returns on time — a model member!",
                        Type = InsightType.Positive
                    });
                }
            }

            // Favorite genre
            var genreBreakdown = BuildGenreBreakdown(rentals, movieLookup);
            if (genreBreakdown.Count > 0)
            {
                var top = genreBreakdown.First();
                insights.Add(new ActivityInsight
                {
                    Icon = "🎯",
                    Title = $"Top genre: {top.Genre}",
                    Description = $"{top.RentalCount} rentals ({top.Percentage}% of total). Feature new {top.Genre} arrivals!",
                    Type = InsightType.Info
                });
            }

            // Spending trend
            var totalSpent = rentals.Sum(r => r.TotalCost);
            if (totalSpent > 100)
            {
                insights.Add(new ActivityInsight
                {
                    Icon = "💰",
                    Title = "High-value customer",
                    Description = $"Total spend: ${totalSpent:F2}. Consider offering a loyalty discount or membership upgrade.",
                    Type = InsightType.Positive
                });
            }

            // Membership upgrade suggestion
            if (customer.MembershipType == MembershipType.Basic && rentals.Count >= 5)
            {
                insights.Add(new ActivityInsight
                {
                    Icon = "⬆️",
                    Title = "Upgrade candidate",
                    Description = $"With {rentals.Count} rentals, this customer may benefit from a Silver or Gold membership.",
                    Type = InsightType.Info
                });
            }

            // Inactive customer
            var daysSinceLast = (DateTime.Today - rentals.Max(r => r.RentalDate)).TotalDays;
            if (daysSinceLast > 30)
            {
                insights.Add(new ActivityInsight
                {
                    Icon = "📭",
                    Title = "Inactive customer",
                    Description = $"No rentals in {(int)daysSinceLast} days. Send a promotional offer to re-engage.",
                    Type = InsightType.Warning
                });
            }

            return insights;
        }
    }

    // ── Models ─────────────────────────────────────────────────────

    /// <summary>
    /// Complete activity report for a customer.
    /// </summary>
    public class CustomerActivityReport
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public MembershipType MembershipType { get; set; }
        public DateTime? MemberSince { get; set; }
        public IList<Rental> RentalHistory { get; set; } = new List<Rental>();
        public ActivitySummary Summary { get; set; } = new ActivitySummary();
        public List<GenreActivity> GenreBreakdown { get; set; } = new List<GenreActivity>();
        public List<MonthlyActivityEntry> MonthlyActivity { get; set; } = new List<MonthlyActivityEntry>();
        public int LoyaltyScore { get; set; }
        public List<ActivityInsight> Insights { get; set; } = new List<ActivityInsight>();
    }

    /// <summary>
    /// Aggregate rental statistics.
    /// </summary>
    public class ActivitySummary
    {
        public int TotalRentals { get; set; }
        public int ActiveRentals { get; set; }
        public int OverdueRentals { get; set; }
        public int ReturnedRentals { get; set; }
        public decimal TotalSpent { get; set; }
        public decimal TotalLateFees { get; set; }
        public double AverageRentalDays { get; set; }
        public decimal AverageSpentPerRental { get; set; }
        public DateTime FirstRentalDate { get; set; }
        public DateTime LastRentalDate { get; set; }
        public double OnTimeReturnRate { get; set; }
    }

    /// <summary>
    /// Rental activity per genre.
    /// </summary>
    public class GenreActivity
    {
        public Genre Genre { get; set; }
        public int RentalCount { get; set; }
        public decimal TotalSpent { get; set; }
        public double Percentage { get; set; }
    }

    /// <summary>
    /// Monthly rental activity entry.
    /// </summary>
    public class MonthlyActivityEntry
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; }
        public int RentalCount { get; set; }
        public decimal TotalSpent { get; set; }
    }

    /// <summary>
    /// Actionable insight about customer behavior.
    /// </summary>
    public class ActivityInsight
    {
        public string Icon { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public InsightType Type { get; set; }
    }

    /// <summary>
    /// Insight severity/category.
    /// </summary>
    public enum InsightType
    {
        Info,
        Positive,
        Warning
    }
}
