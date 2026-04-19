using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Smart Revenue Alerts — proactive revenue monitoring with anomaly detection,
    /// forecasting, and autonomous recommendations. Monitors rental revenue patterns,
    /// detects unusual drops/spikes, predicts future revenue, and generates actionable alerts.
    /// </summary>
    public class RevenueAlertService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IClock _clock;

        private static readonly List<RevenueAlert> _alerts = new List<RevenueAlert>();
        private static AlertConfig _config = new AlertConfig();
        private static int _alertIdCounter;

        public RevenueAlertService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IClock clock)
        {
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public RevenueDashboard GetDashboard(int? daysBack = null)
        {
            var days = daysBack ?? 30;
            var snapshots = BuildSnapshots(days);
            var forecast = GetForecast(7);
            var genreRevenue = GetGenreRevenue(days);
            var alerts = GetAlerts(false);
            var recommendations = GenerateRecommendations(snapshots, genreRevenue);

            return new RevenueDashboard
            {
                Snapshots = snapshots,
                ActiveAlerts = alerts,
                Forecast = forecast,
                GenreBreakdown = genreRevenue,
                Recommendations = recommendations
            };
        }

        public List<RevenueAlert> GetAlerts(bool includeAcknowledged)
        {
            if (includeAcknowledged)
                return _alerts.OrderByDescending(a => a.Timestamp).ToList();
            return _alerts.Where(a => !a.Acknowledged).OrderByDescending(a => a.Timestamp).ToList();
        }

        public bool AcknowledgeAlert(string alertId)
        {
            var alert = _alerts.FirstOrDefault(a => a.Id == alertId);
            if (alert == null) return false;
            alert.Acknowledged = true;
            return true;
        }

        public RevenueDashboard RunAnalysis()
        {
            var snapshots = BuildSnapshots(_config.AnomalyWindowDays);
            if (snapshots.Count >= 3)
            {
                DetectAnomalies(snapshots);
                DetectTrendShifts(snapshots);
            }

            var forecast = GetForecast(_config.ForecastHorizonDays);
            CheckForecastWarnings(forecast, snapshots);

            return GetDashboard(null);
        }

        public List<RevenueForecast> GetForecast(int days)
        {
            var snapshots = BuildSnapshots(14);
            if (snapshots.Count < 3)
                return Enumerable.Range(1, days).Select(i => new RevenueForecast
                {
                    Date = _clock.Now.Date.AddDays(i),
                    PredictedRevenue = 0,
                    ConfidenceLevel = 0,
                    Trend = "Insufficient data"
                }).ToList();

            // Simple linear regression
            var n = snapshots.Count;
            var xs = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
            var ys = snapshots.Select(s => (double)s.TotalRevenue).ToArray();
            var xMean = xs.Average();
            var yMean = ys.Average();
            var num = xs.Zip(ys, (x, y) => (x - xMean) * (y - yMean)).Sum();
            var den = xs.Select(x => (x - xMean) * (x - xMean)).Sum();
            var slope = den == 0 ? 0 : num / den;
            var intercept = yMean - slope * xMean;

            // R-squared for confidence
            var ssTot = ys.Select(y => (y - yMean) * (y - yMean)).Sum();
            var ssRes = xs.Zip(ys, (x, y) => { var pred = slope * x + intercept; return (y - pred) * (y - pred); }).Sum();
            var rSquared = ssTot == 0 ? 0 : Math.Max(0, 1 - ssRes / ssTot);

            var trend = slope > 0.5 ? "Rising" : slope < -0.5 ? "Declining" : "Stable";

            return Enumerable.Range(1, days).Select(i => new RevenueForecast
            {
                Date = _clock.Now.Date.AddDays(i),
                PredictedRevenue = Math.Max(0, (decimal)(slope * (n - 1 + i) + intercept)),
                ConfidenceLevel = Math.Round((decimal)(rSquared * 100 / (1 + i * 0.1)), 1),
                Trend = trend
            }).ToList();
        }

        public AlertConfig ConfigureAlerts(AlertConfig config)
        {
            _config = config ?? new AlertConfig();
            return _config;
        }

        public AlertConfig GetConfig() => _config;

        public Dictionary<string, GenreRevenueInfo> GetGenreRevenue(int? daysBack = null)
        {
            var days = daysBack ?? 30;
            var since = _clock.Now.Date.AddDays(-days);
            var rentals = _rentalRepository.GetAll()
                .Where(r => r.RentalDate >= since)
                .ToList();

            var movies = _movieRepository.GetAll().ToDictionary(m => m.Id);
            var result = new Dictionary<string, GenreRevenueInfo>();

            foreach (var rental in rentals)
            {
                if (!movies.TryGetValue(rental.MovieId, out var movie) || !movie.Genre.HasValue)
                    continue;

                var genreName = movie.Genre.Value.ToString();
                if (!result.ContainsKey(genreName))
                    result[genreName] = new GenreRevenueInfo();

                result[genreName].Revenue += rental.DailyRate;
                result[genreName].Count++;
            }

            // Calculate trend for each genre (compare first half vs second half)
            var midpoint = since.AddDays(days / 2.0);
            foreach (var genre in result.Keys.ToList())
            {
                var genreRentals = rentals.Where(r =>
                    movies.TryGetValue(r.MovieId, out var m) && m.Genre.HasValue && m.Genre.Value.ToString() == genre);
                var firstHalf = genreRentals.Count(r => r.RentalDate < midpoint);
                var secondHalf = genreRentals.Count(r => r.RentalDate >= midpoint);
                result[genre].Trend = secondHalf > firstHalf * 1.2 ? "Rising"
                    : secondHalf < firstHalf * 0.8 ? "Declining" : "Stable";
            }

            return result;
        }

        private List<RevenueSnapshot> BuildSnapshots(int days)
        {
            var today = _clock.Now.Date;
            var since = today.AddDays(-days);
            var rentals = _rentalRepository.GetAll()
                .Where(r => r.RentalDate >= since && r.RentalDate <= today)
                .ToList();
            var movies = _movieRepository.GetAll().ToDictionary(m => m.Id);

            return Enumerable.Range(0, days).Select(i =>
            {
                var date = since.AddDays(i);
                var dayRentals = rentals.Where(r => r.RentalDate.Date == date).ToList();
                var revenue = dayRentals.Sum(r => r.DailyRate);
                var topMovieId = dayRentals.GroupBy(r => r.MovieId)
                    .OrderByDescending(g => g.Sum(r => r.DailyRate))
                    .FirstOrDefault()?.Key;
                var topGenre = dayRentals
                    .Where(r => movies.TryGetValue(r.MovieId, out var m) && m.Genre.HasValue)
                    .GroupBy(r => movies[r.MovieId].Genre.Value)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key;

                return new RevenueSnapshot
                {
                    Date = date,
                    TotalRevenue = revenue,
                    RentalCount = dayRentals.Count,
                    AvgTransactionValue = dayRentals.Count > 0 ? revenue / dayRentals.Count : 0,
                    TopGenre = topGenre?.ToString() ?? "N/A",
                    TopMovie = topMovieId.HasValue && movies.ContainsKey(topMovieId.Value)
                        ? movies[topMovieId.Value].Name : "N/A"
                };
            }).ToList();
        }

        private void DetectAnomalies(List<RevenueSnapshot> snapshots)
        {
            if (snapshots.Count < 5) return;

            var revenues = snapshots.Select(s => (double)s.TotalRevenue).ToArray();
            var mean = revenues.Average();
            var stdDev = Math.Sqrt(revenues.Select(r => (r - mean) * (r - mean)).Average());
            if (stdDev < 0.01) return;

            var sensitivity = 3.0 - (_config.SensitivityLevel - 1) * 0.4; // Level 1=3.0σ, 5=1.4σ
            var latest = revenues.Last();
            var zScore = (latest - mean) / stdDev;

            if (zScore > sensitivity)
            {
                AddAlert(AlertType.RevenueSpike, AlertSeverity.Info,
                    $"Revenue spike detected: ${snapshots.Last().TotalRevenue:F2} is {zScore:F1}σ above average (${mean:F2})",
                    new[] { "Capitalize on momentum — promote similar titles", "Check if a promotion is driving the spike", "Consider extending successful campaigns" });
            }
            else if (zScore < -sensitivity)
            {
                AddAlert(AlertType.RevenueDrop, AlertSeverity.Warning,
                    $"Revenue drop detected: ${snapshots.Last().TotalRevenue:F2} is {Math.Abs(zScore):F1}σ below average (${mean:F2})",
                    new[] { "Consider running a flash promotion", "Check if popular titles are out of stock", "Review if competitor pricing changed", "Send re-engagement emails to inactive customers" });
            }
        }

        private void DetectTrendShifts(List<RevenueSnapshot> snapshots)
        {
            if (snapshots.Count < 10) return;

            var midpoint = snapshots.Count / 2;
            var firstHalf = snapshots.Take(midpoint).Average(s => (double)s.TotalRevenue);
            var secondHalf = snapshots.Skip(midpoint).Average(s => (double)s.TotalRevenue);
            var changePercent = firstHalf > 0 ? ((secondHalf - firstHalf) / firstHalf) * 100 : 0;

            if (Math.Abs(changePercent) > 20)
            {
                var direction = changePercent > 0 ? "upward" : "downward";
                var severity = Math.Abs(changePercent) > 40 ? AlertSeverity.Critical : AlertSeverity.Warning;
                AddAlert(AlertType.TrendShift, severity,
                    $"Significant {direction} trend shift: {changePercent:+0.0;-0.0}% over the analysis window",
                    changePercent > 0
                        ? new[] { "Sustain growth: ensure inventory meets rising demand", "Identify and double down on top-performing genres" }
                        : new[] { "Urgent: investigate root cause of revenue decline", "Consider aggressive promotions to reverse trend", "Review pricing strategy competitiveness" });
            }
        }

        private void CheckForecastWarnings(List<RevenueForecast> forecast, List<RevenueSnapshot> snapshots)
        {
            if (forecast.Count == 0 || snapshots.Count == 0) return;

            var currentAvg = snapshots.Skip(Math.Max(0, snapshots.Count - 7)).Average(s => s.TotalRevenue);
            var forecastAvg = forecast.Average(f => f.PredictedRevenue);

            if (forecastAvg < currentAvg * 0.7m)
            {
                AddAlert(AlertType.ForecastWarning, AlertSeverity.Warning,
                    $"Forecast predicts {((1 - forecastAvg / currentAvg) * 100):F0}% revenue decline in coming week",
                    new[] { "Pre-emptive action: plan a promotion campaign", "Increase marketing spend", "Consider loyalty point bonuses to drive rentals" });
            }
        }

        private List<string> GenerateRecommendations(List<RevenueSnapshot> snapshots, Dictionary<string, GenreRevenueInfo> genreRevenue)
        {
            var recs = new List<string>();

            if (snapshots.Count >= 7)
            {
                var last7 = snapshots.Skip(Math.Max(0, snapshots.Count - 7)).ToList();
                var weekAvg = last7.Average(s => s.TotalRevenue);
                var monthAvg = snapshots.Average(s => s.TotalRevenue);
                if (weekAvg < monthAvg * 0.8m)
                    recs.Add("This week's revenue is below monthly average — consider a mid-week promotion");
            }

            var risingGenres = genreRevenue.Where(g => g.Value.Trend == "Rising").Select(g => g.Key).ToList();
            if (risingGenres.Any())
                recs.Add($"Genres trending up: {string.Join(", ", risingGenres)} — increase inventory in these categories");

            var decliningGenres = genreRevenue.Where(g => g.Value.Trend == "Declining").Select(g => g.Key).ToList();
            if (decliningGenres.Any())
                recs.Add($"Genres declining: {string.Join(", ", decliningGenres)} — consider discounting or bundling these titles");

            if (snapshots.Count >= 7)
            {
                var weekendRevenue = last7.Where(s => s.Date.DayOfWeek == DayOfWeek.Saturday || s.Date.DayOfWeek == DayOfWeek.Sunday).Sum(s => s.TotalRevenue);
                var weekdayRevenue = last7.Where(s => s.Date.DayOfWeek != DayOfWeek.Saturday && s.Date.DayOfWeek != DayOfWeek.Sunday).Sum(s => s.TotalRevenue);
                if (weekendRevenue > weekdayRevenue * 1.5m)
                    recs.Add("Weekend revenue significantly outperforms weekdays — launch weekday-specific deals");
            }

            if (!recs.Any())
                recs.Add("Revenue patterns look healthy — maintain current strategy");

            return recs;
        }

        private void AddAlert(AlertType type, AlertSeverity severity, string message, string[] recommendations)
        {
            // Avoid duplicate alerts within 24 hours
            var recentCutoff = _clock.Now.AddHours(-24);
            if (_alerts.Any(a => a.Type == type && a.Timestamp > recentCutoff && !a.Acknowledged))
                return;

            _alerts.Add(new RevenueAlert
            {
                Id = $"RA-{++_alertIdCounter:D4}",
                Type = type,
                Severity = severity,
                Message = message,
                Recommendations = recommendations.ToList(),
                Timestamp = _clock.Now,
                Acknowledged = false
            });

            // Keep only last 100 alerts
            while (_alerts.Count > 100)
                _alerts.RemoveAt(0);
        }
    }

    #region DTOs

    public class RevenueAlert
    {
        public string Id { get; set; }
        public AlertType Type { get; set; }
        public AlertSeverity Severity { get; set; }
        public string Message { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
        public DateTime Timestamp { get; set; }
        public bool Acknowledged { get; set; }
    }

    public enum AlertType { RevenueSpike, RevenueDrop, TrendShift, ForecastWarning, OpportunityDetected }
    public enum AlertSeverity { Info, Warning, Critical }

    public class RevenueForecast
    {
        public DateTime Date { get; set; }
        public decimal PredictedRevenue { get; set; }
        public decimal ConfidenceLevel { get; set; }
        public string Trend { get; set; }
    }

    public class RevenueSnapshot
    {
        public DateTime Date { get; set; }
        public decimal TotalRevenue { get; set; }
        public int RentalCount { get; set; }
        public decimal AvgTransactionValue { get; set; }
        public string TopGenre { get; set; }
        public string TopMovie { get; set; }
    }

    public class AlertConfig
    {
        public int SensitivityLevel { get; set; } = 3;
        public List<AlertType> EnabledAlertTypes { get; set; } = new List<AlertType>
        {
            AlertType.RevenueSpike, AlertType.RevenueDrop, AlertType.TrendShift,
            AlertType.ForecastWarning, AlertType.OpportunityDetected
        };
        public int ForecastHorizonDays { get; set; } = 7;
        public int AnomalyWindowDays { get; set; } = 30;
    }

    public class RevenueDashboard
    {
        public List<RevenueSnapshot> Snapshots { get; set; } = new List<RevenueSnapshot>();
        public List<RevenueAlert> ActiveAlerts { get; set; } = new List<RevenueAlert>();
        public List<RevenueForecast> Forecast { get; set; } = new List<RevenueForecast>();
        public Dictionary<string, GenreRevenueInfo> GenreBreakdown { get; set; } = new Dictionary<string, GenreRevenueInfo>();
        public List<string> Recommendations { get; set; } = new List<string>();
    }

    public class GenreRevenueInfo
    {
        public decimal Revenue { get; set; }
        public int Count { get; set; }
        public string Trend { get; set; } = "Stable";
    }

    #endregion
}
