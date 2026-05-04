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
    public class EngagementDecayServiceTests
    {
        private InMemoryMovieRepository _movieRepo;
        private InMemoryRentalRepository _rentalRepo;
        private InMemoryCustomerRepository _customerRepo;
        private TestableDecayClock _clock;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new InMemoryMovieRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _customerRepo = new InMemoryCustomerRepository();
            _clock = new TestableDecayClock(new DateTime(2026, 5, 1, 12, 0, 0));
        }

        private EngagementDecayService CreateService()
        {
            return new EngagementDecayService(_rentalRepo, _customerRepo, _movieRepo, _clock);
        }

        private Customer AddCustomer(string name)
        {
            var c = new Customer { Name = name, Email = name.ToLower().Replace(" ", "") + "@test.com", MembershipType = MembershipType.Basic };
            _customerRepo.Add(c);
            return c;
        }

        private Movie AddMovie(string name, Genre genre)
        {
            var m = new Movie { Name = name, Genre = genre, ReleaseDate = new DateTime(2025, 1, 1) };
            _movieRepo.Add(m);
            return m;
        }

        private void AddRental(int movieId, int custId, DateTime rentalDate, decimal rate = 3.99m)
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

        // ================================================================
        //  Phase Classification Tests
        // ================================================================

        [TestMethod]
        public void Phase_RecentRental_IsActive()
        {
            var cust = AddCustomer("Alice");
            var movie = AddMovie("Action Hero", Genre.Action);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-5));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.AreEqual(EngagementPhase.Active, profile.CurrentPhase);
        }

        [TestMethod]
        public void Phase_14DaysAgo_IsActive()
        {
            var cust = AddCustomer("Bob");
            var movie = AddMovie("Fun Movie", Genre.Comedy);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-14));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.AreEqual(EngagementPhase.Active, profile.CurrentPhase);
        }

        [TestMethod]
        public void Phase_20DaysAgo_IsCooling()
        {
            var cust = AddCustomer("Carol");
            var movie = AddMovie("Drama Queen", Genre.Drama);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-20));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.AreEqual(EngagementPhase.Cooling, profile.CurrentPhase);
        }

        [TestMethod]
        public void Phase_45DaysAgo_IsDormant()
        {
            var cust = AddCustomer("Dave");
            var movie = AddMovie("Old Flick", Genre.Horror);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-45));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.AreEqual(EngagementPhase.Dormant, profile.CurrentPhase);
        }

        [TestMethod]
        public void Phase_75DaysAgo_IsAtRisk()
        {
            var cust = AddCustomer("Eve");
            var movie = AddMovie("Sci-Fi Epic", Genre.SciFi);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-75));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.AreEqual(EngagementPhase.AtRisk, profile.CurrentPhase);
        }

        [TestMethod]
        public void Phase_120DaysAgo_IsChurned()
        {
            var cust = AddCustomer("Frank");
            var movie = AddMovie("Ancient Film", Genre.Drama);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-120));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.AreEqual(EngagementPhase.Churned, profile.CurrentPhase);
        }

        // ================================================================
        //  Engagement Score Tests
        // ================================================================

        [TestMethod]
        public void Score_VeryRecent_IsHigh()
        {
            var cust = AddCustomer("Grace");
            var movie = AddMovie("New Hit", Genre.Action);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-1));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.IsTrue(profile.EngagementScore >= 80, "Score should be high for very recent rental");
        }

        [TestMethod]
        public void Score_LongInactive_IsLow()
        {
            var cust = AddCustomer("Hank");
            var movie = AddMovie("Forgotten Film", Genre.Drama);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-100));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.IsTrue(profile.EngagementScore < 20, "Score should be low for long-inactive customer");
        }

        [TestMethod]
        public void Score_FrequentRenter_GetsBonusPoints()
        {
            var cust = AddCustomer("Iris");
            var movie = AddMovie("Great Film", Genre.Comedy);
            // Add 4 rentals in last 30 days
            for (int i = 0; i < 4; i++)
                AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-7 * i));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            // Frequent renter should get bonus, score should be very high
            Assert.IsTrue(profile.EngagementScore >= 90, "Frequent renter should have high score");
        }

        [TestMethod]
        public void Score_NeverRented_IsZero()
        {
            var cust = AddCustomer("Jill");

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.AreEqual(EngagementPhase.Churned, profile.CurrentPhase);
            Assert.IsTrue(profile.EngagementScore < 5, "Never-rented customer should have near-zero score");
        }

        [TestMethod]
        public void Score_IsBetween0And100()
        {
            var cust = AddCustomer("Kelly");
            var movie = AddMovie("Test Movie", Genre.Action);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-30));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.IsTrue(profile.EngagementScore >= 0 && profile.EngagementScore <= 100);
        }

        // ================================================================
        //  Decay Rate Tests
        // ================================================================

        [TestMethod]
        public void DecayRate_FrequentRenter_HasHigherLambda()
        {
            var cust = AddCustomer("Leo");
            var movie = AddMovie("Popular", Genre.Action);
            // Rents every 5 days
            for (int i = 0; i < 6; i++)
                AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-5 * i));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            // Lambda for 5-day interval: ln(2)/5 ≈ 0.1386
            Assert.IsTrue(profile.DecayRate > 0.1, "Frequent renter should have high decay rate");
        }

        [TestMethod]
        public void DecayRate_InfrequentRenter_HasLowerLambda()
        {
            var cust = AddCustomer("Mia");
            var movie = AddMovie("Occasional", Genre.Drama);
            // Rents every 30 days
            for (int i = 0; i < 4; i++)
                AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-30 * i));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            // Lambda for 30-day interval: ln(2)/30 ≈ 0.0231
            Assert.IsTrue(profile.DecayRate < 0.05, "Infrequent renter should have low decay rate");
        }

        [TestMethod]
        public void DecayRate_SingleRental_UsesDefault()
        {
            var cust = AddCustomer("Nora");
            var movie = AddMovie("Single Watch", Genre.Horror);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-10));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.AreEqual(0.05, profile.DecayRate, "Single rental should use default lambda");
        }

        // ================================================================
        //  Average Interval Tests
        // ================================================================

        [TestMethod]
        public void AverageInterval_RegularRenter_ComputesCorrectly()
        {
            var cust = AddCustomer("Oscar");
            var movie = AddMovie("Regular", Genre.Comedy);
            // Rents every 10 days
            for (int i = 0; i < 5; i++)
                AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-10 * i));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.AreEqual(10.0, profile.AverageInterRentalDays, 0.1);
        }

        [TestMethod]
        public void AverageInterval_SingleRental_IsZero()
        {
            var cust = AddCustomer("Pat");
            var movie = AddMovie("One Time", Genre.Thriller);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-5));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.AreEqual(0.0, profile.AverageInterRentalDays);
        }

        // ================================================================
        //  Re-engagement Window Tests
        // ================================================================

        [TestMethod]
        public void Windows_CoolingCustomer_GeneratesWindow()
        {
            var cust = AddCustomer("Quinn");
            var movie = AddMovie("Window Test", Genre.Action);
            // Regular 10-day renter, now 20 days since last
            for (int i = 1; i <= 5; i++)
                AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-20 - (10 * i)));
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-20));

            var svc = CreateService();
            var windows = svc.GetReengagementWindows();

            Assert.IsTrue(windows.Any(w => w.CustomerId == cust.Id), "Should generate window for cooling customer");
        }

        [TestMethod]
        public void Windows_ActiveCustomer_NoWindow()
        {
            var cust = AddCustomer("Rosa");
            var movie = AddMovie("Active Test", Genre.Comedy);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-3));

            var svc = CreateService();
            var windows = svc.GetReengagementWindows();

            Assert.IsFalse(windows.Any(w => w.CustomerId == cust.Id), "Active customer should not have a window");
        }

        [TestMethod]
        public void Windows_ChurnedCustomer_NoWindow()
        {
            var cust = AddCustomer("Steve");
            var movie = AddMovie("Gone Movie", Genre.Horror);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-120));

            var svc = CreateService();
            var windows = svc.GetReengagementWindows();

            Assert.IsFalse(windows.Any(w => w.CustomerId == cust.Id), "Churned customer should not have a window");
        }

        [TestMethod]
        public void Windows_ConfidenceHigherForCooling()
        {
            var cust1 = AddCustomer("Tom");
            var cust2 = AddCustomer("Uma");
            var movie = AddMovie("Conf Test", Genre.Action);

            // Tom: cooling (20 days), regular renter
            for (int i = 0; i < 5; i++)
                AddRental(movie.Id, cust1.Id, _clock.Now.AddDays(-20 - (10 * i)));

            // Uma: dormant (45 days), regular renter
            for (int i = 0; i < 5; i++)
                AddRental(movie.Id, cust2.Id, _clock.Now.AddDays(-45 - (10 * i)));

            var svc = CreateService();
            var windows = svc.GetReengagementWindows();

            var tomWindow = windows.FirstOrDefault(w => w.CustomerId == cust1.Id);
            var umaWindow = windows.FirstOrDefault(w => w.CustomerId == cust2.Id);

            Assert.IsNotNull(tomWindow);
            Assert.IsNotNull(umaWindow);
            Assert.IsTrue(tomWindow.Confidence > umaWindow.Confidence, "Cooling customer should have higher confidence");
        }

        // ================================================================
        //  Intervention Tests
        // ================================================================

        [TestMethod]
        public void Interventions_CoolingCustomer_GetsGenreReminder()
        {
            var cust = AddCustomer("Vera");
            var movie = AddMovie("Action Blast", Genre.Action);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-20));

            var svc = CreateService();
            var interventions = svc.GetInterventions();

            var intervention = interventions.FirstOrDefault(i => i.CustomerId == cust.Id);
            Assert.IsNotNull(intervention);
            Assert.AreEqual(InterventionType.GenreReminder, intervention.Type);
        }

        [TestMethod]
        public void Interventions_DormantCustomer_GetsPersonalizedPick()
        {
            var cust = AddCustomer("Walt");
            var movie = AddMovie("Dormant Film", Genre.Drama);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-45));

            var svc = CreateService();
            var interventions = svc.GetInterventions();

            var intervention = interventions.FirstOrDefault(i => i.CustomerId == cust.Id);
            Assert.IsNotNull(intervention);
            Assert.AreEqual(InterventionType.PersonalizedPick, intervention.Type);
        }

        [TestMethod]
        public void Interventions_AtRiskCustomer_GetsLoyaltyBonus()
        {
            var cust = AddCustomer("Xena");
            var movie = AddMovie("Risky Film", Genre.Thriller);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-75));

            var svc = CreateService();
            var interventions = svc.GetInterventions();

            var intervention = interventions.FirstOrDefault(i => i.CustomerId == cust.Id);
            Assert.IsNotNull(intervention);
            Assert.AreEqual(InterventionType.LoyaltyBonus, intervention.Type);
        }

        [TestMethod]
        public void Interventions_ChurnedCustomer_GetsWinBackOffer()
        {
            var cust = AddCustomer("Yuki");
            var movie = AddMovie("Lost Film", Genre.SciFi);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-120));

            var svc = CreateService();
            var interventions = svc.GetInterventions();

            var intervention = interventions.FirstOrDefault(i => i.CustomerId == cust.Id);
            Assert.IsNotNull(intervention);
            Assert.AreEqual(InterventionType.WinBackOffer, intervention.Type);
        }

        [TestMethod]
        public void Interventions_ActiveCustomer_NoIntervention()
        {
            var cust = AddCustomer("Zara");
            var movie = AddMovie("Recent Film", Genre.Comedy);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-3));

            var svc = CreateService();
            var interventions = svc.GetInterventions();

            Assert.IsFalse(interventions.Any(i => i.CustomerId == cust.Id), "Active customer should not get intervention");
        }

        [TestMethod]
        public void Interventions_SortedByPriorityDescending()
        {
            var movie = AddMovie("Test", Genre.Action);
            var cooling = AddCustomer("Cool Guy");
            var atrisk = AddCustomer("Risk Lady");
            var churned = AddCustomer("Gone Man");

            AddRental(movie.Id, cooling.Id, _clock.Now.AddDays(-20));
            AddRental(movie.Id, atrisk.Id, _clock.Now.AddDays(-75));
            AddRental(movie.Id, churned.Id, _clock.Now.AddDays(-120));

            var svc = CreateService();
            var interventions = svc.GetInterventions();

            Assert.IsTrue(interventions.Count >= 3);
            for (int i = 1; i < interventions.Count; i++)
                Assert.IsTrue(interventions[i - 1].Priority >= interventions[i].Priority);
        }

        // ================================================================
        //  Fleet Health Tests
        // ================================================================

        [TestMethod]
        public void FleetHealth_AllActive_HighScore()
        {
            var movie = AddMovie("Hit Film", Genre.Action);
            for (int i = 0; i < 5; i++)
            {
                var c = AddCustomer("Active" + i);
                AddRental(movie.Id, c.Id, _clock.Now.AddDays(-3));
            }

            var svc = CreateService();
            var health = svc.GetFleetHealth();

            Assert.AreEqual(100.0, health.OverallHealthScore);
            Assert.AreEqual("Thriving", health.HealthTier);
            Assert.AreEqual(5, health.ActiveCount);
            Assert.AreEqual(0, health.ChurnedCount);
        }

        [TestMethod]
        public void FleetHealth_AllChurned_LowScore()
        {
            var movie = AddMovie("Old Film", Genre.Drama);
            for (int i = 0; i < 5; i++)
            {
                var c = AddCustomer("Churned" + i);
                AddRental(movie.Id, c.Id, _clock.Now.AddDays(-120));
            }

            var svc = CreateService();
            var health = svc.GetFleetHealth();

            Assert.AreEqual(0.0, health.OverallHealthScore);
            Assert.AreEqual("Emergency", health.HealthTier);
            Assert.AreEqual(0, health.ActiveCount);
            Assert.AreEqual(5, health.ChurnedCount);
        }

        [TestMethod]
        public void FleetHealth_Mixed_CorrectCounts()
        {
            var movie = AddMovie("Mix Film", Genre.Comedy);
            var active = AddCustomer("Active1");
            var cooling = AddCustomer("Cooling1");
            var dormant = AddCustomer("Dormant1");
            var atrisk = AddCustomer("AtRisk1");

            AddRental(movie.Id, active.Id, _clock.Now.AddDays(-5));
            AddRental(movie.Id, cooling.Id, _clock.Now.AddDays(-20));
            AddRental(movie.Id, dormant.Id, _clock.Now.AddDays(-45));
            AddRental(movie.Id, atrisk.Id, _clock.Now.AddDays(-75));

            var svc = CreateService();
            var health = svc.GetFleetHealth();

            Assert.AreEqual(4, health.TotalCustomers);
            Assert.AreEqual(1, health.ActiveCount);
            Assert.AreEqual(1, health.CoolingCount);
            Assert.AreEqual(1, health.DormantCount);
            Assert.AreEqual(1, health.AtRiskCount);
            Assert.AreEqual(0, health.ChurnedCount);
        }

        [TestMethod]
        public void FleetHealth_NoCustomers_ReturnsEmergency()
        {
            var svc = CreateService();
            var health = svc.GetFleetHealth();

            Assert.AreEqual(0, health.TotalCustomers);
            Assert.AreEqual("Emergency", health.HealthTier);
        }

        [TestMethod]
        public void FleetHealth_ChurnRate_Calculated()
        {
            var movie = AddMovie("Rate Film", Genre.Horror);
            var active = AddCustomer("A");
            var churned = AddCustomer("C");

            AddRental(movie.Id, active.Id, _clock.Now.AddDays(-5));
            AddRental(movie.Id, churned.Id, _clock.Now.AddDays(-120));

            var svc = CreateService();
            var health = svc.GetFleetHealth();

            Assert.AreEqual(50.0, health.ChurnRate);
        }

        // ================================================================
        //  Insight Tests
        // ================================================================

        [TestMethod]
        public void Insights_NonEmpty_WhenCustomersExist()
        {
            var movie = AddMovie("Insight Film", Genre.Action);
            var cust = AddCustomer("InsightGuy");
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-10));

            var svc = CreateService();
            var report = svc.GenerateReport();

            Assert.IsTrue(report.Insights.Count >= 2, "Should generate multiple insights");
        }

        [TestMethod]
        public void Insights_MentionsChurnedCustomers()
        {
            var movie = AddMovie("Churn Film", Genre.Drama);
            var cust = AddCustomer("ChurnedDude");
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-120));

            var svc = CreateService();
            var report = svc.GenerateReport();

            Assert.IsTrue(report.Insights.Any(i => i.Contains("churned")),
                "Insights should mention churned customers");
        }

        [TestMethod]
        public void Insights_Empty_WhenNoCustomers()
        {
            var svc = CreateService();
            var report = svc.GenerateReport();

            Assert.IsTrue(report.Insights.Count >= 1);
            Assert.IsTrue(report.Insights.Any(i => i.Contains("No customers")));
        }

        // ================================================================
        //  Preferred Genre Tests
        // ================================================================

        [TestMethod]
        public void PreferredGenre_DetectsCorrectly()
        {
            var cust = AddCustomer("GenreFan");
            var action1 = AddMovie("Action1", Genre.Action);
            var action2 = AddMovie("Action2", Genre.Action);
            var comedy = AddMovie("Comedy1", Genre.Comedy);

            AddRental(action1.Id, cust.Id, _clock.Now.AddDays(-5));
            AddRental(action2.Id, cust.Id, _clock.Now.AddDays(-10));
            AddRental(comedy.Id, cust.Id, _clock.Now.AddDays(-15));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.AreEqual("Action", profile.PreferredGenre);
        }

        // ================================================================
        //  Phase Transition Warning Tests
        // ================================================================

        [TestMethod]
        public void PhaseWarning_NearActiveThreshold_ShowsWarning()
        {
            var cust = AddCustomer("NearCool");
            var movie = AddMovie("Warn Film", Genre.Action);
            // 12 days ago = 2 days from Cooling threshold (14)
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-12));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.IsNotNull(profile.PhaseTransitionWarning);
            Assert.IsTrue(profile.PhaseTransitionWarning.Contains("Cooling"));
        }

        [TestMethod]
        public void PhaseWarning_FarFromThreshold_NoWarning()
        {
            var cust = AddCustomer("SafeGuy");
            var movie = AddMovie("Safe Film", Genre.Comedy);
            // 3 days ago = 11 days from Cooling threshold
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-3));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.IsNull(profile.PhaseTransitionWarning);
        }

        // ================================================================
        //  Predicted Next Rental Tests
        // ================================================================

        [TestMethod]
        public void PredictedNextRental_RegularRenter_Calculated()
        {
            var cust = AddCustomer("Predictor");
            var movie = AddMovie("Predict Film", Genre.Action);
            // Rents every 10 days, last rental 5 days ago
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-5));
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-15));
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-25));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.IsNotNull(profile.PredictedNextRentalDate);
            // Last was 5 days ago, avg interval 10, so predicted = 5 days from now
            var expected = _clock.Now.AddDays(-5).AddDays(10);
            Assert.AreEqual(expected.Date, profile.PredictedNextRentalDate.Value.Date);
        }

        // ================================================================
        //  Report Generation Tests
        // ================================================================

        [TestMethod]
        public void Report_ContainsAllSections()
        {
            var movie = AddMovie("Report Film", Genre.Action);
            var cust = AddCustomer("ReportGuy");
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-20));

            var svc = CreateService();
            var report = svc.GenerateReport();

            Assert.IsNotNull(report.FleetHealth);
            Assert.IsNotNull(report.Profiles);
            Assert.IsNotNull(report.Windows);
            Assert.IsNotNull(report.Interventions);
            Assert.IsNotNull(report.TrendHistory);
            Assert.IsNotNull(report.Insights);
            Assert.IsTrue(report.Profiles.Count > 0);
        }

        [TestMethod]
        public void Report_TrendHistory_HasEntries()
        {
            var movie = AddMovie("Trend Film", Genre.Comedy);
            var cust = AddCustomer("TrendGuy");
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-10));

            var svc = CreateService();
            var report = svc.GenerateReport();

            Assert.IsTrue(report.TrendHistory.Count >= 4, "Should have multi-week trend data");
        }

        [TestMethod]
        public void Report_GeneratedAt_IsCurrentTime()
        {
            var svc = CreateService();
            var report = svc.GenerateReport();

            Assert.AreEqual(_clock.Now, report.GeneratedAt);
        }

        // ================================================================
        //  Edge Cases
        // ================================================================

        [TestMethod]
        public void GetProfile_NonexistentCustomer_ReturnsNull()
        {
            var svc = CreateService();
            var profile = svc.GetProfile(9999);

            Assert.IsNull(profile);
        }

        [TestMethod]
        public void DaysSinceLastRental_NoRentals_IsHigh()
        {
            var cust = AddCustomer("NoRentals");

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.IsTrue(profile.DaysSinceLastRental >= 90);
            Assert.AreEqual(0, profile.TotalRentals);
        }

        [TestMethod]
        public void PredictedDaysToChurn_AlreadyChurned_IsZero()
        {
            var cust = AddCustomer("AlreadyGone");
            var movie = AddMovie("Old Old", Genre.Drama);
            AddRental(movie.Id, cust.Id, _clock.Now.AddDays(-150));

            var svc = CreateService();
            var profile = svc.GetProfile(cust.Id);

            Assert.AreEqual(0.0, profile.PredictedDaysToChurn);
        }

        [TestMethod]
        public void Constructor_NullRentalRepo_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new EngagementDecayService(null, _customerRepo, _movieRepo, _clock));
        }

        [TestMethod]
        public void Constructor_NullCustomerRepo_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new EngagementDecayService(_rentalRepo, null, _movieRepo, _clock));
        }

        [TestMethod]
        public void Constructor_NullMovieRepo_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new EngagementDecayService(_rentalRepo, _customerRepo, null, _clock));
        }

        [TestMethod]
        public void Constructor_NullClock_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new EngagementDecayService(_rentalRepo, _customerRepo, _movieRepo, null));
        }

        // ================================================================
        //  Test Clock
        // ================================================================

        private class TestableDecayClock : IClock
        {
            private DateTime _now;
            public TestableDecayClock(DateTime now) { _now = now; }
            public DateTime Now => _now;
            public DateTime Today => _now.Date;
            public void Advance(TimeSpan span) { _now = _now.Add(span); }
        }
    }
}
