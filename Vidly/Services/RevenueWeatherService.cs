using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous Revenue Weather Map Engine — treats revenue patterns like weather
    /// phenomena, detecting storms (sudden spikes), droughts (dry spells), fronts
    /// (genre distribution shifts), and forecasting upcoming conditions.
    /// </summary>
    public class RevenueWeatherService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly IClock _clock;
        private readonly WeatherEngineConfig _config;

        public RevenueWeatherService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            IClock clock,
            WeatherEngineConfig config = null)
        {
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (movieRepository == null) throw new ArgumentNullException("movieRepository");
            if (clock == null) throw new ArgumentNullException("clock");
            _rentalRepository = rentalRepository;
            _movieRepository = movieRepository;
            _clock = clock;
            _config = config ?? new WeatherEngineConfig();
        }

        /// <summary>
        /// Generate a full revenue weather report with phenomena, microclimates,
        /// forecasts, and autonomous insights.
        /// </summary>
        public RevenueWeatherReport Analyze()
        {
            var now = _clock.Now;
            var allRentals = _rentalRepository.GetAll();
            var allMovies = _movieRepository.GetAll();
            var windowStart = now.AddDays(-_config.WindowDays);

            var windowRentals = allRentals
                .Where(r => r.RentalDate >= windowStart && r.RentalDate <= now)
                .ToList();

            // Build movie lookup
            var movieLookup = allMovies.ToDictionary(m => m.Id);

            // Daily revenue series
            var dailyRevenue = BuildDailyRevenue(windowRentals, windowStart, now);

            // Detect phenomena
            var storms = DetectStorms(dailyRevenue, now);
            var droughts = DetectDroughts(dailyRevenue, now);
            var fronts = DetectFronts(windowRentals, movieLookup, windowStart, now);

            var allPhenomena = new List<RevenuePhenomenon>();
            allPhenomena.AddRange(storms);
            allPhenomena.AddRange(droughts);
            allPhenomena.AddRange(fronts);

            // Separate active vs recent
            var activePhenomena = allPhenomena
                .Where(p => p.EndDate == null || p.EndDate >= now.AddDays(-3))
                .ToList();
            var recentPhenomena = allPhenomena
                .Where(p => p.EndDate != null && p.EndDate < now.AddDays(-3))
                .ToList();

            // Genre microclimates
            var microclimates = BuildMicroclimates(windowRentals, movieLookup, allMovies, now);

            // Forecasts
            var forecasts = BuildForecasts(dailyRevenue, now);

            // Overall metrics
            double storeTemp = ComputeStoreTemperature(dailyRevenue);
            var overallCondition = ClassifyCondition(storeTemp, storms, droughts);
            double healthScore = ComputeHealthScore(storeTemp, dailyRevenue, storms, droughts);

            // Summary and insights
            string summary = GenerateSummary(overallCondition, storeTemp, activePhenomena, microclimates);
            var insights = GenerateInsights(activePhenomena, microclimates, dailyRevenue, forecasts);
            var warnings = GenerateWarnings(activePhenomena, forecasts, microclimates);

            return new RevenueWeatherReport
            {
                GeneratedAt = now,
                AnalysisWindowDays = _config.WindowDays,
                OverallCondition = overallCondition,
                StoreTemperature = Math.Round(storeTemp, 1),
                OverallSummary = summary,
                HealthScore = Math.Round(Math.Max(0, Math.Min(100, healthScore)), 1),
                ActivePhenomena = activePhenomena,
                RecentPhenomena = recentPhenomena,
                Microclimates = microclimates,
                Forecasts = forecasts,
                AutonomousInsights = insights,
                StormWarnings = warnings
            };
        }

        // ── Daily Revenue Series ──────────────────────────────────────

        private Dictionary<DateTime, double> BuildDailyRevenue(
            List<Rental> rentals, DateTime windowStart, DateTime now)
        {
            var daily = new Dictionary<DateTime, double>();
            for (var d = windowStart.Date; d <= now.Date; d = d.AddDays(1))
            {
                daily[d] = 0;
            }

            foreach (var r in rentals)
            {
                var day = r.RentalDate.Date;
                if (daily.ContainsKey(day))
                {
                    daily[day] += (double)r.DailyRate;
                }
            }

            return daily;
        }

        // ── Storm Detection ───────────────────────────────────────────

        private List<RevenuePhenomenon> DetectStorms(
            Dictionary<DateTime, double> daily, DateTime now)
        {
            var storms = new List<RevenuePhenomenon>();
            if (daily.Count < 3) return storms;

            var values = daily.Values.ToList();
            double mean = values.Average();
            double stddev = ComputeStdDev(values, mean);

            if (stddev < 0.01) return storms;

            double threshold = mean + _config.StormThresholdZScore * stddev;

            // Find consecutive storm days
            var sortedDays = daily.OrderBy(kv => kv.Key).ToList();
            DateTime? stormStart = null;
            double maxIntensity = 0;

            for (int i = 0; i < sortedDays.Count; i++)
            {
                var kv = sortedDays[i];
                if (kv.Value >= threshold)
                {
                    if (stormStart == null)
                        stormStart = kv.Key;

                    double zScore = (kv.Value - mean) / stddev;
                    maxIntensity = Math.Max(maxIntensity, Math.Min(100, zScore * 25));
                }
                else if (stormStart != null)
                {
                    storms.Add(new RevenuePhenomenon
                    {
                        Type = "Storm",
                        Description = string.Format("Revenue spike detected: daily revenue exceeded {0:F0}% above average",
                            ((sortedDays[i - 1].Value / mean) - 1) * 100),
                        Intensity = Math.Round(maxIntensity, 1),
                        DetectedAt = now,
                        StartDate = stormStart.Value,
                        EndDate = sortedDays[i - 1].Key,
                        AffectedArea = "store-wide",
                        ContributingFactors = new List<string> { "Revenue exceeded " + _config.StormThresholdZScore + " standard deviations above mean" }
                    });
                    stormStart = null;
                    maxIntensity = 0;
                }
            }

            // Handle storm at end of window
            if (stormStart != null)
            {
                storms.Add(new RevenuePhenomenon
                {
                    Type = "Storm",
                    Description = "Active revenue storm — daily revenue significantly above average",
                    Intensity = Math.Round(maxIntensity, 1),
                    DetectedAt = now,
                    StartDate = stormStart.Value,
                    EndDate = null,
                    AffectedArea = "store-wide",
                    ContributingFactors = new List<string> { "Revenue exceeding " + _config.StormThresholdZScore + " standard deviations" }
                });
            }

            return storms;
        }

        // ── Drought Detection ─────────────────────────────────────────

        private List<RevenuePhenomenon> DetectDroughts(
            Dictionary<DateTime, double> daily, DateTime now)
        {
            var droughts = new List<RevenuePhenomenon>();
            if (daily.Count < 3) return droughts;

            var values = daily.Values.ToList();
            double mean = values.Average();
            if (mean < 0.01) return droughts;

            double droughtThreshold = mean * (_config.DroughtThresholdPercent / 100.0);
            var sortedDays = daily.OrderBy(kv => kv.Key).ToList();

            DateTime? droughtStart = null;
            int consecutiveDays = 0;

            for (int i = 0; i < sortedDays.Count; i++)
            {
                if (sortedDays[i].Value <= droughtThreshold)
                {
                    if (droughtStart == null)
                        droughtStart = sortedDays[i].Key;
                    consecutiveDays++;
                }
                else
                {
                    if (droughtStart != null && consecutiveDays >= 3)
                    {
                        double intensity = Math.Min(100, consecutiveDays * 10);
                        droughts.Add(new RevenuePhenomenon
                        {
                            Type = "Drought",
                            Description = string.Format("Revenue drought: {0} consecutive days below {1:F0}% of average",
                                consecutiveDays, _config.DroughtThresholdPercent),
                            Intensity = intensity,
                            DetectedAt = now,
                            StartDate = droughtStart.Value,
                            EndDate = sortedDays[i - 1].Key,
                            AffectedArea = "store-wide",
                            ContributingFactors = new List<string>
                            {
                                string.Format("{0} days below drought threshold", consecutiveDays)
                            }
                        });
                    }
                    droughtStart = null;
                    consecutiveDays = 0;
                }
            }

            // Handle drought at end of window
            if (droughtStart != null && consecutiveDays >= 3)
            {
                droughts.Add(new RevenuePhenomenon
                {
                    Type = "Drought",
                    Description = string.Format("Active revenue drought: {0} consecutive low-revenue days",
                        consecutiveDays),
                    Intensity = Math.Min(100, consecutiveDays * 10),
                    DetectedAt = now,
                    StartDate = droughtStart.Value,
                    EndDate = null,
                    AffectedArea = "store-wide",
                    ContributingFactors = new List<string> { "Ongoing low-revenue period" }
                });
            }

            return droughts;
        }

        // ── Front Detection (Genre Shifts) ────────────────────────────

        private List<RevenuePhenomenon> DetectFronts(
            List<Rental> rentals, Dictionary<int, Movie> movieLookup,
            DateTime windowStart, DateTime now)
        {
            var fronts = new List<RevenuePhenomenon>();
            if (rentals.Count < 2) return fronts;

            var midPoint = windowStart.AddDays((now - windowStart).TotalDays / 2);

            var firstHalf = rentals.Where(r => r.RentalDate < midPoint).ToList();
            var secondHalf = rentals.Where(r => r.RentalDate >= midPoint).ToList();

            if (firstHalf.Count == 0 || secondHalf.Count == 0) return fronts;

            var firstGenreDist = ComputeGenreDistribution(firstHalf, movieLookup);
            var secondGenreDist = ComputeGenreDistribution(secondHalf, movieLookup);

            var allGenres = firstGenreDist.Keys.Union(secondGenreDist.Keys).ToList();

            foreach (var genre in allGenres)
            {
                double firstPct = firstGenreDist.ContainsKey(genre) ? firstGenreDist[genre] : 0;
                double secondPct = secondGenreDist.ContainsKey(genre) ? secondGenreDist[genre] : 0;
                double shift = secondPct - firstPct;

                if (Math.Abs(shift) >= 10)
                {
                    string direction = shift > 0 ? "advancing" : "retreating";
                    fronts.Add(new RevenuePhenomenon
                    {
                        Type = "Front",
                        Description = string.Format("{0} front {1}: genre share shifted {2:+0.0;-0.0}pp",
                            genre, direction, shift),
                        Intensity = Math.Min(100, Math.Abs(shift) * 2),
                        DetectedAt = now,
                        StartDate = midPoint,
                        EndDate = null,
                        AffectedArea = genre.ToString(),
                        ContributingFactors = new List<string>
                        {
                            string.Format("First half: {0:F1}%, Second half: {1:F1}%", firstPct, secondPct)
                        }
                    });
                }
            }

            return fronts;
        }

        private Dictionary<Genre, double> ComputeGenreDistribution(
            List<Rental> rentals, Dictionary<int, Movie> movieLookup)
        {
            var genreCounts = new Dictionary<Genre, int>();
            int total = 0;

            foreach (var r in rentals)
            {
                Movie movie;
                if (movieLookup.TryGetValue(r.MovieId, out movie) && movie.Genre.HasValue)
                {
                    if (!genreCounts.ContainsKey(movie.Genre.Value))
                        genreCounts[movie.Genre.Value] = 0;
                    genreCounts[movie.Genre.Value]++;
                    total++;
                }
            }

            var dist = new Dictionary<Genre, double>();
            if (total > 0)
            {
                foreach (var kv in genreCounts)
                {
                    dist[kv.Key] = (kv.Value * 100.0) / total;
                }
            }
            return dist;
        }

        // ── Genre Microclimates ───────────────────────────────────────

        private List<GenreMicroclimate> BuildMicroclimates(
            List<Rental> rentals, Dictionary<int, Movie> movieLookup,
            IReadOnlyList<Movie> allMovies, DateTime now)
        {
            var microclimates = new List<GenreMicroclimate>();
            if (rentals.Count == 0) return microclimates;

            var genreMovieCounts = allMovies
                .Where(m => m.Genre.HasValue)
                .GroupBy(m => m.Genre.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var genreRentals = new Dictionary<Genre, List<Rental>>();
            foreach (var r in rentals)
            {
                Movie movie;
                if (movieLookup.TryGetValue(r.MovieId, out movie) && movie.Genre.HasValue)
                {
                    if (!genreRentals.ContainsKey(movie.Genre.Value))
                        genreRentals[movie.Genre.Value] = new List<Rental>();
                    genreRentals[movie.Genre.Value].Add(r);
                }
            }

            if (genreRentals.Count == 0) return microclimates;

            double maxGenreRevenue = genreRentals.Values
                .Max(gr => gr.Sum(r => (double)r.DailyRate));

            foreach (var kv in genreRentals.OrderByDescending(g => g.Value.Count))
            {
                var genre = kv.Key;
                var gRentals = kv.Value;

                double totalRevenue = gRentals.Sum(r => (double)r.DailyRate);
                double temperature = maxGenreRevenue > 0
                    ? (totalRevenue / maxGenreRevenue) * 100
                    : 0;

                // Humidity: % of catalog titles that have been rented
                int catalogCount = genreMovieCounts.ContainsKey(genre) ? genreMovieCounts[genre] : 1;
                int rentedTitles = gRentals.Select(r => r.MovieId).Distinct().Count();
                double humidity = Math.Min(100, (rentedTitles * 100.0) / Math.Max(1, catalogCount));

                // Wind: compare recent 7 days vs prior 7 days
                var recent7 = gRentals.Where(r => r.RentalDate >= now.AddDays(-7)).ToList();
                var prior7 = gRentals.Where(r => r.RentalDate >= now.AddDays(-14) && r.RentalDate < now.AddDays(-7)).ToList();
                double recentRev = recent7.Sum(r => (double)r.DailyRate);
                double priorRev = prior7.Sum(r => (double)r.DailyRate);

                double windSpeed;
                string windDirection;
                if (priorRev < 0.01 && recentRev < 0.01)
                {
                    windSpeed = 0;
                    windDirection = "calm";
                }
                else if (priorRev < 0.01)
                {
                    windSpeed = 100;
                    windDirection = "rising";
                }
                else
                {
                    double changeRate = ((recentRev - priorRev) / priorRev) * 100;
                    windSpeed = Math.Min(100, Math.Abs(changeRate));
                    windDirection = changeRate > 5 ? "rising" : (changeRate < -5 ? "falling" : "stable");
                }

                // Pressure: demand relative to supply
                double rentalRate = (double)gRentals.Count / Math.Max(1, _config.WindowDays);
                double pressure = Math.Min(100, rentalRate * 100 / Math.Max(1, catalogCount));

                var condition = ClassifyGenreCondition(temperature, windDirection, humidity);

                string forecast = GenerateGenreForecast(genre, temperature, windDirection, windSpeed);
                var advisories = GenerateGenreAdvisories(genre, temperature, humidity, windDirection, windSpeed, pressure);

                microclimates.Add(new GenreMicroclimate
                {
                    Genre = genre,
                    Condition = condition,
                    Temperature = Math.Round(temperature, 1),
                    Humidity = Math.Round(humidity, 1),
                    WindSpeed = Math.Round(windSpeed, 1),
                    WindDirection = windDirection,
                    Pressure = Math.Round(pressure, 1),
                    Forecast = forecast,
                    Advisories = advisories
                });
            }

            return microclimates;
        }

        private WeatherCondition ClassifyGenreCondition(double temp, string windDir, double humidity)
        {
            if (temp >= 80 && windDir == "rising") return WeatherCondition.Heatwave;
            if (temp >= 70) return WeatherCondition.Sunny;
            if (temp >= 50) return WeatherCondition.PartlyCloudy;
            if (temp >= 30) return WeatherCondition.Cloudy;
            if (temp >= 10) return WeatherCondition.Rainy;
            return WeatherCondition.Drought;
        }

        private string GenerateGenreForecast(Genre genre, double temp, string windDir, double windSpeed)
        {
            if (windDir == "rising" && temp >= 60)
                return string.Format("{0} sector heating up — expect continued strong performance", genre);
            if (windDir == "rising" && temp < 60)
                return string.Format("{0} sector warming — emerging demand detected", genre);
            if (windDir == "falling" && temp >= 50)
                return string.Format("{0} sector cooling — monitor for sustained decline", genre);
            if (windDir == "falling" && temp < 50)
                return string.Format("{0} sector entering cold front — consider promotional support", genre);
            if (windDir == "stable" && temp >= 50)
                return string.Format("{0} sector stable with mild conditions", genre);
            return string.Format("{0} sector quiet — low activity expected", genre);
        }

        private List<string> GenerateGenreAdvisories(Genre genre, double temp, double humidity,
            string windDir, double windSpeed, double pressure)
        {
            var advisories = new List<string>();

            if (temp < 20)
                advisories.Add(string.Format("FROST WARNING: {0} revenue critically low", genre));
            if (humidity > 90)
                advisories.Add(string.Format("SATURATION ALERT: Nearly all {0} titles have been rented", genre));
            if (windDir == "falling" && windSpeed > 50)
                advisories.Add(string.Format("HIGH WIND WARNING: {0} revenue declining rapidly", genre));
            if (pressure > 80)
                advisories.Add(string.Format("HIGH PRESSURE: Strong demand for {0} — consider expanding catalog", genre));
            if (humidity < 20 && temp > 30)
                advisories.Add(string.Format("DRY CONDITIONS: Many {0} titles unseen — promote hidden gems", genre));

            return advisories;
        }

        // ── Forecasting ───────────────────────────────────────────────

        private List<RevenueForecast> BuildForecasts(
            Dictionary<DateTime, double> daily, DateTime now)
        {
            var forecasts = new List<RevenueForecast>();
            if (daily.Count < _config.MinDataPointsForForecast) return forecasts;

            // Use last 14 days for regression
            var recentDays = daily
                .Where(kv => kv.Key >= now.AddDays(-14))
                .OrderBy(kv => kv.Key)
                .ToList();

            if (recentDays.Count < 3) return forecasts;

            // Simple linear regression
            double[] x = new double[recentDays.Count];
            double[] y = new double[recentDays.Count];
            for (int i = 0; i < recentDays.Count; i++)
            {
                x[i] = i;
                y[i] = recentDays[i].Value;
            }

            double slope, intercept;
            LinearRegression(x, y, out slope, out intercept);

            double currentAvg = recentDays.Average(kv => kv.Value);

            // 7-day forecast
            double projected7 = 0;
            for (int d = 1; d <= 7; d++)
                projected7 += Math.Max(0, intercept + slope * (recentDays.Count + d));

            double conf7 = recentDays.Count >= 10 ? 70 : 50;
            forecasts.Add(new RevenueForecast
            {
                Period = "Next 7 days",
                ExpectedCondition = ClassifyForecastCondition(slope, currentAvg),
                ConfidencePercent = conf7,
                ExpectedRevenue = Math.Round(projected7, 2),
                TemperatureRangeLow = Math.Round(Math.Max(0, currentAvg - ComputeStdDev(y.ToList(), y.Average()) * 1.5), 1),
                TemperatureRangeHigh = Math.Round(currentAvg + ComputeStdDev(y.ToList(), y.Average()) * 1.5, 1),
                Watches = slope < -0.5 ? new List<string> { "Declining revenue trend detected" } : new List<string>(),
                Advisories = GenerateForecastAdvisories(slope, currentAvg)
            });

            // 30-day forecast
            double projected30 = 0;
            for (int d = 1; d <= 30; d++)
                projected30 += Math.Max(0, intercept + slope * (recentDays.Count + d));

            double conf30 = recentDays.Count >= 10 ? 45 : 30;
            forecasts.Add(new RevenueForecast
            {
                Period = "Next 30 days",
                ExpectedCondition = ClassifyForecastCondition(slope, currentAvg),
                ConfidencePercent = conf30,
                ExpectedRevenue = Math.Round(projected30, 2),
                TemperatureRangeLow = Math.Round(Math.Max(0, currentAvg - ComputeStdDev(y.ToList(), y.Average()) * 2.5), 1),
                TemperatureRangeHigh = Math.Round(currentAvg + ComputeStdDev(y.ToList(), y.Average()) * 2.5, 1),
                Watches = slope < -1 ? new List<string> { "Sustained decline may lead to drought" } : new List<string>(),
                Advisories = GenerateForecastAdvisories(slope, currentAvg)
            });

            return forecasts;
        }

        private WeatherCondition ClassifyForecastCondition(double slope, double avgRevenue)
        {
            if (slope > 1) return WeatherCondition.Sunny;
            if (slope > 0.2) return WeatherCondition.PartlyCloudy;
            if (slope > -0.2) return WeatherCondition.Cloudy;
            if (slope > -1) return WeatherCondition.Rainy;
            return WeatherCondition.Stormy;
        }

        private List<string> GenerateForecastAdvisories(double slope, double avgRevenue)
        {
            var advisories = new List<string>();
            if (slope > 2)
                advisories.Add("Strong upward trend — ensure adequate inventory");
            else if (slope > 0.5)
                advisories.Add("Moderate growth expected — good time for promotions");
            else if (slope < -2)
                advisories.Add("Sharp decline forecast — consider discount events");
            else if (slope < -0.5)
                advisories.Add("Revenue cooling — review pricing strategy");
            return advisories;
        }

        // ── Store Temperature & Overall Condition ─────────────────────

        private double ComputeStoreTemperature(Dictionary<DateTime, double> daily)
        {
            if (daily.Count == 0) return 0;

            var values = daily.Values.ToList();
            double mean = values.Average();
            double max = values.Max();

            if (max < 0.01) return 0;

            // Recent 7 days average vs overall
            var recent = daily
                .OrderByDescending(kv => kv.Key)
                .Take(7)
                .Select(kv => kv.Value)
                .ToList();

            double recentAvg = recent.Average();
            double temp = (recentAvg / max) * 100;

            return Math.Max(0, Math.Min(100, temp));
        }

        private WeatherCondition ClassifyCondition(double temperature,
            List<RevenuePhenomenon> storms, List<RevenuePhenomenon> droughts)
        {
            bool hasActiveStorms = storms.Any(s => s.EndDate == null);
            bool hasActiveDroughts = droughts.Any(d => d.EndDate == null);

            if (hasActiveStorms) return WeatherCondition.Stormy;
            if (hasActiveDroughts) return WeatherCondition.Drought;
            if (temperature >= 70) return WeatherCondition.Sunny;
            if (temperature >= 50) return WeatherCondition.PartlyCloudy;
            if (temperature >= 30) return WeatherCondition.Cloudy;
            if (temperature >= 15) return WeatherCondition.Rainy;
            return WeatherCondition.Cloudy;
        }

        private double ComputeHealthScore(double temperature,
            Dictionary<DateTime, double> daily,
            List<RevenuePhenomenon> storms,
            List<RevenuePhenomenon> droughts)
        {
            double score = temperature * 0.4;

            // Trend component (30%)
            if (daily.Count >= 7)
            {
                var recent = daily.OrderByDescending(kv => kv.Key).Take(7)
                    .Average(kv => kv.Value);
                var older = daily.OrderBy(kv => kv.Key).Take(7)
                    .Average(kv => kv.Value);

                double trendScore;
                if (older < 0.01)
                    trendScore = recent > 0 ? 100 : 50;
                else
                    trendScore = Math.Min(100, Math.Max(0, 50 + ((recent - older) / older) * 50));

                score += trendScore * 0.3;
            }
            else
            {
                score += 50 * 0.3;
            }

            // Stability component (30%): fewer active phenomena = better
            int activePhenomena = storms.Count(s => s.EndDate == null) +
                                  droughts.Count(d => d.EndDate == null);
            double stabilityScore = Math.Max(0, 100 - activePhenomena * 25);
            score += stabilityScore * 0.3;

            return score;
        }

        // ── Summary & Insights ────────────────────────────────────────

        private string GenerateSummary(WeatherCondition condition, double temperature,
            List<RevenuePhenomenon> activePhenomena, List<GenreMicroclimate> microclimates)
        {
            string condName;
            switch (condition)
            {
                case WeatherCondition.Sunny: condName = "Sunny"; break;
                case WeatherCondition.PartlyCloudy: condName = "Partly Cloudy"; break;
                case WeatherCondition.Cloudy: condName = "Cloudy"; break;
                case WeatherCondition.Rainy: condName = "Rainy"; break;
                case WeatherCondition.Stormy: condName = "Stormy"; break;
                case WeatherCondition.Drought: condName = "Drought"; break;
                case WeatherCondition.Heatwave: condName = "Heatwave"; break;
                default: condName = "Unknown"; break;
            }

            var parts = new List<string>();
            parts.Add(string.Format("Current conditions: {0} with store temperature at {1:F0}\u00b0.",
                condName, temperature));

            if (activePhenomena.Count > 0)
            {
                var stormCount = activePhenomena.Count(p => p.Type == "Storm");
                var droughtCount = activePhenomena.Count(p => p.Type == "Drought");
                var frontCount = activePhenomena.Count(p => p.Type == "Front");

                if (stormCount > 0)
                    parts.Add(string.Format("{0} revenue storm{1} currently active.",
                        stormCount, stormCount > 1 ? "s" : ""));
                if (droughtCount > 0)
                    parts.Add(string.Format("{0} drought zone{1} detected.",
                        droughtCount, droughtCount > 1 ? "s" : ""));
                if (frontCount > 0)
                    parts.Add(string.Format("{0} genre front{1} moving through the catalog.",
                        frontCount, frontCount > 1 ? "s" : ""));
            }

            var hotGenres = microclimates.Where(m => m.Temperature >= 70).ToList();
            if (hotGenres.Count > 0)
            {
                parts.Add(string.Format("Hot zones: {0}.",
                    string.Join(", ", hotGenres.Select(m => m.Genre.ToString()))));
            }

            return string.Join(" ", parts);
        }

        private List<string> GenerateInsights(
            List<RevenuePhenomenon> phenomena, List<GenreMicroclimate> microclimates,
            Dictionary<DateTime, double> daily, List<RevenueForecast> forecasts)
        {
            var insights = new List<string>();

            // Phenomenon-based insights
            foreach (var p in phenomena.Where(p => p.Type == "Storm"))
            {
                insights.Add(string.Format("Revenue storm in {0} — capitalize with cross-promotion and premium bundles",
                    p.AffectedArea));
            }

            foreach (var p in phenomena.Where(p => p.Type == "Drought"))
            {
                insights.Add("Revenue drought detected — consider flash sales, loyalty bonuses, or new release push");
            }

            foreach (var p in phenomena.Where(p => p.Type == "Front"))
            {
                insights.Add(string.Format("Genre shift in {0} — adjust shelf placement and marketing to match evolving demand",
                    p.AffectedArea));
            }

            // Microclimate insights
            var coldGenres = microclimates.Where(m => m.Temperature < 20).ToList();
            if (coldGenres.Count > 0)
            {
                insights.Add(string.Format("Cold zones ({0}) — investigate root cause: pricing, catalog freshness, or seasonal shift",
                    string.Join(", ", coldGenres.Select(m => m.Genre.ToString()))));
            }

            var risingGenres = microclimates.Where(m => m.WindDirection == "rising" && m.WindSpeed > 30).ToList();
            if (risingGenres.Count > 0)
            {
                insights.Add(string.Format("Rapidly warming: {0} — strong momentum, consider expanding inventory",
                    string.Join(", ", risingGenres.Select(m => m.Genre.ToString()))));
            }

            var saturatedGenres = microclimates.Where(m => m.Humidity > 80).ToList();
            if (saturatedGenres.Count > 0)
            {
                insights.Add(string.Format("High saturation in {0} — most titles have been rented, add fresh content",
                    string.Join(", ", saturatedGenres.Select(m => m.Genre.ToString()))));
            }

            // Forecast insights
            foreach (var f in forecasts)
            {
                if (f.ExpectedCondition == WeatherCondition.Stormy || f.ExpectedCondition == WeatherCondition.Rainy)
                {
                    insights.Add(string.Format("{0} outlook is {1} — prepare contingency promotions",
                        f.Period, f.ExpectedCondition));
                }
            }

            // Ensure at least one insight
            if (insights.Count == 0)
            {
                insights.Add("Store revenue patterns are stable — maintain current operations");
            }

            return insights;
        }

        private List<string> GenerateWarnings(
            List<RevenuePhenomenon> phenomena, List<RevenueForecast> forecasts,
            List<GenreMicroclimate> microclimates)
        {
            var warnings = new List<string>();

            // Active storm warnings
            var activeStorms = phenomena.Where(p => p.Type == "Storm" && p.EndDate == null).ToList();
            if (activeStorms.Count > 0)
            {
                warnings.Add("⚡ STORM WARNING: Active revenue spikes may be unsustainable — verify cause");
            }

            // Active drought warnings
            var activeDroughts = phenomena.Where(p => p.Type == "Drought" && p.EndDate == null).ToList();
            if (activeDroughts.Count > 0)
            {
                warnings.Add("🏜️ DROUGHT WARNING: Extended low-revenue period — immediate intervention recommended");
            }

            // Forecast-based warnings
            foreach (var f in forecasts)
            {
                if (f.Watches != null && f.Watches.Count > 0)
                {
                    foreach (var w in f.Watches)
                    {
                        warnings.Add(string.Format("⚠️ {0} WATCH: {1}", f.Period.ToUpper(), w));
                    }
                }
            }

            // Genre-based warnings
            var fallingFast = microclimates
                .Where(m => m.WindDirection == "falling" && m.WindSpeed > 60)
                .ToList();
            foreach (var m in fallingFast)
            {
                warnings.Add(string.Format("🌬️ HIGH WIND WARNING: {0} revenue declining rapidly ({1:F0}% change rate)",
                    m.Genre, m.WindSpeed));
            }

            return warnings;
        }

        // ── Math Helpers ──────────────────────────────────────────────

        private static double ComputeStdDev(List<double> values, double mean)
        {
            if (values.Count < 2) return 0;
            double sumSquares = values.Sum(v => (v - mean) * (v - mean));
            return Math.Sqrt(sumSquares / values.Count);
        }

        private static void LinearRegression(double[] x, double[] y,
            out double slope, out double intercept)
        {
            int n = x.Length;
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            for (int i = 0; i < n; i++)
            {
                sumX += x[i];
                sumY += y[i];
                sumXY += x[i] * y[i];
                sumX2 += x[i] * x[i];
            }

            double denom = n * sumX2 - sumX * sumX;
            if (Math.Abs(denom) < 1e-10)
            {
                slope = 0;
                intercept = n > 0 ? sumY / n : 0;
                return;
            }

            slope = (n * sumXY - sumX * sumY) / denom;
            intercept = (sumY - slope * sumX) / n;
        }
    }
}
