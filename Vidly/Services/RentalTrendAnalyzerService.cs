using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Analyses rental history to surface volume trends, genre momentum shifts,
    /// peak rental periods, emerging/declining movies, and customer segment
    /// behaviour — giving the store data-driven inventory and promotion insights.
    /// </summary>
    public class RentalTrendAnalyzerService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;

        public RentalTrendAnalyzerService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository)
        {
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        // ── Volume time-series ──────────────────────────────────────────

        /// <summary>
        /// Builds a rental volume time-series over the given date range at the
        /// requested granularity with overall trend direction.
        /// </summary>
        public VolumeTimeSeries GetVolumeTimeSeries(
            DateTime from, DateTime to,
            TrendGranularity granularity = TrendGranularity.Monthly)
        {
            if (to <= from) throw new ArgumentException("'to' must be after 'from'.");

            var rentals = GetRentalsInRange(from, to);
            var buckets = GroupIntoBuckets(rentals, from, to, granularity);

            var direction = TrendDirection.Stable;
            double changePct = 0;

            if (buckets.Count >= 2)
            {
                var firstHalf = buckets.Take(buckets.Count / 2).Sum(b => b.RentalCount);
                var secondHalf = buckets.Skip(buckets.Count / 2).Sum(b => b.RentalCount);

                if (firstHalf > 0)
                {
                    changePct = ((double)(secondHalf - firstHalf) / firstHalf) * 100;
                    direction = changePct > 5 ? TrendDirection.Rising
                              : changePct < -5 ? TrendDirection.Declining
                              : TrendDirection.Stable;
                }
                else if (secondHalf > 0)
                {
                    changePct = 100;
                    direction = TrendDirection.Rising;
                }
            }

            return new VolumeTimeSeries
            {
                Granularity = granularity,
                Buckets = buckets,
                OverallDirection = direction,
                ChangePercent = Math.Round(changePct, 1)
            };
        }

        // ── Genre momentum ──────────────────────────────────────────────

        /// <summary>
        /// Compares genre popularity between two consecutive periods of equal
        /// length ending at <paramref name="asOf"/>.
        /// </summary>
        public List<GenreMomentum> GetGenreMomentum(DateTime asOf, int periodDays = 30)
        {
            if (periodDays < 1) throw new ArgumentException("Period must be at least 1 day.", nameof(periodDays));

            var currentStart = asOf.AddDays(-periodDays);
            var previousStart = currentStart.AddDays(-periodDays);

            var currentRentals = GetRentalsInRange(currentStart, asOf);
            var previousRentals = GetRentalsInRange(previousStart, currentStart);

            var movies = _movieRepository.GetAll().ToDictionary(m => m.Id);
            var allGenres = movies.Values
                .Where(m => m.Genre.HasValue)
                .Select(m => m.Genre.Value.ToString())
                .Distinct()
                .ToList();

            var currentByGenre = CountByGenre(currentRentals, movies);
            var previousByGenre = CountByGenre(previousRentals, movies);

            var currentRanked = currentByGenre.OrderByDescending(kv => kv.Value)
                .Select((kv, i) => (kv.Key, Rank: i + 1)).ToDictionary(x => x.Key, x => x.Rank);
            var previousRanked = previousByGenre.OrderByDescending(kv => kv.Value)
                .Select((kv, i) => (kv.Key, Rank: i + 1)).ToDictionary(x => x.Key, x => x.Rank);

            var result = new List<GenreMomentum>();
            foreach (var genre in allGenres)
            {
                currentByGenre.TryGetValue(genre, out var cur);
                previousByGenre.TryGetValue(genre, out var prev);
                currentRanked.TryGetValue(genre, out var curRank);
                previousRanked.TryGetValue(genre, out var prevRank);

                if (curRank == 0) curRank = allGenres.Count;
                if (prevRank == 0) prevRank = allGenres.Count;

                double change = prev > 0 ? ((double)(cur - prev) / prev) * 100 : (cur > 0 ? 100 : 0);
                var dir = change > 10 ? TrendDirection.Rising
                        : change < -10 ? TrendDirection.Declining
                        : TrendDirection.Stable;

                result.Add(new GenreMomentum
                {
                    Genre = genre,
                    CurrentPeriodRentals = cur,
                    PreviousPeriodRentals = prev,
                    ChangePercent = Math.Round(change, 1),
                    Direction = dir,
                    Rank = curRank,
                    PreviousRank = prevRank,
                    RankChange = prevRank - curRank
                });
            }

            return result.OrderBy(g => g.Rank).ToList();
        }

        // ── Peak period heatmap ─────────────────────────────────────────

        /// <summary>
        /// Builds a day-of-week × hour-of-day rental heatmap for the given range.
        /// </summary>
        public PeakPeriodAnalysis GetPeakPeriods(DateTime from, DateTime to)
        {
            if (to <= from) throw new ArgumentException("'to' must be after 'from'.");

            var rentals = GetRentalsInRange(from, to);
            var grid = new Dictionary<(DayOfWeek, int), int>();

            foreach (var r in rentals)
            {
                var key = (r.RentalDate.DayOfWeek, r.RentalDate.Hour);
                grid[key] = grid.TryGetValue(key, out var c) ? c + 1 : 1;
            }

            int maxCount = grid.Count > 0 ? grid.Values.Max() : 1;
            var cells = new List<PeakPeriodCell>();

            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            {
                for (int h = 0; h < 24; h++)
                {
                    grid.TryGetValue((day, h), out var count);
                    cells.Add(new PeakPeriodCell
                    {
                        DayOfWeek = day,
                        Hour = h,
                        RentalCount = count,
                        Intensity = maxCount > 0 ? Math.Round((double)count / maxCount, 2) : 0
                    });
                }
            }

            var byDay = cells.GroupBy(c => c.DayOfWeek)
                .Select(g => (Day: g.Key, Total: g.Sum(c => c.RentalCount)))
                .ToList();

            var peakDay = byDay.OrderByDescending(d => d.Total).First().Day;
            var quietDay = byDay.OrderBy(d => d.Total).First().Day;

            var byHour = cells.GroupBy(c => c.Hour)
                .Select(g => (Hour: g.Key, Total: g.Sum(c => c.RentalCount)))
                .ToList();

            var peakHour = byHour.OrderByDescending(h => h.Total).First().Hour;
            var quietHour = byHour.OrderBy(h => h.Total).First().Hour;

            var weekdays = byDay.Where(d => d.Day != DayOfWeek.Saturday && d.Day != DayOfWeek.Sunday);
            var weekends = byDay.Where(d => d.Day == DayOfWeek.Saturday || d.Day == DayOfWeek.Sunday);

            return new PeakPeriodAnalysis
            {
                Cells = cells,
                PeakDay = peakDay,
                PeakHour = peakHour,
                QuietestDay = quietDay,
                QuietestHour = quietHour,
                WeekdayAvg = weekdays.Any() ? Math.Round(weekdays.Average(d => d.Total), 1) : 0,
                WeekendAvg = weekends.Any() ? Math.Round(weekends.Average(d => d.Total), 1) : 0
            };
        }

        // ── Trending / declining movies ─────────────────────────────────

        /// <summary>
        /// Identifies movies with the largest velocity increase or decrease
        /// by comparing two consecutive periods.
        /// </summary>
        public (List<TrendingMovie> Trending, List<TrendingMovie> Declining) GetTrendingMovies(
            DateTime asOf, int periodDays = 14, int topN = 10)
        {
            if (periodDays < 1) throw new ArgumentException("Period must be at least 1 day.", nameof(periodDays));
            if (topN < 1) throw new ArgumentException("topN must be at least 1.", nameof(topN));

            var currentStart = asOf.AddDays(-periodDays);
            var previousStart = currentStart.AddDays(-periodDays);

            var currentRentals = GetRentalsInRange(currentStart, asOf);
            var previousRentals = GetRentalsInRange(previousStart, currentStart);

            var movies = _movieRepository.GetAll().ToDictionary(m => m.Id);

            var currentByMovie = currentRentals.GroupBy(r => r.MovieId)
                .ToDictionary(g => g.Key, g => g.Count());
            var previousByMovie = previousRentals.GroupBy(r => r.MovieId)
                .ToDictionary(g => g.Key, g => g.Count());

            var allMovieIds = currentByMovie.Keys.Union(previousByMovie.Keys).ToList();

            var items = new List<TrendingMovie>();
            foreach (var mid in allMovieIds)
            {
                currentByMovie.TryGetValue(mid, out var cur);
                previousByMovie.TryGetValue(mid, out var prev);

                double velocity = prev > 0 ? ((double)(cur - prev) / prev) * 100
                                 : cur > 0 ? 100 : 0;

                var signal = Math.Abs(velocity) >= 50 ? TrendSignalStrength.Strong
                           : Math.Abs(velocity) >= 20 ? TrendSignalStrength.Moderate
                           : TrendSignalStrength.Weak;

                movies.TryGetValue(mid, out var movie);
                items.Add(new TrendingMovie
                {
                    MovieId = mid,
                    MovieName = movie?.Name ?? $"Movie #{mid}",
                    Genre = movie?.Genre?.ToString() ?? "Unknown",
                    RecentRentals = cur,
                    PriorRentals = prev,
                    VelocityChange = Math.Round(velocity, 1),
                    Signal = signal
                });
            }

            var trending = items.Where(i => i.VelocityChange > 0)
                .OrderByDescending(i => i.VelocityChange)
                .Take(topN).ToList();

            var declining = items.Where(i => i.VelocityChange < 0)
                .OrderBy(i => i.VelocityChange)
                .Take(topN).ToList();

            return (trending, declining);
        }

        // ── Customer segments ───────────────────────────────────────────

        /// <summary>
        /// Segments customers by rental frequency over the given range.
        /// Heavy (≥8/mo), Moderate (4-7), Light (1-3), Lapsed (0 in range
        /// but has prior history).
        /// </summary>
        public List<CustomerSegmentTrend> GetCustomerSegments(DateTime from, DateTime to)
        {
            if (to <= from) throw new ArgumentException("'to' must be after 'from'.");

            var months = Math.Max(1, (to - from).TotalDays / 30.0);
            var rentals = GetRentalsInRange(from, to);
            var allRentals = _rentalRepository.GetAll();
            var customers = _customerRepository.GetAll();

            var rentalsByCustomer = rentals.GroupBy(r => r.CustomerId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var segments = new Dictionary<string, (int Count, int Rentals, decimal Revenue)>
            {
                ["Heavy"] = (0, 0, 0m),
                ["Moderate"] = (0, 0, 0m),
                ["Light"] = (0, 0, 0m),
                ["Lapsed"] = (0, 0, 0m)
            };

            foreach (var c in customers)
            {
                if (rentalsByCustomer.TryGetValue(c.Id, out var cRentals))
                {
                    double perMonth = cRentals.Count / months;
                    decimal rev = cRentals.Sum(r => r.DailyRate);
                    string seg = perMonth >= 8 ? "Heavy" : perMonth >= 4 ? "Moderate" : "Light";

                    var s = segments[seg];
                    segments[seg] = (s.Count + 1, s.Rentals + cRentals.Count, s.Revenue + rev);
                }
                else
                {
                    // Check if they have any prior rentals at all
                    bool hasPrior = allRentals.Any(r => r.CustomerId == c.Id);
                    if (hasPrior)
                    {
                        var s = segments["Lapsed"];
                        segments["Lapsed"] = (s.Count + 1, s.Rentals, s.Revenue);
                    }
                }
            }

            return segments.Select(kv => new CustomerSegmentTrend
            {
                Segment = kv.Key,
                CustomerCount = kv.Value.Count,
                TotalRentals = kv.Value.Rentals,
                AvgRentalsPerCustomer = kv.Value.Count > 0 ? Math.Round((double)kv.Value.Rentals / kv.Value.Count, 1) : 0,
                AvgRevenue = kv.Value.Count > 0 ? Math.Round(kv.Value.Revenue / kv.Value.Count, 2) : 0
            }).Where(s => s.CustomerCount > 0).ToList();
        }

        // ── Full report ─────────────────────────────────────────────────

        /// <summary>
        /// Generates a comprehensive trend report with auto-generated insights.
        /// </summary>
        public TrendReport GenerateReport(DateTime from, DateTime to,
            TrendGranularity granularity = TrendGranularity.Monthly,
            int trendingTopN = 5)
        {
            if (to <= from) throw new ArgumentException("'to' must be after 'from'.");

            var volume = GetVolumeTimeSeries(from, to, granularity);
            var momentum = GetGenreMomentum(to, Math.Max(1, (int)(to - from).TotalDays / 2));
            var peaks = GetPeakPeriods(from, to);
            var (trending, declining) = GetTrendingMovies(to, Math.Max(1, (int)(to - from).TotalDays / 2), trendingTopN);
            var segments = GetCustomerSegments(from, to);

            var insights = GenerateInsights(volume, momentum, peaks, trending, declining, segments);

            return new TrendReport
            {
                GeneratedAt = DateTime.Now,
                AnalysisStart = from,
                AnalysisEnd = to,
                Volume = volume,
                GenreMomentum = momentum,
                PeakPeriods = peaks,
                TrendingMovies = trending,
                DecliningMovies = declining,
                CustomerSegments = segments,
                Insights = insights
            };
        }

        // ── Private helpers ─────────────────────────────────────────────

        private List<Rental> GetRentalsInRange(DateTime from, DateTime to)
        {
            return _rentalRepository.GetAll()
                .Where(r => r.RentalDate >= from && r.RentalDate < to)
                .ToList();
        }

        private List<RentalVolumeBucket> GroupIntoBuckets(
            List<Rental> rentals, DateTime from, DateTime to,
            TrendGranularity granularity)
        {
            var buckets = new List<RentalVolumeBucket>();
            var cursor = from;

            while (cursor < to)
            {
                var nextCursor = AdvanceCursor(cursor, granularity);
                if (nextCursor > to) nextCursor = to;

                var inBucket = rentals.Where(r => r.RentalDate >= cursor && r.RentalDate < nextCursor).ToList();

                buckets.Add(new RentalVolumeBucket
                {
                    PeriodStart = cursor,
                    PeriodEnd = nextCursor,
                    RentalCount = inBucket.Count,
                    Revenue = inBucket.Sum(r => r.DailyRate),
                    AvgDailyRate = inBucket.Count > 0
                        ? Math.Round(inBucket.Average(r => (double)r.DailyRate), 2)
                        : 0
                });

                cursor = nextCursor;
            }

            return buckets;
        }

        private DateTime AdvanceCursor(DateTime cursor, TrendGranularity g)
        {
            if (g == TrendGranularity.Daily) return cursor.AddDays(1);
            if (g == TrendGranularity.Weekly) return cursor.AddDays(7);
            if (g == TrendGranularity.Monthly) return cursor.AddMonths(1);
            if (g == TrendGranularity.Quarterly) return cursor.AddMonths(3);
            if (g == TrendGranularity.Yearly) return cursor.AddYears(1);
            return cursor.AddMonths(1);
        }

        private Dictionary<string, int> CountByGenre(List<Rental> rentals, Dictionary<int, Movie> movies)
        {
            var result = new Dictionary<string, int>();
            foreach (var r in rentals)
            {
                if (movies.TryGetValue(r.MovieId, out var m) && m.Genre.HasValue)
                {
                    var g = m.Genre.Value.ToString();
                    result[g] = result.TryGetValue(g, out var c) ? c + 1 : 1;
                }
            }
            return result;
        }

        private List<string> GenerateInsights(
            VolumeTimeSeries volume,
            List<GenreMomentum> momentum,
            PeakPeriodAnalysis peaks,
            List<TrendingMovie> trending,
            List<TrendingMovie> declining,
            List<CustomerSegmentTrend> segments)
        {
            var insights = new List<string>();

            // Volume trend
            if (volume.OverallDirection == TrendDirection.Rising)
                insights.Add($"Rental volume is up {volume.ChangePercent}% — consider expanding inventory.");
            else if (volume.OverallDirection == TrendDirection.Declining)
                insights.Add($"Rental volume is down {Math.Abs(volume.ChangePercent)}% — promotional campaigns may help.");

            // Genre momentum
            var risingGenres = momentum.Where(g => g.Direction == TrendDirection.Rising).ToList();
            if (risingGenres.Any())
                insights.Add($"Rising genres: {string.Join(", ", risingGenres.Select(g => $"{g.Genre} (+{g.ChangePercent}%)"))}. Stock up!");

            var decliningGenres = momentum.Where(g => g.Direction == TrendDirection.Declining).ToList();
            if (decliningGenres.Any())
                insights.Add($"Declining genres: {string.Join(", ", decliningGenres.Select(g => $"{g.Genre} ({g.ChangePercent}%)"))}. Consider promotions or discounts.");

            // Peak periods
            if (peaks.WeekendAvg > peaks.WeekdayAvg * 1.5)
                insights.Add("Weekend rentals are 50%+ higher than weekdays — consider weekend-only promotions.");
            else if (peaks.WeekdayAvg > peaks.WeekendAvg * 1.5)
                insights.Add("Weekday rentals outpace weekends — target commuter audiences.");

            insights.Add($"Peak rental time: {peaks.PeakDay}s at {FormatHour(peaks.PeakHour)}. Ensure full staffing.");

            // Trending movies
            if (trending.Any())
            {
                var top = trending.First();
                insights.Add($"Hottest title: \"{top.MovieName}\" (+{top.VelocityChange}% velocity). Ensure copies are available.");
            }

            // Lapsed customers
            var lapsed = segments.FirstOrDefault(s => s.Segment == "Lapsed");
            if (lapsed != null && lapsed.CustomerCount > 0)
                insights.Add($"{lapsed.CustomerCount} lapsed customer(s) detected — win-back campaign recommended.");

            // Heavy renters
            var heavy = segments.FirstOrDefault(s => s.Segment == "Heavy");
            if (heavy != null && heavy.CustomerCount > 0)
                insights.Add($"{heavy.CustomerCount} power renter(s) averaging {heavy.AvgRentalsPerCustomer}/mo — loyalty rewards opportunity.");

            return insights;
        }

        private static string FormatHour(int hour) =>
            hour == 0 ? "12 AM" : hour < 12 ? $"{hour} AM" : hour == 12 ? "12 PM" : $"{hour - 12} PM";
    }
}
