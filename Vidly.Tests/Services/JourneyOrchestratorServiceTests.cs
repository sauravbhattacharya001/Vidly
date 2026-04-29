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
    public class JourneyOrchestratorServiceTests
    {
        private InMemoryTestMovieRepo _movies;
        private InMemoryTestRentalRepo _rentals;
        private InMemoryTestCustomerRepo _customers;
        private FakeClock _clock;
        private JourneyOrchestratorService _service;

        [TestInitialize]
        public void SetUp()
        {
            _movies = new InMemoryTestMovieRepo();
            _rentals = new InMemoryTestRentalRepo();
            _customers = new InMemoryTestCustomerRepo();
            _clock = new FakeClock(new DateTime(2026, 4, 28));
            _service = new JourneyOrchestratorService(_customers, _rentals, _movies, _clock);

            // 12 movies across all genres
            _movies.Add(new Movie { Id = 1, Name = "Action Hero", Genre = Genre.Action, Rating = 5 });
            _movies.Add(new Movie { Id = 2, Name = "Laugh Out Loud", Genre = Genre.Comedy, Rating = 4 });
            _movies.Add(new Movie { Id = 3, Name = "Tears of Joy", Genre = Genre.Drama, Rating = 4 });
            _movies.Add(new Movie { Id = 4, Name = "Scream Night", Genre = Genre.Horror, Rating = 3 });
            _movies.Add(new Movie { Id = 5, Name = "Stars Beyond", Genre = Genre.SciFi, Rating = 5 });
            _movies.Add(new Movie { Id = 6, Name = "Toon World", Genre = Genre.Animation, Rating = 4 });
            _movies.Add(new Movie { Id = 7, Name = "Edge Runner", Genre = Genre.Thriller, Rating = 3 });
            _movies.Add(new Movie { Id = 8, Name = "Love Story", Genre = Genre.Romance, Rating = 4 });
            _movies.Add(new Movie { Id = 9, Name = "True Facts", Genre = Genre.Documentary, Rating = 5 });
            _movies.Add(new Movie { Id = 10, Name = "Quest Land", Genre = Genre.Adventure, Rating = 4 });
            _movies.Add(new Movie { Id = 11, Name = "Mystery Manor", Genre = Genre.Mystery, Rating = 3 });
            _movies.Add(new Movie { Id = 12, Name = "Dragon Realm", Genre = Genre.Fantasy, Rating = 5,
                ReleaseDate = new DateTime(2026, 4, 1) }); // new release
        }

        // ── Helpers ────────────────────────────────────────────────

        private Rental MakeRental(int id, int custId, int movieId, DateTime date,
            int days = 3, bool returned = true)
        {
            var due = date.AddDays(days);
            return new Rental
            {
                Id = id,
                CustomerId = custId,
                MovieId = movieId,
                RentalDate = date,
                DueDate = due,
                ReturnDate = returned ? due : (DateTime?)null,
                DailyRate = 2.99m,
                Status = returned ? RentalStatus.Returned : RentalStatus.Active
            };
        }

        private void AddRentals(int custId, int count, DateTime startDate, int intervalDays = 2)
        {
            for (int i = 0; i < count; i++)
            {
                var movieId = (i % 12) + 1;
                _rentals.Add(MakeRental(custId * 1000 + i, custId, movieId,
                    startDate.AddDays(i * intervalDays)));
            }
        }

        // ── Newcomer Tests ─────────────────────────────────────────

        [TestMethod]
        public void Classify_NewCustomerNoRentals_Newcomer()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-5)
            });

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.Newcomer, profile.CurrentStage);
        }

        [TestMethod]
        public void Classify_NewCustomerFewRentals_Newcomer()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-10)
            });
            _rentals.Add(MakeRental(1, 1, 1, _clock.Now.AddDays(-5)));
            _rentals.Add(MakeRental(2, 1, 2, _clock.Now.AddDays(-3)));

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.Newcomer, profile.CurrentStage);
            Assert.AreEqual(2, profile.TotalRentals);
        }

        [TestMethod]
        public void Classify_NoMemberSince_DefaultsToNewcomer()
        {
            _customers.Add(new Customer { Id = 1, Name = "Alice" });

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.Newcomer, profile.CurrentStage);
        }

        // ── Exploring Tests ────────────────────────────────────────

        [TestMethod]
        public void Classify_ThreeToNineRentals_Exploring()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-90)
            });
            AddRentals(1, 5, _clock.Now.AddDays(-20));

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.Exploring, profile.CurrentStage);
        }

        [TestMethod]
        public void Classify_ExactlyThreeRentalsOldMember_Exploring()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-60)
            });
            AddRentals(1, 3, _clock.Now.AddDays(-10));

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.Exploring, profile.CurrentStage);
        }

        // ── Active Tests ───────────────────────────────────────────

        [TestMethod]
        public void Classify_TenToTwentyFourRentals_Active()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-120)
            });
            AddRentals(1, 15, _clock.Now.AddDays(-30), 2);

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.Active, profile.CurrentStage);
        }

        [TestMethod]
        public void Classify_ExactlyTenRentals_Active()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-120)
            });
            AddRentals(1, 10, _clock.Now.AddDays(-20), 2);

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.Active, profile.CurrentStage);
        }

        // ── Loyal Tests ────────────────────────────────────────────

        [TestMethod]
        public void Classify_TwentyFiveToFortyNineRentals_Loyal()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-200)
            });
            AddRentals(1, 30, _clock.Now.AddDays(-60), 2);

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.Loyal, profile.CurrentStage);
        }

        // ── Champion Tests ─────────────────────────────────────────

        [TestMethod]
        public void Classify_FiftyPlusRentals_Champion()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-300)
            });
            AddRentals(1, 55, _clock.Now.AddDays(-110), 2);

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.Champion, profile.CurrentStage);
        }

        [TestMethod]
        public void Classify_PlatinumWith25Rentals_Champion()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MembershipType = MembershipType.Platinum,
                MemberSince = _clock.Now.AddDays(-200)
            });
            AddRentals(1, 25, _clock.Now.AddDays(-50), 2);

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.Champion, profile.CurrentStage);
        }

        [TestMethod]
        public void Classify_GoldWith25Rentals_NotChampion()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MembershipType = MembershipType.Gold,
                MemberSince = _clock.Now.AddDays(-200)
            });
            AddRentals(1, 25, _clock.Now.AddDays(-50), 2);

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.Loyal, profile.CurrentStage);
        }

        // ── AtRisk Tests ───────────────────────────────────────────

        [TestMethod]
        public void Classify_NoRentalIn35Days_AtRisk()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-180)
            });
            // 5 rentals, last one 35 days ago
            AddRentals(1, 5, _clock.Now.AddDays(-70), 7);

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.AtRisk, profile.CurrentStage);
        }

        // ── Lapsed Tests ───────────────────────────────────────────

        [TestMethod]
        public void Classify_NoRentalIn65Days_Lapsed()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-200)
            });
            // Last rental 65 days ago
            AddRentals(1, 5, _clock.Now.AddDays(-100), 7);

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.Lapsed, profile.CurrentStage);
        }

        [TestMethod]
        public void Classify_NoRentalIn90Days_Lapsed()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-300)
            });
            _rentals.Add(MakeRental(1, 1, 1, _clock.Now.AddDays(-90)));

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.Lapsed, profile.CurrentStage);
        }

        // ── Cooling Tests ──────────────────────────────────────────

        [TestMethod]
        public void Classify_VelocityDropped_Cooling()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-120)
            });
            // Historical period (31-60 days ago): many rentals
            for (int i = 0; i < 10; i++)
                _rentals.Add(MakeRental(100 + i, 1, (i % 12) + 1,
                    _clock.Now.AddDays(-55 + i * 2)));
            // Recent period (last 30 days): very few
            _rentals.Add(MakeRental(200, 1, 1, _clock.Now.AddDays(-5)));

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.Cooling, profile.CurrentStage);
        }

        // ── Profile Property Tests ─────────────────────────────────

        [TestMethod]
        public void Profile_RentalVelocity_Calculated()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-60)
            });
            // 8 rentals in last 30 days
            for (int i = 0; i < 8; i++)
                _rentals.Add(MakeRental(i + 1, 1, (i % 12) + 1,
                    _clock.Now.AddDays(-25 + i * 3)));

            var profile = _service.ClassifyCustomer(1);
            Assert.IsTrue(profile.RentalVelocity > 0);
        }

        [TestMethod]
        public void Profile_GenreBreadth_ReflectsVariety()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-60)
            });
            // Rent movies across 6 genres
            for (int i = 0; i < 6; i++)
                _rentals.Add(MakeRental(i + 1, 1, i + 1, _clock.Now.AddDays(-20 + i * 3)));

            var profile = _service.ClassifyCustomer(1);
            Assert.IsTrue(profile.GenreExplorationBreadth >= 0.4);
        }

        [TestMethod]
        public void Profile_TrendDeclining_WhenRecentSlower()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-90)
            });
            // 8 rentals 31-60 days ago (historical)
            for (int i = 0; i < 8; i++)
                _rentals.Add(MakeRental(100 + i, 1, (i % 12) + 1,
                    _clock.Now.AddDays(-55 + i * 3)));
            // 2 rentals in last 30 days (recent)
            _rentals.Add(MakeRental(200, 1, 1, _clock.Now.AddDays(-10)));
            _rentals.Add(MakeRental(201, 1, 2, _clock.Now.AddDays(-5)));

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(EngagementTrend.Declining, profile.Trend);
        }

        [TestMethod]
        public void Profile_TrendRising_WhenRecentFaster()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-90)
            });
            // 2 rentals 31-60 days ago
            _rentals.Add(MakeRental(100, 1, 1, _clock.Now.AddDays(-50)));
            _rentals.Add(MakeRental(101, 1, 2, _clock.Now.AddDays(-40)));
            // 8 rentals in last 30 days
            for (int i = 0; i < 8; i++)
                _rentals.Add(MakeRental(200 + i, 1, (i % 12) + 1,
                    _clock.Now.AddDays(-25 + i * 3)));

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(EngagementTrend.Rising, profile.Trend);
        }

        // ── Journey Map Tests ──────────────────────────────────────

        [TestMethod]
        public void FullJourney_TracksTransitions()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-200)
            });
            AddRentals(1, 15, _clock.Now.AddDays(-40), 2);

            var map = _service.GetFullJourney(1);
            Assert.IsNotNull(map.CurrentProfile);
            Assert.IsTrue(map.Transitions.Count > 0);
            Assert.AreEqual("Alice", map.CustomerName);
        }

        [TestMethod]
        public void FullJourney_NewcomerNoTransitions()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-5)
            });

            var map = _service.GetFullJourney(1);
            Assert.AreEqual(0, map.Transitions.Count);
        }

        [TestMethod]
        public void FullJourney_LapsedShowsNegativeTransitions()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-200)
            });
            AddRentals(1, 5, _clock.Now.AddDays(-100), 3);

            var map = _service.GetFullJourney(1);
            Assert.IsTrue(map.Transitions.Any(
                t => t.ToStage == JourneyStage.Lapsed));
        }

        // ── Intervention Tests ─────────────────────────────────────

        [TestMethod]
        public void Interventions_Newcomer_GetsWelcomeSeries()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-5)
            });

            var interventions = _service.GetInterventions(1);
            Assert.IsTrue(interventions.Any(
                i => i.Type == InterventionType.WelcomeSeries));
        }

        [TestMethod]
        public void Interventions_Exploring_GetsGenreDiscovery()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-60)
            });
            AddRentals(1, 5, _clock.Now.AddDays(-15));

            var interventions = _service.GetInterventions(1);
            Assert.IsTrue(interventions.Any(
                i => i.Type == InterventionType.GenreDiscovery));
        }

        [TestMethod]
        public void Interventions_Active_GetsLoyaltyMilestone()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-120)
            });
            AddRentals(1, 15, _clock.Now.AddDays(-30), 2);

            var interventions = _service.GetInterventions(1);
            Assert.IsTrue(interventions.Any(
                i => i.Type == InterventionType.LoyaltyMilestone));
        }

        [TestMethod]
        public void Interventions_Lapsed_GetsWinBackCampaign()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-200)
            });
            _rentals.Add(MakeRental(1, 1, 1, _clock.Now.AddDays(-90)));

            var interventions = _service.GetInterventions(1);
            Assert.IsTrue(interventions.Any(
                i => i.Type == InterventionType.WinBackCampaign));
        }

        [TestMethod]
        public void Interventions_AtRisk_GetsRetentionOffer()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-180)
            });
            AddRentals(1, 5, _clock.Now.AddDays(-70), 7);

            var interventions = _service.GetInterventions(1);
            Assert.IsTrue(interventions.Any(
                i => i.Type == InterventionType.RetentionOffer));
        }

        [TestMethod]
        public void Interventions_Champion_GetsAmbassador()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-300)
            });
            AddRentals(1, 55, _clock.Now.AddDays(-110), 2);

            var interventions = _service.GetInterventions(1);
            Assert.IsTrue(interventions.Any(
                i => i.Type == InterventionType.AmbassadorProgram));
        }

        [TestMethod]
        public void Interventions_Loyal_GetsVipPreview()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-200)
            });
            AddRentals(1, 30, _clock.Now.AddDays(-60), 2);

            var interventions = _service.GetInterventions(1);
            Assert.IsTrue(interventions.Any(
                i => i.Type == InterventionType.VipPreview));
        }

        [TestMethod]
        public void Interventions_HavePriorityAndExpiry()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-5)
            });

            var interventions = _service.GetInterventions(1);
            Assert.IsTrue(interventions.All(i => i.Priority >= 1 && i.Priority <= 5));
            Assert.IsTrue(interventions.All(i => i.ExpiresInDays > 0));
        }

        // ── Dashboard Tests ────────────────────────────────────────

        [TestMethod]
        public void Dashboard_CountsAllStages()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-5)
            });
            _customers.Add(new Customer
            {
                Id = 2, Name = "Bob",
                MemberSince = _clock.Now.AddDays(-200)
            });
            AddRentals(2, 30, _clock.Now.AddDays(-60), 2);

            var dashboard = _service.GetDashboard();
            Assert.AreEqual(2, dashboard.TotalCustomers);
            Assert.IsTrue(dashboard.StageDistribution.Values.Sum() == 2);
        }

        [TestMethod]
        public void Dashboard_HealthPercentage_Calculated()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-5)
            });

            var dashboard = _service.GetDashboard();
            Assert.IsTrue(dashboard.HealthPercentage >= 0
                && dashboard.HealthPercentage <= 100);
        }

        [TestMethod]
        public void Dashboard_AllPositiveCustomers_100Health()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-5)
            });
            _customers.Add(new Customer
            {
                Id = 2, Name = "Bob",
                MemberSince = _clock.Now.AddDays(-10)
            });

            var dashboard = _service.GetDashboard();
            Assert.AreEqual(100.0, dashboard.HealthPercentage);
        }

        [TestMethod]
        public void Dashboard_InterventionQueue_SortedByPriority()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-5)
            });
            _customers.Add(new Customer
            {
                Id = 2, Name = "Bob",
                MemberSince = _clock.Now.AddDays(-200)
            });
            _rentals.Add(MakeRental(1, 2, 1, _clock.Now.AddDays(-90)));

            var dashboard = _service.GetDashboard();
            if (dashboard.InterventionQueue.Count > 1)
            {
                for (int i = 1; i < dashboard.InterventionQueue.Count; i++)
                    Assert.IsTrue(dashboard.InterventionQueue[i].Priority
                        >= dashboard.InterventionQueue[i - 1].Priority);
            }
        }

        // ── Alert Tests ────────────────────────────────────────────

        [TestMethod]
        public void Alerts_LapsedCustomer_GeneratesAlert()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-200)
            });
            // Last rental 65 days ago — within 30-day alert window for the transition
            AddRentals(1, 5, _clock.Now.AddDays(-75), 2);

            var alerts = _service.GetAlerts();
            Assert.IsTrue(alerts.Any(a =>
                a.CustomerId == 1
                && a.AlertType == "NegativeTransition"
                && a.ToStage == JourneyStage.Lapsed));
        }

        [TestMethod]
        public void Alerts_MilestoneApproaching_ActiveNear25()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-120)
            });
            AddRentals(1, 23, _clock.Now.AddDays(-50), 2);

            var alerts = _service.GetAlerts();
            Assert.IsTrue(alerts.Any(a =>
                a.CustomerId == 1
                && a.AlertType == "MilestoneApproaching"));
        }

        [TestMethod]
        public void Alerts_SortedBySeverity()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-200)
            });
            AddRentals(1, 5, _clock.Now.AddDays(-75), 2);
            _customers.Add(new Customer
            {
                Id = 2, Name = "Bob",
                MemberSince = _clock.Now.AddDays(-120)
            });
            AddRentals(2, 23, _clock.Now.AddDays(-50), 2);

            var alerts = _service.GetAlerts();
            if (alerts.Count > 1)
            {
                for (int i = 1; i < alerts.Count; i++)
                    Assert.IsTrue(alerts[i].Severity >= alerts[i - 1].Severity);
            }
        }

        // ── Error Handling ─────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Classify_InvalidCustomer_Throws()
        {
            _service.ClassifyCustomer(999);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new JourneyOrchestratorService(null, _rentals, _movies);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new JourneyOrchestratorService(_customers, null, _movies);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new JourneyOrchestratorService(_customers, _rentals, null);
        }

        // ── Edge Cases ─────────────────────────────────────────────

        [TestMethod]
        public void Classify_ExactThresholdRentals_CorrectStage()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-200)
            });
            AddRentals(1, 50, _clock.Now.AddDays(-100), 2);

            var profile = _service.ClassifyCustomer(1);
            Assert.AreEqual(JourneyStage.Champion, profile.CurrentStage);
        }

        [TestMethod]
        public void Dashboard_EmptyStore_Works()
        {
            var dashboard = _service.GetDashboard();
            Assert.AreEqual(0, dashboard.TotalCustomers);
            Assert.AreEqual(100.0, dashboard.HealthPercentage);
        }

        [TestMethod]
        public void Profile_Confidence_InValidRange()
        {
            _customers.Add(new Customer
            {
                Id = 1, Name = "Alice",
                MemberSince = _clock.Now.AddDays(-5)
            });

            var profile = _service.ClassifyCustomer(1);
            Assert.IsTrue(profile.StageConfidence >= 50
                && profile.StageConfidence <= 100);
        }

        // ── Test helpers ───────────────────────────────────────────

        private class FakeClock : IClock
        {
            public DateTime Now { get; }
            public FakeClock(DateTime now) { Now = now; }
        }

        private class InMemoryTestCustomerRepo : ICustomerRepository
        {
            private readonly List<Customer> _data = new List<Customer>();
            public void Add(Customer entity) => _data.Add(entity);
            public void Delete(int id) => _data.RemoveAll(c => c.Id == id);
            public IReadOnlyList<Customer> GetAll() => _data.AsReadOnly();
            public Customer GetById(int id) => _data.FirstOrDefault(c => c.Id == id);
            public void Update(Customer entity) { }
            public IReadOnlyList<Customer> Search(string query, MembershipType? type) => _data.AsReadOnly();
            public IReadOnlyList<Customer> GetByMemberSince(int year, int month) => _data.AsReadOnly();
            public CustomerStats GetStats() => new CustomerStats { TotalCustomers = _data.Count };
        }

        private class InMemoryTestMovieRepo : IMovieRepository
        {
            private readonly List<Movie> _data = new List<Movie>();
            public void Add(Movie entity) => _data.Add(entity);
            public void Delete(int id) => _data.RemoveAll(m => m.Id == id);
            public IReadOnlyList<Movie> GetAll() => _data.AsReadOnly();
            public Movie GetById(int id) => _data.FirstOrDefault(m => m.Id == id);
            public void Update(Movie entity) { }
            public IReadOnlyList<Movie> GetByReleaseDate(int year, int month) => _data.AsReadOnly();
            public Movie GetRandom() => _data.FirstOrDefault();
            public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating) => _data.AsReadOnly();
        }

        private class InMemoryTestRentalRepo : IRentalRepository
        {
            private readonly List<Rental> _data = new List<Rental>();
            public void Add(Rental entity) => _data.Add(entity);
            public void Delete(int id) => _data.RemoveAll(r => r.Id == id);
            public IReadOnlyList<Rental> GetAll() => _data.AsReadOnly();
            public Rental GetById(int id) => _data.FirstOrDefault(r => r.Id == id);
            public void Update(Rental entity) { }
            public IReadOnlyList<Rental> GetByCustomer(int customerId) =>
                _data.Where(r => r.CustomerId == customerId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) =>
                _data.Where(r => r.CustomerId == customerId && r.Status == RentalStatus.Active)
                    .ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetByMovie(int movieId) =>
                _data.Where(r => r.MovieId == movieId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetOverdue() =>
                _data.Where(r => r.Status == RentalStatus.Overdue).ToList().AsReadOnly();
            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) =>
                _data.AsReadOnly();
            public Rental ReturnRental(int rentalId) => GetById(rentalId);
            public bool IsMovieRentedOut(int movieId) =>
                _data.Any(r => r.MovieId == movieId && r.Status == RentalStatus.Active);
            public Rental Checkout(Rental rental) => rental;
            public Rental Checkout(Rental rental, int maxConcurrentRentals) => rental;
            public Rental ExtendRental(int rentalId, int days) => GetById(rentalId);
            public bool IsExtended(int rentalId) => false;
            public RentalStats GetStats() => new RentalStats();
        }
    }
}
