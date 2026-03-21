using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Tracks customer rental spending against monthly budgets.
    /// Provides spending breakdowns, alerts, and savings tips.
    /// </summary>
    public class RentalBudgetService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IMovieRepository _movieRepository;
        private static readonly Dictionary<int, CustomerBudget> _budgets = new Dictionary<int, CustomerBudget>();

        public RentalBudgetService(
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository,
            IMovieRepository movieRepository)
        {
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        /// <summary>
        /// Sets or updates a customer's monthly rental budget.
        /// </summary>
        public CustomerBudget SetBudget(int customerId, decimal monthlyLimit, bool alertsEnabled = true, decimal alertThreshold = 0.8m)
        {
            if (monthlyLimit <= 0 || monthlyLimit > 9999.99m)
                throw new ArgumentException("Monthly budget must be between $0.01 and $9,999.99.");
            if (alertThreshold < 0.1m || alertThreshold > 1.0m)
                throw new ArgumentException("Alert threshold must be between 10% and 100%.");

            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new KeyNotFoundException($"Customer {customerId} not found.");

            var budget = new CustomerBudget
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                MonthlyLimit = monthlyLimit,
                AlertsEnabled = alertsEnabled,
                AlertThreshold = alertThreshold,
                CreatedDate = DateTime.Now,
                UpdatedDate = DateTime.Now
            };

            _budgets[customerId] = budget;
            return budget;
        }

        /// <summary>
        /// Gets a customer's budget configuration, or null if not set.
        /// </summary>
        public CustomerBudget GetBudget(int customerId)
        {
            return _budgets.ContainsKey(customerId) ? _budgets[customerId] : null;
        }

        /// <summary>
        /// Removes a customer's budget.
        /// </summary>
        public bool RemoveBudget(int customerId)
        {
            return _budgets.Remove(customerId);
        }

        /// <summary>
        /// Gets the full spending dashboard for a customer in a given month.
        /// </summary>
        public BudgetDashboard GetDashboard(int customerId, int? year = null, int? month = null)
        {
            var targetYear = year ?? DateTime.Now.Year;
            var targetMonth = month ?? DateTime.Now.Month;
            var budget = GetBudget(customerId);
            var rentals = _rentalRepository.GetByCustomer(customerId);
            var movies = _movieRepository.GetMovies().ToDictionary(m => m.Id);

            // Filter to target month
            var monthRentals = rentals
                .Where(r => r.RentalDate.Year == targetYear && r.RentalDate.Month == targetMonth)
                .OrderByDescending(r => r.RentalDate)
                .ToList();

            decimal totalSpent = monthRentals.Sum(r => r.TotalCost);
            decimal monthlyLimit = budget?.MonthlyLimit ?? 0;

            // Genre breakdown
            var genreBreakdown = monthRentals
                .Where(r => movies.ContainsKey(r.MovieId) && movies[r.MovieId].Genre.HasValue)
                .GroupBy(r => movies[r.MovieId].Genre.Value)
                .Select(g => new GenreSpending
                {
                    Genre = g.Key,
                    Amount = g.Sum(r => r.TotalCost),
                    RentalCount = g.Count(),
                    Percentage = totalSpent > 0 ? Math.Round(g.Sum(r => r.TotalCost) / totalSpent * 100, 1) : 0
                })
                .OrderByDescending(g => g.Amount)
                .ToList();

            // Weekly breakdown
            var weeklyBreakdown = new List<WeeklySpending>();
            var firstDay = new DateTime(targetYear, targetMonth, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);
            for (int week = 0; week < 5; week++)
            {
                var weekStart = firstDay.AddDays(week * 7);
                if (weekStart > lastDay) break;
                var weekEnd = weekStart.AddDays(6) > lastDay ? lastDay : weekStart.AddDays(6);
                var weekRentals = monthRentals
                    .Where(r => r.RentalDate.Date >= weekStart && r.RentalDate.Date <= weekEnd)
                    .ToList();
                weeklyBreakdown.Add(new WeeklySpending
                {
                    WeekNumber = week + 1,
                    StartDate = weekStart,
                    EndDate = weekEnd,
                    Amount = weekRentals.Sum(r => r.TotalCost),
                    RentalCount = weekRentals.Count
                });
            }

            // Spending history (last 6 months)
            var history = new List<MonthlySpendingSummary>();
            for (int i = 5; i >= 0; i--)
            {
                var histDate = new DateTime(targetYear, targetMonth, 1).AddMonths(-i);
                var histRentals = rentals
                    .Where(r => r.RentalDate.Year == histDate.Year && r.RentalDate.Month == histDate.Month)
                    .ToList();
                history.Add(new MonthlySpendingSummary
                {
                    Year = histDate.Year,
                    Month = histDate.Month,
                    MonthName = histDate.ToString("MMM yyyy"),
                    TotalSpent = histRentals.Sum(r => r.TotalCost),
                    RentalCount = histRentals.Count,
                    BudgetLimit = monthlyLimit,
                    WasOverBudget = monthlyLimit > 0 && histRentals.Sum(r => r.TotalCost) > monthlyLimit
                });
            }

            // Alerts
            var alerts = new List<BudgetAlert>();
            if (budget != null && budget.AlertsEnabled && monthlyLimit > 0)
            {
                var usagePercent = totalSpent / monthlyLimit;
                if (usagePercent >= 1.0m)
                    alerts.Add(new BudgetAlert
                    {
                        Level = AlertLevel.Danger,
                        Message = $"You've exceeded your ${monthlyLimit:F2} monthly budget by ${totalSpent - monthlyLimit:F2}!",
                        Icon = "🚨"
                    });
                else if (usagePercent >= budget.AlertThreshold)
                    alerts.Add(new BudgetAlert
                    {
                        Level = AlertLevel.Warning,
                        Message = $"You've used {usagePercent:P0} of your monthly budget. ${monthlyLimit - totalSpent:F2} remaining.",
                        Icon = "⚠️"
                    });

                // Pace alert - on track to exceed?
                var daysInMonth = DateTime.DaysInMonth(targetYear, targetMonth);
                var dayOfMonth = Math.Min(DateTime.Now.Day, daysInMonth);
                if (dayOfMonth > 0 && dayOfMonth < daysInMonth)
                {
                    var projectedSpend = totalSpent / dayOfMonth * daysInMonth;
                    if (projectedSpend > monthlyLimit * 1.1m && usagePercent < 1.0m)
                        alerts.Add(new BudgetAlert
                        {
                            Level = AlertLevel.Info,
                            Message = $"At your current pace, you'll spend ~${projectedSpend:F2} this month (${projectedSpend - monthlyLimit:F2} over budget).",
                            Icon = "📊"
                        });
                }
            }

            // Tips
            var tips = GenerateTips(monthRentals, genreBreakdown, totalSpent, monthlyLimit, movies);

            return new BudgetDashboard
            {
                CustomerId = customerId,
                Year = targetYear,
                Month = targetMonth,
                MonthName = new DateTime(targetYear, targetMonth, 1).ToString("MMMM yyyy"),
                Budget = budget,
                TotalSpent = totalSpent,
                Remaining = monthlyLimit > 0 ? Math.Max(0, monthlyLimit - totalSpent) : 0,
                UsagePercent = monthlyLimit > 0 ? Math.Min(Math.Round(totalSpent / monthlyLimit * 100, 1), 999) : 0,
                RentalCount = monthRentals.Count,
                Rentals = monthRentals,
                GenreBreakdown = genreBreakdown,
                WeeklyBreakdown = weeklyBreakdown,
                SpendingHistory = history,
                Alerts = alerts,
                Tips = tips
            };
        }

        /// <summary>
        /// Gets all customers with active budgets and their current status.
        /// </summary>
        public IReadOnlyList<BudgetSummary> GetAllBudgetSummaries()
        {
            var now = DateTime.Now;
            return _budgets.Values
                .Select(b =>
                {
                    var rentals = _rentalRepository.GetByCustomer(b.CustomerId);
                    var monthSpent = rentals
                        .Where(r => r.RentalDate.Year == now.Year && r.RentalDate.Month == now.Month)
                        .Sum(r => r.TotalCost);

                    return new BudgetSummary
                    {
                        CustomerId = b.CustomerId,
                        CustomerName = b.CustomerName,
                        MonthlyLimit = b.MonthlyLimit,
                        TotalSpent = monthSpent,
                        UsagePercent = b.MonthlyLimit > 0 ? Math.Round(monthSpent / b.MonthlyLimit * 100, 1) : 0,
                        Status = monthSpent > b.MonthlyLimit ? BudgetStatus.OverBudget
                               : monthSpent >= b.MonthlyLimit * b.AlertThreshold ? BudgetStatus.Warning
                               : BudgetStatus.OnTrack
                    };
                })
                .OrderByDescending(s => s.UsagePercent)
                .ToList();
        }

        private List<string> GenerateTips(
            List<Rental> monthRentals,
            List<GenreSpending> genreBreakdown,
            decimal totalSpent,
            decimal monthlyLimit,
            Dictionary<int, Movie> movies)
        {
            var tips = new List<string>();

            if (monthRentals.Count == 0)
            {
                tips.Add("No rentals yet this month — your budget is wide open!");
                return tips;
            }

            // Tip: Top spending genre
            if (genreBreakdown.Any())
            {
                var topGenre = genreBreakdown.First();
                if (topGenre.Percentage > 50)
                    tips.Add($"You spent {topGenre.Percentage}% on {topGenre.Genre} — try mixing in other genres for variety!");
            }

            // Tip: Average cost per rental
            var avgCost = totalSpent / monthRentals.Count;
            if (avgCost > 5.0m)
                tips.Add($"Your average rental costs ${avgCost:F2}. Looking for older releases could save you money.");

            // Tip: Weekend vs weekday
            var weekendRentals = monthRentals.Count(r =>
                r.RentalDate.DayOfWeek == DayOfWeek.Saturday ||
                r.RentalDate.DayOfWeek == DayOfWeek.Sunday);
            if (weekendRentals > monthRentals.Count * 0.6)
                tips.Add("Most of your rentals are on weekends. Spreading rentals across the week might help pace your budget.");

            // Tip: Budget utilization
            if (monthlyLimit > 0 && totalSpent < monthlyLimit * 0.3m && monthRentals.Count > 0)
                tips.Add($"Only using {totalSpent / monthlyLimit:P0} of your budget — there's room for a few more movies!");

            if (tips.Count == 0)
                tips.Add("You're managing your rental budget well — keep it up! 🎬");

            return tips;
        }
    }

    #region Models

    public class CustomerBudget
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public decimal MonthlyLimit { get; set; }
        public bool AlertsEnabled { get; set; }
        public decimal AlertThreshold { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime UpdatedDate { get; set; }
    }

    public class BudgetDashboard
    {
        public int CustomerId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; }
        public CustomerBudget Budget { get; set; }
        public decimal TotalSpent { get; set; }
        public decimal Remaining { get; set; }
        public decimal UsagePercent { get; set; }
        public int RentalCount { get; set; }
        public List<Rental> Rentals { get; set; }
        public List<GenreSpending> GenreBreakdown { get; set; }
        public List<WeeklySpending> WeeklyBreakdown { get; set; }
        public List<MonthlySpendingSummary> SpendingHistory { get; set; }
        public List<BudgetAlert> Alerts { get; set; }
        public List<string> Tips { get; set; }
    }

    public class GenreSpending
    {
        public Genre Genre { get; set; }
        public decimal Amount { get; set; }
        public int RentalCount { get; set; }
        public decimal Percentage { get; set; }
    }

    public class WeeklySpending
    {
        public int WeekNumber { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal Amount { get; set; }
        public int RentalCount { get; set; }
    }

    public class MonthlySpendingSummary
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; }
        public decimal TotalSpent { get; set; }
        public int RentalCount { get; set; }
        public decimal BudgetLimit { get; set; }
        public bool WasOverBudget { get; set; }
    }

    public class BudgetAlert
    {
        public AlertLevel Level { get; set; }
        public string Message { get; set; }
        public string Icon { get; set; }
    }

    public class BudgetSummary
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public decimal MonthlyLimit { get; set; }
        public decimal TotalSpent { get; set; }
        public decimal UsagePercent { get; set; }
        public BudgetStatus Status { get; set; }
    }

    public enum AlertLevel { Info, Warning, Danger }
    public enum BudgetStatus { OnTrack, Warning, OverBudget }

    #endregion
}
