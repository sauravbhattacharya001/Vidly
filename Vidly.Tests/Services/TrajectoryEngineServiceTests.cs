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
    public class TrajectoryEngineServiceTests
    {
        private InMemoryRentalRepository _rentalRepo;
        private InMemoryMovieRepository _movieRepo;
        private InMemoryCustomerRepository _customerRepo;
        private TestClock _clock;
        private TrajectoryEngineService _service;

        [TestInitialize]
        public void Setup()
        {
            _rentalRepo = new InMemoryRentalRepository();
            _movieRepo = new InMemoryMovieRepository();
            _customerRepo = new InMemoryCustomerRepository();
            _clock = new TestClock(new DateTime(2026, 5, 1));
            _service = new TrajectoryEngineService(_rentalRepo, _movieRepo, _customerRepo, _clock);
        }

        // ── Constructor guard tests ─────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullRentalRepo_Throws()
        {
            new TrajectoryEngineService(null, _movieRepo, _customerRepo, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullMovieRepo_Throws()
        {
            new TrajectoryEngineService(_rentalRepo, null, _customerRepo, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullCustomerRepo_Throws()
        {
            new TrajectoryEngineService(_rentalRepo, _movieRepo, null, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullClock_Throws()
        {
            new TrajectoryEngineService(_rentalRepo, _movieRepo, _customerRepo, null);
        }

        // ── Empty data ──────────────────────────────────────────────

        [TestMethod]
        public void GenerateReport_EmptyData_ReturnsEmptyReport()
        {
            var report = _service.GenerateReport();

            Assert.IsNotNull(report);
            Assert.AreEqual(0, report.Trajectories.Count);
            Assert.AreEqual(0, report.FleetHealth.TotalCustomers);
            Assert.AreEqual(0, report.TrajectoryScore);
        }

        [TestMethod]
        public void GenerateReport_EmptyData_HasNoDataInsight()
        {
            var report = _service.GenerateReport();
            Assert.IsTrue(report.Insights.Any(i => i.Contains("No customer data")));
        }

        // ── Single customer with no rentals ─────────────────────────

        [TestMethod]
        public void GenerateReport_CustomerNoRentals_DiscoveryPhase()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Alice" });

            var report = _service.GenerateReport();
            Assert.AreEqual(1, report.Trajectories.Count);
            Assert.AreEqual(LifecyclePhase.Discovery, report.Trajectories[0].Lifecycle.Phase);
        }

        [TestMethod]
        public void GenerateReport_CustomerNoRentals_ChurnWarning()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Alice" });

            var report = _service.GenerateReport();
            Assert.AreEqual(ChurnRiskTier.Warning, report.Trajectories[0].ChurnRisk.Tier);
        }

        [TestMethod]
        public void GenerateReport_CustomerNoRentals_ZeroLTV()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Alice" });

            var report = _service.GenerateReport();
            Assert.AreEqual(0m, report.Trajectories[0].LTV.HistoricalLTV);
            Assert.AreEqual(0m, report.Trajectories[0].LTV.ProjectedRevenue90Days);
        }

        // ── Single rental customer ──────────────────────────────────

        [TestMethod]
        public void GetCustomerTrajectory_SingleRental_DiscoveryPhase()
        {
            SeedCustomerWithRentals(1, "Bob", new[] { -10 }); // 10 days ago

            var t = _service.GetCustomerTrajectory(1);
            Assert.AreEqual(LifecyclePhase.Discovery, t.Lifecycle.Phase);
        }

        [TestMethod]
        public void GetCustomerTrajectory_SingleRental_StalledVelocity()
        {
            SeedCustomerWithRentals(1, "Bob", new[] { -10 });

            var t = _service.GetCustomerTrajectory(1);
            Assert.AreEqual("Stalled", t.Velocity.Trend);
            Assert.IsNull(t.Velocity.PredictedNextRental);
        }

        // ── Velocity trend tests ────────────────────────────────────

        [TestMethod]
        public void Velocity_AcceleratingPattern_DetectsAccelerating()
        {
            // Rentals getting closer together: 30, 25, 20, 15, 10, 5 days ago
            SeedCustomerWithRentals(1, "Carol",
                new[] { -90, -60, -40, -25, -15, -5 });

            var t = _service.GetCustomerTrajectory(1);
            Assert.AreEqual("Accelerating", t.Velocity.Trend);
        }

        [TestMethod]
        public void Velocity_DeceleratingPattern_DetectsDecelerating()
        {
            // Rentals getting farther apart: close early, spread out later
            SeedCustomerWithRentals(1, "Dave",
                new[] { -200, -195, -190, -185, -150, -100, -40, -5 });

            var t = _service.GetCustomerTrajectory(1);
            Assert.AreEqual("Decelerating", t.Velocity.Trend);
        }

        [TestMethod]
        public void Velocity_SteadyPattern_DetectsSteady()
        {
            // Even intervals: every 14 days
            SeedCustomerWithRentals(1, "Eve",
                new[] { -70, -56, -42, -28, -14 });

            var t = _service.GetCustomerTrajectory(1);
            Assert.AreEqual("Steady", t.Velocity.Trend);
        }

        [TestMethod]
        public void Velocity_PredictedNextRental_HasValue()
        {
            SeedCustomerWithRentals(1, "Frank",
                new[] { -60, -45, -30, -15 });

            var t = _service.GetCustomerTrajectory(1);
            Assert.IsNotNull(t.Velocity.PredictedNextRental);
        }

        [TestMethod]
        public void Velocity_Confidence_HighForConsistentIntervals()
        {
            SeedCustomerWithRentals(1, "Gina",
                new[] { -60, -45, -30, -15 });

            var t = _service.GetCustomerTrajectory(1);
            Assert.IsTrue(t.Velocity.ConfidencePercent > 50,
                "Confidence should be > 50 for consistent intervals, got " + t.Velocity.ConfidencePercent);
        }

        // ── Genre evolution tests ───────────────────────────────────

        [TestMethod]
        public void GenreEvolution_SingleGenre_LoyalPattern()
        {
            var m1 = new Movie { Id = 1, Name = "M1", Genre = Genre.Action };
            var m2 = new Movie { Id = 2, Name = "M2", Genre = Genre.Action };
            _movieRepo.Add(m1);
            _movieRepo.Add(m2);
            _customerRepo.Add(new Customer { Id = 1, Name = "Loyal" });
            AddRental(1, 1, -30);
            AddRental(1, 2, -15);

            var t = _service.GetCustomerTrajectory(1);
            Assert.AreEqual("Loyal", t.GenreEvolution.Pattern);
            Assert.IsTrue(t.GenreEvolution.CurrentPreferences.ContainsKey("Action"));
        }

        [TestMethod]
        public void GenreEvolution_ManyGenres_ExplorerPattern()
        {
            _movieRepo.Add(new Movie { Id = 1, Name = "M1", Genre = Genre.Action });
            _movieRepo.Add(new Movie { Id = 2, Name = "M2", Genre = Genre.Comedy });
            _movieRepo.Add(new Movie { Id = 3, Name = "M3", Genre = Genre.Drama });
            _movieRepo.Add(new Movie { Id = 4, Name = "M4", Genre = Genre.Horror });
            _movieRepo.Add(new Movie { Id = 5, Name = "M5", Genre = Genre.SciFi });
            _movieRepo.Add(new Movie { Id = 6, Name = "M6", Genre = Genre.Romance });
            _customerRepo.Add(new Customer { Id = 1, Name = "Explorer" });
            AddRental(1, 1, -60);
            AddRental(1, 2, -50);
            AddRental(1, 3, -40);
            AddRental(1, 4, -30);
            AddRental(1, 5, -20);
            AddRental(1, 6, -10);

            var t = _service.GetCustomerTrajectory(1);
            Assert.AreEqual("Explorer", t.GenreEvolution.Pattern);
        }

        [TestMethod]
        public void GenreEvolution_ShiftingPreferences_DetectsEmergingGenres()
        {
            _movieRepo.Add(new Movie { Id = 1, Name = "M1", Genre = Genre.Action });
            _movieRepo.Add(new Movie { Id = 2, Name = "M2", Genre = Genre.Action });
            _movieRepo.Add(new Movie { Id = 3, Name = "M3", Genre = Genre.SciFi });
            _movieRepo.Add(new Movie { Id = 4, Name = "M4", Genre = Genre.SciFi });
            _movieRepo.Add(new Movie { Id = 5, Name = "M5", Genre = Genre.SciFi });
            _movieRepo.Add(new Movie { Id = 6, Name = "M6", Genre = Genre.SciFi });
            _customerRepo.Add(new Customer { Id = 1, Name = "Shifter" });
            // Early: all Action; Late: all SciFi
            AddRental(1, 1, -60);
            AddRental(1, 2, -50);
            AddRental(1, 3, -30);
            AddRental(1, 4, -20);
            AddRental(1, 5, -10);
            AddRental(1, 6, -5);

            var t = _service.GetCustomerTrajectory(1);
            Assert.IsTrue(t.GenreEvolution.EmergingGenres.Contains("SciFi"),
                "SciFi should be emerging");
        }

        // ── Spending trajectory tests ───────────────────────────────

        [TestMethod]
        public void Spending_RisingSpend_DetectsRising()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "BigSpender" });
            // Increasing cost rentals across months
            AddRentalWithCost(1, -90, 5m);
            AddRentalWithCost(1, -60, 10m);
            AddRentalWithCost(1, -30, 20m);

            var t = _service.GetCustomerTrajectory(1);
            Assert.AreEqual("Rising", t.Spending.Trend);
            Assert.IsTrue(t.Spending.SpendVelocity > 0);
        }

        [TestMethod]
        public void Spending_DecliningSpend_DetectsDeclining()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Saver" });
            AddRentalWithCost(1, -90, 30m);
            AddRentalWithCost(1, -60, 15m);
            AddRentalWithCost(1, -30, 5m);

            var t = _service.GetCustomerTrajectory(1);
            Assert.AreEqual("Declining", t.Spending.Trend);
            Assert.IsTrue(t.Spending.SpendVelocity < 0);
        }

        [TestMethod]
        public void Spending_StableSpend_DetectsStable()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Steady" });
            AddRentalWithCost(1, -90, 10m);
            AddRentalWithCost(1, -60, 10m);
            AddRentalWithCost(1, -30, 10m);

            var t = _service.GetCustomerTrajectory(1);
            Assert.AreEqual("Stable", t.Spending.Trend);
        }

        // ── Lifecycle phase tests ───────────────────────────────────

        [TestMethod]
        public void Lifecycle_FewRentals_Discovery()
        {
            SeedCustomerWithRentals(1, "New", new[] { -10, -5 });
            var t = _service.GetCustomerTrajectory(1);
            Assert.AreEqual(LifecyclePhase.Discovery, t.Lifecycle.Phase);
        }

        [TestMethod]
        public void Lifecycle_DormantCustomer_Dormant()
        {
            // Last rental 80 days ago
            SeedCustomerWithRentals(1, "Dormant", new[] { -200, -180, -160, -80 });
            var t = _service.GetCustomerTrajectory(1);
            Assert.AreEqual(LifecyclePhase.Dormant, t.Lifecycle.Phase);
        }

        [TestMethod]
        public void Lifecycle_ChurnedCustomer_Churned()
        {
            // Last rental 150 days ago
            SeedCustomerWithRentals(1, "Gone", new[] { -300, -250, -200, -150 });
            var t = _service.GetCustomerTrajectory(1);
            Assert.AreEqual(LifecyclePhase.Churned, t.Lifecycle.Phase);
        }

        [TestMethod]
        public void Lifecycle_PredictedNextPhase_HasValue()
        {
            SeedCustomerWithRentals(1, "Active", new[] { -40, -30, -20, -10 });
            var t = _service.GetCustomerTrajectory(1);
            Assert.IsNotNull(t.Lifecycle.PredictedNextPhase,
                "Active customer should have predicted next phase");
        }

        // ── Churn risk tests ────────────────────────────────────────

        [TestMethod]
        public void ChurnRisk_RecentActiveCustomer_LowRisk()
        {
            SeedCustomerWithRentals(1, "Happy",
                new[] { -50, -40, -30, -20, -10, -3 });
            var t = _service.GetCustomerTrajectory(1);
            Assert.IsTrue(t.ChurnRisk.RiskScore < 40,
                "Recent active customer should have low risk, got " + t.ChurnRisk.RiskScore);
        }

        [TestMethod]
        public void ChurnRisk_LongInactiveCustomer_HighRisk()
        {
            SeedCustomerWithRentals(1, "Gone",
                new[] { -300, -250, -200, -150 });
            var t = _service.GetCustomerTrajectory(1);
            Assert.IsTrue(t.ChurnRisk.RiskScore >= 60,
                "Long inactive customer should have high risk, got " + t.ChurnRisk.RiskScore);
        }

        [TestMethod]
        public void ChurnRisk_HighLateFees_IncreasesRisk()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "LateGuy" });
            _movieRepo.Add(new Movie { Id = 1, Name = "M1", Genre = Genre.Action });
            // All rentals with high late fees
            for (int i = 0; i < 6; i++)
            {
                _rentalRepo.Add(new Rental
                {
                    CustomerId = 1, MovieId = 1,
                    RentalDate = _clock.Now.AddDays(-60 + i * 10),
                    DueDate = _clock.Now.AddDays(-55 + i * 10),
                    DailyRate = 3m,
                    LateFee = 5m,
                    Status = RentalStatus.Returned,
                    ReturnDate = _clock.Now.AddDays(-50 + i * 10)
                });
            }

            var t = _service.GetCustomerTrajectory(1);
            Assert.IsTrue(t.ChurnRisk.RiskFactors.Any(f => f.Contains("late fee")),
                "Should detect high late fee rate");
        }

        [TestMethod]
        public void ChurnRisk_Tiers_CorrectClassification()
        {
            // Safe customer (very active, recent)
            SeedCustomerWithRentals(1, "Safe",
                new[] { -28, -21, -14, -7, -2 });
            var safe = _service.GetCustomerTrajectory(1);
            Assert.AreEqual(ChurnRiskTier.Safe, safe.ChurnRisk.Tier);
        }

        // ── LTV projection tests ────────────────────────────────────

        [TestMethod]
        public void LTV_HistoricalLTV_SumsAllRentals()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Valued" });
            AddRentalWithCost(1, -30, 10m);
            AddRentalWithCost(1, -15, 15m);

            var t = _service.GetCustomerTrajectory(1);
            Assert.IsTrue(t.LTV.HistoricalLTV > 0,
                "Historical LTV should be positive");
        }

        [TestMethod]
        public void LTV_LowChurnRisk_HigherProjection()
        {
            // Active customer
            _customerRepo.Add(new Customer { Id = 1, Name = "Active" });
            AddRentalWithCost(1, -90, 10m);
            AddRentalWithCost(1, -60, 10m);
            AddRentalWithCost(1, -30, 10m);
            AddRentalWithCost(1, -5, 10m);

            var t = _service.GetCustomerTrajectory(1);
            Assert.IsTrue(t.LTV.ProjectedRevenue90Days > 0,
                "Active customer should have positive 90-day projection");
        }

        [TestMethod]
        public void LTV_HighChurnRisk_LowerProjection()
        {
            // Churned customer
            SeedCustomerWithRentals(1, "Churned", new[] { -300, -250 });
            var t = _service.GetCustomerTrajectory(1);
            // Churned customer should have very low projected value
            Assert.IsTrue(t.LTV.ProjectedRevenue90Days < 5,
                "Churned customer should have near-zero projection, got " + t.LTV.ProjectedRevenue90Days);
        }

        [TestMethod]
        public void LTV_ProjectionPeriods_Increasing()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Normal" });
            AddRentalWithCost(1, -90, 10m);
            AddRentalWithCost(1, -60, 10m);
            AddRentalWithCost(1, -30, 10m);
            AddRentalWithCost(1, -5, 10m);

            var t = _service.GetCustomerTrajectory(1);
            // 60 >= 30, 90 >= 60, 180 >= 90
            Assert.IsTrue(t.LTV.ProjectedRevenue60Days >= t.LTV.ProjectedRevenue30Days);
            Assert.IsTrue(t.LTV.ProjectedRevenue90Days >= t.LTV.ProjectedRevenue60Days);
            Assert.IsTrue(t.LTV.ProjectedRevenue180Days >= t.LTV.ProjectedRevenue90Days);
        }

        // ── Fleet health tests ──────────────────────────────────────

        [TestMethod]
        public void FleetHealth_MixedCustomers_ComputesScore()
        {
            // Active customer
            SeedCustomerWithRentals(1, "Active",
                new[] { -50, -40, -30, -20, -10, -3 });
            // Churned customer
            SeedCustomerWithRentals(2, "Churned",
                new[] { -300, -250, -200, -150 });

            var report = _service.GenerateReport();
            Assert.AreEqual(2, report.FleetHealth.TotalCustomers);
            Assert.IsTrue(report.FleetHealth.HealthScore > 0);
            Assert.IsTrue(report.FleetHealth.HealthScore < 100);
        }

        [TestMethod]
        public void FleetHealth_PhaseDistribution_PopulatedCorrectly()
        {
            SeedCustomerWithRentals(1, "Active", new[] { -30, -20, -10 });
            SeedCustomerWithRentals(2, "Gone", new[] { -300, -250, -200, -150 });

            var report = _service.GenerateReport();
            Assert.IsTrue(report.FleetHealth.PhaseDistribution.Count > 0);
        }

        [TestMethod]
        public void FleetHealth_ChurnDistribution_PopulatedCorrectly()
        {
            SeedCustomerWithRentals(1, "Active", new[] { -30, -20, -10 });
            SeedCustomerWithRentals(2, "Gone", new[] { -300, -250, -200, -150 });

            var report = _service.GenerateReport();
            Assert.IsTrue(report.FleetHealth.ChurnRiskDistribution.Count > 0);
        }

        // ── Insight generation tests ────────────────────────────────

        [TestMethod]
        public void Insights_StalledCustomer_GeneratesStallInsight()
        {
            SeedCustomerWithRentals(1, "Staller", new[] { -200, -190, -180 });
            // 180 days since last rental → Stalled
            var t = _service.GetCustomerTrajectory(1);
            Assert.IsTrue(t.Insights.Any(i => i.Contains("stalled") || i.Contains("churn")),
                "Should generate insight about stalled/churn pattern");
        }

        [TestMethod]
        public void Insights_AcceleratingCustomer_GeneratesAcceleratingInsight()
        {
            SeedCustomerWithRentals(1, "Speedster",
                new[] { -90, -60, -40, -25, -15, -5 });
            var t = _service.GetCustomerTrajectory(1);
            Assert.IsTrue(t.Insights.Any(i => i.Contains("accelerating")),
                "Should generate accelerating insight");
        }

        [TestMethod]
        public void Insights_FleetReport_HasFleetSummary()
        {
            SeedCustomerWithRentals(1, "Alice", new[] { -30, -15 });
            SeedCustomerWithRentals(2, "Bob", new[] { -60, -30 });

            var report = _service.GenerateReport();
            Assert.IsTrue(report.Insights.Any(i => i.Contains("Fleet of 2")),
                "Should include fleet count in insights");
        }

        // ── GetCustomerTrajectory edge cases ────────────────────────

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void GetCustomerTrajectory_InvalidId_Throws()
        {
            _service.GetCustomerTrajectory(999);
        }

        [TestMethod]
        public void GetCustomerTrajectory_ValidCustomer_ReturnsTrajectory()
        {
            SeedCustomerWithRentals(1, "Valid", new[] { -10 });
            var t = _service.GetCustomerTrajectory(1);
            Assert.IsNotNull(t);
            Assert.AreEqual(1, t.CustomerId);
            Assert.AreEqual("Valid", t.CustomerName);
        }

        // ── Helper methods ──────────────────────────────────────────

        private void SeedCustomerWithRentals(int customerId, string name, int[] daysAgoArr)
        {
            if (!_customerRepo.GetAll().Any(c => c.Id == customerId))
                _customerRepo.Add(new Customer { Id = customerId, Name = name });
            if (!_movieRepo.GetAll().Any(m => m.Id == 100))
                _movieRepo.Add(new Movie { Id = 100, Name = "GenericMovie", Genre = Genre.Action });

            foreach (var d in daysAgoArr)
            {
                _rentalRepo.Add(new Rental
                {
                    CustomerId = customerId,
                    MovieId = 100,
                    RentalDate = _clock.Now.AddDays(d),
                    DueDate = _clock.Now.AddDays(d + 7),
                    DailyRate = 3.00m,
                    Status = RentalStatus.Returned,
                    ReturnDate = _clock.Now.AddDays(d + 5)
                });
            }
        }

        private void AddRental(int customerId, int movieId, int daysAgo)
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = _clock.Now.AddDays(daysAgo),
                DueDate = _clock.Now.AddDays(daysAgo + 7),
                DailyRate = 3.00m,
                Status = RentalStatus.Returned,
                ReturnDate = _clock.Now.AddDays(daysAgo + 5)
            });
        }

        private void AddRentalWithCost(int customerId, int daysAgo, decimal dailyRate)
        {
            if (!_movieRepo.GetAll().Any(m => m.Id == 100))
                _movieRepo.Add(new Movie { Id = 100, Name = "GenericMovie", Genre = Genre.Action });

            _rentalRepo.Add(new Rental
            {
                CustomerId = customerId,
                MovieId = 100,
                RentalDate = _clock.Now.AddDays(daysAgo),
                DueDate = _clock.Now.AddDays(daysAgo + 7),
                DailyRate = dailyRate,
                Status = RentalStatus.Returned,
                ReturnDate = _clock.Now.AddDays(daysAgo + 5)
            });
        }
    }
}
