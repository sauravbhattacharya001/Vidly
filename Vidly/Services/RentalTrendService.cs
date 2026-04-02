using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Analyses rental patterns over time: day-of-week volumes, genre popularity
    /// trends, monthly volumes, customer retention cohorts, and peak/quiet periods.
    /// Gives store managers actionable data to optimise staffing, inventory, and
    /// promotions.
    /// </summary>
    public class RentalTrendService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly IClock _clock;

        /// <summary>
        /// Minimum daily average multiplier to qualify as a peak period.
        /// A 7-day window is "peak" when its daily average exceeds
        /// the overall daily average by this factor.
        /// </summary>
        public const decimal PeakThreshold = 1.5m;

        /// <summary>
        /// Maximum daily average multiplier to qualify as a quiet period.
        /// </summary>
        public const decimal QuietThreshold = 0.5m;

        public RentalTrendService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            IClock clock = null)
        {
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _clock = clock ?? new SystemClock();
        }

        /// <summary>
        /// Generate a comprehensive trend report for the given date range.
        /// </summary>
        public RentalTrendReport Analyze(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("'to' must be on or after 'from'.");

            var allRentals = _rentalRepository.GetAll();
            var movies = _movieRepository.GetAll().ToDictionary(m => m.Id);

            var rentals = allRentals
                .Where(r => r.RentalDate >= from && r.RentalDate <= to)
                .ToList();

            var totalDays = Math.Max(1, (to - from).Days + 1);

            var report = new RentalTrendReport
            {
                AnalysisStart = from,
                AnalysisEnd = to,
                TotalRentals = rentals.Count,
                TotalRevenue = rentals.Sum(r => r.TotalCost),
                AverageRentalsPerDay = Math.Round((decimal)rentals.Count / totalDays, 2)
            };

            // Day-of-week breakdown
            report.DayOfWeekBreakdown = BuildDayOfWeekBreakdown(rentals);
            if (report.DayOfWeekBreakdown.Count > 0)
            {
                report.BusiestDay = report.DayOfWeekBreakdown
                    .OrderByDescending(d => d.RentalCount).First().Day;
                report.QuietestDay = report.DayOfWeekBreakdown
                    .OrderBy(d => d.RentalCount).First().Day;
            }

            // Genre trends
            report.GenreTrends = BuildGenreTrends(rentals, movies, from, to);
            report.TopGenre = report.GenreTrends.Count > 0
                ? (Genre?)report.GenreTrends
                    .OrderByDescending(g => g.RentalCount).First().Genre
                : null;

            // Monthly volumes
            report.MonthlyVolumes = BuildMonthlyVolumes(rentals);

            // Retention cohorts
            report.RetentionCohorts = BuildRetentionCohorts(allRentals, from, to);

            // Peak/quiet periods
            report.PeakPeriods = BuildPeakPeriods(rentals, from, to, report.AverageRentalsPerDay);

            return report;
        }

        /// <summary>
        /// Get day-of-week breakdown for a date range.
        /// </summary>
        public List<DayOfWeekBreakdown> GetDayOfWeekBreakdown(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("'to' must be on or after 'from'.");

            var rentals = _rentalRepository.GetAll()
                .Where(r => r.RentalDate >= from && r.RentalDate <= to)
                .ToList();
            return BuildDayOfWeekBreakdown(rentals);
        }

        /// <summary>
        /// Get genre trends for a date range.
        /// </summary>
        public List<GenrePopularity> GetGenreTrends(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("'to' must be on or after 'from'.");

            var rentals = _rentalRepository.GetAll()
                .Where(r => r.RentalDate >= from && r.RentalDate <= to)
                .ToList();
            var movies = _movieRepository.GetAll().ToDictionary(m => m.Id);
            return BuildGenreTrends(rentals, movies, from, to);
        }

        /// <summary>
        /// Get monthly volume data for a date range.
        /// </summary>
        public List<MonthlyVolume> GetMonthlyVolumes(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("'to' must be on or after 'from'.");

            var rentals = _rentalRepository.GetAll()
                .Where(r => r.RentalDate >= from && r.RentalDate <= to)
                .ToList();
            return BuildMonthlyVolumes(rentals);
        }

        /// <summary>
        /// Get customer retention cohorts.
        /// </summary>
        public List<RetentionCohort> GetRetentionCohorts(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("'to' must be on or after 'from'.");

            var allRentals = _rentalRepository.GetAll();
            return BuildRetentionCohorts(allRentals, from, to);
        }

        /// <summary>
        /// Generate a text report summarizing trends.
        /// </summary>
        public string GenerateTextReport(DateTime from, DateTime to)
        {
            var report = Analyze(from, to);
            var sb = new StringBuilder();

            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine("         RENTAL TREND ANALYSIS REPORT      ");
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine($"Period: {report.AnalysisStart:yyyy-MM-dd} to {report.AnalysisEnd:yyyy-MM-dd}");
            sb.AppendLine($"Total Rentals: {report.TotalRentals}");
            sb.AppendLine($"Total Revenue: {report.TotalRevenue:C}");
            sb.AppendLine($"Avg Rentals/Day: {report.AverageRentalsPerDay}");
            sb.AppendLine();

            // Day-of-week
            if (report.DayOfWeekBreakdown.Count > 0)
            {
                sb.AppendLine("── Day-of-Week Breakdown ──────────────────");
                foreach (var d in report.DayOfWeekBreakdown.OrderByDescending(x => x.RentalCount))
                {
                    var bar = new string('█', Math.Max(1, (int)(d.Percentage / 5)));
                    sb.AppendLine($"  {d.Day,-10} {d.RentalCount,4} ({d.Percentage,5:F1}%) {bar}  avg ${d.AverageRevenue:F2}");
                }
                sb.AppendLine($"  Busiest: {report.BusiestDay}  |  Quietest: {report.QuietestDay}");
                sb.AppendLine();
            }

            // Genre trends
            if (report.GenreTrends.Count > 0)
            {
                sb.AppendLine("── Genre Trends ──────────────────────────");
                foreach (var g in report.GenreTrends.OrderByDescending(x => x.RentalCount))
                {
                    var arrow = g.Direction > 0 ? "▲" : g.Direction < 0 ? "▼" : "─";
                    sb.AppendLine($"  {g.Genre,-12} {g.RentalCount,4} ({g.Percentage,5:F1}%) {arrow}  {g.UniqueCustomers} customers  ${g.TotalRevenue:F2}");
                }
                sb.AppendLine();
            }

            // Monthly volumes
            if (report.MonthlyVolumes.Count > 0)
            {
                sb.AppendLine("── Monthly Volume ────────────────────────");
                foreach (var m in report.MonthlyVolumes)
                {
                    var change = m.ChangePercent >= 0 ? $"+{m.ChangePercent}%" : $"{m.ChangePercent}%";
                    sb.AppendLine($"  {m.Year}-{m.Month:D2}  {m.RentalCount,4} rentals  ${m.Revenue:F2}  {m.UniqueCustomers} customers  {change}");
                }
                sb.AppendLine();
            }

            // Retention
            if (report.RetentionCohorts.Count > 0)
            {
                sb.AppendLine("── Customer Retention Cohorts ─────────────");
                foreach (var c in report.RetentionCohorts)
                {
                    sb.AppendLine($"  {c.Year}-{c.Month:D2}  {c.CohortSize} new  30d: {c.RetentionRate30:F0}%  90d: {c.RetentionRate90:F0}%");
                }
                sb.AppendLine();
            }

            // Peak/quiet
            if (report.PeakPeriods.Count > 0)
            {
                sb.AppendLine("── Peak & Quiet Periods ──────────────────");
                foreach (var p in report.PeakPeriods)
                {
                    var label = p.IsPeak ? "PEAK " : "QUIET";
                    sb.AppendLine($"  {label}  {p.StartDate:MM-dd} to {p.EndDate:MM-dd}  {p.RentalCount} rentals  avg {p.DailyAverage:F1}/day");
                }
            }

            sb.AppendLine("═══════════════════════════════════════════");
            return sb.ToString();
        }

        /// <summary>
        /// Export trend data as JSON-formatted string.
        /// </summary>
        public string ExportJson(DateTime from, DateTime to)
        {
            var report = Analyze(from, to);
            return Vidly.Utilities.JsonSerializer.Serialize(report);
        }

        // ────────── Private helpers ──────────

        private List<DayOfWeekBreakdown> BuildDayOfWeekBreakdown(List<Rental> rentals)
        {
            if (rentals.Count == 0)
                return new List<DayOfWeekBreakdown>();

            var groups = new Dictionary<DayOfWeek, List<Rental>>();
            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
                groups[day] = new List<Rental>();

            foreach (var r in rentals)
                groups[r.RentalDate.DayOfWeek].Add(r);

            var result = new List<DayOfWeekBreakdown>();
            foreach (var kvp in groups)
            {
                var count = kvp.Value.Count;
                result.Add(new DayOfWeekBreakdown
                {
                    Day = kvp.Key,
                    RentalCount = count,
                    Percentage = rentals.Count > 0
                        ? Math.Round(100m * count / rentals.Count, 1)
                        : 0,
                    AverageRevenue = count > 0
                        ? Math.Round(kvp.Value.Sum(r => r.TotalCost) / count, 2)
                        : 0
                });
            }

            return result;
        }

        private List<GenrePopularity> BuildGenreTrends(
            List<Rental> rentals,
            Dictionary<int, Movie> movies,
            DateTime from, DateTime to)
        {
            if (rentals.Count == 0)
                return new List<GenrePopularity>();

            // Group rentals by genre
            var genreRentals = new Dictionary<Genre, List<Rental>>();
            foreach (var r in rentals)
            {
                if (movies.TryGetValue(r.MovieId, out var movie) && movie.Genre.HasValue)
                {
                    if (!genreRentals.ContainsKey(movie.Genre.Value))
                        genreRentals[movie.Genre.Value] = new List<Rental>();
                    genreRentals[movie.Genre.Value].Add(r);
                }
            }

            // Determine direction by comparing first half vs second half
            var midpoint = from.AddDays((to - from).TotalDays / 2);
            var totalWithGenre = genreRentals.Values.Sum(g => g.Count);

            var result = new List<GenrePopularity>();
            foreach (var kvp in genreRentals)
            {
                var firstHalf = kvp.Value.Count(r => r.RentalDate < midpoint);
                var secondHalf = kvp.Value.Count(r => r.RentalDate >= midpoint);

                int direction = 0;
                if (firstHalf > 0 && secondHalf > 0)
                {
                    var ratio = (decimal)secondHalf / firstHalf;
                    if (ratio > 1.2m) direction = 1;
                    else if (ratio < 0.8m) direction = -1;
                }
                else if (firstHalf == 0 && secondHalf > 0)
                {
                    direction = 1;
                }
                else if (firstHalf > 0 && secondHalf == 0)
                {
                    direction = -1;
                }

                result.Add(new GenrePopularity
                {
                    Genre = kvp.Key,
                    RentalCount = kvp.Value.Count,
                    Percentage = totalWithGenre > 0
                        ? Math.Round(100m * kvp.Value.Count / totalWithGenre, 1)
                        : 0,
                    TotalRevenue = kvp.Value.Sum(r => r.TotalCost),
                    UniqueCustomers = kvp.Value.Select(r => r.CustomerId).Distinct().Count(),
                    Direction = direction
                });
            }

            return result.OrderByDescending(g => g.RentalCount).ToList();
        }

        private List<MonthlyVolume> BuildMonthlyVolumes(List<Rental> rentals)
        {
            if (rentals.Count == 0)
                return new List<MonthlyVolume>();

            var grouped = rentals
                .GroupBy(r => new { r.RentalDate.Year, r.RentalDate.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .ToList();

            var result = new List<MonthlyVolume>();
            int? previousCount = null;

            foreach (var g in grouped)
            {
                var count = g.Count();
                int changePercent = 0;
                if (previousCount.HasValue && previousCount.Value > 0)
                {
                    changePercent = (int)Math.Round(
                        100m * (count - previousCount.Value) / previousCount.Value);
                }

                result.Add(new MonthlyVolume
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    RentalCount = count,
                    Revenue = g.Sum(r => r.TotalCost),
                    UniqueCustomers = g.Select(r => r.CustomerId).Distinct().Count(),
                    ChangePercent = changePercent
                });

                previousCount = count;
            }

            return result;
        }

        private List<RetentionCohort> BuildRetentionCohorts(
            IReadOnlyList<Rental> allRentals, DateTime from, DateTime to)
        {
            if (allRentals.Count == 0)
                return new List<RetentionCohort>();

            // Find each customer's first rental date
            var firstRental = new Dictionary<int, DateTime>();
            foreach (var r in allRentals)
            {
                if (!firstRental.ContainsKey(r.CustomerId) ||
                    r.RentalDate < firstRental[r.CustomerId])
                {
                    firstRental[r.CustomerId] = r.RentalDate;
                }
            }

            // Group by first-rental month (within analysis window)
            var cohortCustomers = firstRental
                .Where(kv => kv.Value >= from && kv.Value <= to)
                .GroupBy(kv => new { kv.Value.Year, kv.Value.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month);

            // Build per-customer rental list for retention checking
            var customerRentals = new Dictionary<int, List<DateTime>>();
            foreach (var r in allRentals)
            {
                if (!customerRentals.ContainsKey(r.CustomerId))
                    customerRentals[r.CustomerId] = new List<DateTime>();
                customerRentals[r.CustomerId].Add(r.RentalDate);
            }

            var result = new List<RetentionCohort>();
            foreach (var cohort in cohortCustomers)
            {
                var cohortSize = cohort.Count();
                int returned30 = 0, returned90 = 0;

                foreach (var kv in cohort)
                {
                    var customerId = kv.Key;
                    var firstDate = kv.Value;

                    if (customerRentals.TryGetValue(customerId, out var dates))
                    {
                        // Check if they rented again within 30/90 days
                        var hasReturn30 = dates.Any(d =>
                            d > firstDate && (d - firstDate).TotalDays <= 30);
                        var hasReturn90 = dates.Any(d =>
                            d > firstDate && (d - firstDate).TotalDays <= 90);

                        if (hasReturn30) returned30++;
                        if (hasReturn90) returned90++;
                    }
                }

                result.Add(new RetentionCohort
                {
                    Year = cohort.Key.Year,
                    Month = cohort.Key.Month,
                    CohortSize = cohortSize,
                    ReturnedWithin30Days = returned30,
                    ReturnedWithin90Days = returned90,
                    RetentionRate30 = cohortSize > 0
                        ? Math.Round(100m * returned30 / cohortSize, 1)
                        : 0,
                    RetentionRate90 = cohortSize > 0
                        ? Math.Round(100m * returned90 / cohortSize, 1)
                        : 0
                });
            }

            return result;
        }

        private List<PeakPeriod> BuildPeakPeriods(
            List<Rental> rentals, DateTime from, DateTime to, decimal overallAvg)
        {
            if (rentals.Count == 0 || overallAvg == 0)
                return new List<PeakPeriod>();

            var totalDays = (to - from).Days + 1;
            if (totalDays < 7)
                return new List<PeakPeriod>();

            // Count rentals per day
            var dailyCounts = new Dictionary<DateTime, int>();
            foreach (var r in rentals)
            {
                var date = r.RentalDate.Date;
                if (!dailyCounts.ContainsKey(date))
                    dailyCounts[date] = 0;
                dailyCounts[date]++;
            }

            // Sliding 7-day window
            var periods = new List<PeakPeriod>();
            var windowStart = from.Date;

            while (windowStart.AddDays(6) <= to.Date)
            {
                int windowCount = 0;
                for (int i = 0; i < 7; i++)
                {
                    var day = windowStart.AddDays(i);
                    if (dailyCounts.TryGetValue(day, out var count))
                        windowCount += count;
                }

                var dailyAvg = (decimal)windowCount / 7;

                if (dailyAvg >= overallAvg * PeakThreshold)
                {
                    periods.Add(new PeakPeriod
                    {
                        StartDate = windowStart,
                        EndDate = windowStart.AddDays(6),
                        RentalCount = windowCount,
                        DailyAverage = Math.Round(dailyAvg, 1),
                        IsPeak = true
                    });
                }
                else if (dailyAvg <= overallAvg * QuietThreshold && dailyAvg >= 0)
                {
                    periods.Add(new PeakPeriod
                    {
                        StartDate = windowStart,
                        EndDate = windowStart.AddDays(6),
                        RentalCount = windowCount,
                        DailyAverage = Math.Round(dailyAvg, 1),
                        IsPeak = false
                    });
                }

                windowStart = windowStart.AddDays(7);
            }

            return periods;
        }
    }
}
