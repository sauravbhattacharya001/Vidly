using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous Rental Seasonality Engine — detects seasonal rental patterns,
    /// holiday effects, genre-season affinity, and generates demand forecasts
    /// with proactive stocking recommendations.
    /// 
    /// 7 engines:
    /// 1. Monthly Volume Profiler — baseline rental volume by month
    /// 2. Genre-Season Affinity Mapper — which genres peak in which seasons
    /// 3. Holiday Effect Detector — rental spikes/dips around holidays
    /// 4. Day-of-Week Rhythm Analyzer — weekday vs weekend patterns
    /// 5. Demand Forecaster — predict next month's demand by genre
    /// 6. Stocking Recommender — proactive inventory recommendations
    /// 7. Insight Generator — autonomous natural-language insights
    /// </summary>
    public class SeasonalityEngineService
    {
        private readonly IRentalRepository _rentalRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly IClock _clock;

        public SeasonalityEngineService(
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo,
            IClock clock)
        {
            if (rentalRepo == null) throw new ArgumentNullException("rentalRepo");
            if (movieRepo == null) throw new ArgumentNullException("movieRepo");
            if (clock == null) throw new ArgumentNullException("clock");
            _rentalRepo = rentalRepo;
            _movieRepo = movieRepo;
            _clock = clock;
        }

        // ----------------------------------------------------------------
        //  Public API
        // ----------------------------------------------------------------

        /// <summary>Generate a full seasonality report.</summary>
        public SeasonalityReport GenerateReport()
        {
            var now = _clock.Now;
            var rentals = _rentalRepo.GetAll();
            var movies = _movieRepo.GetAll();
            var movieLookup = movies.ToDictionary(m => m.Id, m => m);

            var monthlyProfile = BuildMonthlyProfile(rentals);
            var genreSeasonMap = BuildGenreSeasonAffinity(rentals, movieLookup);
            var holidayEffects = DetectHolidayEffects(rentals);
            var dayOfWeekRhythm = AnalyzeDayOfWeekRhythm(rentals);
            var forecasts = ForecastDemand(rentals, movieLookup, now);
            var recommendations = GenerateStockingRecommendations(forecasts, genreSeasonMap, now);
            var insights = GenerateInsights(monthlyProfile, genreSeasonMap, holidayEffects,
                dayOfWeekRhythm, forecasts, now);

            var score = ComputeHealthScore(monthlyProfile, genreSeasonMap, dayOfWeekRhythm);

            return new SeasonalityReport
            {
                GeneratedAt = now,
                MonthlyProfile = monthlyProfile,
                GenreSeasonAffinity = genreSeasonMap,
                HolidayEffects = holidayEffects,
                DayOfWeekRhythm = dayOfWeekRhythm,
                Forecasts = forecasts,
                Recommendations = recommendations,
                Insights = insights,
                SeasonalityScore = score
            };
        }

        /// <summary>Get the genre-season affinity map only.</summary>
        public List<GenreSeasonAffinity> GetGenreSeasonAffinity()
        {
            var rentals = _rentalRepo.GetAll();
            var movies = _movieRepo.GetAll();
            var movieLookup = movies.ToDictionary(m => m.Id, m => m);
            return BuildGenreSeasonAffinity(rentals, movieLookup);
        }

        /// <summary>Get demand forecast for the next N months.</summary>
        public List<DemandForecast> GetForecast(int months = 3)
        {
            var rentals = _rentalRepo.GetAll();
            var movies = _movieRepo.GetAll();
            var movieLookup = movies.ToDictionary(m => m.Id, m => m);
            var forecasts = ForecastDemand(rentals, movieLookup, _clock.Now);
            return forecasts.Take(months).ToList();
        }

        /// <summary>Get holiday effects analysis.</summary>
        public List<HolidayEffect> GetHolidayEffects()
        {
            return DetectHolidayEffects(_rentalRepo.GetAll());
        }

        // ----------------------------------------------------------------
        //  Engine 1: Monthly Volume Profiler
        // ----------------------------------------------------------------

        private List<MonthlyVolume> BuildMonthlyProfile(IReadOnlyList<Rental> rentals)
        {
            var result = new List<MonthlyVolume>();
            if (!rentals.Any()) return result;

            var byMonth = rentals.GroupBy(r => r.RentalDate.Month);
            var totalRentals = rentals.Count;
            var avgPerMonth = totalRentals / 12.0;

            for (int month = 1; month <= 12; month++)
            {
                var monthRentals = byMonth.FirstOrDefault(g => g.Key == month);
                var count = monthRentals?.Count() ?? 0;
                var revenue = monthRentals?.Sum(r => r.DailyRate *
                    Math.Max(1, (int)Math.Ceiling(((r.ReturnDate ?? r.DueDate) - r.RentalDate).TotalDays))) ?? 0m;

                var seasonalIndex = avgPerMonth > 0 ? count / avgPerMonth : 0;

                result.Add(new MonthlyVolume
                {
                    Month = month,
                    MonthName = new DateTime(2000, month, 1).ToString("MMMM"),
                    RentalCount = count,
                    Revenue = revenue,
                    SeasonalIndex = Math.Round(seasonalIndex, 2),
                    Classification = ClassifyVolume(seasonalIndex)
                });
            }

            return result;
        }

        private string ClassifyVolume(double index)
        {
            if (index >= 1.5) return "Peak";
            if (index >= 1.2) return "High";
            if (index >= 0.8) return "Normal";
            if (index >= 0.5) return "Low";
            return "Trough";
        }

        // ----------------------------------------------------------------
        //  Engine 2: Genre-Season Affinity Mapper
        // ----------------------------------------------------------------

        private List<GenreSeasonAffinity> BuildGenreSeasonAffinity(
            IReadOnlyList<Rental> rentals, Dictionary<int, Movie> movieLookup)
        {
            var result = new List<GenreSeasonAffinity>();

            var rentalsByGenreSeason = rentals
                .Where(r => movieLookup.ContainsKey(r.MovieId) && movieLookup[r.MovieId].Genre.HasValue)
                .GroupBy(r => new
                {
                    Genre = movieLookup[r.MovieId].Genre.Value,
                    Season = GetSeason(r.RentalDate.Month)
                });

            var totalByGenre = rentals
                .Where(r => movieLookup.ContainsKey(r.MovieId) && movieLookup[r.MovieId].Genre.HasValue)
                .GroupBy(r => movieLookup[r.MovieId].Genre.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var group in rentalsByGenreSeason)
            {
                var genreTotal = totalByGenre.ContainsKey(group.Key.Genre) ? totalByGenre[group.Key.Genre] : 0;
                var affinityScore = genreTotal > 0 ? (group.Count() * 4.0) / genreTotal : 0;

                result.Add(new GenreSeasonAffinity
                {
                    Genre = group.Key.Genre,
                    GenreName = group.Key.Genre.ToString(),
                    Season = group.Key.Season,
                    RentalCount = group.Count(),
                    AffinityScore = Math.Round(affinityScore, 2),
                    Strength = ClassifyAffinity(affinityScore)
                });
            }

            return result.OrderByDescending(a => a.AffinityScore).ToList();
        }

        private string ClassifyAffinity(double score)
        {
            if (score >= 2.0) return "Strong";
            if (score >= 1.5) return "Moderate";
            if (score >= 1.0) return "Normal";
            if (score >= 0.5) return "Weak";
            return "Absent";
        }

        // ----------------------------------------------------------------
        //  Engine 3: Holiday Effect Detector
        // ----------------------------------------------------------------

        private static readonly List<HolidayDefinition> KnownHolidays = new List<HolidayDefinition>
        {
            new HolidayDefinition { Name = "New Year", Month = 1, DayStart = 1, DayEnd = 3 },
            new HolidayDefinition { Name = "Valentine's Day", Month = 2, DayStart = 13, DayEnd = 15 },
            new HolidayDefinition { Name = "Spring Break", Month = 3, DayStart = 15, DayEnd = 31 },
            new HolidayDefinition { Name = "Easter Weekend", Month = 4, DayStart = 1, DayEnd = 7 },
            new HolidayDefinition { Name = "Memorial Day", Month = 5, DayStart = 25, DayEnd = 31 },
            new HolidayDefinition { Name = "Independence Day", Month = 7, DayStart = 2, DayEnd = 6 },
            new HolidayDefinition { Name = "Labor Day", Month = 9, DayStart = 1, DayEnd = 7 },
            new HolidayDefinition { Name = "Halloween", Month = 10, DayStart = 28, DayEnd = 31 },
            new HolidayDefinition { Name = "Thanksgiving", Month = 11, DayStart = 22, DayEnd = 28 },
            new HolidayDefinition { Name = "Christmas", Month = 12, DayStart = 22, DayEnd = 31 },
            new HolidayDefinition { Name = "Summer Peak", Month = 6, DayStart = 1, DayEnd = 30 },
            new HolidayDefinition { Name = "Back to School", Month = 8, DayStart = 15, DayEnd = 31 }
        };

        private List<HolidayEffect> DetectHolidayEffects(IReadOnlyList<Rental> rentals)
        {
            var result = new List<HolidayEffect>();
            if (!rentals.Any()) return result;

            // Compute baseline daily average
            var minDate = rentals.Min(r => r.RentalDate);
            var maxDate = rentals.Max(r => r.RentalDate);
            var totalDays = Math.Max(1, (maxDate - minDate).TotalDays);
            var baselineDailyAvg = rentals.Count / totalDays;

            foreach (var holiday in KnownHolidays)
            {
                var holidayRentals = rentals.Where(r =>
                    r.RentalDate.Month == holiday.Month &&
                    r.RentalDate.Day >= holiday.DayStart &&
                    r.RentalDate.Day <= holiday.DayEnd).ToList();

                if (!holidayRentals.Any()) continue;

                var holidayDays = holiday.DayEnd - holiday.DayStart + 1;
                var holidayDailyAvg = (double)holidayRentals.Count / holidayDays;
                var liftPercent = baselineDailyAvg > 0
                    ? ((holidayDailyAvg - baselineDailyAvg) / baselineDailyAvg) * 100.0
                    : 0;

                result.Add(new HolidayEffect
                {
                    HolidayName = holiday.Name,
                    Month = holiday.Month,
                    RentalCount = holidayRentals.Count,
                    DailyAverage = Math.Round(holidayDailyAvg, 2),
                    BaselineDailyAverage = Math.Round(baselineDailyAvg, 2),
                    LiftPercent = Math.Round(liftPercent, 1),
                    Impact = ClassifyHolidayImpact(liftPercent),
                    TopGenres = GetTopGenresForRentals(holidayRentals,
                        _movieRepo.GetAll().ToDictionary(m => m.Id, m => m))
                });
            }

            return result.OrderByDescending(h => h.LiftPercent).ToList();
        }

        private string ClassifyHolidayImpact(double liftPercent)
        {
            if (liftPercent >= 100) return "Massive Surge";
            if (liftPercent >= 50) return "Strong Boost";
            if (liftPercent >= 20) return "Moderate Boost";
            if (liftPercent >= -20) return "Neutral";
            if (liftPercent >= -50) return "Moderate Dip";
            return "Sharp Decline";
        }

        private List<string> GetTopGenresForRentals(
            List<Rental> rentals, Dictionary<int, Movie> movieLookup)
        {
            return rentals
                .Where(r => movieLookup.ContainsKey(r.MovieId) && movieLookup[r.MovieId].Genre.HasValue)
                .GroupBy(r => movieLookup[r.MovieId].Genre.Value)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => g.Key.ToString())
                .ToList();
        }

        // ----------------------------------------------------------------
        //  Engine 4: Day-of-Week Rhythm Analyzer
        // ----------------------------------------------------------------

        private List<DayOfWeekVolume> AnalyzeDayOfWeekRhythm(IReadOnlyList<Rental> rentals)
        {
            var result = new List<DayOfWeekVolume>();
            if (!rentals.Any()) return result;

            var avgPerDay = rentals.Count / 7.0;
            var byDay = rentals.GroupBy(r => r.RentalDate.DayOfWeek);

            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            {
                var dayGroup = byDay.FirstOrDefault(g => g.Key == day);
                var count = dayGroup?.Count() ?? 0;
                var index = avgPerDay > 0 ? count / avgPerDay : 0;
                var isWeekend = day == DayOfWeek.Saturday || day == DayOfWeek.Sunday;

                result.Add(new DayOfWeekVolume
                {
                    Day = day,
                    DayName = day.ToString(),
                    RentalCount = count,
                    VolumeIndex = Math.Round(index, 2),
                    IsWeekend = isWeekend,
                    Classification = ClassifyVolume(index)
                });
            }

            return result;
        }

        // ----------------------------------------------------------------
        //  Engine 5: Demand Forecaster
        // ----------------------------------------------------------------

        private List<DemandForecast> ForecastDemand(
            IReadOnlyList<Rental> rentals, Dictionary<int, Movie> movieLookup, DateTime now)
        {
            var result = new List<DemandForecast>();
            if (!rentals.Any()) return result;

            // Compute historical monthly averages per genre
            var genreMonthCounts = rentals
                .Where(r => movieLookup.ContainsKey(r.MovieId) && movieLookup[r.MovieId].Genre.HasValue)
                .GroupBy(r => new { Genre = movieLookup[r.MovieId].Genre.Value, Month = r.RentalDate.Month })
                .ToDictionary(g => g.Key, g => g.Count());

            // Count distinct years in data for averaging
            var yearSpan = Math.Max(1, rentals.Select(r => r.RentalDate.Year).Distinct().Count());

            // Forecast next 3 months
            for (int offset = 1; offset <= 3; offset++)
            {
                var targetDate = now.AddMonths(offset);
                var targetMonth = targetDate.Month;
                var genreForecasts = new List<GenreDemandForecast>();

                foreach (Genre genre in Enum.GetValues(typeof(Genre)))
                {
                    var key = new { Genre = genre, Month = targetMonth };
                    var historicalCount = genreMonthCounts.ContainsKey(key) ? genreMonthCounts[key] : 0;
                    var predicted = Math.Round((double)historicalCount / yearSpan, 1);

                    // Confidence based on data availability
                    var confidence = historicalCount > 0 ? Math.Min(1.0, historicalCount / (yearSpan * 5.0)) : 0;

                    genreForecasts.Add(new GenreDemandForecast
                    {
                        Genre = genre,
                        GenreName = genre.ToString(),
                        PredictedRentals = predicted,
                        Confidence = Math.Round(confidence, 2)
                    });
                }

                var totalPredicted = genreForecasts.Sum(f => f.PredictedRentals);

                result.Add(new DemandForecast
                {
                    Month = targetMonth,
                    MonthName = new DateTime(2000, targetMonth, 1).ToString("MMMM"),
                    Year = targetDate.Year,
                    TotalPredictedRentals = totalPredicted,
                    GenreBreakdown = genreForecasts.OrderByDescending(f => f.PredictedRentals).ToList(),
                    Season = GetSeason(targetMonth)
                });
            }

            return result;
        }

        // ----------------------------------------------------------------
        //  Engine 6: Stocking Recommender
        // ----------------------------------------------------------------

        private List<StockingRecommendation> GenerateStockingRecommendations(
            List<DemandForecast> forecasts, List<GenreSeasonAffinity> affinities, DateTime now)
        {
            var result = new List<StockingRecommendation>();
            if (!forecasts.Any()) return result;

            var nextMonthForecast = forecasts.First();
            var nextSeason = GetSeason(nextMonthForecast.Month);

            // Get top genre affinities for the upcoming season
            var seasonAffinities = affinities
                .Where(a => a.Season == nextSeason)
                .OrderByDescending(a => a.AffinityScore)
                .ToList();

            foreach (var genreForecast in nextMonthForecast.GenreBreakdown.Where(g => g.PredictedRentals > 0))
            {
                var affinity = seasonAffinities.FirstOrDefault(a => a.Genre == genreForecast.Genre);
                var affinityScore = affinity?.AffinityScore ?? 1.0;

                var urgency = "Low";
                if (affinityScore >= 2.0 && genreForecast.PredictedRentals >= 5)
                    urgency = "Critical";
                else if (affinityScore >= 1.5 || genreForecast.PredictedRentals >= 3)
                    urgency = "High";
                else if (affinityScore >= 1.0)
                    urgency = "Medium";

                result.Add(new StockingRecommendation
                {
                    Genre = genreForecast.Genre,
                    GenreName = genreForecast.GenreName,
                    TargetMonth = nextMonthForecast.MonthName,
                    PredictedDemand = genreForecast.PredictedRentals,
                    SeasonalAffinity = affinityScore,
                    Urgency = urgency,
                    Reason = $"{genreForecast.GenreName} has {affinityScore:F1}x seasonal affinity in {nextSeason} " +
                             $"with ~{genreForecast.PredictedRentals:F0} predicted rentals"
                });
            }

            return result.OrderByDescending(r => r.PredictedDemand * r.SeasonalAffinity).ToList();
        }

        // ----------------------------------------------------------------
        //  Engine 7: Insight Generator
        // ----------------------------------------------------------------

        private List<SeasonalityInsight> GenerateInsights(
            List<MonthlyVolume> monthlyProfile,
            List<GenreSeasonAffinity> affinities,
            List<HolidayEffect> holidays,
            List<DayOfWeekVolume> dayOfWeek,
            List<DemandForecast> forecasts,
            DateTime now)
        {
            var insights = new List<SeasonalityInsight>();

            // Peak month insight
            var peakMonth = monthlyProfile.OrderByDescending(m => m.RentalCount).FirstOrDefault();
            if (peakMonth != null && peakMonth.RentalCount > 0)
            {
                insights.Add(new SeasonalityInsight
                {
                    Category = "Peak Season",
                    Title = $"{peakMonth.MonthName} is the busiest month",
                    Description = $"{peakMonth.MonthName} has a seasonal index of {peakMonth.SeasonalIndex:F2}x " +
                                  $"with {peakMonth.RentalCount} rentals",
                    Severity = peakMonth.SeasonalIndex >= 1.5 ? "High" : "Medium",
                    Actionable = true
                });
            }

            // Trough month insight
            var troughMonth = monthlyProfile.Where(m => m.RentalCount > 0)
                .OrderBy(m => m.RentalCount).FirstOrDefault();
            if (troughMonth != null && peakMonth != null && troughMonth.Month != peakMonth.Month)
            {
                insights.Add(new SeasonalityInsight
                {
                    Category = "Slow Season",
                    Title = $"{troughMonth.MonthName} needs attention",
                    Description = $"{troughMonth.MonthName} has the lowest volume with seasonal index {troughMonth.SeasonalIndex:F2}x — " +
                                  "consider promotions or events to boost rentals",
                    Severity = troughMonth.SeasonalIndex < 0.5 ? "High" : "Low",
                    Actionable = true
                });
            }

            // Strongest genre-season affinity
            var strongestAffinity = affinities.FirstOrDefault();
            if (strongestAffinity != null && strongestAffinity.AffinityScore >= 1.5)
            {
                insights.Add(new SeasonalityInsight
                {
                    Category = "Genre Affinity",
                    Title = $"{strongestAffinity.GenreName} dominates in {strongestAffinity.Season}",
                    Description = $"{strongestAffinity.GenreName} has {strongestAffinity.AffinityScore:F1}x affinity " +
                                  $"in {strongestAffinity.Season} — stock up before the season starts",
                    Severity = "Medium",
                    Actionable = true
                });
            }

            // Holiday with biggest lift
            var biggestHoliday = holidays.FirstOrDefault();
            if (biggestHoliday != null && biggestHoliday.LiftPercent > 20)
            {
                insights.Add(new SeasonalityInsight
                {
                    Category = "Holiday Effect",
                    Title = $"{biggestHoliday.HolidayName} drives {biggestHoliday.LiftPercent:F0}% lift",
                    Description = $"Rentals during {biggestHoliday.HolidayName} average {biggestHoliday.DailyAverage:F1}/day " +
                                  $"vs baseline {biggestHoliday.BaselineDailyAverage:F1}/day",
                    Severity = biggestHoliday.LiftPercent >= 100 ? "High" : "Medium",
                    Actionable = true
                });
            }

            // Weekend vs weekday pattern
            var weekendAvg = dayOfWeek.Where(d => d.IsWeekend).Average(d => d.RentalCount);
            var weekdayAvg = dayOfWeek.Where(d => !d.IsWeekend).Average(d => d.RentalCount);
            if (weekdayAvg > 0)
            {
                var weekendLift = ((weekendAvg - weekdayAvg) / weekdayAvg) * 100;
                if (Math.Abs(weekendLift) > 10)
                {
                    var direction = weekendLift > 0 ? "higher" : "lower";
                    insights.Add(new SeasonalityInsight
                    {
                        Category = "Weekly Rhythm",
                        Title = $"Weekends are {Math.Abs(weekendLift):F0}% {direction} than weekdays",
                        Description = $"Weekend average: {weekendAvg:F0} rentals, weekday average: {weekdayAvg:F0} rentals — " +
                                      (weekendLift > 0
                                          ? "ensure weekend staffing and inventory"
                                          : "consider weekday promotions to boost traffic"),
                        Severity = "Medium",
                        Actionable = true
                    });
                }
            }

            // Forecast alert for next month
            var nextForecast = forecasts.FirstOrDefault();
            if (nextForecast != null)
            {
                var topGenre = nextForecast.GenreBreakdown.FirstOrDefault();
                if (topGenre != null)
                {
                    insights.Add(new SeasonalityInsight
                    {
                        Category = "Forecast",
                        Title = $"Next month ({nextForecast.MonthName}): ~{nextForecast.TotalPredictedRentals:F0} rentals expected",
                        Description = $"Top genre: {topGenre.GenreName} with ~{topGenre.PredictedRentals:F0} predicted rentals. " +
                                      $"Season: {nextForecast.Season}",
                        Severity = "Info",
                        Actionable = false
                    });
                }
            }

            return insights;
        }

        // ----------------------------------------------------------------
        //  Health Score
        // ----------------------------------------------------------------

        private int ComputeHealthScore(
            List<MonthlyVolume> monthly,
            List<GenreSeasonAffinity> affinities,
            List<DayOfWeekVolume> dayOfWeek)
        {
            if (!monthly.Any()) return 0;

            double score = 50; // base

            // Data richness: reward having data across all months
            var monthsWithData = monthly.Count(m => m.RentalCount > 0);
            score += (monthsWithData / 12.0) * 20; // up to +20

            // Seasonal variation: some variation is good (means patterns exist)
            var indices = monthly.Where(m => m.RentalCount > 0).Select(m => m.SeasonalIndex).ToList();
            if (indices.Count >= 2)
            {
                var stdDev = ComputeStdDev(indices);
                // Moderate variation (0.2-0.6) is healthy — too much or too little is suboptimal
                if (stdDev >= 0.2 && stdDev <= 0.6)
                    score += 15;
                else if (stdDev < 0.2)
                    score += 5; // too flat
                else
                    score += 8; // too volatile
            }

            // Genre diversity in affinities
            var distinctGenres = affinities.Select(a => a.Genre).Distinct().Count();
            score += Math.Min(15, distinctGenres * 1.5);

            // Day-of-week balance
            if (dayOfWeek.Any())
            {
                var dowIndices = dayOfWeek.Select(d => d.VolumeIndex).ToList();
                var dowStdDev = ComputeStdDev(dowIndices);
                if (dowStdDev < 0.3)
                    score += 10; // well balanced
                else if (dowStdDev < 0.6)
                    score += 5;
            }

            return (int)Math.Round(Math.Max(0, Math.Min(100, score)));
        }

        // ----------------------------------------------------------------
        //  Helpers
        // ----------------------------------------------------------------

        private static string GetSeason(int month)
        {
            if (month >= 3 && month <= 5) return "Spring";
            if (month >= 6 && month <= 8) return "Summer";
            if (month >= 9 && month <= 11) return "Fall";
            return "Winter";
        }

        private static double ComputeStdDev(List<double> values)
        {
            if (values.Count < 2) return 0;
            var mean = values.Average();
            var sumSqDiff = values.Sum(v => (v - mean) * (v - mean));
            return Math.Sqrt(sumSqDiff / values.Count);
        }
    }

    // ====================================================================
    //  Models
    // ====================================================================

    public class SeasonalityReport
    {
        public DateTime GeneratedAt { get; set; }
        public List<MonthlyVolume> MonthlyProfile { get; set; }
        public List<GenreSeasonAffinity> GenreSeasonAffinity { get; set; }
        public List<HolidayEffect> HolidayEffects { get; set; }
        public List<DayOfWeekVolume> DayOfWeekRhythm { get; set; }
        public List<DemandForecast> Forecasts { get; set; }
        public List<StockingRecommendation> Recommendations { get; set; }
        public List<SeasonalityInsight> Insights { get; set; }
        public int SeasonalityScore { get; set; }
    }

    public class MonthlyVolume
    {
        public int Month { get; set; }
        public string MonthName { get; set; }
        public int RentalCount { get; set; }
        public decimal Revenue { get; set; }
        public double SeasonalIndex { get; set; }
        public string Classification { get; set; }
    }

    public class GenreSeasonAffinity
    {
        public Genre Genre { get; set; }
        public string GenreName { get; set; }
        public string Season { get; set; }
        public int RentalCount { get; set; }
        public double AffinityScore { get; set; }
        public string Strength { get; set; }
    }

    public class HolidayEffect
    {
        public string HolidayName { get; set; }
        public int Month { get; set; }
        public int RentalCount { get; set; }
        public double DailyAverage { get; set; }
        public double BaselineDailyAverage { get; set; }
        public double LiftPercent { get; set; }
        public string Impact { get; set; }
        public List<string> TopGenres { get; set; }
    }

    public class DayOfWeekVolume
    {
        public DayOfWeek Day { get; set; }
        public string DayName { get; set; }
        public int RentalCount { get; set; }
        public double VolumeIndex { get; set; }
        public bool IsWeekend { get; set; }
        public string Classification { get; set; }
    }

    public class DemandForecast
    {
        public int Month { get; set; }
        public string MonthName { get; set; }
        public int Year { get; set; }
        public double TotalPredictedRentals { get; set; }
        public List<GenreDemandForecast> GenreBreakdown { get; set; }
        public string Season { get; set; }
    }

    public class GenreDemandForecast
    {
        public Genre Genre { get; set; }
        public string GenreName { get; set; }
        public double PredictedRentals { get; set; }
        public double Confidence { get; set; }
    }

    public class StockingRecommendation
    {
        public Genre Genre { get; set; }
        public string GenreName { get; set; }
        public string TargetMonth { get; set; }
        public double PredictedDemand { get; set; }
        public double SeasonalAffinity { get; set; }
        public string Urgency { get; set; }
        public string Reason { get; set; }
    }

    public class SeasonalityInsight
    {
        public string Category { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Severity { get; set; }
        public bool Actionable { get; set; }
    }

    internal class HolidayDefinition
    {
        public string Name { get; set; }
        public int Month { get; set; }
        public int DayStart { get; set; }
        public int DayEnd { get; set; }
    }
}
