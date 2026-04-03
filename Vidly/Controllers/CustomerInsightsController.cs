using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class CustomerInsightsController : Controller
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly RentalHistoryService _historyService;

        public CustomerInsightsController()
            : this(new InMemoryCustomerRepository(), new InMemoryRentalRepository(), new InMemoryMovieRepository()) { }

        public CustomerInsightsController(ICustomerRepository customerRepository, IRentalRepository rentalRepository, IMovieRepository movieRepository)
        {
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _historyService = new RentalHistoryService(_rentalRepository, _movieRepository, _customerRepository);
        }

        public ActionResult Index(int? customerId)
        {
            var allCustomers = _customerRepository.GetAll();
            if (!customerId.HasValue)
                return View(new CustomerInsightsViewModel { AllCustomers = allCustomers, StatusMessage = "Select a customer to view their insights." });

            var customer = _customerRepository.GetById(customerId.Value);
            if (customer == null)
                return View(new CustomerInsightsViewModel { AllCustomers = allCustomers, StatusMessage = "Customer not found." });

            var history = _historyService.GetRentalHistory(customerId: customerId.Value, movieId: null, from: null, to: null, status: null);
            var timeline = _historyService.GetCustomerTimeline(customerId.Value);
            var loyalty = _historyService.GetLoyaltyScore(customerId.Value);

            return View(new CustomerInsightsViewModel
            {
                Customer = customer, Loyalty = loyalty, Timeline = timeline, RentalHistory = history,
                GenreStats = BuildGenreBreakdown(history), Spending = BuildSpendingSummary(history),
                Patterns = BuildRentalPatterns(history), AllCustomers = allCustomers,
            });
        }

        internal static GenreBreakdown BuildGenreBreakdown(IReadOnlyList<RentalHistoryEntry> history)
        {
            var counts = new Dictionary<string, int>();
            foreach (var entry in history)
            {
                var genre = entry.MovieGenre?.ToString() ?? "Unknown";
                counts[genre] = counts.TryGetValue(genre, out var c) ? c + 1 : 1;
            }

            return new GenreBreakdown
            {
                GenreCounts = counts,
                FavoriteGenre = MaxKey(counts),
                TotalRentals = history.Count,
                UniqueGenres = counts.Count,
            };
        }

        internal static ViewModels.SpendingSummary BuildSpendingSummary(IReadOnlyList<RentalHistoryEntry> history)
        {
            decimal totalSpent = 0, totalLateFees = 0;
            foreach (var entry in history)
            {
                totalSpent += entry.TotalCost;
                totalLateFees += entry.LateFee;
            }

            return new ViewModels.SpendingSummary
            {
                TotalSpent = totalSpent,
                AveragePerRental = history.Count > 0 ? Math.Round(totalSpent / history.Count, 2) : 0,
                TotalLateFees = totalLateFees,
                LateFeePct = totalSpent > 0 ? Math.Round(totalLateFees / totalSpent * 100, 1) : 0,
                TotalRentals = history.Count,
            };
        }

        internal static RentalPatterns BuildRentalPatterns(IReadOnlyList<RentalHistoryEntry> history)
        {
            if (history.Count == 0)
                return new RentalPatterns { OnTimeReturnRate = 100, MostActiveDay = "N/A" };

            double totalDuration = 0;
            int longestRental = 0;
            int overdueCount = 0;
            var dayOfWeekCounts = new Dictionary<DayOfWeek, int>();

            foreach (var entry in history)
            {
                totalDuration += entry.RentalDurationDays;
                if (entry.RentalDurationDays > longestRental)
                    longestRental = entry.RentalDurationDays;
                if (entry.WasLate)
                    overdueCount++;

                var dow = entry.RentalDate.DayOfWeek;
                dayOfWeekCounts[dow] = dayOfWeekCounts.TryGetValue(dow, out var n) ? n + 1 : 1;
            }

            // Count consecutive on-time returns from the most recent rental
            int onTimeStreak = 0;
            foreach (var entry in history.OrderByDescending(h => h.RentalDate))
            {
                if (!entry.WasLate) onTimeStreak++;
                else break;
            }

            return new RentalPatterns
            {
                AverageDurationDays = Math.Round(totalDuration / history.Count, 1),
                LongestRentalDays = longestRental,
                OnTimeReturnRate = Math.Round((double)(history.Count - overdueCount) / history.Count * 100, 1),
                TotalOverdue = overdueCount,
                MostActiveDay = MaxKey(dayOfWeekCounts)?.ToString() ?? "N/A",
                CurrentStreak = onTimeStreak,
            };
        }

        /// <summary>
        /// Returns the key with the highest value from a dictionary, or default
        /// if the dictionary is empty. Consolidates the repeated
        /// "iterate-and-track-max" pattern used by multiple analytics methods.
        /// </summary>
        private static TKey MaxKey<TKey>(Dictionary<TKey, int> dict)
        {
            TKey best = default;
            int max = 0;
            foreach (var kvp in dict)
            {
                if (kvp.Value > max)
                {
                    max = kvp.Value;
                    best = kvp.Key;
                }
            }
            return best;
        }
    }
}
