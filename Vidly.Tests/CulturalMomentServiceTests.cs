using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Xunit;

namespace Vidly.Tests
{
    public class CulturalMomentServiceTests
    {
        private readonly InMemoryMovieRepository _movieRepo;
        private readonly InMemoryRentalRepository _rentalRepo;
        private readonly TestClock _clock;

        public CulturalMomentServiceTests()
        {
            _movieRepo = new InMemoryMovieRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _clock = new TestClock(new DateTime(2025, 7, 1, 12, 0, 0));
        }

        private CulturalMomentService CreateService(CulturalMomentConfig config = null)
        {
            return new CulturalMomentService(_rentalRepo, _movieRepo, _clock, config);
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
        public void Constructor_NullRentalRepo_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CulturalMomentService(null, _movieRepo, _clock));
        }

        [Fact]
        public void Constructor_NullMovieRepo_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CulturalMomentService(_rentalRepo, null, _clock));
        }

        [Fact]
        public void Constructor_NullClock_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CulturalMomentService(_rentalRepo, _movieRepo, null));
        }

        [Fact]
        public void Constructor_NullConfig_UsesDefaults()
        {
            var service = CreateService(null);
            var report = service.Analyze();
            Assert.NotNull(report);
        }

        // --- Empty Data ---

        [Fact]
        public void Analyze_NoMovies_ReturnsEmptyReport()
        {
            var service = CreateService();
            var report = service.Analyze();
            Assert.Equal(0, report.TotalMomentsDetected);
            Assert.NotNull(report.Moments);
            Assert.NotNull(report.Insights);
        }

        [Fact]
        public void Analyze_NoRentals_ReturnsReport()
        {
            AddMovie("Test Movie", Genre.Action, new DateTime(2020, 1, 1));
            var service = CreateService();
            var report = service.Analyze();
            Assert.NotNull(report);
            Assert.True(report.CulturalPulseScore >= 50);
        }

        [Fact]
        public void Analyze_GeneratedAt_MatchesClock()
        {
            var service = CreateService();
            var report = service.Analyze();
            Assert.Equal(_clock.Now, report.GeneratedAt);
        }

        // --- Anniversary Detection ---

        [Fact]
        public void Anniversary_10YearMilestone_Detected()
        {
            // Movie released 10 years ago within 30-day window
            AddMovie("Classic Film", Genre.Drama, new DateTime(2015, 7, 15));
            var service = CreateService();
            var report = service.Analyze();
            var anniversaries = report.Moments.Where(m => m.MomentType == "Anniversary").ToList();
            Assert.True(anniversaries.Count > 0);
            Assert.Contains(anniversaries, a => a.Description.Contains("10-year"));
        }

        [Fact]
        public void Anniversary_NotInWindow_NotDetected()
        {
            // Movie released 10 years ago but far outside window
            AddMovie("Old Film", Genre.Drama, new DateTime(2015, 1, 1));
            var service = CreateService();
            var report = service.Analyze();
            var anniversaries = report.Moments.Where(m => m.MomentType == "Anniversary").ToList();
            Assert.Empty(anniversaries);
        }

        [Fact]
        public void Anniversary_NoReleaseDate_Skipped()
        {
            AddMovie("Unknown Date Film", Genre.Action, null);
            var service = CreateService();
            var report = service.Analyze();
            var anniversaries = report.Moments.Where(m => m.MomentType == "Anniversary").ToList();
            Assert.Empty(anniversaries);
        }

        [Fact]
        public void Anniversary_25Year_HigherPriority()
        {
            AddMovie("Ancient Classic", Genre.Drama, new DateTime(2000, 7, 15));
            var service = CreateService();
            var report = service.Analyze();
            var anniversaries = report.Moments.Where(m => m.MomentType == "Anniversary").ToList();
            Assert.True(anniversaries.Count > 0);
            var moment = anniversaries.First();
            Assert.Equal(1, moment.Priority);
            Assert.Equal("Feature", moment.RecommendedAction);
        }

        [Fact]
        public void Anniversary_5Year_LowerPriority()
        {
            AddMovie("Recent Hit", Genre.Comedy, new DateTime(2020, 7, 10));
            var service = CreateService();
            var report = service.Analyze();
            var anniversaries = report.Moments.Where(m => m.MomentType == "Anniversary").ToList();
            Assert.True(anniversaries.Count > 0);
            Assert.Equal(2, anniversaries.First().Priority);
        }

        // --- Franchise Surge Detection ---

        [Fact]
        public void FranchiseSurge_MultipleRentals_Detected()
        {
            var m1 = AddMovie("Star Wars", Genre.SciFi, new DateTime(1977, 5, 25));
            var m2 = AddMovie("Star Trek", Genre.SciFi, new DateTime(1979, 12, 7));
            // Enough rentals on franchise prefix "star"
            AddRental(m1.Id, new DateTime(2025, 6, 10));
            AddRental(m1.Id, new DateTime(2025, 6, 15));
            AddRental(m1.Id, new DateTime(2025, 6, 20));

            var service = CreateService();
            var report = service.Analyze();
            var surges = report.Moments.Where(m => m.MomentType == "FranchiseSurge").ToList();
            Assert.True(surges.Count > 0);
        }

        [Fact]
        public void FranchiseSurge_BelowThreshold_NotDetected()
        {
            var m1 = AddMovie("Star Wars", Genre.SciFi, new DateTime(1977, 5, 25));
            var m2 = AddMovie("Star Trek", Genre.SciFi, new DateTime(1979, 12, 7));
            // Only 1 rental - below default threshold of 3
            AddRental(m1.Id, new DateTime(2025, 6, 10));

            var service = CreateService();
            var report = service.Analyze();
            var surges = report.Moments.Where(m => m.MomentType == "FranchiseSurge").ToList();
            Assert.Empty(surges);
        }

        [Fact]
        public void FranchiseSurge_UnrentedMovieGetsRestock()
        {
            var m1 = AddMovie("Fast Five", Genre.Action, new DateTime(2011, 4, 29));
            var m2 = AddMovie("Fast Six", Genre.Action, new DateTime(2013, 5, 24));
            AddRental(m1.Id, new DateTime(2025, 6, 5));
            AddRental(m1.Id, new DateTime(2025, 6, 10));
            AddRental(m1.Id, new DateTime(2025, 6, 15));

            var service = CreateService();
            var report = service.Analyze();
            var surges = report.Moments.Where(m => m.MomentType == "FranchiseSurge" && m.MovieId == m2.Id).ToList();
            Assert.True(surges.Count > 0);
            Assert.Equal("Restock", surges.First().RecommendedAction);
        }

        // --- Genre Momentum ---

        [Fact]
        public void GenreMomentum_SurgingGenre_Detected()
        {
            var m1 = AddMovie("Horror One", Genre.Horror, new DateTime(2020, 1, 1));
            var m2 = AddMovie("Horror Two", Genre.Horror, new DateTime(2021, 1, 1));
            // Lots of recent rentals, very few historical
            for (int i = 0; i < 10; i++)
                AddRental(m1.Id, new DateTime(2025, 6, 5 + i));
            // Only 1 historical
            AddRental(m2.Id, new DateTime(2025, 3, 1));

            var service = CreateService();
            var report = service.Analyze();
            var momentum = report.GenreMomentum.FirstOrDefault(g => g.Genre == Genre.Horror);
            Assert.NotNull(momentum);
            Assert.Equal("Surging", momentum.Trend);
        }

        [Fact]
        public void GenreMomentum_StableGenre_NoMoment()
        {
            var m1 = AddMovie("Drama Film", Genre.Drama, new DateTime(2020, 1, 1));
            // Equal distribution
            AddRental(m1.Id, new DateTime(2025, 6, 15));
            AddRental(m1.Id, new DateTime(2025, 4, 15));
            AddRental(m1.Id, new DateTime(2025, 3, 15));
            AddRental(m1.Id, new DateTime(2025, 2, 15));

            var service = CreateService();
            var report = service.Analyze();
            var dramaMomentum = report.GenreMomentum.FirstOrDefault(g => g.Genre == Genre.Drama);
            Assert.NotNull(dramaMomentum);
            // Should not be surging
            Assert.NotEqual("Surging", dramaMomentum.Trend);
        }

        [Fact]
        public void GenreMomentum_AllGenresPresent()
        {
            foreach (Genre g in Enum.GetValues(typeof(Genre)))
                AddMovie("Movie " + g, g, new DateTime(2020, 1, 1));

            var service = CreateService();
            var momentum = service.GetGenreMomentum();
            Assert.Equal(Enum.GetValues(typeof(Genre)).Length, momentum.Count);
        }

        // --- Nostalgia Cycle ---

        [Fact]
        public void NostalgiaCycle_20YearOldWithRentals_Detected()
        {
            // 20 years old: released 2005, now = 2025
            var m = AddMovie("Nostalgia Film", Genre.Comedy, new DateTime(2005, 3, 1));
            AddRental(m.Id, new DateTime(2025, 6, 20));

            var service = CreateService();
            var report = service.Analyze();
            var nostalgia = report.Moments.Where(x => x.MomentType == "NostalgiaCycle").ToList();
            Assert.True(nostalgia.Count > 0);
            Assert.Contains(nostalgia, n => n.MovieId == m.Id);
        }

        [Fact]
        public void NostalgiaCycle_10YearOld_NotDetected()
        {
            var m = AddMovie("Too Young", Genre.Comedy, new DateTime(2015, 3, 1));
            AddRental(m.Id, new DateTime(2025, 6, 20));

            var service = CreateService();
            var report = service.Analyze();
            var nostalgia = report.Moments.Where(x => x.MomentType == "NostalgiaCycle").ToList();
            Assert.DoesNotContain(nostalgia, n => n.MovieId == m.Id);
        }

        [Fact]
        public void NostalgiaCycle_NoRecentRentals_NotDetected()
        {
            var m = AddMovie("Forgotten Classic", Genre.Drama, new DateTime(2005, 3, 1));
            // No rentals at all
            var service = CreateService();
            var report = service.Analyze();
            var nostalgia = report.Moments.Where(x => x.MomentType == "NostalgiaCycle").ToList();
            Assert.DoesNotContain(nostalgia, n => n.MovieId == m.Id);
        }

        // --- Dormant Revival ---

        [Fact]
        public void DormantRevival_LongGapThenRental_Detected()
        {
            var m = AddMovie("Sleeper Hit", Genre.Thriller, new DateTime(2018, 1, 1));
            // Old rental
            AddRental(m.Id, new DateTime(2025, 2, 1));
            // Recent rental after >60 day gap
            AddRental(m.Id, new DateTime(2025, 6, 20));

            var service = CreateService();
            var report = service.Analyze();
            var revivals = report.Moments.Where(x => x.MomentType == "DormantRevival").ToList();
            Assert.True(revivals.Count > 0);
            Assert.Contains(revivals, r => r.MovieId == m.Id);
        }

        [Fact]
        public void DormantRevival_ShortGap_NotDetected()
        {
            var m = AddMovie("Active Film", Genre.Action, new DateTime(2020, 1, 1));
            AddRental(m.Id, new DateTime(2025, 5, 20));
            AddRental(m.Id, new DateTime(2025, 6, 20));

            var service = CreateService();
            var report = service.Analyze();
            var revivals = report.Moments.Where(x => x.MomentType == "DormantRevival").ToList();
            Assert.DoesNotContain(revivals, r => r.MovieId == m.Id);
        }

        [Fact]
        public void DormantRevival_OnlyOneRental_NotDetected()
        {
            var m = AddMovie("Single Rental", Genre.Drama, new DateTime(2019, 1, 1));
            AddRental(m.Id, new DateTime(2025, 6, 20));

            var service = CreateService();
            var report = service.Analyze();
            var revivals = report.Moments.Where(x => x.MomentType == "DormantRevival").ToList();
            Assert.Empty(revivals);
        }

        // --- Spotlight Detection ---

        [Fact]
        public void Spotlight_RelatedTitlesTrending_Detected()
        {
            // Movies sharing second word "Returns"
            var m1 = AddMovie("Batman Returns", Genre.Action, new DateTime(1992, 6, 19));
            var m2 = AddMovie("Superman Returns", Genre.Action, new DateTime(2006, 6, 28));
            var m3 = AddMovie("Jedi Returns", Genre.SciFi, new DateTime(1983, 5, 25));

            AddRental(m1.Id, new DateTime(2025, 6, 10));
            AddRental(m2.Id, new DateTime(2025, 6, 15));
            AddRental(m2.Id, new DateTime(2025, 6, 20));

            var service = CreateService();
            var report = service.Analyze();
            var spotlights = report.Moments.Where(x => x.MomentType == "Spotlight").ToList();
            // m3 should be spotted as unrented related title
            Assert.Contains(spotlights, s => s.MovieId == m3.Id);
        }

        // --- Health Score ---

        [Fact]
        public void HealthScore_EmptyCatalog_Returns50()
        {
            var service = CreateService();
            var report = service.Analyze();
            Assert.Equal(50, report.CulturalPulseScore);
        }

        [Fact]
        public void HealthScore_WithAnniversary_Increases()
        {
            AddMovie("Anniversary Film", Genre.Drama, new DateTime(2015, 7, 15));
            var service = CreateService();
            var report = service.Analyze();
            Assert.True(report.CulturalPulseScore >= 60);
        }

        [Fact]
        public void HealthScore_CappedAt100()
        {
            // Create conditions for all moment types
            AddMovie("Star One", Genre.SciFi, new DateTime(2000, 7, 5)); // Anniversary 25yr
            var m2 = AddMovie("Star Two", Genre.SciFi, new DateTime(2005, 1, 1)); // Nostalgia 20yr
            var m3 = AddMovie("Star Three", Genre.SciFi, new DateTime(2020, 1, 1));
            var m4 = AddMovie("Dormant X", Genre.Horror, new DateTime(2018, 1, 1));

            // Franchise surge
            AddRental(m2.Id, new DateTime(2025, 6, 5));
            AddRental(m2.Id, new DateTime(2025, 6, 10));
            AddRental(m3.Id, new DateTime(2025, 6, 15));
            // Nostalgia rental
            AddRental(m2.Id, new DateTime(2025, 6, 20));
            // Dormant revival
            AddRental(m4.Id, new DateTime(2025, 2, 1));
            AddRental(m4.Id, new DateTime(2025, 6, 25));

            // Genre momentum boost - many horror rentals recently
            for (int i = 0; i < 8; i++)
                AddRental(m4.Id, new DateTime(2025, 6, 1 + i));

            var service = CreateService();
            var report = service.Analyze();
            Assert.True(report.CulturalPulseScore <= 100);
        }

        // --- Insights ---

        [Fact]
        public void Insights_WithMoments_NotEmpty()
        {
            AddMovie("Anniversary Film", Genre.Drama, new DateTime(2015, 7, 15));
            var service = CreateService();
            var report = service.Analyze();
            Assert.True(report.Insights.Count > 0);
        }

        [Fact]
        public void Insights_NoMoments_QuietMessage()
        {
            var service = CreateService();
            var report = service.Analyze();
            Assert.Contains(report.Insights, i => i.Contains("quiet period"));
        }

        // --- GetTopMoments ---

        [Fact]
        public void GetTopMoments_ReturnsLimitedCount()
        {
            // Create many moment-triggering movies
            for (int y = 2000; y <= 2020; y += 5)
                AddMovie("Film " + y, Genre.Action, new DateTime(y, 7, 10));

            var service = CreateService();
            var top = service.GetTopMoments(3);
            Assert.True(top.Count <= 3);
        }

        [Fact]
        public void GetTopMoments_SortedByRelevance()
        {
            AddMovie("Film A", Genre.Action, new DateTime(2000, 7, 5)); // 25yr anniversary
            AddMovie("Film B", Genre.Drama, new DateTime(2020, 7, 10)); // 5yr anniversary

            var service = CreateService();
            var top = service.GetTopMoments(10);
            if (top.Count >= 2)
            {
                Assert.True(top[0].RelevanceScore >= top[1].RelevanceScore);
            }
        }

        // --- GetMomentsByType ---

        [Fact]
        public void GetMomentsByType_FiltersCorrectly()
        {
            AddMovie("Anniversary Film", Genre.Drama, new DateTime(2015, 7, 15));
            var m = AddMovie("Dormant Film", Genre.Horror, new DateTime(2018, 1, 1));
            AddRental(m.Id, new DateTime(2025, 2, 1));
            AddRental(m.Id, new DateTime(2025, 6, 20));

            var service = CreateService();
            var anniversaries = service.GetMomentsByType("Anniversary");
            Assert.All(anniversaries, a => Assert.Equal("Anniversary", a.MomentType));
        }

        [Fact]
        public void GetMomentsByType_CaseInsensitive()
        {
            AddMovie("Anniversary Film", Genre.Drama, new DateTime(2015, 7, 15));
            var service = CreateService();
            var result = service.GetMomentsByType("anniversary");
            Assert.True(result.Count > 0);
        }

        [Fact]
        public void GetMomentsByType_NullOrEmpty_ReturnsEmpty()
        {
            var service = CreateService();
            Assert.Empty(service.GetMomentsByType(null));
            Assert.Empty(service.GetMomentsByType(""));
            Assert.Empty(service.GetMomentsByType("   "));
        }

        // --- Config Customization ---

        [Fact]
        public void Config_CustomDormantThreshold_Respected()
        {
            var m = AddMovie("Short Dormant", Genre.Thriller, new DateTime(2018, 1, 1));
            // Gap of 40 days (below default 60 but above custom 30)
            AddRental(m.Id, new DateTime(2025, 5, 20));
            AddRental(m.Id, new DateTime(2025, 6, 29));

            var configStrict = new CulturalMomentConfig { DormantDaysThreshold = 30 };
            var service = CreateService(configStrict);
            var report = service.Analyze();
            var revivals = report.Moments.Where(x => x.MomentType == "DormantRevival").ToList();
            Assert.Contains(revivals, r => r.MovieId == m.Id);
        }

        [Fact]
        public void Config_CustomAnniversaryWindow_Respected()
        {
            // Movie anniversary is 20 days away
            AddMovie("Narrow Window", Genre.Comedy, new DateTime(2015, 7, 20));
            // With default window (30 days) it would be detected
            // With narrow window (10 days) it won't
            var config = new CulturalMomentConfig { AnniversaryWindowDays = 10 };
            var service = CreateService(config);
            var report = service.Analyze();
            var anniversaries = report.Moments.Where(m => m.MomentType == "Anniversary").ToList();
            Assert.Empty(anniversaries);
        }

        // --- MomentsByType Dictionary ---

        [Fact]
        public void Report_MomentsByType_CorrectCounts()
        {
            AddMovie("Ann Film", Genre.Drama, new DateTime(2015, 7, 15));
            var m = AddMovie("Revival Film", Genre.Horror, new DateTime(2018, 1, 1));
            AddRental(m.Id, new DateTime(2025, 2, 1));
            AddRental(m.Id, new DateTime(2025, 6, 20));

            var service = CreateService();
            var report = service.Analyze();

            foreach (var kvp in report.MomentsByType)
            {
                var actual = report.Moments.Count(x => x.MomentType == kvp.Key);
                Assert.Equal(actual, kvp.Value);
            }
        }

        // --- Edge Cases ---

        [Fact]
        public void Analyze_MovieWithEmptyName_NoException()
        {
            AddMovie("", Genre.Action, new DateTime(2020, 7, 5));
            var service = CreateService();
            var report = service.Analyze(); // Should not throw
            Assert.NotNull(report);
        }

        [Fact]
        public void Analyze_AllMoviesNoRelease_NoAnniversaries()
        {
            AddMovie("No Date 1", Genre.Action, null);
            AddMovie("No Date 2", Genre.Drama, null);
            var service = CreateService();
            var report = service.Analyze();
            Assert.DoesNotContain(report.Moments, m => m.MomentType == "Anniversary");
        }

        [Fact]
        public void GenreMomentum_NoRentals_AllStable()
        {
            foreach (Genre g in Enum.GetValues(typeof(Genre)))
                AddMovie("M " + g, g, new DateTime(2020, 1, 1));

            var service = CreateService();
            var momentum = service.GetGenreMomentum();
            Assert.All(momentum, m => Assert.Equal("Stable", m.Trend));
        }
    }
}
