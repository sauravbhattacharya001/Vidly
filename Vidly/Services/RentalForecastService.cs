using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Forecasts rental demand using historical patterns. Analyzes day-of-week
    /// trends, seasonal patterns, genre popularity cycles, and per-movie velocity
    /// to predict future demand and help with inventory planning.
    /// </summary>
    public class RentalForecastService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly IClock _clock;

        public RentalForecastService(
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

        // ── Day-of-Week Analysis ─────────────────────────────────────

        /// <summary>
        /// Analyzes rental frequency by day of week from historical data.
        /// Returns a distribution showing which days are busiest.
        /// </summary>
        public DayOfWeekDistribution GetDayOfWeekDistribution()
        {
            var rentals = _rentalRepository.GetAll();
            if (rentals.Count == 0)
                return DayOfWeekDistribution.Empty();

            var grouped = rentals
                .GroupBy(r => r.RentalDate.DayOfWeek)
                .ToDictionary(g => g.Key, g => g.Count());

            var total = rentals.Count;
            var dayCounts = new Dictionary<DayOfWeek, DayStats>();

            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            {
                var count = grouped.TryGetValue(day, out var _v1) ? _v1 : 0;
                dayCounts[day] = new DayStats
                {
                    Day = day,
                    Count = count,
                    Percentage = total > 0 ? Math.Round((double)count / total * 100, 1) : 0
                };
            }

            var peakDay = dayCounts.OrderByDescending(d => d.Value.Count).First().Key;
            var slowDay = dayCounts.OrderBy(d => d.Value.Count).First().Key;

            return new DayOfWeekDistribution
            {
                Days = dayCounts,
                PeakDay = peakDay,
                SlowDay = slowDay,
                TotalRentals = total
            };
        }

        // ── Monthly Trends ───────────────────────────────────────────

        /// <summary>
        /// Analyzes rental volume by month to identify seasonal patterns.
        /// </summary>
        public IReadOnlyList<MonthlyTrend> GetMonthlyTrends()
        {
            var rentals = _rentalRepository.GetAll();
            if (rentals.Count == 0)
                return new List<MonthlyTrend>();

            return rentals
                .GroupBy(r => new { r.RentalDate.Year, r.RentalDate.Month })
                .OrderBy(g => g.Key.Year)
                .ThenBy(g => g.Key.Month)
                .Select(g =>
                {
                    var revenue = g.Sum(r => r.TotalCost);
                    return new MonthlyTrend
                    {
                        Year = g.Key.Year,
                        Month = g.Key.Month,
                        MonthName = CultureInfo.InvariantCulture.DateTimeFormat
                            .GetAbbreviatedMonthName(g.Key.Month),
                        RentalCount = g.Count(),
                        Revenue = revenue,
                        AverageRevenuePerRental = g.Count() > 0
                            ? Math.Round(revenue / g.Count(), 2) : 0
                    };
                })
                .ToList();
        }

        // ── Genre Popularity ─────────────────────────────────────────

        /// <summary>
        /// Ranks genres by rental frequency and calculates momentum
        /// (trending up or down based on recent vs older rentals).
        /// </summary>
        public IReadOnlyList<GenrePopularity> GetGenrePopularity(int recentDays = 30)
        {
            if (recentDays <= 0)
                throw new ArgumentOutOfRangeException(nameof(recentDays),
                    "Recent days must be positive.");

            var rentals = _rentalRepository.GetAll();
            var movies = _movieRepository.GetAll().ToDictionary(m => m.Id, m => m);

            if (rentals.Count == 0)
                return new List<GenrePopularity>();

            var cutoff = _clock.Today.AddDays(-recentDays);
            var totalRentals = rentals.Count;

            var rentalsByGenre = rentals
                .Where(r => movies.ContainsKey(r.MovieId) && movies[r.MovieId].Genre.HasValue)
                .GroupBy(r => movies[r.MovieId].Genre.Value);

            // Pre-compute the overall date span once. Previously rentals.Max()
            // and rentals.Min() were called inside the per-genre loop, making
            // each O(N) — total O(G*N) where G = number of genres. Now O(N) once.
            var minDate = rentals[0].RentalDate;
            var maxDate = rentals[0].RentalDate;
            for (int i = 1; i < rentals.Count; i++)
            {
                if (rentals[i].RentalDate < minDate) minDate = rentals[i].RentalDate;
                if (rentals[i].RentalDate > maxDate) maxDate = rentals[i].RentalDate;
            }
            var overallSpanDays = (maxDate - minDate).TotalDays;

            var results = new List<GenrePopularity>();
            foreach (var group in rentalsByGenre)
            {
                var all = group.ToList();
                var recent = all.Count(r => r.RentalDate >= cutoff);

                double momentum;
                if (all.Count == recent && recent > 0)
                    momentum = 2.0;
                else if (recent == 0)
                    momentum = 0.0;
                else
                {
                    var recentRate = (double)recent / recentDays;
                    var overallRate = overallSpanDays > 0 ? (double)all.Count / overallSpanDays : 1.0;
                    momentum = overallRate > 0 ? Math.Round(recentRate / overallRate, 2) : 0;
                }

                results.Add(new GenrePopularity
                {
                    Genre = group.Key,
                    TotalRentals = all.Count,
                    RecentRentals = recent,
                    SharePercent = Math.Round((double)all.Count / totalRentals * 100, 1),
                    Momentum = momentum,
                    Trend = momentum > 1.2 ? TrendDirection.Rising
                          : momentum < 0.8 ? TrendDirection.Declining
                          : TrendDirection.Stable
                });
            }

            return results.OrderByDescending(g => g.TotalRentals).ToList();
        }

        // ── Movie Velocity ───────────────────────────────────────────

        /// <summary>
        /// Calculates rental velocity for each movie — how frequently it gets
        /// rented. Useful for identifying hot titles and dead stock.
        /// </summary>
        public IReadOnlyList<MovieVelocity> GetMovieVelocity(int topN = 10)
        {
            if (topN < 0)
                throw new ArgumentOutOfRangeException(nameof(topN),
                    "topN must be non-negative.");

            var rentals = _rentalRepository.GetAll();
            var movies = _movieRepository.GetAll().ToDictionary(m => m.Id, m => m);

            if (rentals.Count == 0)
                return new List<MovieVelocity>();

            var results = rentals
                .GroupBy(r => r.MovieId)
                .Select(g =>
                {
                    var movieRentals = g.OrderBy(r => r.RentalDate).ToList();
                    var first = movieRentals.First().RentalDate;
                    var last = movieRentals.Last().RentalDate;
                    var spanDays = Math.Max(1, (last - first).TotalDays);
                    var movie = movies.TryGetValue(g.Key, out var _v2) ? _v2 : null;

                    double avgGap = 0;
                    if (movieRentals.Count > 1)
                    {
                        var gaps = new List<double>();
                        for (int i = 1; i < movieRentals.Count; i++)
                            gaps.Add((movieRentals[i].RentalDate -
                                      movieRentals[i - 1].RentalDate).TotalDays);
                        avgGap = gaps.Average();
                    }

                    return new MovieVelocity
                    {
                        MovieId = g.Key,
                        MovieName = movie?.Name ?? $"Movie #{g.Key}",
                        Genre = movie?.Genre,
                        TotalRentals = movieRentals.Count,
                        FirstRental = first,
                        LastRental = last,
                        RentalsPerMonth = spanDays >= 30
                            ? Math.Round(movieRentals.Count / (spanDays / 30.0), 2)
                            : movieRentals.Count,
                        AverageDaysBetweenRentals = Math.Round(avgGap, 1),
                        DaysSinceLastRental = (int)(_clock.Today - last).TotalDays
                    };
                })
                .OrderByDescending(v => v.RentalsPerMonth)
                .ToList();

            return topN > 0 ? results.Take(topN).ToList() : results;
        }

        // ── Demand Forecast ──────────────────────────────────────────

        /// <summary>
        /// Generates a simple demand forecast for the next N days based on
        /// historical day-of-week patterns and recent trend multiplier.
        /// </summary>
        public IReadOnlyList<DailyForecast> ForecastDemand(
            int days = 7, DateTime? startDate = null)
        {
            if (days <= 0 || days > 365)
                throw new ArgumentOutOfRangeException(nameof(days),
                    "Days must be between 1 and 365.");

            var start = startDate ?? _clock.Today.AddDays(1);
            var rentals = _rentalRepository.GetAll();

            if (rentals.Count == 0)
                return Enumerable.Range(0, days)
                    .Select(i => new DailyForecast
                    {
                        Date = start.AddDays(i),
                        DayOfWeek = start.AddDays(i).DayOfWeek,
                        PredictedRentals = 0,
                        Confidence = 0
                    })
                    .ToList();

            // Single pass: compute min/max dates, day-of-week counts,
            // and recent-30-day count simultaneously.  Previously this
            // was 4 separate O(N) passes (Min, Max, GroupBy, Count).
            var minDate = rentals[0].RentalDate;
            var maxDate = rentals[0].RentalDate;
            var dayCounts = new int[7];
            var recentCutoff = _clock.Today.AddDays(-30);
            var last30 = 0;

            for (int i = 0; i < rentals.Count; i++)
            {
                var rd = rentals[i].RentalDate;
                if (rd < minDate) minDate = rd;
                if (rd > maxDate) maxDate = rd;
                dayCounts[(int)rd.DayOfWeek]++;
                if (rd >= recentCutoff) last30++;
            }

            var dateRange = maxDate - minDate;
            var totalWeeks = Math.Max(1, dateRange.TotalDays / 7.0);

            var dayAvgs = new Dictionary<DayOfWeek, double>();
            for (int d = 0; d < 7; d++)
            {
                if (dayCounts[d] > 0)
                    dayAvgs[(DayOfWeek)d] = dayCounts[d] / totalWeeks;
            }

            var overallDailyAvg = rentals.Count / Math.Max(1, dateRange.TotalDays);
            var recentDailyAvg = last30 / 30.0;
            var trendMultiplier = overallDailyAvg > 0
                ? recentDailyAvg / overallDailyAvg : 1.0;

            var baseConfidence = Math.Min(1.0, rentals.Count / 50.0);

            var forecasts = new List<DailyForecast>();
            for (int i = 0; i < days; i++)
            {
                var date = start.AddDays(i);
                var dayAvg = dayAvgs.TryGetValue(date.DayOfWeek, out var _v3) ? _v3 : 0;
                var predicted = dayAvg * trendMultiplier;
                var horizonPenalty = 1.0 - (i * 0.02);

                forecasts.Add(new DailyForecast
                {
                    Date = date,
                    DayOfWeek = date.DayOfWeek,
                    PredictedRentals = Math.Round(predicted, 1),
                    Confidence = Math.Round(Math.Max(0, baseConfidence * horizonPenalty), 2),
                    TrendMultiplier = Math.Round(trendMultiplier, 2)
                });
            }

            return forecasts;
        }

        // ── Inventory Recommendations ────────────────────────────────

        /// <summary>
        /// Generates inventory recommendations: which movies to stock more of,
        /// which to retire, and which genres need more titles.
        /// </summary>
        public InventoryRecommendation GetInventoryRecommendations()
        {
            var velocity = GetMovieVelocity(0);
            var genrePop = GetGenrePopularity();
            var movies = _movieRepository.GetAll();

            var highDemand = velocity
                .Where(v => v.RentalsPerMonth >= 2 && v.DaysSinceLastRental <= 30)
                .OrderByDescending(v => v.RentalsPerMonth)
                .Take(5)
                .Select(v => new InventoryMovieRecommendation
                {
                    MovieId = v.MovieId,
                    MovieName = v.MovieName,
                    Reason = $"High velocity ({v.RentalsPerMonth}/month), " +
                             $"last rented {v.DaysSinceLastRental} days ago",
                    Action = RecommendedAction.StockMore
                })
                .ToList();

            var deadStock = velocity
                .Where(v => v.DaysSinceLastRental >= 90)
                .OrderByDescending(v => v.DaysSinceLastRental)
                .Take(5)
                .Select(v => new InventoryMovieRecommendation
                {
                    MovieId = v.MovieId,
                    MovieName = v.MovieName,
                    Reason = $"Not rented in {v.DaysSinceLastRental} days",
                    Action = RecommendedAction.ConsiderRetiring
                })
                .ToList();

            var rentedIds = new HashSet<int>(velocity.Select(v => v.MovieId));
            var neverRented = movies
                .Where(m => !rentedIds.Contains(m.Id))
                .Select(m => new InventoryMovieRecommendation
                {
                    MovieId = m.Id,
                    MovieName = m.Name,
                    Reason = "Never been rented",
                    Action = RecommendedAction.NeedsPromotion
                })
                .ToList();

            var genreTitleCounts = movies
                .Where(m => m.Genre.HasValue)
                .GroupBy(m => m.Genre.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var genreGaps = genrePop
                .Where(g => g.Trend != TrendDirection.Declining)
                .Where(g =>
                {
                    var titleCount = genreTitleCounts.ContainsKey(g.Genre)
                        ? genreTitleCounts[g.Genre] : 0;
                    return g.TotalRentals > 0 &&
                           (titleCount == 0 || (double)g.TotalRentals / titleCount > 3);
                })
                .Select(g => new GenreRecommendation
                {
                    Genre = g.Genre,
                    CurrentTitles = genreTitleCounts.ContainsKey(g.Genre)
                        ? genreTitleCounts[g.Genre] : 0,
                    RentalDemand = g.TotalRentals,
                    Trend = g.Trend,
                    Suggestion = $"High demand ({g.TotalRentals} rentals) with " +
                                 $"{(genreTitleCounts.TryGetValue(g.Genre, out var _v4) ? _v4 : 0)} titles — consider adding more"
                })
                .ToList();

            return new InventoryRecommendation
            {
                HighDemandMovies = highDemand,
                DeadStockMovies = deadStock,
                NeverRentedMovies = neverRented,
                GenreGaps = genreGaps,
                GeneratedAt = _clock.Now
            };
        }

        // ── Summary Report ───────────────────────────────────────────

        /// <summary>
        /// Generates a human-readable forecast summary.
        /// </summary>
        public string GetForecastSummary(int forecastDays = 7)
        {
            var dow = GetDayOfWeekDistribution();
            var forecast = ForecastDemand(forecastDays);
            var recommendations = GetInventoryRecommendations();

            var lines = new List<string>
            {
                "═══ RENTAL DEMAND FORECAST ═══",
                ""
            };

            if (dow.TotalRentals > 0)
            {
                lines.Add("Day-of-Week Pattern:");
                foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
                {
                    if (dow.Days.ContainsKey(day))
                    {
                        var d = dow.Days[day];
                        var bar = new string('#', (int)(d.Percentage / 5));
                        lines.Add($"  {day,-9} {bar} {d.Percentage}% ({d.Count})");
                    }
                }
                lines.Add($"  Peak: {dow.PeakDay} | Slow: {dow.SlowDay}");
                lines.Add("");
            }

            if (forecast.Count > 0)
            {
                lines.Add($"Next {forecastDays}-Day Forecast:");
                foreach (var f in forecast)
                {
                    lines.Add($"  {f.Date:ddd MMM d}: ~{f.PredictedRentals} rentals " +
                              $"(confidence: {f.Confidence:P0})");
                }
                var totalPredicted = forecast.Sum(f => f.PredictedRentals);
                lines.Add($"  Total predicted: ~{totalPredicted:F0} rentals");
                lines.Add("");
            }

            if (recommendations.HighDemandMovies.Count > 0)
            {
                lines.Add("High Demand — Consider Stocking More:");
                foreach (var m in recommendations.HighDemandMovies)
                    lines.Add($"  * {m.MovieName}: {m.Reason}");
                lines.Add("");
            }

            if (recommendations.DeadStockMovies.Count > 0)
            {
                lines.Add("Dead Stock — Consider Retiring:");
                foreach (var m in recommendations.DeadStockMovies)
                    lines.Add($"  * {m.MovieName}: {m.Reason}");
                lines.Add("");
            }

            if (recommendations.GenreGaps.Count > 0)
            {
                lines.Add("Genre Gaps — Need More Titles:");
                foreach (var g in recommendations.GenreGaps)
                    lines.Add($"  * {g.Genre}: {g.Suggestion}");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    // ── DTOs ─────────────────────────────────────────────────────────

    public class DayStats
    {
        public DayOfWeek Day { get; set; }
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    public class DayOfWeekDistribution
    {
        public Dictionary<DayOfWeek, DayStats> Days { get; set; }
            = new Dictionary<DayOfWeek, DayStats>();
        public DayOfWeek PeakDay { get; set; }
        public DayOfWeek SlowDay { get; set; }
        public int TotalRentals { get; set; }

        public static DayOfWeekDistribution Empty()
        {
            var days = new Dictionary<DayOfWeek, DayStats>();
            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
                days[day] = new DayStats { Day = day, Count = 0, Percentage = 0 };
            return new DayOfWeekDistribution { Days = days, TotalRentals = 0 };
        }
    }

    public class MonthlyTrend
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; }
        public int RentalCount { get; set; }
        public decimal Revenue { get; set; }
        public decimal AverageRevenuePerRental { get; set; }
    }

    public class GenrePopularity
    {
        public Genre Genre { get; set; }
        public int TotalRentals { get; set; }
        public int RecentRentals { get; set; }
        public double SharePercent { get; set; }
        public double Momentum { get; set; }
        public TrendDirection Trend { get; set; }
    }

    public enum TrendDirection { Rising, Stable, Declining }

    public class MovieVelocity
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? Genre { get; set; }
        public int TotalRentals { get; set; }
        public DateTime FirstRental { get; set; }
        public DateTime LastRental { get; set; }
        public double RentalsPerMonth { get; set; }
        public double AverageDaysBetweenRentals { get; set; }
        public int DaysSinceLastRental { get; set; }
    }

    public class DailyForecast
    {
        public DateTime Date { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public double PredictedRentals { get; set; }
        public double Confidence { get; set; }
        public double TrendMultiplier { get; set; }
    }

    public class InventoryRecommendation
    {
        public List<InventoryMovieRecommendation> HighDemandMovies { get; set; }
            = new List<InventoryMovieRecommendation>();
        public List<InventoryMovieRecommendation> DeadStockMovies { get; set; }
            = new List<InventoryMovieRecommendation>();
        public List<InventoryMovieRecommendation> NeverRentedMovies { get; set; }
            = new List<InventoryMovieRecommendation>();
        public List<GenreRecommendation> GenreGaps { get; set; }
            = new List<GenreRecommendation>();
        public DateTime GeneratedAt { get; set; }
    }

    public class InventoryMovieRecommendation
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public string Reason { get; set; }
        public RecommendedAction Action { get; set; }
    }

    public enum RecommendedAction { StockMore, ConsiderRetiring, NeedsPromotion }

    public class GenreRecommendation
    {
        public Genre Genre { get; set; }
        public int CurrentTitles { get; set; }
        public int RentalDemand { get; set; }
        public TrendDirection Trend { get; set; }
        public string Suggestion { get; set; }
    }
}
