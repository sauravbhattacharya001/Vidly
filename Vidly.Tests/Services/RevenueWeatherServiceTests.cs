using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests.Services
{
    [TestClass]
    public class RevenueWeatherServiceTests
    {
        private InMemoryRentalRepository _rentals;
        private InMemoryMovieRepository _movies;
        private TestClock _clock;

        [TestInitialize]
        public void Setup()
        {
            _rentals = new InMemoryRentalRepository();
            _movies = new InMemoryMovieRepository();
            _clock = new TestClock(new DateTime(2025, 7, 1, 12, 0, 0));
        }

        private RevenueWeatherService CreateService(WeatherEngineConfig config = null)
        {
            return new RevenueWeatherService(_rentals, _movies, _clock, config);
        }

        private Movie AddMovie(string name, Genre genre)
        {
            var m = new Movie { Name = name, Genre = genre, ReleaseDate = new DateTime(2024, 1, 1) };
            _movies.Add(m);
            return m;
        }

        private Rental AddRental(int movieId, DateTime rentalDate, decimal dailyRate = 3.99m, int customerId = 1)
        {
            var r = new Rental
            {
                MovieId = movieId,
                CustomerId = customerId,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(7),
                ReturnDate = rentalDate.AddDays(5),
                DailyRate = dailyRate,
                Status = RentalStatus.Returned
            };
            _rentals.Add(r);
            return r;
        }

        // ── Constructor Validation ────────────────────────────────────

        [TestMethod]
        public void Constructor_NullRentalRepo_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new RevenueWeatherService(null, _movies, _clock));
        }

        [TestMethod]
        public void Constructor_NullMovieRepo_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new RevenueWeatherService(_rentals, null, _clock));
        }

        [TestMethod]
        public void Constructor_NullClock_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new RevenueWeatherService(_rentals, _movies, null));
        }

        // ── Empty State ──────────────────────────────────────────────

        [TestMethod]
        public void Analyze_EmptyRentals_ReturnsValidReport()
        {
            var service = CreateService();
            var report = service.Analyze();

            Assert.IsNotNull(report);
            Assert.AreEqual(90, report.AnalysisWindowDays);
            Assert.IsNotNull(report.ActivePhenomena);
            Assert.IsNotNull(report.RecentPhenomena);
            Assert.IsNotNull(report.Microclimates);
            Assert.IsNotNull(report.Forecasts);
            Assert.IsNotNull(report.AutonomousInsights);
            Assert.IsNotNull(report.StormWarnings);
        }

        [TestMethod]
        public void Analyze_EmptyRentals_HealthScoreIsValid()
        {
            var service = CreateService();
            var report = service.Analyze();

            Assert.IsTrue(report.HealthScore >= 0 && report.HealthScore <= 100,
                "Health score should be 0-100, was " + report.HealthScore);
        }

        // ── Storm Detection ──────────────────────────────────────────

        [TestMethod]
        public void Analyze_RevenueSpike_DetectsStorm()
        {
            var movie = AddMovie("Action Hero", Genre.Action);

            // Normal days
            for (int d = 90; d >= 5; d--)
            {
                AddRental(movie.Id, _clock.Now.AddDays(-d), 3.00m);
            }

            // Spike days (much higher)
            for (int d = 4; d >= 1; d--)
            {
                for (int i = 0; i < 20; i++)
                {
                    AddRental(movie.Id, _clock.Now.AddDays(-d), 10.00m, customerId: i + 10);
                }
            }

            var service = CreateService();
            var report = service.Analyze();

            var storms = report.ActivePhenomena
                .Concat(report.RecentPhenomena)
                .Where(p => p.Type == "Storm")
                .ToList();

            Assert.IsTrue(storms.Count > 0, "Should detect at least one storm");
        }

        [TestMethod]
        public void Analyze_StormHasPositiveIntensity()
        {
            var movie = AddMovie("Big Hit", Genre.Action);

            for (int d = 90; d >= 5; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 2.00m);

            for (int d = 4; d >= 1; d--)
                for (int i = 0; i < 30; i++)
                    AddRental(movie.Id, _clock.Now.AddDays(-d), 15.00m, customerId: i + 10);

            var service = CreateService();
            var report = service.Analyze();

            var storms = report.ActivePhenomena
                .Concat(report.RecentPhenomena)
                .Where(p => p.Type == "Storm")
                .ToList();

            if (storms.Count > 0)
            {
                Assert.IsTrue(storms[0].Intensity > 0, "Storm intensity should be positive");
            }
        }

        // ── Drought Detection ────────────────────────────────────────

        [TestMethod]
        public void Analyze_LowRevenuePeriod_DetectsDrought()
        {
            var movie = AddMovie("Popular Film", Genre.Comedy);

            // Normal early period
            for (int d = 90; d >= 20; d--)
            {
                AddRental(movie.Id, _clock.Now.AddDays(-d), 10.00m);
            }

            // Very low revenue period (below 30% threshold)
            for (int d = 10; d >= 1; d--)
            {
                AddRental(movie.Id, _clock.Now.AddDays(-d), 0.50m);
            }

            var service = CreateService();
            var report = service.Analyze();

            var droughts = report.ActivePhenomena
                .Concat(report.RecentPhenomena)
                .Where(p => p.Type == "Drought")
                .ToList();

            Assert.IsTrue(droughts.Count > 0, "Should detect at least one drought");
        }

        [TestMethod]
        public void Analyze_DroughtHasCorrectType()
        {
            var movie = AddMovie("Old Classic", Genre.Drama);

            for (int d = 90; d >= 20; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 8.00m);

            for (int d = 10; d >= 1; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 0.10m);

            var service = CreateService();
            var report = service.Analyze();

            var droughts = report.ActivePhenomena
                .Concat(report.RecentPhenomena)
                .Where(p => p.Type == "Drought")
                .ToList();

            foreach (var d in droughts)
            {
                Assert.AreEqual("Drought", d.Type);
                Assert.AreEqual("store-wide", d.AffectedArea);
            }
        }

        // ── Front Detection ─────────────────────────────────────────

        [TestMethod]
        public void Analyze_GenreShift_DetectsFront()
        {
            var action = AddMovie("Action 1", Genre.Action);
            var comedy = AddMovie("Comedy 1", Genre.Comedy);

            // First half: mostly action
            for (int d = 90; d >= 46; d--)
            {
                AddRental(action.Id, _clock.Now.AddDays(-d), 3.00m);
            }

            // Second half: mostly comedy
            for (int d = 45; d >= 1; d--)
            {
                AddRental(comedy.Id, _clock.Now.AddDays(-d), 3.00m);
            }

            var service = CreateService();
            var report = service.Analyze();

            var fronts = report.ActivePhenomena
                .Concat(report.RecentPhenomena)
                .Where(p => p.Type == "Front")
                .ToList();

            Assert.IsTrue(fronts.Count > 0, "Should detect genre fronts");
        }

        [TestMethod]
        public void Analyze_FrontHasAffectedArea()
        {
            var scifi = AddMovie("Sci-Fi Film", Genre.SciFi);
            var drama = AddMovie("Drama Film", Genre.Drama);

            for (int d = 90; d >= 46; d--)
                AddRental(scifi.Id, _clock.Now.AddDays(-d), 3.00m);

            for (int d = 45; d >= 1; d--)
                AddRental(drama.Id, _clock.Now.AddDays(-d), 3.00m);

            var service = CreateService();
            var report = service.Analyze();

            var fronts = report.ActivePhenomena
                .Concat(report.RecentPhenomena)
                .Where(p => p.Type == "Front")
                .ToList();

            foreach (var f in fronts)
            {
                Assert.IsFalse(string.IsNullOrEmpty(f.AffectedArea),
                    "Front should have an affected area");
            }
        }

        // ── Microclimates ────────────────────────────────────────────

        [TestMethod]
        public void Analyze_MultipleGenres_CreatesMicroclimates()
        {
            var action = AddMovie("Action X", Genre.Action);
            var comedy = AddMovie("Comedy Y", Genre.Comedy);
            var drama = AddMovie("Drama Z", Genre.Drama);

            for (int d = 30; d >= 1; d--)
            {
                AddRental(action.Id, _clock.Now.AddDays(-d), 5.00m);
                AddRental(comedy.Id, _clock.Now.AddDays(-d), 3.00m);
                AddRental(drama.Id, _clock.Now.AddDays(-d), 2.00m);
            }

            var service = CreateService();
            var report = service.Analyze();

            Assert.IsTrue(report.Microclimates.Count >= 3,
                "Should have microclimates for each genre with rentals");
        }

        [TestMethod]
        public void Analyze_Microclimate_TemperatureInRange()
        {
            var movie = AddMovie("Solo Film", Genre.Horror);

            for (int d = 30; d >= 1; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 4.00m);

            var service = CreateService();
            var report = service.Analyze();

            foreach (var m in report.Microclimates)
            {
                Assert.IsTrue(m.Temperature >= 0 && m.Temperature <= 100,
                    "Temperature should be 0-100, was " + m.Temperature);
            }
        }

        [TestMethod]
        public void Analyze_Microclimate_WindDirectionIsValid()
        {
            var movie = AddMovie("Wind Test", Genre.Thriller);

            for (int d = 30; d >= 1; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 3.00m);

            var service = CreateService();
            var report = service.Analyze();

            var validDirections = new HashSet<string> { "rising", "falling", "stable", "calm" };
            foreach (var m in report.Microclimates)
            {
                Assert.IsTrue(validDirections.Contains(m.WindDirection),
                    "Wind direction should be valid, was: " + m.WindDirection);
            }
        }

        [TestMethod]
        public void Analyze_RisingGenre_DetectsRisingWind()
        {
            var movie = AddMovie("Rising Star", Genre.SciFi);

            // Few rentals 2 weeks ago
            AddRental(movie.Id, _clock.Now.AddDays(-13), 2.00m);

            // Many rentals this week
            for (int d = 6; d >= 1; d--)
            {
                for (int i = 0; i < 5; i++)
                    AddRental(movie.Id, _clock.Now.AddDays(-d), 5.00m, customerId: i + 1);
            }

            var service = CreateService();
            var report = service.Analyze();

            var scifi = report.Microclimates.FirstOrDefault(m => m.Genre == Genre.SciFi);
            Assert.IsNotNull(scifi, "Should have SciFi microclimate");
            Assert.AreEqual("rising", scifi.WindDirection, "Should detect rising wind");
        }

        // ── Forecasts ────────────────────────────────────────────────

        [TestMethod]
        public void Analyze_SufficientData_GeneratesForecasts()
        {
            var movie = AddMovie("Forecast Test", Genre.Drama);

            for (int d = 30; d >= 1; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 4.00m);

            var service = CreateService();
            var report = service.Analyze();

            Assert.IsTrue(report.Forecasts.Count >= 2,
                "Should generate at least 7-day and 30-day forecasts");
        }

        [TestMethod]
        public void Analyze_Forecast_HasExpectedPeriods()
        {
            var movie = AddMovie("Period Test", Genre.Comedy);

            for (int d = 30; d >= 1; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 4.00m);

            var service = CreateService();
            var report = service.Analyze();

            var periods = report.Forecasts.Select(f => f.Period).ToList();
            Assert.IsTrue(periods.Contains("Next 7 days"), "Should have 7-day forecast");
            Assert.IsTrue(periods.Contains("Next 30 days"), "Should have 30-day forecast");
        }

        [TestMethod]
        public void Analyze_Forecast_ConfidenceIsPositive()
        {
            var movie = AddMovie("Confidence Test", Genre.Action);

            for (int d = 30; d >= 1; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 4.00m);

            var service = CreateService();
            var report = service.Analyze();

            foreach (var f in report.Forecasts)
            {
                Assert.IsTrue(f.ConfidencePercent > 0,
                    "Forecast confidence should be positive");
            }
        }

        [TestMethod]
        public void Analyze_InsufficientData_NoForecasts()
        {
            var movie = AddMovie("Tiny Data", Genre.Horror);
            AddRental(movie.Id, _clock.Now.AddDays(-1), 3.00m);

            var config = new WeatherEngineConfig { MinDataPointsForForecast = 100 };
            var service = CreateService(config);
            var report = service.Analyze();

            Assert.AreEqual(0, report.Forecasts.Count,
                "Should not generate forecasts with insufficient data");
        }

        // ── Overall Condition ────────────────────────────────────────

        [TestMethod]
        public void Analyze_HighRevenue_SunnyCondition()
        {
            var movie = AddMovie("Blockbuster", Genre.Action);

            // Consistently high revenue
            for (int d = 30; d >= 1; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 10.00m);

            var service = CreateService();
            var report = service.Analyze();

            // With consistent high revenue, should be Sunny or PartlyCloudy
            Assert.IsTrue(
                report.OverallCondition == WeatherCondition.Sunny ||
                report.OverallCondition == WeatherCondition.PartlyCloudy,
                "High revenue should yield Sunny/PartlyCloudy, was " + report.OverallCondition);
        }

        // ── Health Score ─────────────────────────────────────────────

        [TestMethod]
        public void Analyze_HealthScore_BoundedZeroToHundred()
        {
            var movie = AddMovie("Health Test", Genre.Comedy);

            for (int d = 60; d >= 1; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 5.00m);

            var service = CreateService();
            var report = service.Analyze();

            Assert.IsTrue(report.HealthScore >= 0, "Health score should be >= 0");
            Assert.IsTrue(report.HealthScore <= 100, "Health score should be <= 100");
        }

        // ── Summary ──────────────────────────────────────────────────

        [TestMethod]
        public void Analyze_Summary_IsNonEmpty()
        {
            var movie = AddMovie("Summary Test", Genre.Drama);

            for (int d = 30; d >= 1; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 4.00m);

            var service = CreateService();
            var report = service.Analyze();

            Assert.IsFalse(string.IsNullOrWhiteSpace(report.OverallSummary),
                "Summary should not be empty");
            Assert.IsTrue(report.OverallSummary.Contains("Current conditions"),
                "Summary should include weather language");
        }

        // ── Insights ─────────────────────────────────────────────────

        [TestMethod]
        public void Analyze_WithData_GeneratesInsights()
        {
            var movie = AddMovie("Insight Test", Genre.Action);

            for (int d = 60; d >= 1; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 4.00m);

            var service = CreateService();
            var report = service.Analyze();

            Assert.IsTrue(report.AutonomousInsights.Count > 0,
                "Should generate at least one insight");
        }

        // ── Config Customization ─────────────────────────────────────

        [TestMethod]
        public void Analyze_CustomConfig_AffectsWindowDays()
        {
            var config = new WeatherEngineConfig { WindowDays = 30 };
            var service = CreateService(config);
            var report = service.Analyze();

            Assert.AreEqual(30, report.AnalysisWindowDays);
        }

        [TestMethod]
        public void Analyze_LowerStormThreshold_DetectsMoreStorms()
        {
            var movie = AddMovie("Threshold Test", Genre.Horror);

            for (int d = 90; d >= 5; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 3.00m);

            for (int d = 4; d >= 1; d--)
                for (int i = 0; i < 5; i++)
                    AddRental(movie.Id, _clock.Now.AddDays(-d), 8.00m, customerId: i + 10);

            // High threshold: fewer storms
            var highConfig = new WeatherEngineConfig { StormThresholdZScore = 3.0 };
            var highReport = CreateService(highConfig).Analyze();
            int highStorms = highReport.ActivePhenomena
                .Concat(highReport.RecentPhenomena)
                .Count(p => p.Type == "Storm");

            // Low threshold: more storms
            var lowConfig = new WeatherEngineConfig { StormThresholdZScore = 0.5 };
            var lowReport = CreateService(lowConfig).Analyze();
            int lowStorms = lowReport.ActivePhenomena
                .Concat(lowReport.RecentPhenomena)
                .Count(p => p.Type == "Storm");

            Assert.IsTrue(lowStorms >= highStorms,
                "Lower threshold should detect >= storms than higher threshold");
        }

        // ── Edge Cases ───────────────────────────────────────────────

        [TestMethod]
        public void Analyze_SingleRental_NoExceptions()
        {
            var movie = AddMovie("Solo Rental", Genre.Animation);
            AddRental(movie.Id, _clock.Now.AddDays(-5), 5.00m);

            var service = CreateService();
            var report = service.Analyze();

            Assert.IsNotNull(report);
            Assert.IsTrue(report.HealthScore >= 0);
        }

        [TestMethod]
        public void Analyze_AllSameGenre_ProducesSingleMicroclimate()
        {
            var m1 = AddMovie("Action 1", Genre.Action);
            var m2 = AddMovie("Action 2", Genre.Action);

            for (int d = 30; d >= 1; d--)
            {
                AddRental(m1.Id, _clock.Now.AddDays(-d), 3.00m);
                AddRental(m2.Id, _clock.Now.AddDays(-d), 4.00m);
            }

            var service = CreateService();
            var report = service.Analyze();

            var actionClimates = report.Microclimates
                .Where(m => m.Genre == Genre.Action)
                .ToList();
            Assert.AreEqual(1, actionClimates.Count,
                "Should have exactly one Action microclimate");
        }

        [TestMethod]
        public void Analyze_AllRentalsSameDay_NoExceptions()
        {
            var movie = AddMovie("Same Day", Genre.Documentary);
            var day = _clock.Now.AddDays(-10);

            for (int i = 0; i < 20; i++)
                AddRental(movie.Id, day, 3.00m, customerId: i + 1);

            var service = CreateService();
            var report = service.Analyze();

            Assert.IsNotNull(report);
            Assert.IsNotNull(report.OverallSummary);
        }

        [TestMethod]
        public void Analyze_GeneratedAt_MatchesClock()
        {
            var service = CreateService();
            var report = service.Analyze();

            Assert.AreEqual(_clock.Now, report.GeneratedAt);
        }

        [TestMethod]
        public void Analyze_StoreTemperature_BoundedZeroToHundred()
        {
            var movie = AddMovie("Temp Bounds", Genre.Thriller);

            for (int d = 60; d >= 1; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 5.00m);

            var service = CreateService();
            var report = service.Analyze();

            Assert.IsTrue(report.StoreTemperature >= 0 && report.StoreTemperature <= 100,
                "Store temperature should be 0-100, was " + report.StoreTemperature);
        }

        [TestMethod]
        public void Analyze_Microclimate_HasForecastText()
        {
            var movie = AddMovie("Forecast Genre", Genre.Romance);

            for (int d = 30; d >= 1; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 4.00m);

            var service = CreateService();
            var report = service.Analyze();

            foreach (var m in report.Microclimates)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(m.Forecast),
                    "Microclimate should have forecast text");
            }
        }

        [TestMethod]
        public void Analyze_Microclimate_PressureInRange()
        {
            var movie = AddMovie("Pressure Test", Genre.Adventure);

            for (int d = 30; d >= 1; d--)
                AddRental(movie.Id, _clock.Now.AddDays(-d), 4.00m);

            var service = CreateService();
            var report = service.Analyze();

            foreach (var m in report.Microclimates)
            {
                Assert.IsTrue(m.Pressure >= 0 && m.Pressure <= 100,
                    "Pressure should be 0-100, was " + m.Pressure);
            }
        }
    }
}
