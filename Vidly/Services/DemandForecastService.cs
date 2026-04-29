using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    public class DemandForecastService
    {
        private readonly IMovieRepository _movies;
        private readonly IRentalRepository _rentals;
        private static readonly string[] DayNames = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        public DemandForecastService(IMovieRepository movies, IRentalRepository rentals)
        {
            _movies = movies ?? throw new ArgumentNullException(nameof(movies));
            _rentals = rentals ?? throw new ArgumentNullException(nameof(rentals));
        }

        public DemandForecast GenerateForecast()
        {
            var allMovies = _movies.GetAll();
            var allRentals = _rentals.GetAll();
            var now = DateTime.Now;

            // O(M) movie lookup dictionary — eliminates O(R×M) linear scans in rental loop
            var movieById = allMovies.ToDictionary(m => m.Id);

            // Build per-movie rental history (count per day-of-week)
            var movieDowCounts = new Dictionary<int, double[]>(); // movieId -> [7] day-of-week counts
            var movieRentalCounts = new Dictionary<int, int>();
            var genreDowCounts = new Dictionary<string, double[]>();

            foreach (var rental in allRentals)
            {
                var dow = (int)rental.RentalDate.DayOfWeek;

                if (!movieDowCounts.TryGetValue(rental.MovieId, out var dowArr))
                {
                    dowArr = new double[7];
                    movieDowCounts[rental.MovieId] = dowArr;
                }
                dowArr[dow]++;

                if (!movieRentalCounts.ContainsKey(rental.MovieId))
                    movieRentalCounts[rental.MovieId] = 0;
                movieRentalCounts[rental.MovieId]++;

                movieById.TryGetValue(rental.MovieId, out var movie);
                var genre = movie?.Genre?.ToString() ?? "Unknown";
                if (!genreDowCounts.TryGetValue(genre, out var genreArr))
                {
                    genreArr = new double[7];
                    genreDowCounts[genre] = genreArr;
                }
                genreArr[dow]++;
            }

            // Compute weeks of data for averaging
            var minDate = allRentals.Any() ? allRentals.Min(r => r.RentalDate) : now;
            var weeks = Math.Max(1, (now - minDate).TotalDays / 7.0);

            // EMA smoothing factor
            const double alpha = 0.3;

            var movieForecasts = new List<MovieForecast>();
            foreach (var movie in allMovies)
            {
                var dowAvg = new double[7];
                if (movieDowCounts.ContainsKey(movie.Id))
                {
                    for (int d = 0; d < 7; d++)
                        dowAvg[d] = movieDowCounts[movie.Id][d] / weeks;
                }

                // EMA: blend historical average with recent trend
                var predictions = new double[7];
                var startDow = (int)now.AddDays(1).DayOfWeek;
                double prev = dowAvg.Length > 0 ? dowAvg.Average() : 0;
                for (int i = 0; i < 7; i++)
                {
                    var dayIdx = (startDow + i) % 7;
                    prev = alpha * dowAvg[dayIdx] + (1 - alpha) * prev;
                    predictions[i] = Math.Round(prev, 2);
                }

                var histAvg = dowAvg.Average();
                var predAvg = predictions.Average();
                var surge = histAvg > 0 && predAvg > histAvg * 2;
                var trend = predAvg > histAvg * 1.1 ? "Rising"
                    : predAvg < histAvg * 0.9 ? "Falling" : "Stable";

                movieForecasts.Add(new MovieForecast
                {
                    MovieId = movie.Id,
                    Name = movie.Name,
                    Genre = movie.Genre?.ToString() ?? "Unknown",
                    DailyPredictions = predictions,
                    TrendDirection = trend,
                    SurgeDetected = surge,
                    HistoricalAverage = Math.Round(histAvg, 2)
                });
            }

            // Genre heatmap
            var genreHeatmap = new List<GenreHeatmapEntry>();
            foreach (var kvp in genreDowCounts)
            {
                for (int d = 0; d < 7; d++)
                {
                    var avg = kvp.Value[d] / weeks;
                    genreHeatmap.Add(new GenreHeatmapEntry
                    {
                        Genre = kvp.Key,
                        DayOfWeek = DayNames[d],
                        PredictedDemand = Math.Round(alpha * avg + (1 - alpha) * (kvp.Value.Sum() / (7 * weeks)), 2),
                        HistoricalAvg = Math.Round(avg, 2)
                    });
                }
            }

            // Stock alerts - simulate stock as 3 copies per movie minus active rentals
            // Pre-count active rentals per movie in O(A) instead of O(M×A)
            var activeCountByMovie = new Dictionary<int, int>();
            foreach (var r in allRentals)
            {
                if (r.Status == RentalStatus.Active || r.Status == RentalStatus.Overdue)
                {
                    if (!activeCountByMovie.TryGetValue(r.MovieId, out var cnt))
                        cnt = 0;
                    activeCountByMovie[r.MovieId] = cnt + 1;
                }
            }

            var stockAlerts = new List<StockAlert>();
            foreach (var mf in movieForecasts)
            {
                activeCountByMovie.TryGetValue(mf.MovieId, out var activeCount);
                var stock = Math.Max(0, 3 - activeCount);
                var weekDemand = mf.DailyPredictions.Sum();
                if (weekDemand > stock * 0.7)
                {
                    var daysUntil = stock > 0 && mf.DailyPredictions.Average() > 0
                        ? (int)Math.Floor(stock / mf.DailyPredictions.Average())
                        : 0;
                    var risk = stock == 0 ? "Critical"
                        : daysUntil <= 2 ? "High"
                        : daysUntil <= 4 ? "Medium" : "Low";
                    stockAlerts.Add(new StockAlert
                    {
                        MovieName = mf.Name,
                        CurrentStock = stock,
                        PredictedDemand = Math.Round(weekDemand, 1),
                        RiskLevel = risk,
                        DaysUntilStockout = daysUntil
                    });
                }
            }
            stockAlerts = stockAlerts.OrderBy(a =>
                a.RiskLevel == "Critical" ? 0 : a.RiskLevel == "High" ? 1 : a.RiskLevel == "Medium" ? 2 : 3
            ).ToList();

            // Restock recommendations — O(1) genre lookup via dictionary instead of O(alerts×M) name search
            var forecastByName = movieForecasts.ToDictionary(f => f.Name);
            var recommendations = new List<RestockRecommendation>();
            foreach (var alert in stockAlerts.Where(a => a.RiskLevel == "Critical" || a.RiskLevel == "High"))
            {
                forecastByName.TryGetValue(alert.MovieName, out var mf);
                var qty = Math.Max(1, (int)Math.Ceiling(alert.PredictedDemand - alert.CurrentStock));
                recommendations.Add(new RestockRecommendation
                {
                    MovieName = alert.MovieName,
                    Genre = mf?.Genre ?? "Unknown",
                    RecommendedQuantity = qty,
                    Urgency = alert.RiskLevel,
                    Reason = alert.RiskLevel == "Critical"
                        ? $"Out of stock with {alert.PredictedDemand:F1} predicted rentals this week"
                        : $"Only {alert.CurrentStock} copies left, {alert.PredictedDemand:F1} predicted demand"
                });
            }

            // Summary
            var totalPredicted = movieForecasts.Sum(f => f.DailyPredictions.Sum());
            var topGenre = movieForecasts.GroupBy(f => f.Genre)
                .OrderByDescending(g => g.Sum(f => f.DailyPredictions.Sum()))
                .FirstOrDefault()?.Key ?? "N/A";
            var topMovie = movieForecasts.OrderByDescending(f => f.DailyPredictions.Sum())
                .FirstOrDefault()?.Name ?? "N/A";
            var healthScore = Math.Max(0, Math.Min(100,
                100 - (stockAlerts.Count(a => a.RiskLevel == "Critical") * 20)
                    - (stockAlerts.Count(a => a.RiskLevel == "High") * 10)
                    - (stockAlerts.Count(a => a.RiskLevel == "Medium") * 5)));

            return new DemandForecast
            {
                GeneratedAt = now,
                MovieForecasts = movieForecasts,
                GenreHeatmap = genreHeatmap,
                StockAlerts = stockAlerts,
                Recommendations = recommendations,
                Summary = new DemandSummary
                {
                    TotalPredictedRentals = Math.Round(totalPredicted, 1),
                    TopGenre = topGenre,
                    TopMovie = topMovie,
                    AlertCount = stockAlerts.Count,
                    HealthScore = healthScore
                }
            };
        }
    }
}
