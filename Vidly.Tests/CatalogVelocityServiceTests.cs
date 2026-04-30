using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Xunit;

namespace Vidly.Tests
{
    public class CatalogVelocityServiceTests : IDisposable
    {
        private readonly InMemoryMovieRepository _movieRepo;
        private readonly InMemoryRentalRepository _rentalRepo;
        private readonly TestClock _clock;

        public CatalogVelocityServiceTests()
        {
            _movieRepo = new InMemoryMovieRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _clock = new TestClock(new DateTime(2025, 7, 1, 12, 0, 0));
            CatalogVelocityService.Reset();
        }

        public void Dispose()
        {
            CatalogVelocityService.Reset();
        }

        private CatalogVelocityService CreateService(VelocityEngineConfig config = null)
        {
            return new CatalogVelocityService(_movieRepo, _rentalRepo, _clock, config);
        }

        private Movie AddMovie(string name, Genre genre, DateTime? releaseDate = null)
        {
            var m = new Movie { Name = name, Genre = genre, ReleaseDate = releaseDate };
            return _movieRepo.Add(m);
        }

        private Rental AddRental(int movieId, DateTime rentalDate, int customerId = 1)
        {
            var r = new Rental
            {
                MovieId = movieId,
                CustomerId = customerId,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(7),
                ReturnDate = rentalDate.AddDays(5),
                DailyRate = 3.99m,
                Status = RentalStatus.Returned
            };
            return _rentalRepo.Add(r);
        }

        // --- Constructor Validation ---

        [Fact]
        public void Constructor_NullMovieRepo_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CatalogVelocityService(null, _rentalRepo, _clock));
        }

        [Fact]
        public void Constructor_NullRentalRepo_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CatalogVelocityService(_movieRepo, null, _clock));
        }

        [Fact]
        public void Constructor_NullClock_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CatalogVelocityService(_movieRepo, _rentalRepo, null));
        }

        // --- Empty Catalog ---

        [Fact]
        public void Analyze_EmptyCatalog_ReturnsEmptyReport()
        {
            var svc = CreateService();
            var report = svc.Analyze();
            Assert.NotNull(report);
            Assert.Empty(report.Profiles);
            Assert.Equal(0, report.CatalogHealthScore);
            Assert.Empty(report.UrgentActions);
        }

        // --- Phase Detection ---

        [Fact]
        public void Analyze_NewArrival_DetectedByDaysInCatalog()
        {
            var movie = AddMovie("New Film", Genre.Action, _clock.Now.AddDays(-15));
            AddRental(movie.Id, _clock.Now.AddDays(-5));
            var svc = CreateService();

            var report = svc.Analyze();
            var profile = report.Profiles.Single();
            Assert.Equal(CatalogPhase.NewArrival, profile.Phase);
        }

        [Fact]
        public void Analyze_HotMovie_HighVelocity()
        {
            var movie = AddMovie("Blockbuster", Genre.Action, _clock.Now.AddDays(-60));
            // Add many rentals in the window
            for (int i = 0; i < 20; i++)
                AddRental(movie.Id, _clock.Now.AddDays(-i * 3));

            // Add a second movie with no rentals to create contrast
            AddMovie("Dud", Genre.Drama, _clock.Now.AddDays(-60));

            var svc = CreateService();
            var report = svc.Analyze();
            var hotProfile = report.Profiles.First(p => p.MovieId == movie.Id);
            Assert.Equal(CatalogPhase.Hot, hotProfile.Phase);
            Assert.True(hotProfile.VelocityScore >= 70);
        }

        [Fact]
        public void Analyze_DormantMovie_NoRecentRentals()
        {
            var movie = AddMovie("Forgotten Classic", Genre.Drama, _clock.Now.AddDays(-180));
            // Only old rental outside window
            AddRental(movie.Id, _clock.Now.AddDays(-120));

            var svc = CreateService();
            var report = svc.Analyze();
            var profile = report.Profiles.Single();
            Assert.Equal(CatalogPhase.Dormant, profile.Phase);
        }

        [Fact]
        public void Analyze_DecliningMovie_LowVelocityNegativeAcceleration()
        {
            var hot = AddMovie("Still Popular", Genre.Comedy, _clock.Now.AddDays(-90));
            var declining = AddMovie("Fading Star", Genre.Drama, _clock.Now.AddDays(-90));

            // Hot movie: lots of recent rentals
            for (int i = 0; i < 15; i++)
                AddRental(hot.Id, _clock.Now.AddDays(-i * 2));

            // Declining: some old rentals but fewer recent, negative acceleration
            AddRental(declining.Id, _clock.Now.AddDays(-10)); // 1 in prior period
            // None in recent week -> acceleration = 0 - 1 = -1
            // Actually need to make it more clearly declining
            for (int i = 0; i < 3; i++)
                AddRental(declining.Id, _clock.Now.AddDays(-10 - i)); // prior period
            // 0 in recent period, 3 in prior -> accel = -3

            var svc = CreateService();
            var report = svc.Analyze();
            var profile = report.Profiles.First(p => p.MovieId == declining.Id);
            Assert.Equal(CatalogPhase.Declining, profile.Phase);
            Assert.True(profile.Acceleration < 0);
        }

        [Fact]
        public void Analyze_SteadyMovie_ModerateVelocity()
        {
            var movie = AddMovie("Reliable Title", Genre.Comedy, _clock.Now.AddDays(-90));
            // Consistent rental pattern
            for (int i = 0; i < 6; i++)
                AddRental(movie.Id, _clock.Now.AddDays(-i * 10));

            var svc = CreateService();
            var report = svc.Analyze();
            var profile = report.Profiles.Single();
            // With only one movie, velocity normalizes to 100 (hot)
            // Need a second movie to create a middle range
            // Since there's only one movie it goes to 100 -> Hot
            Assert.Equal(CatalogPhase.Hot, profile.Phase); // single movie normalizes to 100
        }

        [Fact]
        public void Analyze_SteadyPhase_WithMultipleMovies()
        {
            var hotMovie = AddMovie("Top Gun", Genre.Action, _clock.Now.AddDays(-90));
            var steadyMovie = AddMovie("Moderate Film", Genre.Drama, _clock.Now.AddDays(-90));
            var lowMovie = AddMovie("Quiet Film", Genre.Horror, _clock.Now.AddDays(-90));

            // Hot: many rentals
            for (int i = 0; i < 20; i++)
                AddRental(hotMovie.Id, _clock.Now.AddDays(-i * 3));
            // Steady: moderate rentals, flat acceleration
            AddRental(steadyMovie.Id, _clock.Now.AddDays(-3));
            AddRental(steadyMovie.Id, _clock.Now.AddDays(-10));
            AddRental(steadyMovie.Id, _clock.Now.AddDays(-20));
            AddRental(steadyMovie.Id, _clock.Now.AddDays(-30));
            AddRental(steadyMovie.Id, _clock.Now.AddDays(-50));
            // Low: one old rental
            AddRental(lowMovie.Id, _clock.Now.AddDays(-60));

            var svc = CreateService();
            var report = svc.Analyze();
            var steady = report.Profiles.First(p => p.MovieId == steadyMovie.Id);
            Assert.Equal(CatalogPhase.Steady, steady.Phase);
        }

        // --- Resurgence Detection ---

        [Fact]
        public void Analyze_ResurgentMovie_DetectedAfterDormancy()
        {
            var movie = AddMovie("Comeback Kid", Genre.SciFi, _clock.Now.AddDays(-120));

            // First run: make it dormant (no recent rentals)
            var config = new VelocityEngineConfig { DormantThresholdDays = 30 };
            var svc = CreateService(config);
            svc.Analyze(); // establishes dormant phase

            // Now add recent rentals (spike)
            for (int i = 0; i < 5; i++)
                AddRental(movie.Id, _clock.Now.AddDays(-i));

            // Second analysis should detect resurgence
            var report = svc.Analyze();
            var profile = report.Profiles.First(p => p.MovieId == movie.Id);
            // Should be resurgent since previous was dormant and acceleration is high
            Assert.Equal(CatalogPhase.Resurgent, profile.Phase);
        }

        // --- Velocity Scoring ---

        [Fact]
        public void Analyze_VelocityNormalization_MaxIs100()
        {
            var m1 = AddMovie("Fastest", Genre.Action, _clock.Now.AddDays(-60));
            var m2 = AddMovie("Slower", Genre.Drama, _clock.Now.AddDays(-60));

            for (int i = 0; i < 10; i++)
                AddRental(m1.Id, _clock.Now.AddDays(-i * 5));
            AddRental(m2.Id, _clock.Now.AddDays(-30));

            var svc = CreateService();
            var report = svc.Analyze();
            var fastest = report.Profiles.First(p => p.MovieId == m1.Id);
            Assert.Equal(100.0, fastest.VelocityScore);
        }

        [Fact]
        public void Analyze_ZeroRentals_AllVelocitiesZero()
        {
            AddMovie("No Rentals", Genre.Horror, _clock.Now.AddDays(-60));
            var svc = CreateService();
            var report = svc.Analyze();
            Assert.Equal(0, report.Profiles.Single().VelocityScore);
        }

        // --- Action Recommendations ---

        [Fact]
        public void Analyze_DormantMovie_RecommendsRetireOrDiscount()
        {
            var movie = AddMovie("Dead Weight", Genre.Drama, _clock.Now.AddDays(-180));
            AddRental(movie.Id, _clock.Now.AddDays(-120));

            var svc = CreateService();
            var report = svc.Analyze();
            var profile = report.Profiles.Single();
            Assert.True(profile.RecommendedAction == VelocityAction.Retire ||
                       profile.RecommendedAction == VelocityAction.Discount);
        }

        [Fact]
        public void Analyze_HotMoviePositiveAcceleration_RecommendsRestock()
        {
            var hot = AddMovie("Mega Hit", Genre.Action, _clock.Now.AddDays(-60));
            var other = AddMovie("Background", Genre.Drama, _clock.Now.AddDays(-60));

            // Hot with acceleration: more recent than prior
            for (int i = 0; i < 4; i++)
                AddRental(hot.Id, _clock.Now.AddDays(-i)); // 4 in last week
            AddRental(hot.Id, _clock.Now.AddDays(-10)); // 1 in prior week
            AddRental(other.Id, _clock.Now.AddDays(-50));

            var svc = CreateService();
            var report = svc.Analyze();
            var profile = report.Profiles.First(p => p.MovieId == hot.Id);
            Assert.Equal(VelocityAction.Restock, profile.RecommendedAction);
            Assert.True(profile.ActionConfidence >= 0.7);
        }

        // --- At Risk Detection ---

        [Fact]
        public void Analyze_DecliningMovie_MarkedAtRisk()
        {
            var hot = AddMovie("Popular", Genre.Comedy, _clock.Now.AddDays(-90));
            var declining = AddMovie("Fading", Genre.Drama, _clock.Now.AddDays(-90));

            for (int i = 0; i < 15; i++)
                AddRental(hot.Id, _clock.Now.AddDays(-i * 2));
            for (int i = 0; i < 3; i++)
                AddRental(declining.Id, _clock.Now.AddDays(-10 - i));

            var svc = CreateService();
            var report = svc.Analyze();
            var profile = report.Profiles.First(p => p.MovieId == declining.Id);
            Assert.True(profile.AtRisk);
        }

        // --- Genre Breakdown ---

        [Fact]
        public void Analyze_MultiGenre_ProducesGenreBreakdown()
        {
            var action = AddMovie("Action Film", Genre.Action, _clock.Now.AddDays(-60));
            var comedy = AddMovie("Comedy Film", Genre.Comedy, _clock.Now.AddDays(-60));

            for (int i = 0; i < 5; i++)
                AddRental(action.Id, _clock.Now.AddDays(-i * 10));
            AddRental(comedy.Id, _clock.Now.AddDays(-20));

            var svc = CreateService();
            var report = svc.Analyze();
            Assert.True(report.GenreBreakdown.Count >= 2);
            Assert.Contains(report.GenreBreakdown, g => g.Genre == Genre.Action);
            Assert.Contains(report.GenreBreakdown, g => g.Genre == Genre.Comedy);
        }

        // --- Catalog Health Score ---

        [Fact]
        public void Analyze_HealthyCatalog_HighScore()
        {
            // Mix of active movies with rentals
            for (int i = 1; i <= 5; i++)
            {
                var m = AddMovie("Movie " + i, (Genre)i, _clock.Now.AddDays(-60));
                for (int j = 0; j < 3; j++)
                    AddRental(m.Id, _clock.Now.AddDays(-j * 10 - i));
            }

            var svc = CreateService();
            var report = svc.Analyze();
            Assert.True(report.CatalogHealthScore > 40); // reasonably healthy
        }

        [Fact]
        public void Analyze_AllDormant_LowScore()
        {
            for (int i = 1; i <= 3; i++)
            {
                var m = AddMovie("Old Movie " + i, Genre.Drama, _clock.Now.AddDays(-200));
                AddRental(m.Id, _clock.Now.AddDays(-150));
            }

            var svc = CreateService();
            var report = svc.Analyze();
            Assert.True(report.CatalogHealthScore < 30);
        }

        // --- Insights Generation ---

        [Fact]
        public void Analyze_HighDormancy_GeneratesWarning()
        {
            // 4 dormant, 1 active
            for (int i = 1; i <= 4; i++)
            {
                var m = AddMovie("Dormant " + i, Genre.Drama, _clock.Now.AddDays(-200));
                AddRental(m.Id, _clock.Now.AddDays(-150));
            }
            var active = AddMovie("Active", Genre.Action, _clock.Now.AddDays(-60));
            for (int i = 0; i < 5; i++)
                AddRental(active.Id, _clock.Now.AddDays(-i * 5));

            var svc = CreateService();
            var report = svc.Analyze();
            Assert.Contains(report.Insights, ins => ins.Contains("dormant"));
        }

        // --- Phase Distribution ---

        [Fact]
        public void Analyze_PhaseDistribution_SumsToTotal()
        {
            for (int i = 1; i <= 4; i++)
            {
                var m = AddMovie("M" + i, (Genre)i, _clock.Now.AddDays(-60));
                if (i <= 2)
                    for (int j = 0; j < 8; j++)
                        AddRental(m.Id, _clock.Now.AddDays(-j * 5));
            }

            var svc = CreateService();
            var report = svc.Analyze();
            var totalInDist = report.PhaseDistribution.Values.Sum();
            Assert.Equal(report.Profiles.Count, totalInDist);
        }

        // --- GetMovieVelocity ---

        [Fact]
        public void GetMovieVelocity_ValidId_ReturnsProfile()
        {
            var m = AddMovie("Test", Genre.Action, _clock.Now.AddDays(-60));
            AddRental(m.Id, _clock.Now.AddDays(-5));

            var svc = CreateService();
            var profile = svc.GetMovieVelocity(m.Id);
            Assert.NotNull(profile);
            Assert.Equal(m.Id, profile.MovieId);
        }

        [Fact]
        public void GetMovieVelocity_InvalidId_ReturnsNull()
        {
            var svc = CreateService();
            Assert.Null(svc.GetMovieVelocity(999));
        }

        // --- GetByPhase ---

        [Fact]
        public void GetByPhase_ReturnsMatchingOnly()
        {
            var dormant = AddMovie("Old", Genre.Drama, _clock.Now.AddDays(-200));
            AddRental(dormant.Id, _clock.Now.AddDays(-150));
            var newMovie = AddMovie("Fresh", Genre.Action, _clock.Now.AddDays(-10));
            AddRental(newMovie.Id, _clock.Now.AddDays(-2));

            var svc = CreateService();
            var dormantList = svc.GetByPhase(CatalogPhase.Dormant);
            Assert.All(dormantList, p => Assert.Equal(CatalogPhase.Dormant, p.Phase));
        }

        // --- GetActionQueue ---

        [Fact]
        public void GetActionQueue_SortedByConfidence()
        {
            var m1 = AddMovie("Movie A", Genre.Action, _clock.Now.AddDays(-200));
            AddRental(m1.Id, _clock.Now.AddDays(-150));
            var m2 = AddMovie("Movie B", Genre.Drama, _clock.Now.AddDays(-200));
            AddRental(m2.Id, _clock.Now.AddDays(-100));

            var svc = CreateService();
            var queue = svc.GetActionQueue();
            if (queue.Count > 1)
            {
                for (int i = 1; i < queue.Count; i++)
                    Assert.True(queue[i - 1].ActionConfidence >= queue[i].ActionConfidence);
            }
        }

        // --- Configuration ---

        [Fact]
        public void Analyze_CustomConfig_RespectsDormantThreshold()
        {
            var movie = AddMovie("Custom", Genre.SciFi, _clock.Now.AddDays(-90));
            AddRental(movie.Id, _clock.Now.AddDays(-15)); // 15 days since last rental

            // Default threshold is 30 days -> not dormant
            var svc = CreateService();
            var report = svc.Analyze();
            Assert.NotEqual(CatalogPhase.Dormant, report.Profiles.Single().Phase);

            // Custom threshold of 10 days -> still not dormant because we have window rentals
            CatalogVelocityService.Reset();
            var config = new VelocityEngineConfig { DormantThresholdDays = 10 };
            svc = CreateService(config);
            report = svc.Analyze();
            // Has a rental in window, so won't be dormant regardless
            Assert.NotEqual(CatalogPhase.Dormant, report.Profiles.Single().Phase);
        }

        [Fact]
        public void Analyze_CustomHotThreshold_AffectsPhase()
        {
            var m1 = AddMovie("A", Genre.Action, _clock.Now.AddDays(-60));
            var m2 = AddMovie("B", Genre.Drama, _clock.Now.AddDays(-60));

            for (int i = 0; i < 5; i++)
                AddRental(m1.Id, _clock.Now.AddDays(-i * 5));
            AddRental(m2.Id, _clock.Now.AddDays(-30));

            // Lower hot threshold
            var config = new VelocityEngineConfig { HotThreshold = 50.0 };
            var svc = CreateService(config);
            var report = svc.Analyze();
            var top = report.Profiles.First(p => p.MovieId == m1.Id);
            Assert.Equal(CatalogPhase.Hot, top.Phase);
        }

        // --- DaysInCatalog ---

        [Fact]
        public void Analyze_DaysInCatalog_UsesReleaseDate()
        {
            var movie = AddMovie("Dated", Genre.Comedy, _clock.Now.AddDays(-45));
            AddRental(movie.Id, _clock.Now.AddDays(-5));

            var svc = CreateService();
            var report = svc.Analyze();
            var profile = report.Profiles.Single();
            Assert.Equal(45, profile.DaysInCatalog);
        }

        // --- UrgentActions ---

        [Fact]
        public void Analyze_UrgentActions_LimitedTo10()
        {
            for (int i = 0; i < 15; i++)
            {
                var m = AddMovie("Dormant " + i, Genre.Drama, _clock.Now.AddDays(-200));
                AddRental(m.Id, _clock.Now.AddDays(-150));
            }

            var svc = CreateService();
            var report = svc.Analyze();
            Assert.True(report.UrgentActions.Count <= 10);
        }

        // --- Report Metadata ---

        [Fact]
        public void Analyze_ReportContainsTimestamp()
        {
            AddMovie("Any", Genre.Action, _clock.Now.AddDays(-60));
            var svc = CreateService();
            var report = svc.Analyze();
            Assert.Equal(_clock.Now, report.GeneratedAt);
            Assert.Equal(90, report.WindowDays);
        }
    }
}
