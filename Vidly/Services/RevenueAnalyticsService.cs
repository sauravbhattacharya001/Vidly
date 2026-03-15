using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Revenue analytics service providing financial reporting, trend analysis,
    /// period comparisons, and revenue forecasting for the video rental business.
    /// </summary>
    public class RevenueAnalyticsService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IClock _clock;

        public RevenueAnalyticsService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IClock clock = null)
        {
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _clock = clock ?? SystemClock.Instance;
        }

        /// <summary>
        /// Generate a comprehensive revenue report for a date range.
        /// </summary>
        public RevenueReport GetReport(DateTime periodStart, DateTime periodEnd, int topCount = 5)
        {
            if (periodEnd < periodStart)
                throw new ArgumentException("periodEnd must be after periodStart.");

            var allRentals = _rentalRepository.GetAll();
            var periodRentals = allRentals
                .Where(r => r.RentalDate >= periodStart && r.RentalDate <= periodEnd)
                .ToList();

            var movies = _movieRepository.GetAll().ToDictionary(m => m.Id);
            var customers = _customerRepository.GetAll().ToDictionary(c => c.Id);

            decimal totalRentalRevenue = 0m;
            decimal totalLateFees = 0m;
            int completed = 0;
            int active = 0;
            int overdue = 0;

            foreach (var r in periodRentals)
            {
                // TotalCost already includes LateFee (= days*DailyRate + LateFee),
                // so extract base rental revenue by subtracting LateFee.
                totalRentalRevenue += r.TotalCost - r.LateFee;
                totalLateFees += r.LateFee;

                if (r.Status == RentalStatus.Returned)
                    completed++;
                else if (r.Status == RentalStatus.Overdue)
                    overdue++;
                else
                    active++;
            }

            // TotalRevenue = base rental revenue + late fees (no double-counting)
            decimal totalRevenue = totalRentalRevenue + totalLateFees;
            int totalDays = Math.Max(1, (int)(periodEnd - periodStart).TotalDays);

            var report = new RevenueReport
            {
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                TotalRevenue = totalRevenue,
                RentalRevenue = totalRentalRevenue,
                LateFeeRevenue = totalLateFees,
                TotalRentals = periodRentals.Count,
                CompletedRentals = completed,
                ActiveRentals = active,
                OverdueRentals = overdue,
                AverageRevenuePerRental = periodRentals.Count > 0
                    ? totalRevenue / periodRentals.Count : 0m,
                AverageRevenuePerDay = totalRevenue / totalDays,
                RevenueByGenre = BuildGenreBreakdown(periodRentals, movies, totalRevenue),
                RevenueByMembership = BuildMembershipBreakdown(periodRentals, customers),
                MonthlyTrend = BuildMonthlyTrend(periodRentals),
                TopCustomers = BuildTopCustomers(periodRentals, topCount),
                TopMovies = BuildTopMovies(periodRentals, movies, topCount),
            };

            return report;
        }

        /// <summary>
        /// Get revenue report for the last N days from the given reference date.
        /// </summary>
        public RevenueReport GetRecentReport(int days, DateTime? asOf = null, int topCount = 5)
        {
            if (days <= 0)
                throw new ArgumentException("days must be positive.");

            var end = asOf ?? _clock.Now;
            var start = end.AddDays(-days);
            return GetReport(start, end, topCount);
        }

        /// <summary>
        /// Compare two time periods to identify trends and changes.
        /// </summary>
        public PeriodComparison ComparePeriods(
            DateTime currentStart, DateTime currentEnd,
            DateTime previousStart, DateTime previousEnd,
            int topCount = 5)
        {
            var current = GetReport(currentStart, currentEnd, topCount);
            var previous = GetReport(previousStart, previousEnd, topCount);

            decimal revChange = current.TotalRevenue - previous.TotalRevenue;
            double revChangePct = previous.TotalRevenue > 0
                ? (double)(revChange / previous.TotalRevenue) * 100.0
                : (current.TotalRevenue > 0 ? 100.0 : 0.0);

            int rentalChange = current.TotalRentals - previous.TotalRentals;
            double rentalChangePct = previous.TotalRentals > 0
                ? (double)rentalChange / previous.TotalRentals * 100.0
                : (current.TotalRentals > 0 ? 100.0 : 0.0);

            decimal avgChange = current.AverageRevenuePerRental - previous.AverageRevenuePerRental;

            return new PeriodComparison
            {
                Current = current,
                Previous = previous,
                RevenueChange = revChange,
                RevenueChangePercent = Math.Round(revChangePct, 2),
                RentalCountChange = rentalChange,
                RentalCountChangePercent = Math.Round(rentalChangePct, 2),
                AverageRevenueChange = avgChange,
            };
        }

        /// <summary>
        /// Compare month-over-month performance.
        /// </summary>
        public PeriodComparison CompareMonthOverMonth(int year, int month, int topCount = 5)
        {
            var currentStart = new DateTime(year, month, 1);
            var currentEnd = currentStart.AddMonths(1).AddDays(-1);
            var previousStart = currentStart.AddMonths(-1);
            var previousEnd = currentStart.AddDays(-1);

            return ComparePeriods(currentStart, currentEnd, previousStart, previousEnd, topCount);
        }

        /// <summary>
        /// Forecast future revenue based on historical trends using simple
        /// linear regression over monthly data.
        /// </summary>
        public RevenueForecast ForecastRevenue(int forecastDays, DateTime? asOf = null)
        {
            if (forecastDays <= 0)
                throw new ArgumentException("forecastDays must be positive.");

            var end = asOf ?? _clock.Now;
            var allRentals = _rentalRepository.GetAll()
                .Where(r => r.RentalDate <= end)
                .ToList();

            if (allRentals.Count == 0)
            {
                return new RevenueForecast
                {
                    ForecastStart = end,
                    ForecastEnd = end.AddDays(forecastDays),
                    ProjectedRevenue = 0m,
                    ProjectedRentals = 0,
                    ConfidenceLow = 0m,
                    ConfidenceHigh = 0m,
                    Method = "no_data",
                };
            }

            // Build monthly revenue data points for regression
            var monthly = allRentals
                .GroupBy(r => new { r.RentalDate.Year, r.RentalDate.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Revenue = g.Sum(r => r.TotalCost),
                    Count = g.Count(),
                })
                .OrderBy(m => m.Year)
                .ThenBy(m => m.Month)
                .ToList();

            if (monthly.Count < 2)
            {
                // Not enough data for regression — use average
                decimal avgDailyRevenue = allRentals.Sum(r => r.TotalCost);
                var firstDate = allRentals.Min(r => r.RentalDate);
                int totalHistDays = Math.Max(1, (int)(end - firstDate).TotalDays);
                avgDailyRevenue = avgDailyRevenue / totalHistDays;

                decimal projected = avgDailyRevenue * forecastDays;
                int avgDailyRentals = allRentals.Count / totalHistDays;

                return new RevenueForecast
                {
                    ForecastStart = end,
                    ForecastEnd = end.AddDays(forecastDays),
                    ProjectedRevenue = Math.Round(projected, 2),
                    ProjectedRentals = avgDailyRentals * forecastDays,
                    ConfidenceLow = Math.Round(projected * 0.7m, 2),
                    ConfidenceHigh = Math.Round(projected * 1.3m, 2),
                    Method = "average",
                };
            }

            // Simple linear regression: y = a + b*x
            int n = monthly.Count;
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            double sumRentals = 0;

            for (int i = 0; i < n; i++)
            {
                double x = i;
                double y = (double)monthly[i].Revenue;
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumX2 += x * x;
                sumRentals += monthly[i].Count;
            }

            double meanX = sumX / n;
            double meanY = sumY / n;
            double denom = sumX2 - n * meanX * meanX;

            double slope = denom != 0 ? (sumXY - n * meanX * meanY) / denom : 0;
            double intercept = meanY - slope * meanX;

            // Project forward: estimate monthly revenue for the forecast period
            double forecastMonths = forecastDays / 30.0;
            double nextMonthIndex = n;
            double totalProjected = 0;

            for (int i = 0; i < Math.Max(1, (int)Math.Ceiling(forecastMonths)); i++)
            {
                double monthRevenue = intercept + slope * (nextMonthIndex + i);
                if (monthRevenue < 0) monthRevenue = 0;
                totalProjected += monthRevenue;
            }

            // Adjust for partial month
            double fullMonths = Math.Floor(forecastMonths);
            double partialFraction = forecastMonths - fullMonths;
            if (partialFraction > 0 && fullMonths >= 1)
            {
                // Already included in the ceiling loop
                double lastMonth = intercept + slope * (nextMonthIndex + fullMonths);
                totalProjected -= lastMonth * (1 - partialFraction);
            }

            // Compute residual standard error for confidence interval
            double sse = 0;
            for (int i = 0; i < n; i++)
            {
                double predicted = intercept + slope * i;
                double actual = (double)monthly[i].Revenue;
                sse += (actual - predicted) * (actual - predicted);
            }
            double stdError = n > 2 ? Math.Sqrt(sse / (n - 2)) : 0;

            decimal projectedRevenue = (decimal)Math.Max(0, totalProjected);
            decimal confidenceMargin = (decimal)(stdError * 1.96 * Math.Sqrt(forecastMonths));
            double avgMonthlyRentals = sumRentals / n;

            return new RevenueForecast
            {
                ForecastStart = end,
                ForecastEnd = end.AddDays(forecastDays),
                ProjectedRevenue = Math.Round(projectedRevenue, 2),
                ProjectedRentals = (int)Math.Round(avgMonthlyRentals * forecastMonths),
                ConfidenceLow = Math.Round(Math.Max(0m, projectedRevenue - confidenceMargin), 2),
                ConfidenceHigh = Math.Round(projectedRevenue + confidenceMargin, 2),
                Method = "linear_regression",
            };
        }

        /// <summary>
        /// Get the single highest-revenue day in the given period.
        /// </summary>
        public KeyValuePair<DateTime, decimal> GetPeakRevenueDay(
            DateTime periodStart, DateTime periodEnd)
        {
            var rentals = _rentalRepository.GetAll()
                .Where(r => r.RentalDate >= periodStart && r.RentalDate <= periodEnd);

            var daily = rentals
                .GroupBy(r => r.RentalDate.Date)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.TotalCost));

            if (daily.Count == 0)
                return new KeyValuePair<DateTime, decimal>(periodStart, 0m);

            return daily.OrderByDescending(kv => kv.Value).First();
        }

        /// <summary>
        /// Get revenue by day-of-week for pattern analysis.
        /// </summary>
        public Dictionary<DayOfWeek, decimal> GetRevenueByDayOfWeek(
            DateTime periodStart, DateTime periodEnd)
        {
            var result = new Dictionary<DayOfWeek, decimal>();
            for (int d = 0; d < 7; d++)
                result[(DayOfWeek)d] = 0m;

            var rentals = _rentalRepository.GetAll()
                .Where(r => r.RentalDate >= periodStart && r.RentalDate <= periodEnd);

            foreach (var r in rentals)
            {
                result[r.RentalDate.DayOfWeek] += r.TotalCost;
            }

            return result;
        }

        // ── Private Helpers ──────────────────────────────────────────

        private List<GenreRevenue> BuildGenreBreakdown(
            List<Rental> rentals,
            Dictionary<int, Movie> movies,
            decimal totalRevenue)
        {
            var genreGroups = new Dictionary<Genre, List<Rental>>();

            foreach (var r in rentals)
            {
                Movie movie;
                if (!movies.TryGetValue(r.MovieId, out movie) || !movie.Genre.HasValue)
                    continue;

                Genre g = movie.Genre.Value;
                List<Rental> list;
                if (!genreGroups.TryGetValue(g, out list))
                {
                    list = new List<Rental>();
                    genreGroups[g] = list;
                }
                list.Add(r);
            }

            var result = new List<GenreRevenue>();
            foreach (var kv in genreGroups.OrderByDescending(kv => kv.Value.Sum(r => r.TotalCost)))
            {
                decimal genreRev = kv.Value.Sum(r => r.TotalCost);
                result.Add(new GenreRevenue
                {
                    Genre = kv.Key,
                    Revenue = genreRev,
                    RentalCount = kv.Value.Count,
                    AverageRevenue = kv.Value.Count > 0 ? genreRev / kv.Value.Count : 0m,
                    SharePercent = totalRevenue > 0
                        ? Math.Round((double)(genreRev / totalRevenue) * 100, 2) : 0,
                });
            }

            return result;
        }

        private List<MembershipRevenue> BuildMembershipBreakdown(
            List<Rental> rentals,
            Dictionary<int, Customer> customers)
        {
            var groups = new Dictionary<MembershipType, MembershipAccumulator>();

            foreach (var r in rentals)
            {
                Customer cust;
                if (!customers.TryGetValue(r.CustomerId, out cust))
                    continue;

                MembershipAccumulator acc;
                if (!groups.TryGetValue(cust.MembershipType, out acc))
                {
                    acc = new MembershipAccumulator();
                    groups[cust.MembershipType] = acc;
                }
                acc.Revenue += r.TotalCost;
                acc.RentalCount++;
                acc.CustomerIds.Add(cust.Id);
            }

            var result = new List<MembershipRevenue>();
            foreach (var kv in groups.OrderByDescending(kv => kv.Value.Revenue))
            {
                result.Add(new MembershipRevenue
                {
                    Membership = kv.Key,
                    Revenue = kv.Value.Revenue,
                    CustomerCount = kv.Value.CustomerIds.Count,
                    RentalCount = kv.Value.RentalCount,
                    AverageRevenuePerCustomer = kv.Value.CustomerIds.Count > 0
                        ? kv.Value.Revenue / kv.Value.CustomerIds.Count : 0m,
                });
            }

            return result;
        }

        private List<MonthlyRevenue> BuildMonthlyTrend(List<Rental> rentals)
        {
            var monthly = rentals
                .GroupBy(r => new { r.RentalDate.Year, r.RentalDate.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Revenue = g.Sum(r => r.TotalCost),
                    Count = g.Count(),
                })
                .OrderBy(m => m.Year)
                .ThenBy(m => m.Month)
                .ToList();

            var result = new List<MonthlyRevenue>();
            decimal prevRevenue = 0m;

            foreach (var m in monthly)
            {
                double growth = prevRevenue > 0
                    ? Math.Round((double)((m.Revenue - prevRevenue) / prevRevenue) * 100, 2)
                    : 0.0;

                result.Add(new MonthlyRevenue
                {
                    Year = m.Year,
                    Month = m.Month,
                    Revenue = m.Revenue,
                    RentalCount = m.Count,
                    GrowthPercent = (decimal)growth,
                });

                prevRevenue = m.Revenue;
            }

            return result;
        }

        private List<TopCustomerRevenue> BuildTopCustomers(
            List<Rental> rentals, int topCount)
        {
            var customers = _customerRepository.GetAll().ToDictionary(c => c.Id);
            return rentals
                .GroupBy(r => r.CustomerId)
                .Select(g =>
                {
                    Customer cust;
                    customers.TryGetValue(g.Key, out cust);
                    return new TopCustomerRevenue
                    {
                        CustomerId = g.Key,
                        CustomerName = cust != null ? cust.Name : ("Customer " + g.Key),
                        TotalRevenue = g.Sum(r => r.TotalCost),
                        RentalCount = g.Count(),
                        AverageSpend = g.Count() > 0
                            ? g.Sum(r => r.TotalCost) / g.Count() : 0m,
                    };
                })
                .OrderByDescending(c => c.TotalRevenue)
                .Take(topCount)
                .ToList();
        }

        private List<TopMovieRevenue> BuildTopMovies(
            List<Rental> rentals,
            Dictionary<int, Movie> movies,
            int topCount)
        {
            return rentals
                .GroupBy(r => r.MovieId)
                .Select(g =>
                {
                    Movie movie;
                    movies.TryGetValue(g.Key, out movie);
                    return new TopMovieRevenue
                    {
                        MovieId = g.Key,
                        MovieName = movie != null ? movie.Name : ("Movie " + g.Key),
                        TotalRevenue = g.Sum(r => r.TotalCost),
                        RentalCount = g.Count(),
                        AverageDailyRate = g.Count() > 0
                            ? g.Average(r => r.DailyRate) : 0m,
                    };
                })
                .OrderByDescending(m => m.TotalRevenue)
                .Take(topCount)
                .ToList();
        }

        /// <summary>
        /// Internal accumulator for membership breakdowns.
        /// </summary>
        private class MembershipAccumulator
        {
            public decimal Revenue;
            public int RentalCount;
            public HashSet<int> CustomerIds = new HashSet<int>();
        }
    }
}
