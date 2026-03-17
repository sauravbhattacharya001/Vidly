using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Interface for rental history querying and analytics.
    /// </summary>
    public interface IRentalHistoryService
    {
        /// <summary>
        /// Retrieves filtered rental history entries, ordered by rental date descending.
        /// </summary>
        /// <param name="customerId">Optional filter by customer ID.</param>
        /// <param name="movieId">Optional filter by movie ID.</param>
        /// <param name="from">Optional start date (inclusive).</param>
        /// <param name="to">Optional end date (inclusive).</param>
        /// <param name="status">Optional filter by rental status.</param>
        /// <returns>Filtered and enriched rental history entries.</returns>
        IReadOnlyList<RentalHistoryEntry> GetRentalHistory(int? customerId, int? movieId, DateTime? from, DateTime? to, RentalStatus? status);

        /// <summary>
        /// Builds a chronological timeline of rental events for a customer,
        /// including rented, returned, late fee, and overdue warning events.
        /// </summary>
        /// <param name="customerId">The customer whose timeline to build.</param>
        /// <returns>Timeline events ordered by date, then by event type.</returns>
        IReadOnlyList<TimelineEvent> GetCustomerTimeline(int customerId);

        /// <summary>
        /// Analyzes rental patterns to identify the busiest days and hours.
        /// </summary>
        /// <returns>Rental counts grouped by day of week and hour of day.</returns>
        PopularTimesResult GetPopularTimes();

        /// <summary>
        /// Computes customer retention metrics including return rate, repeat
        /// customer rate, average inter-rental gap, and per-customer churn risk.
        /// </summary>
        /// <returns>Retention and churn analysis for the entire customer base.</returns>
        RetentionMetrics GetRetentionMetrics();

        /// <summary>
        /// Predicts movie availability over the next <paramref name="daysAhead"/> days
        /// based on average rental durations.
        /// </summary>
        /// <param name="daysAhead">Number of days to forecast (must be non-negative).</param>
        /// <returns>Per-movie availability predictions.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="daysAhead"/> is negative.</exception>
        InventoryForecast GetInventoryForecast(int daysAhead);

        /// <summary>
        /// Calculates a loyalty score (0–100) for a customer based on frequency,
        /// recency, on-time return rate, and total spend. Maps to a tier
        /// (Bronze/Silver/Gold/Platinum).
        /// </summary>
        /// <param name="customerId">The customer to evaluate.</param>
        /// <returns>Loyalty score, tier, and per-factor breakdown.</returns>
        LoyaltyResult GetLoyaltyScore(int customerId);

        /// <summary>
        /// Identifies seasonal rental trends by genre and month.
        /// </summary>
        /// <returns>Rental counts grouped by genre and month, ordered chronologically
        /// with the most popular genres first within each month.</returns>
        IReadOnlyList<SeasonalTrend> GetSeasonalTrends();

        /// <summary>
        /// Generates a formatted rental report of the specified type (Summary,
        /// Detailed, CustomerFocused, or MovieFocused).
        /// </summary>
        /// <param name="type">The type of report to generate.</param>
        /// <returns>A report with titled sections and formatted content.</returns>
        RentalReport GenerateReport(ReportType type);
    }

    /// <summary>
    /// Provides rich rental history querying and analytics for the video rental app.
    /// </summary>
    public class RentalHistoryService : IRentalHistoryService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly IClock _clock;

        public RentalHistoryService(
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository,
            IMovieRepository movieRepository,
            IClock clock = null)
        {
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _clock = clock ?? new SystemClock();
        }

        /// <inheritdoc />
        public IReadOnlyList<RentalHistoryEntry> GetRentalHistory(int? customerId, int? movieId, DateTime? from, DateTime? to, RentalStatus? status)
        {
            var rentals = _rentalRepository.GetAll().AsEnumerable();

            if (customerId.HasValue)
                rentals = rentals.Where(r => r.CustomerId == customerId.Value);
            if (movieId.HasValue)
                rentals = rentals.Where(r => r.MovieId == movieId.Value);
            if (from.HasValue)
                rentals = rentals.Where(r => r.RentalDate >= from.Value);
            if (to.HasValue)
                rentals = rentals.Where(r => r.RentalDate <= to.Value);
            if (status.HasValue)
                rentals = rentals.Where(r => r.Status == status.Value);

            var movies = _movieRepository.GetAll().ToDictionary(m => m.Id);

            return rentals.Select(r =>
            {
                var movie = movies.TryGetValue(r.MovieId, out var _v1) ? _v1 : null;
                var endDate = r.ReturnDate ?? _clock.Today;
                var days = Math.Max(1, (int)Math.Ceiling((endDate - r.RentalDate).TotalDays));
                var wasLate = r.ReturnDate.HasValue && r.ReturnDate.Value > r.DueDate;
                if (!r.ReturnDate.HasValue && _clock.Today > r.DueDate)
                    wasLate = true;

                return new RentalHistoryEntry
                {
                    RentalId = r.Id,
                    CustomerId = r.CustomerId,
                    CustomerName = r.CustomerName,
                    MovieId = r.MovieId,
                    MovieName = r.MovieName ?? movie?.Name,
                    MovieGenre = movie?.Genre,
                    RentalDate = r.RentalDate,
                    DueDate = r.DueDate,
                    ReturnDate = r.ReturnDate,
                    Status = r.Status,
                    DailyRate = r.DailyRate,
                    LateFee = r.LateFee,
                    TotalCost = r.TotalCost,
                    RentalDurationDays = days,
                    DaysOverdue = r.DaysOverdue,
                    WasLate = wasLate
                };
            })
            .OrderByDescending(e => e.RentalDate)
            .ToList();
        }

        /// <inheritdoc />
        public IReadOnlyList<TimelineEvent> GetCustomerTimeline(int customerId)
        {
            // Use GetByCustomer instead of GetAll() + Where() to avoid
            // loading every rental in the system.
            var rentals = _rentalRepository.GetByCustomer(customerId);
            var events = new List<TimelineEvent>();

            foreach (var r in rentals)
            {
                // Rented event
                events.Add(new TimelineEvent
                {
                    Date = r.RentalDate,
                    EventType = TimelineEventType.Rented,
                    Description = $"Rented \"{r.MovieName}\"",
                    RentalId = r.Id,
                    MovieId = r.MovieId,
                    MovieName = r.MovieName
                });

                // Returned event
                if (r.ReturnDate.HasValue)
                {
                    events.Add(new TimelineEvent
                    {
                        Date = r.ReturnDate.Value,
                        EventType = TimelineEventType.Returned,
                        Description = $"Returned \"{r.MovieName}\"",
                        RentalId = r.Id,
                        MovieId = r.MovieId,
                        MovieName = r.MovieName
                    });
                }

                // Late fee event
                if (r.LateFee > 0)
                {
                    var feeDate = r.ReturnDate ?? r.DueDate;
                    events.Add(new TimelineEvent
                    {
                        Date = feeDate,
                        EventType = TimelineEventType.LateFee,
                        Description = $"Late fee of {r.LateFee:C} for \"{r.MovieName}\"",
                        RentalId = r.Id,
                        MovieId = r.MovieId,
                        MovieName = r.MovieName,
                        Amount = r.LateFee
                    });
                }

                // Overdue warning
                if (r.Status == RentalStatus.Overdue || (r.Status != RentalStatus.Returned && _clock.Today > r.DueDate))
                {
                    events.Add(new TimelineEvent
                    {
                        Date = r.DueDate,
                        EventType = TimelineEventType.OverdueWarning,
                        Description = $"Overdue warning for \"{r.MovieName}\"",
                        RentalId = r.Id,
                        MovieId = r.MovieId,
                        MovieName = r.MovieName
                    });
                }
            }

            return events.OrderBy(e => e.Date).ThenBy(e => e.EventType).ToList();
        }

        /// <inheritdoc />
        public PopularTimesResult GetPopularTimes()
        {
            var rentals = _rentalRepository.GetAll();
            var result = new PopularTimesResult { TotalRentals = rentals.Count };

            if (rentals.Count == 0)
                return result;

            // By day of week
            var byDay = rentals.GroupBy(r => r.RentalDate.DayOfWeek)
                .ToDictionary(g => g.Key, g => g.Count());
            result.RentalsByDayOfWeek = byDay;

            // By hour
            var byHour = rentals.GroupBy(r => r.RentalDate.Hour)
                .ToDictionary(g => g.Key, g => g.Count());
            result.RentalsByHour = byHour;

            if (byDay.Any())
                result.BusiestDay = byDay.OrderByDescending(kv => kv.Value).First().Key;
            if (byHour.Any())
                result.BusiestHour = byHour.OrderByDescending(kv => kv.Value).First().Key;

            return result;
        }

        /// <inheritdoc />
        public RetentionMetrics GetRetentionMetrics()
        {
            var rentals = _rentalRepository.GetAll();
            var customers = _customerRepository.GetAll();
            var metrics = new RetentionMetrics { TotalCustomers = customers.Count };

            if (rentals.Count == 0)
                return metrics;

            var returned = rentals.Where(r => r.Status == RentalStatus.Returned).ToList();
            metrics.ReturnRate = rentals.Count > 0 ? (double)returned.Count / rentals.Count : 0;

            var byCustomer = rentals.GroupBy(r => r.CustomerId).ToList();
            var repeatCustomers = byCustomer.Where(g => g.Count() > 1).ToList();
            metrics.RepeatCustomers = repeatCustomers.Count;

            var customersWithRentals = byCustomer.Count;
            metrics.RepeatRentalRate = customersWithRentals > 0
                ? (double)repeatCustomers.Count / customersWithRentals : 0;

            // Average gap between rentals for repeat customers
            var gaps = new List<double>();
            foreach (var group in repeatCustomers)
            {
                var dates = group.OrderBy(r => r.RentalDate).Select(r => r.RentalDate).ToList();
                for (int i = 1; i < dates.Count; i++)
                    gaps.Add((dates[i] - dates[i - 1]).TotalDays);
            }
            metrics.AverageGapDays = gaps.Any() ? gaps.Average() : 0;

            // Churn risk
            var customerLookup = customers.ToDictionary(c => c.Id);
            foreach (var group in byCustomer)
            {
                var lastRental = group.Max(r => r.RentalDate);
                var daysSince = (_clock.Today - lastRental).TotalDays;
                var name = customerLookup.ContainsKey(group.Key) ? customerLookup[group.Key].Name : "Unknown";

                string risk;
                if (daysSince > 90) risk = "High";
                else if (daysSince > 30) risk = "Medium";
                else risk = "Low";

                metrics.ChurnRisks.Add(new CustomerChurnRisk
                {
                    CustomerId = group.Key,
                    CustomerName = name,
                    TotalRentals = group.Count(),
                    DaysSinceLastRental = daysSince,
                    RiskLevel = risk
                });
            }

            return metrics;
        }

        /// <inheritdoc />
        public InventoryForecast GetInventoryForecast(int daysAhead)
        {
            if (daysAhead < 0)
                throw new ArgumentOutOfRangeException(nameof(daysAhead), "Days ahead must be non-negative.");

            var rentals = _rentalRepository.GetAll();
            var movies = _movieRepository.GetAll();
            var forecast = new InventoryForecast { DaysAhead = daysAhead };

            // Pre-group rentals by movie ID for O(1) lookups instead of O(R) per movie
            var rentalsByMovie = rentals.GroupBy(r => r.MovieId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var movie in movies)
            {
                var movieRentals = rentalsByMovie.TryGetValue(movie.Id, out var grouped) ? grouped : new List<Rental>();
                var returnedRentals = movieRentals.Where(r => r.ReturnDate.HasValue).ToList();

                var avgDuration = returnedRentals.Any()
                    ? returnedRentals.Average(r => Math.Max(1, (int)Math.Ceiling((r.ReturnDate.Value - r.RentalDate).TotalDays)))
                    : 3.0; // default assumption

                var activeRental = movieRentals.FirstOrDefault(r => r.Status != RentalStatus.Returned);
                var currentlyAvailable = activeRental == null;

                DateTime? estimatedAvailable = null;
                if (!currentlyAvailable && activeRental != null)
                {
                    estimatedAvailable = activeRental.RentalDate.AddDays(avgDuration);
                    if (estimatedAvailable < _clock.Today)
                        estimatedAvailable = _clock.Today;
                }

                forecast.Predictions.Add(new MovieAvailabilityPrediction
                {
                    MovieId = movie.Id,
                    MovieName = movie.Name,
                    CurrentlyAvailable = currentlyAvailable,
                    EstimatedAvailableDate = estimatedAvailable,
                    AverageRentalDurationDays = Math.Round(avgDuration, 1)
                });
            }

            return forecast;
        }

        /// <inheritdoc />
        public LoyaltyResult GetLoyaltyScore(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            // Use GetByCustomer for O(n) filtered query instead of loading
            // all rentals and filtering client-side.
            var rentals = _rentalRepository.GetByCustomer(customerId);

            var name = customer?.Name ?? "Unknown";

            // Frequency: up to 30 points. 1 rental = 3 points, max 30.
            var frequencyPoints = Math.Min(30, rentals.Count * 3);

            // Recency: up to 25 points.
            int recencyPoints = 0;
            if (rentals.Any())
            {
                var lastRental = rentals.Max(r => r.RentalDate);
                var daysSince = (_clock.Today - lastRental).TotalDays;
                if (daysSince <= 7) recencyPoints = 25;
                else if (daysSince <= 30) recencyPoints = 20;
                else if (daysSince <= 60) recencyPoints = 15;
                else if (daysSince <= 90) recencyPoints = 10;
                else if (daysSince <= 180) recencyPoints = 5;
                else recencyPoints = 0;
            }

            // On-time returns: up to 25 points.
            int onTimePoints = 0;
            var returnedRentals = rentals.Where(r => r.ReturnDate.HasValue).ToList();
            if (returnedRentals.Any())
            {
                var onTimeCount = returnedRentals.Count(r => r.ReturnDate.Value <= r.DueDate);
                var onTimeRate = (double)onTimeCount / returnedRentals.Count;
                onTimePoints = (int)Math.Round(onTimeRate * 25);
            }
            else if (rentals.Any())
            {
                onTimePoints = 12; // no returns yet, neutral
            }

            // Spend: up to 20 points.
            var totalSpend = rentals.Sum(r => r.TotalCost);
            var spendPoints = Math.Min(20, (int)(totalSpend / 5m));

            var score = Math.Min(100, frequencyPoints + recencyPoints + onTimePoints + spendPoints);

            string tier;
            if (score >= 80) tier = "Platinum";
            else if (score >= 60) tier = "Gold";
            else if (score >= 40) tier = "Silver";
            else tier = "Bronze";

            return new LoyaltyResult
            {
                CustomerId = customerId,
                CustomerName = name,
                Score = score,
                Tier = tier,
                Breakdown = new LoyaltyBreakdown
                {
                    FrequencyPoints = frequencyPoints,
                    RecencyPoints = recencyPoints,
                    OnTimePoints = onTimePoints,
                    SpendPoints = spendPoints
                }
            };
        }

        /// <inheritdoc />
        public IReadOnlyList<SeasonalTrend> GetSeasonalTrends()
        {
            var rentals = _rentalRepository.GetAll();
            var movies = _movieRepository.GetAll().ToDictionary(m => m.Id);

            return rentals
                .Where(r => movies.ContainsKey(r.MovieId) && movies[r.MovieId].Genre.HasValue)
                .GroupBy(r => new { Genre = movies[r.MovieId].Genre.Value, Month = r.RentalDate.Month })
                .Select(g => new SeasonalTrend
                {
                    Genre = g.Key.Genre,
                    Month = g.Key.Month,
                    Count = g.Count()
                })
                .OrderBy(t => t.Month)
                .ThenByDescending(t => t.Count)
                .ToList();
        }

        /// <inheritdoc />
        public RentalReport GenerateReport(ReportType type)
        {
            var report = new RentalReport
            {
                Type = type,
                GeneratedAt = _clock.Now
            };

            switch (type)
            {
                case ReportType.Summary:
                    report.Title = "Rental Summary Report";
                    report.Sections = GenerateSummaryReport();
                    break;
                case ReportType.Detailed:
                    report.Title = "Detailed Rental Report";
                    report.Sections = GenerateDetailedReport();
                    break;
                case ReportType.CustomerFocused:
                    report.Title = "Customer-Focused Report";
                    report.Sections = GenerateCustomerReport();
                    break;
                case ReportType.MovieFocused:
                    report.Title = "Movie-Focused Report";
                    report.Sections = GenerateMovieReport();
                    break;
            }

            return report;
        }

        /// <summary>
        /// Generates overview section with total/active/overdue/returned counts and revenue.
        /// Single-pass aggregation: computes all 6 metrics in one iteration
        /// instead of 5 separate LINQ passes (3× Count + 2× Sum).
        /// </summary>
        private List<ReportSection> GenerateSummaryReport()
        {
            return GenerateSummaryReport(_rentalRepository.GetAll());
        }

        /// <summary>
        /// Internal overload that accepts a pre-fetched rental list to avoid
        /// redundant GetAll() calls from GenerateDetailedReport.
        /// </summary>
        private static List<ReportSection> GenerateSummaryReport(IReadOnlyList<Rental> rentals)
        {
            var sections = new List<ReportSection>();

            int active = 0, overdue = 0, returned = 0;
            decimal totalRevenue = 0m, totalLateFees = 0m;

            foreach (var r in rentals)
            {
                switch (r.Status)
                {
                    case RentalStatus.Active: active++; break;
                    case RentalStatus.Overdue: overdue++; break;
                    case RentalStatus.Returned: returned++; break;
                }
                totalRevenue += r.TotalCost;
                totalLateFees += r.LateFee;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Total Rentals: {rentals.Count}");
            sb.AppendLine($"Active: {active}");
            sb.AppendLine($"Overdue: {overdue}");
            sb.AppendLine($"Returned: {returned}");
            sb.AppendLine($"Total Revenue: {totalRevenue:C}");
            sb.AppendLine($"Total Late Fees: {totalLateFees:C}");

            sections.Add(new ReportSection { Heading = "Overview", Content = sb.ToString() });
            return sections;
        }

        /// <summary>
        /// Extends the summary report with popular-times and retention sections.
        /// Shares the rental list with GenerateSummaryReport to avoid a redundant
        /// GetAll() call.
        /// </summary>
        private List<ReportSection> GenerateDetailedReport()
        {
            var rentals = _rentalRepository.GetAll();
            var sections = GenerateSummaryReport(rentals);

            // Popular times
            var popular = GetPopularTimes();
            var sb = new StringBuilder();
            if (popular.BusiestDay.HasValue)
                sb.AppendLine($"Busiest Day: {popular.BusiestDay}");
            if (popular.BusiestHour.HasValue)
                sb.AppendLine($"Busiest Hour: {popular.BusiestHour}:00");
            sections.Add(new ReportSection { Heading = "Popular Times", Content = sb.ToString() });

            // Retention
            var retention = GetRetentionMetrics();
            sb = new StringBuilder();
            sb.AppendLine($"Return Rate: {retention.ReturnRate:P1}");
            sb.AppendLine($"Repeat Customer Rate: {retention.RepeatRentalRate:P1}");
            sb.AppendLine($"Avg Gap Between Rentals: {retention.AverageGapDays:F1} days");
            sections.Add(new ReportSection { Heading = "Retention", Content = sb.ToString() });

            return sections;
        }

        /// <summary>
        /// Generates customer-centric report with total counts and top-5 by rental count.
        /// Uses dictionary lookup for customer names instead of O(C) FirstOrDefault per group.
        /// </summary>
        private List<ReportSection> GenerateCustomerReport()
        {
            var sections = new List<ReportSection>();
            var rentals = _rentalRepository.GetAll();
            var customers = _customerRepository.GetAll();

            var sb = new StringBuilder();
            sb.AppendLine($"Total Customers: {customers.Count}");
            var customersWithRentals = rentals.Select(r => r.CustomerId).Distinct().Count();
            sb.AppendLine($"Customers with Rentals: {customersWithRentals}");
            sections.Add(new ReportSection { Heading = "Customer Overview", Content = sb.ToString() });

            // Build O(1) lookup instead of O(C) FirstOrDefault per group
            var customerLookup = new Dictionary<int, Customer>();
            foreach (var c in customers)
                customerLookup[c.Id] = c;

            // Top customers by rental count
            var topCustomers = rentals.GroupBy(r => r.CustomerId)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .ToList();

            sb = new StringBuilder();
            foreach (var g in topCustomers)
            {
                var name = customerLookup.TryGetValue(g.Key, out var c) ? c.Name : "Unknown";
                sb.AppendLine($"{name}: {g.Count()} rentals, {g.Sum(r => r.TotalCost):C} spent");
            }
            sections.Add(new ReportSection { Heading = "Top Customers", Content = sb.ToString() });

            return sections;
        }

        /// <summary>
        /// Generates movie-centric report with inventory overview, top-5 movies, and seasonal trends.
        /// Uses dictionary lookup for movie names instead of O(M) FirstOrDefault per group.
        /// </summary>
        private List<ReportSection> GenerateMovieReport()
        {
            var sections = new List<ReportSection>();
            var rentals = _rentalRepository.GetAll();
            var movies = _movieRepository.GetAll();

            var sb = new StringBuilder();
            sb.AppendLine($"Total Movies: {movies.Count}");
            var rentedMovies = rentals.Select(r => r.MovieId).Distinct().Count();
            sb.AppendLine($"Movies Rented at Least Once: {rentedMovies}");
            sections.Add(new ReportSection { Heading = "Movie Overview", Content = sb.ToString() });

            // Build O(1) lookup instead of O(M) FirstOrDefault per group
            var movieLookup = new Dictionary<int, Movie>();
            foreach (var m in movies)
                movieLookup[m.Id] = m;

            // Top movies by rental count
            var topMovies = rentals.GroupBy(r => r.MovieId)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .ToList();

            sb = new StringBuilder();
            foreach (var g in topMovies)
            {
                var name = movieLookup.TryGetValue(g.Key, out var m) ? m.Name : "Unknown";
                sb.AppendLine($"{name}: {g.Count()} rentals, {g.Sum(r => r.TotalCost):C} revenue");
            }
            sections.Add(new ReportSection { Heading = "Top Movies", Content = sb.ToString() });

            // Seasonal trends
            var trends = GetSeasonalTrends();
            if (trends.Any())
            {
                sb = new StringBuilder();
                foreach (var t in trends.Take(10))
                    sb.AppendLine($"{t.Genre} - Month {t.Month}: {t.Count} rentals");
                sections.Add(new ReportSection { Heading = "Seasonal Trends", Content = sb.ToString() });
            }

            return sections;
        }
    }
}
