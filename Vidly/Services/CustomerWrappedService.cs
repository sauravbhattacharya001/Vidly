using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Generates "Wrapped"-style summaries for customers — a personalised
    /// breakdown of their rental history with fun insights and statistics.
    /// Supports all-time and year-scoped summaries.
    /// </summary>
    public class CustomerWrappedService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;

        public CustomerWrappedService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository)
        {
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        /// <summary>
        /// Generate an all-time wrapped summary for a customer.
        /// </summary>
        public CustomerWrapped GetAllTime(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.", nameof(customerId));

            var allRentals = _rentalRepository.GetAll()
                .Where(r => r.CustomerId == customerId)
                .OrderBy(r => r.RentalDate)
                .ToList();

            return BuildWrapped(customer, allRentals, null, null);
        }

        /// <summary>
        /// Generate a wrapped summary for a specific year.
        /// </summary>
        public CustomerWrapped GetForYear(int customerId, int year)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.", nameof(customerId));

            var start = new DateTime(year, 1, 1);
            var end = new DateTime(year, 12, 31);

            var rentals = _rentalRepository.GetAll()
                .Where(r => r.CustomerId == customerId &&
                            r.RentalDate >= start && r.RentalDate <= end)
                .OrderBy(r => r.RentalDate)
                .ToList();

            return BuildWrapped(customer, rentals, start, end);
        }

        /// <summary>
        /// Generate wrapped summaries for all customers (leaderboard mode).
        /// Returns summaries sorted by total rentals descending.
        /// </summary>
        public List<CustomerWrapped> GetLeaderboard(int? year = null)
        {
            var customers = _customerRepository.GetAll();
            var results = new List<CustomerWrapped>();

            foreach (var c in customers)
            {
                try
                {
                    var wrapped = year.HasValue
                        ? GetForYear(c.Id, year.Value)
                        : GetAllTime(c.Id);
                    if (wrapped.TotalRentals > 0)
                        results.Add(wrapped);
                }
                catch
                {
                    // Skip customers with issues
                }
            }

            return results.OrderByDescending(w => w.TotalRentals).ToList();
        }

        /// <summary>
        /// Generate a text report for a customer's wrapped summary.
        /// </summary>
        public string GenerateReport(CustomerWrapped wrapped)
        {
            if (wrapped == null) throw new ArgumentNullException(nameof(wrapped));

            var sb = new System.Text.StringBuilder();
            var period = wrapped.IsAllTime ? "All-Time" : $"{wrapped.PeriodStart:yyyy}";

            sb.AppendLine($"╔══════════════════════════════════════╗");
            sb.AppendLine($"║   🎬 {wrapped.CustomerName}'s {period} Wrapped   ║");
            sb.AppendLine($"╚══════════════════════════════════════╝");
            sb.AppendLine();

            if (wrapped.TotalRentals == 0)
            {
                sb.AppendLine("No rentals found for this period.");
                return sb.ToString();
            }

            sb.AppendLine($"📊 BY THE NUMBERS");
            sb.AppendLine($"   Total Rentals:     {wrapped.TotalRentals}");
            sb.AppendLine($"   Unique Movies:     {wrapped.UniqueMovies}");
            sb.AppendLine($"   Repeat Rentals:    {wrapped.RepeatRentals}");
            sb.AppendLine($"   Total Spent:       ${wrapped.TotalSpent:F2}");
            sb.AppendLine($"   Avg Cost/Rental:   ${wrapped.AverageCostPerRental:F2}");
            sb.AppendLine($"   Late Fees:         ${wrapped.TotalLateFees:F2}");
            sb.AppendLine();

            sb.AppendLine($"⏱️ TIMING");
            sb.AppendLine($"   Avg Duration:      {wrapped.AverageRentalDays:F1} days");
            sb.AppendLine($"   Shortest:          {wrapped.ShortestRentalDays} days");
            sb.AppendLine($"   Longest:           {wrapped.LongestRentalDays} days");
            sb.AppendLine();

            sb.AppendLine($"🎭 GENRE PROFILE");
            if (wrapped.FavoriteGenre.HasValue)
                sb.AppendLine($"   Favorite Genre:    {wrapped.FavoriteGenre}");
            sb.AppendLine($"   Diversity Score:   {wrapped.GenreDiversity:F2}");
            foreach (var g in wrapped.GenreBreakdown.Take(5))
                sb.AppendLine($"   {g.Genre,-18} {g.Count,3} ({g.Percentage:F1}%)");
            sb.AppendLine();

            if (wrapped.LongestRentalStreak > 1)
            {
                sb.AppendLine($"🔥 STREAKS");
                sb.AppendLine($"   Longest Streak:    {wrapped.LongestRentalStreak} consecutive days");
                if (wrapped.StreakStartDate.HasValue)
                    sb.AppendLine($"   Started:           {wrapped.StreakStartDate:MMM d, yyyy}");
                sb.AppendLine();
            }

            if (wrapped.FavoriteRentalDay.HasValue)
            {
                sb.AppendLine($"📅 FAVORITE DAY");
                sb.AppendLine($"   {wrapped.FavoriteRentalDay}");
                sb.AppendLine();
            }

            sb.AppendLine($"🏷️ PERSONALITY");
            sb.AppendLine($"   \"{wrapped.RentalPersonality}\"");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(wrapped.TopRatedMovieRented))
                sb.AppendLine($"⭐ Top Rated:         {wrapped.TopRatedMovieRented}");
            sb.AppendLine($"💰 Biggest Rental:    ${wrapped.MostExpensiveRental:F2}");

            return sb.ToString();
        }

        // ── Private ──

        private CustomerWrapped BuildWrapped(Customer customer, List<Rental> rentals,
            DateTime? periodStart, DateTime? periodEnd)
        {
            var wrapped = new CustomerWrapped
            {
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd
            };

            if (!rentals.Any())
            {
                wrapped.RentalPersonality = "The Ghost";
                return wrapped;
            }

            // Volume
            wrapped.TotalRentals = rentals.Count;
            var movieIds = rentals.Select(r => r.MovieId).ToList();
            wrapped.UniqueMovies = movieIds.Distinct().Count();
            wrapped.RepeatRentals = rentals.Count - wrapped.UniqueMovies;

            // Spending
            wrapped.TotalSpent = rentals.Sum(r => r.TotalCost);
            wrapped.AverageCostPerRental = wrapped.TotalSpent / rentals.Count;
            wrapped.TotalLateFees = rentals.Sum(r => r.LateFee);
            wrapped.MostExpensiveRental = rentals.Max(r => r.TotalCost);

            // Timing
            var durations = rentals.Select(r =>
            {
                var end = r.ReturnDate ?? DateTime.Today;
                return Math.Max(1, (int)Math.Ceiling((end - r.RentalDate).TotalDays));
            }).ToList();

            wrapped.AverageRentalDays = durations.Average();
            wrapped.ShortestRentalDays = durations.Min();
            wrapped.LongestRentalDays = durations.Max();

            // Genre breakdown
            var movies = _movieRepository.GetAll().ToDictionary(m => m.Id);
            var genreCounts = new Dictionary<Genre, int>();

            foreach (var r in rentals)
            {
                if (movies.TryGetValue(r.MovieId, out var movie) && movie.Genre.HasValue)
                {
                    if (!genreCounts.ContainsKey(movie.Genre.Value))
                        genreCounts[movie.Genre.Value] = 0;
                    genreCounts[movie.Genre.Value]++;
                }
            }

            if (genreCounts.Any())
            {
                var total = genreCounts.Values.Sum();
                wrapped.GenreBreakdown = genreCounts
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => new GenreBreakdownEntry
                    {
                        Genre = kv.Key,
                        Count = kv.Value,
                        Percentage = (double)kv.Value / total * 100.0
                    })
                    .ToList();

                wrapped.FavoriteGenre = wrapped.GenreBreakdown.First().Genre;
                wrapped.GenreDiversity = CalculateShannonEvenness(genreCounts.Values.ToList());
            }

            // Streaks (consecutive days with a new rental)
            var rentalDates = rentals.Select(r => r.RentalDate.Date).Distinct().OrderBy(d => d).ToList();
            int maxStreak = 1, currentStreak = 1;
            DateTime streakStart = rentalDates.First(), bestStreakStart = rentalDates.First();

            for (int i = 1; i < rentalDates.Count; i++)
            {
                if ((rentalDates[i] - rentalDates[i - 1]).Days == 1)
                {
                    currentStreak++;
                    if (currentStreak > maxStreak)
                    {
                        maxStreak = currentStreak;
                        bestStreakStart = streakStart;
                    }
                }
                else
                {
                    currentStreak = 1;
                    streakStart = rentalDates[i];
                }
            }

            wrapped.LongestRentalStreak = maxStreak;
            wrapped.StreakStartDate = bestStreakStart;

            // Day of week
            var dayGroups = rentals.GroupBy(r => r.RentalDate.DayOfWeek)
                .ToDictionary(g => g.Key, g => g.Count());
            wrapped.RentalsByDayOfWeek = dayGroups;
            wrapped.FavoriteRentalDay = dayGroups.OrderByDescending(kv => kv.Value).First().Key;

            // Top rated movie
            var bestRating = 0;
            string bestMovie = null;
            foreach (var r in rentals)
            {
                if (movies.TryGetValue(r.MovieId, out var movie) &&
                    movie.Rating.HasValue && movie.Rating.Value > bestRating)
                {
                    bestRating = movie.Rating.Value;
                    bestMovie = movie.Name;
                }
            }
            wrapped.TopRatedMovieRented = bestMovie;

            // Personality
            wrapped.RentalPersonality = DeterminePersonality(wrapped, genreCounts);

            return wrapped;
        }

        private static double CalculateShannonEvenness(List<int> counts)
        {
            if (counts.Count <= 1) return 0.0;
            double total = counts.Sum();
            double entropy = 0;
            foreach (var c in counts)
            {
                if (c <= 0) continue;
                double p = c / total;
                entropy -= p * Math.Log(p);
            }
            double maxEntropy = Math.Log(counts.Count);
            return maxEntropy > 0 ? entropy / maxEntropy : 0.0;
        }

        private static string DeterminePersonality(CustomerWrapped w, Dictionary<Genre, int> genreCounts)
        {
            if (w.TotalRentals >= 20 && w.GenreDiversity >= 0.8)
                return "The Cinephile";
            if (w.TotalRentals >= 20 && w.GenreDiversity < 0.3)
                return "The Specialist";
            if (w.TotalLateFees > w.TotalSpent * 0.3m)
                return "The Procrastinator";
            if (w.RepeatRentals > w.UniqueMovies)
                return "The Rewatcher";
            if (w.LongestRentalStreak >= 5)
                return "The Binge King";

            if (w.FavoriteGenre.HasValue)
            {
                switch (w.FavoriteGenre.Value)
                {
                    case Genre.Action: return "The Adrenaline Junkie";
                    case Genre.Comedy: return "The Laugh Seeker";
                    case Genre.Drama: return "The Deep Thinker";
                    case Genre.Horror: return "The Thrill Seeker";
                    case Genre.SciFi: return "The Futurist";
                    case Genre.Animation: return "The Young at Heart";
                    case Genre.Thriller: return "The Edge Sitter";
                    case Genre.Romance: return "The Romantic";
                    case Genre.Documentary: return "The Knowledge Hunter";
                    case Genre.Adventure: return "The Explorer";
                }
            }

            return "The Casual Viewer";
        }
    }
}
