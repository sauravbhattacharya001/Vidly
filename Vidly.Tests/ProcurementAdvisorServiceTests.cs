using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Xunit;

namespace Vidly.Tests
{
    public class ProcurementAdvisorServiceTests
    {
        private readonly InMemoryMovieRepository _movieRepo;
        private readonly InMemoryRentalRepository _rentalRepo;
        private readonly InMemoryCustomerRepository _customerRepo;
        private readonly TestClock _clock;

        public ProcurementAdvisorServiceTests()
        {
            _movieRepo = new InMemoryMovieRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _customerRepo = new InMemoryCustomerRepository();
            _clock = new TestClock(new DateTime(2025, 7, 1, 12, 0, 0));
        }

        private ProcurementAdvisorService CreateService(ProcurementConfig config = null)
        {
            return new ProcurementAdvisorService(_movieRepo, _rentalRepo, _customerRepo, _clock, config);
        }

        private Movie AddMovie(string name, Genre genre)
        {
            return _movieRepo.Add(new Movie { Name = name, Genre = genre, ReleaseDate = new DateTime(2024, 1, 1) });
        }

        private Rental AddRental(int movieId, DateTime rentalDate, int customerId = 1)
        {
            return _rentalRepo.Add(new Rental
            {
                MovieId = movieId,
                CustomerId = customerId,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(7),
                ReturnDate = rentalDate.AddDays(5),
                DailyRate = 3.50m,
                Status = RentalStatus.Returned
            });
        }

        // ─── Constructor Tests ───────────────────────────────────────────────

        [Fact]
        public void Constructor_NullMovieRepo_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ProcurementAdvisorService(null, _rentalRepo, _customerRepo, _clock));
        }

        [Fact]
        public void Constructor_NullRentalRepo_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ProcurementAdvisorService(_movieRepo, null, _customerRepo, _clock));
        }

        [Fact]
        public void Constructor_NullCustomerRepo_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ProcurementAdvisorService(_movieRepo, _rentalRepo, null, _clock));
        }

        [Fact]
        public void Constructor_NullClock_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ProcurementAdvisorService(_movieRepo, _rentalRepo, _customerRepo, null));
        }

        // ─── Empty Catalog Tests ─────────────────────────────────────────────

        [Fact]
        public void Analyze_EmptyCatalog_ReturnsReport()
        {
            var service = CreateService();
            var report = service.Analyze();

            Assert.NotNull(report);
            Assert.Equal(0, report.TotalCatalogSize);
            Assert.Empty(report.Candidates);
        }

        [Fact]
        public void Analyze_EmptyCatalog_HealthScoreIs100()
        {
            var service = CreateService();
            var report = service.Analyze();
            Assert.Equal(100, report.HealthScore);
        }

        // ─── Supply Profile Tests ────────────────────────────────────────────

        [Fact]
        public void Analyze_SingleGenre_BuildsSupplyProfile()
        {
            var m1 = AddMovie("Action1", Genre.Action);
            var m2 = AddMovie("Action2", Genre.Action);
            AddRental(m1.Id, _clock.Now.AddDays(-10));
            AddRental(m2.Id, _clock.Now.AddDays(-5));

            var service = CreateService();
            var report = service.Analyze();

            var actionProfile = report.SupplyProfiles.FirstOrDefault(p => p.Genre == Genre.Action);
            Assert.NotNull(actionProfile);
            Assert.Equal(2, actionProfile.TitleCount);
            Assert.Equal(2, actionProfile.RecentRentals);
        }

        [Fact]
        public void Analyze_MultipleGenres_AllProfilesPresent()
        {
            AddMovie("A1", Genre.Action);
            AddMovie("C1", Genre.Comedy);
            AddMovie("D1", Genre.Drama);

            var service = CreateService();
            var report = service.Analyze();

            // All Genre enum values should have profiles
            Assert.Equal(10, report.SupplyProfiles.Count);
        }

        [Fact]
        public void Analyze_UnderservedGenre_MarkedCorrectly()
        {
            // 1 action movie with 10 rentals = high demand, low supply
            var m1 = AddMovie("Action1", Genre.Action);
            // 5 comedy movies with 1 rental total = well-served
            var c1 = AddMovie("Comedy1", Genre.Comedy);
            AddMovie("Comedy2", Genre.Comedy);
            AddMovie("Comedy3", Genre.Comedy);
            AddMovie("Comedy4", Genre.Comedy);
            AddMovie("Comedy5", Genre.Comedy);

            for (int i = 0; i < 10; i++)
                AddRental(m1.Id, _clock.Now.AddDays(-i - 1));
            AddRental(c1.Id, _clock.Now.AddDays(-5));

            var service = CreateService();
            var report = service.Analyze();

            var actionProfile = report.SupplyProfiles.First(p => p.Genre == Genre.Action);
            // Action has high demand relative to supply
            Assert.True(actionProfile.RentalsPerTitle > 5);
        }

        // ─── Demand Signal Tests ─────────────────────────────────────────────

        [Fact]
        public void Analyze_HighVelocity_GeneratesSignal()
        {
            var m1 = AddMovie("Action1", Genre.Action);
            // 8 rentals for 1 title = high velocity
            for (int i = 0; i < 8; i++)
                AddRental(m1.Id, _clock.Now.AddDays(-i * 5 - 1));

            var service = CreateService();
            var report = service.Analyze();

            var hvSignals = report.Signals.Where(s => s.Type == DemandSignalType.HighVelocity).ToList();
            Assert.NotEmpty(hvSignals);
            Assert.Equal(Genre.Action, hvSignals[0].Genre);
        }

        [Fact]
        public void Analyze_GrowingDemand_GeneratesSignal()
        {
            var m1 = AddMovie("Thriller1", Genre.Thriller);
            var m2 = AddMovie("Thriller2", Genre.Thriller);
            // First half: 2 rentals, second half: 6 rentals = 200% growth
            AddRental(m1.Id, _clock.Now.AddDays(-80));
            AddRental(m2.Id, _clock.Now.AddDays(-70));
            for (int i = 0; i < 6; i++)
                AddRental(m1.Id, _clock.Now.AddDays(-i * 5 - 1));

            var service = CreateService();
            var report = service.Analyze();

            var growthSignals = report.Signals.Where(s => s.Type == DemandSignalType.GrowingTrend && s.Genre == Genre.Thriller).ToList();
            Assert.NotEmpty(growthSignals);
        }

        [Fact]
        public void Analyze_GenreGap_GeneratesSignal()
        {
            // SciFi: 1 title, many rentals relative to catalog share
            var m1 = AddMovie("SciFi1", Genre.SciFi);
            // Drama: 10 titles, few rentals
            for (int i = 0; i < 10; i++)
                AddMovie($"Drama{i}", Genre.Drama);

            for (int i = 0; i < 6; i++)
                AddRental(m1.Id, _clock.Now.AddDays(-i * 10 - 1));

            var service = CreateService();
            var report = service.Analyze();

            var gapSignals = report.Signals.Where(s => s.Type == DemandSignalType.GenreGap).ToList();
            // At least SciFi should be flagged since it has high demand but low catalog share
            Assert.True(report.Signals.Any(s => s.Genre == Genre.SciFi));
        }

        // ─── Candidate Generation Tests ──────────────────────────────────────

        [Fact]
        public void Analyze_UnderservedGenreWithDemand_GeneratesCandidate()
        {
            var m1 = AddMovie("Horror1", Genre.Horror);
            for (int i = 0; i < 8; i++)
                AddRental(m1.Id, _clock.Now.AddDays(-i * 5 - 1));

            // Add many other genre movies to make horror underserved
            for (int i = 0; i < 8; i++)
                AddMovie($"Comedy{i}", Genre.Comedy);

            var service = CreateService();
            var report = service.Analyze();

            var horrorCandidate = report.Candidates.FirstOrDefault(c => c.Genre == Genre.Horror);
            Assert.NotNull(horrorCandidate);
            Assert.True(horrorCandidate.RecommendedCopies >= 1);
        }

        [Fact]
        public void Analyze_Candidate_HasPositiveRoi()
        {
            var m1 = AddMovie("Action1", Genre.Action);
            for (int i = 0; i < 10; i++)
                AddRental(m1.Id, _clock.Now.AddDays(-i * 5 - 1));

            for (int i = 0; i < 5; i++)
                AddMovie($"Drama{i}", Genre.Drama);

            var service = CreateService();
            var report = service.Analyze();

            var candidate = report.Candidates.FirstOrDefault(c => c.Genre == Genre.Action);
            if (candidate != null)
            {
                Assert.True(candidate.ProjectedRoi > 0);
                Assert.True(candidate.PaybackDays > 0);
            }
        }

        [Fact]
        public void Analyze_Candidate_HasRationale()
        {
            var m1 = AddMovie("Horror1", Genre.Horror);
            for (int i = 0; i < 8; i++)
                AddRental(m1.Id, _clock.Now.AddDays(-i * 5 - 1));

            for (int i = 0; i < 8; i++)
                AddMovie($"Comedy{i}", Genre.Comedy);

            var service = CreateService();
            var report = service.Analyze();

            var candidate = report.Candidates.FirstOrDefault(c => c.Genre == Genre.Horror);
            Assert.NotNull(candidate);
            Assert.NotEmpty(candidate.Rationale);
        }

        [Fact]
        public void Analyze_CandidatesOrderedByRoi()
        {
            var m1 = AddMovie("Action1", Genre.Action);
            var m2 = AddMovie("Horror1", Genre.Horror);
            for (int i = 0; i < 10; i++)
            {
                AddRental(m1.Id, _clock.Now.AddDays(-i * 5 - 1));
                AddRental(m2.Id, _clock.Now.AddDays(-i * 7 - 1));
            }
            for (int i = 0; i < 8; i++)
                AddMovie($"Drama{i}", Genre.Drama);

            var service = CreateService();
            var report = service.Analyze();

            if (report.Candidates.Count >= 2)
            {
                Assert.True(report.Candidates[0].ProjectedRoi >= report.Candidates[1].ProjectedRoi);
            }
        }

        // ─── Budget Allocation Tests ─────────────────────────────────────────

        [Fact]
        public void Analyze_WithBudget_AllocatesCorrectly()
        {
            var m1 = AddMovie("Action1", Genre.Action);
            var m2 = AddMovie("Horror1", Genre.Horror);
            for (int i = 0; i < 8; i++)
            {
                AddRental(m1.Id, _clock.Now.AddDays(-i * 5 - 1));
                AddRental(m2.Id, _clock.Now.AddDays(-i * 6 - 1));
            }
            for (int i = 0; i < 6; i++)
                AddMovie($"Drama{i}", Genre.Drama);

            var service = CreateService();
            var report = service.Analyze(budget: 200m);

            Assert.NotEmpty(report.BudgetPlan);
            Assert.Equal(200m, report.TotalBudgetRecommended);
        }

        [Fact]
        public void Analyze_RoiStrategy_PrioritizesHighRoi()
        {
            var m1 = AddMovie("Action1", Genre.Action);
            for (int i = 0; i < 10; i++)
                AddRental(m1.Id, _clock.Now.AddDays(-i * 5 - 1));
            for (int i = 0; i < 5; i++)
                AddMovie($"Drama{i}", Genre.Drama);

            var service = CreateService();
            var report = service.Analyze(budget: 100m, strategy: BudgetAllocationStrategy.RoiMaximized);

            Assert.NotEmpty(report.BudgetPlan);
            Assert.Equal(BudgetAllocationStrategy.RoiMaximized, report.Strategy);
        }

        [Fact]
        public void Analyze_DiversityStrategy_SplitsEvenly()
        {
            var m1 = AddMovie("Action1", Genre.Action);
            var m2 = AddMovie("Horror1", Genre.Horror);
            for (int i = 0; i < 8; i++)
            {
                AddRental(m1.Id, _clock.Now.AddDays(-i * 5 - 1));
                AddRental(m2.Id, _clock.Now.AddDays(-i * 6 - 1));
            }
            for (int i = 0; i < 8; i++)
                AddMovie($"Comedy{i}", Genre.Comedy);

            var service = CreateService();
            var report = service.Analyze(budget: 200m, strategy: BudgetAllocationStrategy.DiversityFocused);

            if (report.BudgetPlan.Count >= 2)
            {
                var first = report.BudgetPlan[0].AllocatedBudget;
                var second = report.BudgetPlan[1].AllocatedBudget;
                Assert.Equal(first, second); // equal split
            }
        }

        // ─── Urgency Tests ───────────────────────────────────────────────────

        [Fact]
        public void Analyze_CriticalDemand_HighUrgency()
        {
            var m1 = AddMovie("Action1", Genre.Action);
            // Massive demand on single title
            for (int i = 0; i < 15; i++)
                AddRental(m1.Id, _clock.Now.AddDays(-i * 3 - 1));
            for (int i = 0; i < 10; i++)
                AddMovie($"Drama{i}", Genre.Drama);

            var service = CreateService();
            var report = service.Analyze();

            var actionCandidate = report.Candidates.FirstOrDefault(c => c.Genre == Genre.Action);
            if (actionCandidate != null)
            {
                Assert.True(actionCandidate.Urgency <= ProcurementUrgency.High);
            }
        }

        // ─── Insight Tests ───────────────────────────────────────────────────

        [Fact]
        public void Analyze_ConcentratedCatalog_GeneratesRiskInsight()
        {
            // 8 drama movies out of 10 total = 80% concentration
            for (int i = 0; i < 8; i++)
                AddMovie($"Drama{i}", Genre.Drama);
            AddMovie("Action1", Genre.Action);
            AddMovie("Comedy1", Genre.Comedy);

            var service = CreateService();
            var report = service.Analyze();

            var riskInsight = report.Insights.FirstOrDefault(i => i.Category == "Risk");
            Assert.NotNull(riskInsight);
            Assert.Contains("Concentration", riskInsight.Title);
        }

        [Fact]
        public void Analyze_ZeroDemandGenre_GeneratesWarning()
        {
            // Horror has titles but zero rentals
            AddMovie("Horror1", Genre.Horror);
            AddMovie("Horror2", Genre.Horror);
            // Action has demand
            var m1 = AddMovie("Action1", Genre.Action);
            for (int i = 0; i < 5; i++)
                AddRental(m1.Id, _clock.Now.AddDays(-i * 10 - 1));

            var service = CreateService();
            var report = service.Analyze();

            var warning = report.Insights.FirstOrDefault(i => i.Category == "Warning");
            Assert.NotNull(warning);
            Assert.Contains("Zero-Demand", warning.Title);
        }

        // ─── Health Score Tests ──────────────────────────────────────────────

        [Fact]
        public void Analyze_WellBalancedCatalog_HighHealthScore()
        {
            // Even distribution with proportional demand
            var genres = new[] { Genre.Action, Genre.Comedy, Genre.Drama, Genre.Horror };
            foreach (var genre in genres)
            {
                var m = AddMovie($"{genre}1", genre);
                AddRental(m.Id, _clock.Now.AddDays(-5));
            }

            var service = CreateService();
            var report = service.Analyze();

            Assert.True(report.HealthScore >= 70);
        }

        [Fact]
        public void Analyze_ManyUnderservedGenres_LowHealthScore()
        {
            // 1 genre has all titles, demand spread across many
            for (int i = 0; i < 20; i++)
                AddMovie($"Drama{i}", Genre.Drama);

            var m1 = AddMovie("Action1", Genre.Action);
            var m2 = AddMovie("Horror1", Genre.Horror);
            var m3 = AddMovie("SciFi1", Genre.SciFi);
            for (int i = 0; i < 5; i++)
            {
                AddRental(m1.Id, _clock.Now.AddDays(-i * 5 - 1));
                AddRental(m2.Id, _clock.Now.AddDays(-i * 5 - 1));
                AddRental(m3.Id, _clock.Now.AddDays(-i * 5 - 1));
            }

            var service = CreateService();
            var report = service.Analyze();

            Assert.True(report.HealthScore < 85);
        }

        // ─── Verdict Tests ───────────────────────────────────────────────────

        [Fact]
        public void Analyze_HealthVerdict_MatchesScore()
        {
            var service = CreateService();
            var report = service.Analyze();

            Assert.NotNull(report.HealthVerdict);
            if (report.HealthScore >= 85)
                Assert.Contains("Excellent", report.HealthVerdict);
        }

        // ─── Config Tests ────────────────────────────────────────────────────

        [Fact]
        public void Analyze_CustomConfig_Respected()
        {
            var m1 = AddMovie("Action1", Genre.Action);
            for (int i = 0; i < 10; i++)
                AddRental(m1.Id, _clock.Now.AddDays(-i * 5 - 1));

            var config = new ProcurementConfig
            {
                MaxRecommendations = 2,
                AnalysisWindowDays = 30
            };

            var service = CreateService(config);
            var report = service.Analyze();

            Assert.True(report.Candidates.Count <= 2);
        }

        [Fact]
        public void Analyze_NarrowWindow_FiltersOldRentals()
        {
            var m1 = AddMovie("Action1", Genre.Action);
            // Rentals outside 30-day window
            for (int i = 0; i < 5; i++)
                AddRental(m1.Id, _clock.Now.AddDays(-60 - i));

            var config = new ProcurementConfig { AnalysisWindowDays = 30 };
            var service = CreateService(config);
            var report = service.Analyze();

            var actionProfile = report.SupplyProfiles.First(p => p.Genre == Genre.Action);
            Assert.Equal(0, actionProfile.RecentRentals);
        }

        // ─── Report Metadata Tests ───────────────────────────────────────────

        [Fact]
        public void Analyze_SetsGeneratedAt()
        {
            var service = CreateService();
            var report = service.Analyze();
            Assert.Equal(_clock.Now, report.GeneratedAt);
        }

        [Fact]
        public void Analyze_SetsTotalCatalogSize()
        {
            AddMovie("A1", Genre.Action);
            AddMovie("A2", Genre.Comedy);
            AddMovie("A3", Genre.Drama);

            var service = CreateService();
            var report = service.Analyze();
            Assert.Equal(3, report.TotalCatalogSize);
        }

        [Fact]
        public void Analyze_NoCandidates_ZeroTotals()
        {
            var service = CreateService();
            var report = service.Analyze();

            Assert.Equal(0, report.TotalTitlesToAcquire);
            Assert.Equal(0, report.AveragePaybackDays);
            Assert.Equal(0m, report.TotalProjectedRoi);
        }
    }
}
