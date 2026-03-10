using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Analyzes rental data to identify trending movies, genre shifts, and
    /// peak activity patterns. Compares current vs previous time windows.
    /// </summary>
    public class RentalTrendsService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;

        public const int DefaultWindowDays = 30;
        public const int MinWindowDays = 1;
        public const int MaxWindowDays = 365;
        public const int MaxTopMovies = 50;
        public const int DefaultTopCount = 10;
        public const double StableThresholdPercent = 10.0;

        public RentalTrendsService(IRentalRepository rentalRepository, IMovieRepository movieRepository)
        {
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        public TrendsReport GetTrendsReport(int windowDays = DefaultWindowDays, int topCount = DefaultTopCount, DateTime? asOf = null)
        {
            windowDays = Math.Max(MinWindowDays, Math.Min(MaxWindowDays, windowDays));
            topCount = Math.Max(1, Math.Min(MaxTopMovies, topCount));

            var refDate = asOf ?? DateTime.Today;
            var periodEnd = refDate;
            var periodStart = refDate.AddDays(-windowDays);
            var prevEnd = periodStart;
            var prevStart = prevEnd.AddDays(-windowDays);

            var allRentals = _rentalRepository.GetAll();
            var current = allRentals.Where(r => r.RentalDate >= periodStart && r.RentalDate < periodEnd).ToList();
            var previous = allRentals.Where(r => r.RentalDate >= prevStart && r.RentalDate < prevEnd).ToList();
            var movies = _movieRepository.GetAll().ToDictionary(m => m.Id);

            var report = new TrendsReport
            {
                PeriodStart = periodStart, PeriodEnd = periodEnd, PeriodDays = windowDays,
                TotalRentals = current.Count, PreviousPeriodRentals = previous.Count,
                OverallChangePercent = CalcChange(previous.Count, current.Count),
                AverageRentalsPerDay = windowDays > 0 ? (double)current.Count / windowDays : 0
            };

            var curByMovie = current.GroupBy(r => r.MovieId).ToDictionary(g => g.Key, g => g.Count());
            var prevByMovie = previous.GroupBy(r => r.MovieId).ToDictionary(g => g.Key, g => g.Count());

            var trends = BuildMovieTrends(curByMovie, prevByMovie, movies);
            var ranked = trends.OrderByDescending(t => t.RentalCount).ToList();
            for (int i = 0; i < ranked.Count; i++) ranked[i].Rank = i + 1;

            var prevRanks = prevByMovie.OrderByDescending(kv => kv.Value)
                .Select((kv, i) => new { kv.Key, Rank = i + 1 }).ToDictionary(x => x.Key, x => x.Rank);
            foreach (var t in ranked)
            {
                if (t.Direction == TrendDirection.NewEntry) t.RankChange = null;
                else if (prevRanks.TryGetValue(t.MovieId, out var pr)) t.RankChange = pr - t.Rank;
                else t.RankChange = null;
            }

            report.TopMovies = ranked.Take(topCount).ToList();
            report.BiggestMovers = ranked.Where(t => t.Direction == TrendDirection.Rising).OrderByDescending(t => t.ChangePercent).Take(topCount).ToList();
            report.NewEntries = ranked.Where(t => t.Direction == TrendDirection.NewEntry).OrderByDescending(t => t.RentalCount).Take(topCount).ToList();
            report.FallingMovies = ranked.Where(t => t.Direction == TrendDirection.Cooling).OrderBy(t => t.ChangePercent).Take(topCount).ToList();
            report.GenreTrends = BuildGenreTrends(current, previous, movies);
            report.DayOfWeekBreakdown = BuildDayOfWeek(current);

            if (current.Any())
            {
                var peak = current.GroupBy(r => r.RentalDate.Date).OrderByDescending(g => g.Count()).First();
                report.PeakDay = peak.Key;
                report.PeakDayRentals = peak.Count();
            }

            return report;
        }

        public MovieTrend GetMovieTrend(int movieId, int windowDays = DefaultWindowDays, DateTime? asOf = null)
        {
            windowDays = Math.Max(MinWindowDays, Math.Min(MaxWindowDays, windowDays));
            var refDate = asOf ?? DateTime.Today;
            var periodStart = refDate.AddDays(-windowDays);
            var prevStart = periodStart.AddDays(-windowDays);

            var movie = _movieRepository.GetById(movieId);
            if (movie == null) return null;

            var rentals = _rentalRepository.GetByMovie(movieId);
            var cur = rentals.Count(r => r.RentalDate >= periodStart && r.RentalDate < refDate);
            var prev = rentals.Count(r => r.RentalDate >= prevStart && r.RentalDate < periodStart);
            var change = CalcChange(prev, cur);

            return new MovieTrend
            {
                MovieId = movieId, MovieName = movie.Name, Genre = movie.Genre,
                RentalCount = cur, PreviousPeriodCount = prev,
                Direction = Classify(prev, cur), ChangePercent = change,
                VelocityScore = Velocity(cur, change)
            };
        }

        public List<MovieTrend> GetTrending(int windowDays = DefaultWindowDays, int count = DefaultTopCount, DateTime? asOf = null)
        {
            var report = GetTrendsReport(windowDays, count * 2, asOf);
            return report.TopMovies
                .Where(t => t.Direction == TrendDirection.Rising || t.Direction == TrendDirection.NewEntry)
                .OrderByDescending(t => t.VelocityScore).Take(count).ToList();
        }

        private List<MovieTrend> BuildMovieTrends(Dictionary<int, int> cur, Dictionary<int, int> prev, Dictionary<int, Movie> movies)
        {
            return cur.Keys.Union(prev.Keys).Distinct()
                .Where(id => cur.ContainsKey(id) && cur[id] > 0)
                .Select(id =>
                {
                    cur.TryGetValue(id, out var c); prev.TryGetValue(id, out var p);
                    movies.TryGetValue(id, out var m);
                    var ch = CalcChange(p, c);
                    return new MovieTrend
                    {
                        MovieId = id, MovieName = m?.Name ?? $"Movie #{id}", Genre = m?.Genre,
                        RentalCount = c, PreviousPeriodCount = p,
                        Direction = Classify(p, c), ChangePercent = ch, VelocityScore = Velocity(c, ch)
                    };
                }).ToList();
        }

        private List<GenreTrend> BuildGenreTrends(List<Rental> cur, List<Rental> prev, Dictionary<int, Movie> movies)
        {
            var curByGenre = cur.Where(r => movies.ContainsKey(r.MovieId) && movies[r.MovieId].Genre.HasValue)
                .GroupBy(r => movies[r.MovieId].Genre.Value).ToDictionary(g => g.Key, g => g.ToList());
            var prevByGenre = prev.Where(r => movies.ContainsKey(r.MovieId) && movies[r.MovieId].Genre.HasValue)
                .GroupBy(r => movies[r.MovieId].Genre.Value).ToDictionary(g => g.Key, g => g.Count());

            var total = cur.Count;
            return Enum.GetValues(typeof(Genre)).Cast<Genre>()
                .Select(genre =>
                {
                    var cc = curByGenre.ContainsKey(genre) ? curByGenre[genre].Count : 0;
                    prevByGenre.TryGetValue(genre, out var pc);
                    if (cc == 0 && pc == 0) return null;
                    string topMovie = null;
                    if (curByGenre.ContainsKey(genre))
                    {
                        var topId = curByGenre[genre].GroupBy(r => r.MovieId).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
                        if (topId.HasValue && movies.ContainsKey(topId.Value)) topMovie = movies[topId.Value].Name;
                    }
                    return new GenreTrend
                    {
                        Genre = genre, RentalCount = cc, PreviousPeriodCount = pc,
                        Direction = Classify(pc, cc), ChangePercent = CalcChange(pc, cc),
                        MarketShare = total > 0 ? Math.Round((double)cc / total * 100, 1) : 0, TopMovie = topMovie
                    };
                }).Where(g => g != null).OrderByDescending(g => g.RentalCount).ToList();
        }

        private List<DayOfWeekActivity> BuildDayOfWeek(List<Rental> rentals)
        {
            var total = rentals.Count;
            var grouped = rentals.GroupBy(r => r.RentalDate.DayOfWeek).ToDictionary(g => g.Key, g => g.Count());
            var max = grouped.Values.DefaultIfEmpty(0).Max();
            return Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>().Select(day =>
            {
                grouped.TryGetValue(day, out var count);
                return new DayOfWeekActivity
                {
                    Day = day, RentalCount = count,
                    Percentage = total > 0 ? Math.Round((double)count / total * 100, 1) : 0,
                    IsPeak = count == max && count > 0
                };
            }).ToList();
        }

        private static TrendDirection Classify(int prev, int cur)
        {
            if (prev == 0 && cur > 0) return TrendDirection.NewEntry;
            if (prev == 0) return TrendDirection.Stable;
            var ch = ((double)cur - prev) / prev * 100;
            return ch > StableThresholdPercent ? TrendDirection.Rising : ch < -StableThresholdPercent ? TrendDirection.Cooling : TrendDirection.Stable;
        }

        private static double CalcChange(int prev, int cur)
        {
            if (prev == 0) return cur > 0 ? 100.0 : 0.0;
            return Math.Round(((double)cur - prev) / prev * 100, 1);
        }

        private static double Velocity(int count, double changePct)
        {
            var cs = Math.Min(count * 10.0, 100.0);
            var ms = Math.Min(Math.Max(changePct, -100), 200) / 2.0;
            return Math.Round(cs * 0.6 + ms * 0.4, 1);
        }
    }
}
