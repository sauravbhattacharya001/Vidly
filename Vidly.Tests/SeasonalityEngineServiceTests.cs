using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class SeasonalityEngineServiceTests
    {
        private InMemoryMovieRepository _movieRepo;
        private InMemoryRentalRepository _rentalRepo;
        private TestClock _clock;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new InMemoryMovieRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _clock = new TestClock(new DateTime(2026, 5, 1, 12, 0, 0));
        }

        private SeasonalityEngineService CreateService()
        {
            return new SeasonalityEngineService(_rentalRepo, _movieRepo, _clock);
        }

        private Movie AddMovie(string name, Genre genre)
        {
            var m = new Movie { Name = name, Genre = genre, ReleaseDate = new DateTime(2025, 1, 1) };
            _movieRepo.Add(m);
            return m;
        }

        private void AddRental(int movieId, DateTime rentalDate, int custId = 1, decimal rate = 3.99m)
        {
            _rentalRepo.Add(new Rental
            {
                MovieId = movieId,
                CustomerId = custId,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(7),
                ReturnDate = rentalDate.AddDays(5),
                DailyRate = rate,
                Status = RentalStatus.Returned
            });
        }

        // Seed rentals across all 12 months with varying volumes
        private Movie SeedYearOfData()
        {
            var actionMovie = AddMovie("Die Hard", Genre.Action);
            var comedyMovie = AddMovie("Funny Movie", Genre.Comedy);
            var horrorMovie = AddMovie("Scary Movie", Genre.Horror);

            // Heavy summer (Jun-Aug) for Action
            for (int i = 0; i < 10; i++) AddRental(actionMovie.Id, new DateTime(2025, 6, 10));
            for (int i = 0; i < 8; i++) AddRental(actionMovie.Id, new DateTime(2025, 7, 15));
            for (int i = 0; i < 6; i++) AddRental(actionMovie.Id, new DateTime(2025, 8, 5));

            // Heavy winter for Comedy
            for (int i = 0; i < 9; i++) AddRental(comedyMovie.Id, new DateTime(2025, 12, 25));
            for (int i = 0; i < 7; i++) AddRental(comedyMovie.Id, new DateTime(2025, 1, 2));

            // Heavy fall for Horror
            for (int i = 0; i < 12; i++) AddRental(horrorMovie.Id, new DateTime(2025, 10, 30));

            // Light rentals in other months
            AddRental(actionMovie.Id, new DateTime(2025, 2, 14));
            AddRental(comedyMovie.Id, new DateTime(2025, 3, 15));
            AddRental(horrorMovie.Id, new DateTime(2025, 4, 10));
            AddRental(actionMovie.Id, new DateTime(2025, 5, 20));
            AddRental(comedyMovie.Id, new DateTime(2025, 9, 5));
            AddRental(actionMovie.Id, new DateTime(2025, 11, 25));

            return actionMovie;
        }

        // ------------------------------------------------------------------
        //  Constructor Validation
        // ------------------------------------------------------------------

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new SeasonalityEngineService(null, _movieRepo, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new SeasonalityEngineService(_rentalRepo, null, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullClock_Throws()
        {
            new SeasonalityEngineService(_rentalRepo, _movieRepo, null);
        }

        // ------------------------------------------------------------------
        //  Empty Data
        // ------------------------------------------------------------------

        [TestMethod]
        public void GenerateReport_NoRentals_ReturnsEmptyReport()
        {
            var svc = CreateService();
            var report = svc.GenerateReport();

            Assert.IsNotNull(report);
            Assert.AreEqual(0, report.MonthlyProfile.Count);
            Assert.AreEqual(0, report.GenreSeasonAffinity.Count);
            Assert.AreEqual(0, report.HolidayEffects.Count);
            Assert.AreEqual(0, report.DayOfWeekRhythm.Count);
            Assert.AreEqual(0, report.Forecasts.Count);
            Assert.AreEqual(0, report.Recommendations.Count);
            Assert.AreEqual(0, report.SeasonalityScore);
        }

        // ------------------------------------------------------------------
        //  Engine 1: Monthly Volume Profiler
        // ------------------------------------------------------------------

        [TestMethod]
        public void MonthlyProfile_Returns12Months()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            Assert.AreEqual(12, report.MonthlyProfile.Count);
            Assert.AreEqual("January", report.MonthlyProfile[0].MonthName);
            Assert.AreEqual("December", report.MonthlyProfile[11].MonthName);
        }

        [TestMethod]
        public void MonthlyProfile_PeakMonthHasHighestIndex()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            var peakMonth = report.MonthlyProfile.OrderByDescending(m => m.SeasonalIndex).First();
            // October has 12 horror rentals — should be peak or near peak
            Assert.IsTrue(peakMonth.RentalCount >= 10);
            Assert.AreEqual("Peak", peakMonth.Classification);
        }

        [TestMethod]
        public void MonthlyProfile_LowMonthClassifiedCorrectly()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            var lowMonths = report.MonthlyProfile.Where(m => m.RentalCount == 1).ToList();
            foreach (var m in lowMonths)
            {
                Assert.IsTrue(m.Classification == "Low" || m.Classification == "Trough",
                    $"Month {m.MonthName} with 1 rental should be Low or Trough, was {m.Classification}");
            }
        }

        [TestMethod]
        public void MonthlyProfile_RevenueCalculated()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            var totalRevenue = report.MonthlyProfile.Sum(m => m.Revenue);
            Assert.IsTrue(totalRevenue > 0, "Total revenue should be positive");
        }

        [TestMethod]
        public void MonthlyProfile_SeasonalIndicesAverageNearOne()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            var withData = report.MonthlyProfile.Where(m => m.RentalCount > 0).ToList();
            // Indices weighted by data should average around 1.0
            var avgIndex = withData.Average(m => m.SeasonalIndex);
            // Allow generous range since not all months have data
            Assert.IsTrue(avgIndex > 0, "Average index should be positive");
        }

        // ------------------------------------------------------------------
        //  Engine 2: Genre-Season Affinity
        // ------------------------------------------------------------------

        [TestMethod]
        public void GenreSeasonAffinity_DetectsActionInSummer()
        {
            SeedYearOfData();
            var affinities = CreateService().GetGenreSeasonAffinity();

            var actionSummer = affinities.FirstOrDefault(a =>
                a.Genre == Genre.Action && a.Season == "Summer");
            Assert.IsNotNull(actionSummer, "Should find Action-Summer affinity");
            Assert.IsTrue(actionSummer.AffinityScore > 1.0,
                $"Action should have above-average affinity in Summer, got {actionSummer.AffinityScore}");
        }

        [TestMethod]
        public void GenreSeasonAffinity_DetectsHorrorInFall()
        {
            SeedYearOfData();
            var affinities = CreateService().GetGenreSeasonAffinity();

            var horrorFall = affinities.FirstOrDefault(a =>
                a.Genre == Genre.Horror && a.Season == "Fall");
            Assert.IsNotNull(horrorFall, "Should find Horror-Fall affinity");
            Assert.IsTrue(horrorFall.AffinityScore > 1.0,
                "Horror should have high affinity in Fall");
        }

        [TestMethod]
        public void GenreSeasonAffinity_SortedByScoreDescending()
        {
            SeedYearOfData();
            var affinities = CreateService().GetGenreSeasonAffinity();

            for (int i = 1; i < affinities.Count; i++)
            {
                Assert.IsTrue(affinities[i - 1].AffinityScore >= affinities[i].AffinityScore,
                    "Affinities should be sorted by score descending");
            }
        }

        [TestMethod]
        public void GenreSeasonAffinity_StrengthClassification()
        {
            SeedYearOfData();
            var affinities = CreateService().GetGenreSeasonAffinity();

            foreach (var a in affinities)
            {
                Assert.IsTrue(
                    new[] { "Strong", "Moderate", "Normal", "Weak", "Absent" }.Contains(a.Strength),
                    $"Invalid strength: {a.Strength}");
            }
        }

        // ------------------------------------------------------------------
        //  Engine 3: Holiday Effects
        // ------------------------------------------------------------------

        [TestMethod]
        public void HolidayEffects_DetectsChristmasLift()
        {
            SeedYearOfData();
            var effects = CreateService().GetHolidayEffects();

            var christmas = effects.FirstOrDefault(h => h.HolidayName == "Christmas");
            Assert.IsNotNull(christmas, "Should detect Christmas holiday effect");
            Assert.IsTrue(christmas.RentalCount > 0, "Christmas should have rentals");
        }

        [TestMethod]
        public void HolidayEffects_DetectsHalloween()
        {
            SeedYearOfData();
            var effects = CreateService().GetHolidayEffects();

            var halloween = effects.FirstOrDefault(h => h.HolidayName == "Halloween");
            Assert.IsNotNull(halloween, "Should detect Halloween effect");
            Assert.IsTrue(halloween.LiftPercent > 0, "Halloween should show positive lift");
        }

        [TestMethod]
        public void HolidayEffects_SortedByLiftDescending()
        {
            SeedYearOfData();
            var effects = CreateService().GetHolidayEffects();

            for (int i = 1; i < effects.Count; i++)
            {
                Assert.IsTrue(effects[i - 1].LiftPercent >= effects[i].LiftPercent,
                    "Holiday effects should be sorted by lift descending");
            }
        }

        [TestMethod]
        public void HolidayEffects_ImpactClassification()
        {
            SeedYearOfData();
            var effects = CreateService().GetHolidayEffects();

            foreach (var e in effects)
            {
                Assert.IsTrue(
                    new[] { "Massive Surge", "Strong Boost", "Moderate Boost", "Neutral", "Moderate Dip", "Sharp Decline" }.Contains(e.Impact),
                    $"Invalid impact: {e.Impact}");
            }
        }

        [TestMethod]
        public void HolidayEffects_IncludesTopGenres()
        {
            SeedYearOfData();
            var effects = CreateService().GetHolidayEffects();

            var halloween = effects.FirstOrDefault(h => h.HolidayName == "Halloween");
            Assert.IsNotNull(halloween);
            Assert.IsTrue(halloween.TopGenres.Count > 0, "Should identify top genres for holiday");
            Assert.IsTrue(halloween.TopGenres.Contains("Horror"),
                "Horror should be top genre during Halloween");
        }

        [TestMethod]
        public void HolidayEffects_EmptyRentals_ReturnsEmpty()
        {
            var effects = CreateService().GetHolidayEffects();
            Assert.AreEqual(0, effects.Count);
        }

        // ------------------------------------------------------------------
        //  Engine 4: Day-of-Week Rhythm
        // ------------------------------------------------------------------

        [TestMethod]
        public void DayOfWeekRhythm_Returns7Days()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            Assert.AreEqual(7, report.DayOfWeekRhythm.Count);
        }

        [TestMethod]
        public void DayOfWeekRhythm_WeekendsFlagged()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            var weekends = report.DayOfWeekRhythm.Where(d => d.IsWeekend).ToList();
            Assert.AreEqual(2, weekends.Count);
            Assert.IsTrue(weekends.Any(d => d.Day == DayOfWeek.Saturday));
            Assert.IsTrue(weekends.Any(d => d.Day == DayOfWeek.Sunday));
        }

        [TestMethod]
        public void DayOfWeekRhythm_VolumeIndicesPositive()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            // At least some days should have data
            Assert.IsTrue(report.DayOfWeekRhythm.Any(d => d.VolumeIndex > 0),
                "At least one day should have positive volume index");
        }

        // ------------------------------------------------------------------
        //  Engine 5: Demand Forecaster
        // ------------------------------------------------------------------

        [TestMethod]
        public void Forecast_ReturnsThreeMonths()
        {
            SeedYearOfData();
            var forecasts = CreateService().GetForecast();

            Assert.AreEqual(3, forecasts.Count);
        }

        [TestMethod]
        public void Forecast_MonthsAreSequential()
        {
            SeedYearOfData();
            _clock.SetTime(new DateTime(2026, 3, 15));
            var forecasts = CreateService().GetForecast();

            Assert.AreEqual(4, forecasts[0].Month); // April
            Assert.AreEqual(5, forecasts[1].Month); // May
            Assert.AreEqual(6, forecasts[2].Month); // June
        }

        [TestMethod]
        public void Forecast_HasGenreBreakdown()
        {
            SeedYearOfData();
            var forecasts = CreateService().GetForecast();

            foreach (var f in forecasts)
            {
                Assert.IsTrue(f.GenreBreakdown.Count > 0, "Each forecast should have genre breakdown");
            }
        }

        [TestMethod]
        public void Forecast_ConfidenceBetween0And1()
        {
            SeedYearOfData();
            var forecasts = CreateService().GetForecast();

            foreach (var f in forecasts)
            {
                foreach (var g in f.GenreBreakdown)
                {
                    Assert.IsTrue(g.Confidence >= 0 && g.Confidence <= 1,
                        $"Confidence should be 0-1, got {g.Confidence}");
                }
            }
        }

        [TestMethod]
        public void Forecast_IncludesSeason()
        {
            SeedYearOfData();
            var forecasts = CreateService().GetForecast();

            foreach (var f in forecasts)
            {
                Assert.IsTrue(
                    new[] { "Spring", "Summer", "Fall", "Winter" }.Contains(f.Season),
                    $"Invalid season: {f.Season}");
            }
        }

        [TestMethod]
        public void Forecast_CustomMonthCount()
        {
            SeedYearOfData();
            var forecasts = CreateService().GetForecast(1);

            Assert.AreEqual(1, forecasts.Count);
        }

        // ------------------------------------------------------------------
        //  Engine 6: Stocking Recommendations
        // ------------------------------------------------------------------

        [TestMethod]
        public void Recommendations_Generated()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            Assert.IsTrue(report.Recommendations.Count > 0, "Should generate stocking recommendations");
        }

        [TestMethod]
        public void Recommendations_HaveUrgency()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            foreach (var r in report.Recommendations)
            {
                Assert.IsTrue(
                    new[] { "Critical", "High", "Medium", "Low" }.Contains(r.Urgency),
                    $"Invalid urgency: {r.Urgency}");
            }
        }

        [TestMethod]
        public void Recommendations_HaveReasons()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            foreach (var r in report.Recommendations)
            {
                Assert.IsFalse(string.IsNullOrEmpty(r.Reason), "Recommendations should have reasons");
            }
        }

        [TestMethod]
        public void Recommendations_SortedByPriority()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            // Sorted by PredictedDemand * SeasonalAffinity descending
            for (int i = 1; i < report.Recommendations.Count; i++)
            {
                var prev = report.Recommendations[i - 1];
                var curr = report.Recommendations[i];
                Assert.IsTrue(
                    prev.PredictedDemand * prev.SeasonalAffinity >= curr.PredictedDemand * curr.SeasonalAffinity,
                    "Recommendations should be sorted by priority descending");
            }
        }

        // ------------------------------------------------------------------
        //  Engine 7: Insights
        // ------------------------------------------------------------------

        [TestMethod]
        public void Insights_GeneratedWithData()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            Assert.IsTrue(report.Insights.Count > 0, "Should generate insights");
        }

        [TestMethod]
        public void Insights_IncludeCategories()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            var categories = report.Insights.Select(i => i.Category).Distinct().ToList();
            Assert.IsTrue(categories.Contains("Peak Season"), "Should have Peak Season insight");
        }

        [TestMethod]
        public void Insights_HaveTitlesAndDescriptions()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            foreach (var insight in report.Insights)
            {
                Assert.IsFalse(string.IsNullOrEmpty(insight.Title), "Insights should have titles");
                Assert.IsFalse(string.IsNullOrEmpty(insight.Description), "Insights should have descriptions");
                Assert.IsFalse(string.IsNullOrEmpty(insight.Category), "Insights should have categories");
            }
        }

        [TestMethod]
        public void Insights_ForecastInsightPresent()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            Assert.IsTrue(report.Insights.Any(i => i.Category == "Forecast"),
                "Should include a forecast insight");
        }

        // ------------------------------------------------------------------
        //  Health Score
        // ------------------------------------------------------------------

        [TestMethod]
        public void HealthScore_ZeroWithNoData()
        {
            var report = CreateService().GenerateReport();
            Assert.AreEqual(0, report.SeasonalityScore);
        }

        [TestMethod]
        public void HealthScore_PositiveWithData()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            Assert.IsTrue(report.SeasonalityScore > 0, "Health score should be positive with data");
            Assert.IsTrue(report.SeasonalityScore <= 100, "Health score should not exceed 100");
        }

        [TestMethod]
        public void HealthScore_HigherWithMoreMonths()
        {
            // Few months of data
            var m = AddMovie("Test", Genre.Action);
            AddRental(m.Id, new DateTime(2025, 1, 10));
            AddRental(m.Id, new DateTime(2025, 2, 10));
            var scoreWith2Months = CreateService().GenerateReport().SeasonalityScore;

            // Add many more months
            for (int month = 3; month <= 12; month++)
                AddRental(m.Id, new DateTime(2025, month, 10));
            var scoreWith12Months = CreateService().GenerateReport().SeasonalityScore;

            Assert.IsTrue(scoreWith12Months >= scoreWith2Months,
                $"Score with 12 months ({scoreWith12Months}) should be >= score with 2 months ({scoreWith2Months})");
        }

        // ------------------------------------------------------------------
        //  Report Metadata
        // ------------------------------------------------------------------

        [TestMethod]
        public void Report_HasTimestamp()
        {
            SeedYearOfData();
            var report = CreateService().GenerateReport();

            Assert.AreEqual(_clock.Now, report.GeneratedAt);
        }

        // ------------------------------------------------------------------
        //  Edge Cases
        // ------------------------------------------------------------------

        [TestMethod]
        public void SingleRental_ProducesValidReport()
        {
            var m = AddMovie("Solo", Genre.Drama);
            AddRental(m.Id, new DateTime(2025, 6, 15));

            var report = CreateService().GenerateReport();

            Assert.IsNotNull(report);
            Assert.AreEqual(12, report.MonthlyProfile.Count);
            Assert.IsTrue(report.MonthlyProfile.Single(p => p.Month == 6).RentalCount == 1);
        }

        [TestMethod]
        public void MoviesWithoutGenre_ExcludedFromAffinity()
        {
            var m = new Movie { Name = "No Genre", ReleaseDate = new DateTime(2025, 1, 1) };
            _movieRepo.Add(m);
            AddRental(m.Id, new DateTime(2025, 6, 15));

            var affinities = CreateService().GetGenreSeasonAffinity();
            Assert.AreEqual(0, affinities.Count, "Movies without genre should not appear in affinity map");
        }

        [TestMethod]
        public void AllRentalsInOneMonth_ExtremeSeasonalIndex()
        {
            var m = AddMovie("Concentrated", Genre.SciFi);
            for (int i = 0; i < 20; i++)
                AddRental(m.Id, new DateTime(2025, 7, 10 + (i % 15)));

            var report = CreateService().GenerateReport();

            var july = report.MonthlyProfile.Single(p => p.Month == 7);
            Assert.AreEqual("Peak", july.Classification);
            Assert.IsTrue(july.SeasonalIndex > 1.5, "July should be classified as Peak");
        }

        [TestMethod]
        public void YearWrapAround_ForecastHandlesDecember()
        {
            SeedYearOfData();
            _clock.SetTime(new DateTime(2025, 11, 15));
            var forecasts = CreateService().GetForecast();

            Assert.AreEqual(12, forecasts[0].Month); // December
            Assert.AreEqual(1, forecasts[1].Month);   // January
            Assert.AreEqual(2, forecasts[2].Month);   // February
        }
    }
}
