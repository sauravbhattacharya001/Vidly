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
            foreach (var e in history) { var g = e.MovieGenre?.ToString() ?? "Unknown"; counts[g] = counts.ContainsKey(g) ? counts[g] + 1 : 1; }
            string fav = null; int max = 0;
            foreach (var kvp in counts) { if (kvp.Value > max) { max = kvp.Value; fav = kvp.Key; } }
            return new GenreBreakdown { GenreCounts = counts, FavoriteGenre = fav, TotalRentals = history.Count, UniqueGenres = counts.Count };
        }

        internal static SpendingSummary BuildSpendingSummary(IReadOnlyList<RentalHistoryEntry> history)
        {
            decimal total = 0, late = 0;
            foreach (var e in history) { total += e.TotalCost; late += e.LateFee; }
            return new SpendingSummary { TotalSpent = total, AveragePerRental = history.Count > 0 ? Math.Round(total / history.Count, 2) : 0,
                TotalLateFees = late, LateFeePct = total > 0 ? Math.Round(late / total * 100, 1) : 0, TotalRentals = history.Count };
        }

        internal static RentalPatterns BuildRentalPatterns(IReadOnlyList<RentalHistoryEntry> history)
        {
            if (history.Count == 0) return new RentalPatterns { OnTimeReturnRate = 100, MostActiveDay = "N/A" };
            double dur = 0; int longest = 0, overdue = 0;
            var dow = new Dictionary<DayOfWeek, int>();
            foreach (var e in history) { dur += e.RentalDurationDays; if (e.RentalDurationDays > longest) longest = e.RentalDurationDays;
                if (e.WasLate) overdue++; var d = e.RentalDate.DayOfWeek; dow[d] = dow.ContainsKey(d) ? dow[d] + 1 : 1; }
            DayOfWeek best = DayOfWeek.Monday; int mx = 0;
            foreach (var kvp in dow) { if (kvp.Value > mx) { mx = kvp.Value; best = kvp.Key; } }
            int streak = 0;
            foreach (var e in history.OrderByDescending(h => h.RentalDate)) { if (!e.WasLate) streak++; else break; }
            return new RentalPatterns { AverageDurationDays = Math.Round(dur / history.Count, 1), LongestRentalDays = longest,
                OnTimeReturnRate = Math.Round((double)(history.Count - overdue) / history.Count * 100, 1),
                TotalOverdue = overdue, MostActiveDay = best.ToString(), CurrentStreak = streak };
        }
    }
}
