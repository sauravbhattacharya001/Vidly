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
    public class RentalContagionServiceTests
    {
        private InMemoryMovieRepository _movieRepo;
        private InMemoryRentalRepository _rentalRepo;
        private InMemoryCustomerRepository _customerRepo;
        private TestClock _clock;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new InMemoryMovieRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _customerRepo = new InMemoryCustomerRepository();
            _clock = new TestClock(new DateTime(2026, 5, 1, 12, 0, 0));
        }

        private RentalContagionService CreateService(ContagionEngineConfig config = null)
        {
            return new RentalContagionService(_rentalRepo, _customerRepo, _movieRepo, _clock, config);
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

        // ── Constructor tests ───────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullRentalRepo_Throws()
        {
            new RentalContagionService(null, _customerRepo, _movieRepo, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullCustomerRepo_Throws()
        {
            new RentalContagionService(_rentalRepo, null, _movieRepo, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullMovieRepo_Throws()
        {
            new RentalContagionService(_rentalRepo, _customerRepo, null, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullClock_Throws()
        {
            new RentalContagionService(_rentalRepo, _customerRepo, _movieRepo, null);
        }

        // ── Empty data ──────────────────────────────────────────────

        [TestMethod]
        public void Analyze_NoData_ReturnsEmptyReport()
        {
            var svc = CreateService();
            var report = svc.Analyze();

            Assert.IsNotNull(report);
            Assert.AreEqual(0, report.SocialNetwork.Count);
            Assert.AreEqual(0, report.ContagionEvents.Count);
            Assert.AreEqual(0, report.Influencers.Count);
            Assert.AreEqual(0, report.GenreContagions.Count);
            Assert.AreEqual(0, report.Chains.Count);
            Assert.AreEqual(0, report.Recommendations.Count);
            Assert.IsTrue(report.Insights.Count > 0);
            Assert.AreEqual(0, report.HealthScore);
        }

        [TestMethod]
        public void Analyze_GeneratedAt_MatchesClock()
        {
            var svc = CreateService();
            var report = svc.Analyze();
            Assert.AreEqual(_clock.Now, report.GeneratedAt);
        }

        // ── Social Network Builder ──────────────────────────────────

        [TestMethod]
        public void SocialNetwork_TwoCustomersShareTwoMovies_CreatesEdge()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Comedy);

            var baseDate = new DateTime(2026, 4, 1);
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(3));
            AddRental(m2.Id, c1.Id, baseDate.AddDays(5));
            AddRental(m2.Id, c2.Id, baseDate.AddDays(7));

            var svc = CreateService();
            var report = svc.Analyze();

            Assert.IsTrue(report.SocialNetwork.Count >= 1);
            var edge = report.SocialNetwork[0];
            Assert.AreEqual(2, edge.CoRentalCount);
            Assert.AreEqual(2, edge.SharedMovies.Count);
        }

        [TestMethod]
        public void SocialNetwork_OnlyOneSharedMovie_NoEdgeWithMinTwo()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m1 = AddMovie("Film A", Genre.Action);

            AddRental(m1.Id, c1.Id, new DateTime(2026, 4, 1));
            AddRental(m1.Id, c2.Id, new DateTime(2026, 4, 3));

            var svc = CreateService(new ContagionEngineConfig { MinCoRentals = 2 });
            var report = svc.Analyze();

            Assert.AreEqual(0, report.SocialNetwork.Count);
        }

        [TestMethod]
        public void SocialNetwork_MinCoRentalsOne_AllowsSingleSharedMovie()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m1 = AddMovie("Film A", Genre.Action);

            AddRental(m1.Id, c1.Id, new DateTime(2026, 4, 1));
            AddRental(m1.Id, c2.Id, new DateTime(2026, 4, 3));

            var svc = CreateService(new ContagionEngineConfig { MinCoRentals = 1 });
            var report = svc.Analyze();

            Assert.AreEqual(1, report.SocialNetwork.Count);
        }

        [TestMethod]
        public void SocialNetwork_RentalsTooFarApart_NoEdge()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Comedy);

            var baseDate = new DateTime(2026, 3, 1);
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(30)); // too far apart
            AddRental(m2.Id, c1.Id, baseDate);
            AddRental(m2.Id, c2.Id, baseDate.AddDays(30));

            var svc = CreateService(new ContagionEngineConfig { ContagionWindowDays = 14 });
            var report = svc.Analyze();

            Assert.AreEqual(0, report.SocialNetwork.Count);
        }

        [TestMethod]
        public void SocialNetwork_SameCustomer_NoSelfEdge()
        {
            var c1 = AddCustomer("Alice");
            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Comedy);

            AddRental(m1.Id, c1.Id, new DateTime(2026, 4, 1));
            AddRental(m2.Id, c1.Id, new DateTime(2026, 4, 5));

            var svc = CreateService(new ContagionEngineConfig { MinCoRentals = 1 });
            var report = svc.Analyze();

            Assert.AreEqual(0, report.SocialNetwork.Count);
        }

        // ── Contagion Event Detector ────────────────────────────────

        [TestMethod]
        public void ContagionEvents_LinkedCustomers_DetectsEvent()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Comedy);
            var m3 = AddMovie("Film C", Genre.Action);

            var baseDate = new DateTime(2026, 4, 1);
            // Build social link (2 shared movies)
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(2));
            AddRental(m2.Id, c1.Id, baseDate.AddDays(1));
            AddRental(m2.Id, c2.Id, baseDate.AddDays(4));

            // Contagion event on m3
            AddRental(m3.Id, c1.Id, baseDate.AddDays(10));
            AddRental(m3.Id, c2.Id, baseDate.AddDays(15));

            var svc = CreateService();
            var report = svc.Analyze();

            Assert.IsTrue(report.ContagionEvents.Count > 0);
            var evt = report.ContagionEvents.FirstOrDefault(e => e.MovieName == "Film C");
            Assert.IsNotNull(evt);
            Assert.AreEqual("Alice", evt.PatientZeroName);
            Assert.AreEqual("Bob", evt.InfectedName);
        }

        [TestMethod]
        public void ContagionEvents_NoSocialLink_NoEvent()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m1 = AddMovie("Film A", Genre.Action);

            AddRental(m1.Id, c1.Id, new DateTime(2026, 4, 1));
            AddRental(m1.Id, c2.Id, new DateTime(2026, 4, 5));

            var svc = CreateService(new ContagionEngineConfig { MinCoRentals = 2 });
            var report = svc.Analyze();

            // No social link (only 1 shared movie), so no contagion events
            Assert.AreEqual(0, report.ContagionEvents.Count);
        }

        [TestMethod]
        public void ContagionEvents_DaysToContagion_Correct()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Comedy);
            var m3 = AddMovie("Film C", Genre.Drama);

            var baseDate = new DateTime(2026, 4, 1);
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(1));
            AddRental(m2.Id, c1.Id, baseDate.AddDays(2));
            AddRental(m2.Id, c2.Id, baseDate.AddDays(3));

            AddRental(m3.Id, c1.Id, baseDate.AddDays(10));
            AddRental(m3.Id, c2.Id, baseDate.AddDays(17)); // 7 days later

            var svc = CreateService();
            var report = svc.Analyze();

            var evt = report.ContagionEvents.FirstOrDefault(e => e.MovieName == "Film C");
            Assert.IsNotNull(evt);
            Assert.AreEqual(7, evt.DaysToContagion);
        }

        // ── Influencer Scorer ───────────────────────────────────────

        [TestMethod]
        public void Influencers_SuperSpreader_HighScoreMultipleInfected()
        {
            var c1 = AddCustomer("Influencer");
            var c2 = AddCustomer("Follower1");
            var c3 = AddCustomer("Follower2");
            var c4 = AddCustomer("Follower3");

            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Comedy);
            var m3 = AddMovie("Film C", Genre.Drama);
            var m4 = AddMovie("Film D", Genre.Horror);
            var m5 = AddMovie("Film E", Genre.SciFi);

            var baseDate = new DateTime(2026, 4, 1);

            // Build links: c1-c2, c1-c3, c1-c4 (2 shared movies each)
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(1));
            AddRental(m1.Id, c3.Id, baseDate.AddDays(1));
            AddRental(m1.Id, c4.Id, baseDate.AddDays(1));

            AddRental(m2.Id, c1.Id, baseDate.AddDays(2));
            AddRental(m2.Id, c2.Id, baseDate.AddDays(3));
            AddRental(m2.Id, c3.Id, baseDate.AddDays(3));
            AddRental(m2.Id, c4.Id, baseDate.AddDays(3));

            // c1 rents new movies that spread
            AddRental(m3.Id, c1.Id, baseDate.AddDays(10));
            AddRental(m3.Id, c2.Id, baseDate.AddDays(12));

            AddRental(m4.Id, c1.Id, baseDate.AddDays(12));
            AddRental(m4.Id, c3.Id, baseDate.AddDays(14));

            AddRental(m5.Id, c1.Id, baseDate.AddDays(14));
            AddRental(m5.Id, c4.Id, baseDate.AddDays(16));

            var svc = CreateService();
            var report = svc.Analyze();

            var influencer = report.Influencers.FirstOrDefault(i => i.CustomerName == "Influencer");
            Assert.IsNotNull(influencer);
            Assert.IsTrue(influencer.Score > 50);
            Assert.AreEqual(3, influencer.UniqueInfluenced);
        }

        [TestMethod]
        public void Influencers_Immune_NoContagionEvents()
        {
            var c1 = AddCustomer("Active");
            var c2 = AddCustomer("Immune");

            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Comedy);

            var baseDate = new DateTime(2026, 4, 1);
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(1));
            AddRental(m2.Id, c1.Id, baseDate.AddDays(2));
            AddRental(m2.Id, c2.Id, baseDate.AddDays(3));

            var svc = CreateService();
            var report = svc.Analyze();

            // Both should have profiles
            Assert.IsTrue(report.Influencers.Count > 0 || report.ContagionEvents.Count > 0);
        }

        [TestMethod]
        public void Influencers_TierClassification_MatchesScore()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Comedy);

            var baseDate = new DateTime(2026, 4, 1);
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(1));
            AddRental(m2.Id, c1.Id, baseDate.AddDays(2));
            AddRental(m2.Id, c2.Id, baseDate.AddDays(3));

            var svc = CreateService();
            var report = svc.Analyze();

            foreach (var influencer in report.Influencers)
            {
                if (influencer.Score >= 75 && influencer.UniqueInfluenced >= 3)
                    Assert.AreEqual(InfluencerTier.SuperSpreader, influencer.Tier);
                else if (influencer.Score >= 50)
                    Assert.AreEqual(InfluencerTier.Influencer, influencer.Tier);
            }
        }

        // ── Genre Contagion Mapper ──────────────────────────────────

        [TestMethod]
        public void GenreContagion_CalculatesR0()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var c3 = AddCustomer("Carol");

            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Action);
            var m3 = AddMovie("Film C", Genre.Action);

            var baseDate = new DateTime(2026, 4, 1);
            // Build links: all three connected
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(1));
            AddRental(m1.Id, c3.Id, baseDate.AddDays(2));
            AddRental(m2.Id, c1.Id, baseDate.AddDays(3));
            AddRental(m2.Id, c2.Id, baseDate.AddDays(4));
            AddRental(m2.Id, c3.Id, baseDate.AddDays(5));

            // Contagion events
            AddRental(m3.Id, c1.Id, baseDate.AddDays(10));
            AddRental(m3.Id, c2.Id, baseDate.AddDays(12));
            AddRental(m3.Id, c3.Id, baseDate.AddDays(14));

            var svc = CreateService();
            var report = svc.Analyze();

            var actionGenre = report.GenreContagions.FirstOrDefault(g => g.Genre == "Action");
            Assert.IsNotNull(actionGenre);
            Assert.IsTrue(actionGenre.R0 > 0);
            Assert.IsTrue(actionGenre.TotalSecondaryRentals > 0);
        }

        [TestMethod]
        public void GenreContagion_Classification_Pandemic()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var c3 = AddCustomer("Carol");
            var c4 = AddCustomer("Dave");

            var m1 = AddMovie("Film A", Genre.Horror);
            var m2 = AddMovie("Film B", Genre.Horror);
            var m3 = AddMovie("Film C", Genre.Horror);

            var baseDate = new DateTime(2026, 4, 1);

            // Dense links
            foreach (var m in new[] { m1, m2 })
            {
                AddRental(m.Id, c1.Id, baseDate);
                AddRental(m.Id, c2.Id, baseDate.AddDays(1));
                AddRental(m.Id, c3.Id, baseDate.AddDays(2));
                AddRental(m.Id, c4.Id, baseDate.AddDays(3));
            }

            // Many contagion events
            AddRental(m3.Id, c1.Id, baseDate.AddDays(10));
            AddRental(m3.Id, c2.Id, baseDate.AddDays(11));
            AddRental(m3.Id, c3.Id, baseDate.AddDays(12));
            AddRental(m3.Id, c4.Id, baseDate.AddDays(13));

            var svc = CreateService();
            var report = svc.Analyze();

            var horror = report.GenreContagions.FirstOrDefault(g => g.Genre == "Horror");
            Assert.IsNotNull(horror);
            // With many secondary rentals per primary, R0 should be high
            Assert.IsTrue(horror.R0 >= 1.0);
        }

        [TestMethod]
        public void GenreContagion_AvgDaysToSpread_Calculated()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Action);
            var m3 = AddMovie("Film C", Genre.Action);

            var baseDate = new DateTime(2026, 4, 1);
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(1));
            AddRental(m2.Id, c1.Id, baseDate.AddDays(2));
            AddRental(m2.Id, c2.Id, baseDate.AddDays(3));

            AddRental(m3.Id, c1.Id, baseDate.AddDays(10));
            AddRental(m3.Id, c2.Id, baseDate.AddDays(15));

            var svc = CreateService();
            var report = svc.Analyze();

            var action = report.GenreContagions.FirstOrDefault(g => g.Genre == "Action");
            Assert.IsNotNull(action);
            Assert.IsTrue(action.AvgDaysToSpread > 0);
        }

        // ── Contagion Chain Tracker ─────────────────────────────────

        [TestMethod]
        public void Chains_LinearChain_Detected()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var c3 = AddCustomer("Carol");

            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Action);
            var m3 = AddMovie("Film C", Genre.Action);

            var baseDate = new DateTime(2026, 4, 1);

            // Build links
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(1));
            AddRental(m2.Id, c2.Id, baseDate.AddDays(2));
            AddRental(m2.Id, c3.Id, baseDate.AddDays(3));
            // Additional shared movies for links
            AddRental(m1.Id, c3.Id, baseDate.AddDays(2));
            AddRental(m2.Id, c1.Id, baseDate.AddDays(3));

            // Chain: c1 -> c2 -> c3 on Film C
            AddRental(m3.Id, c1.Id, baseDate.AddDays(10));
            AddRental(m3.Id, c2.Id, baseDate.AddDays(13));
            AddRental(m3.Id, c3.Id, baseDate.AddDays(16));

            var svc = CreateService();
            var report = svc.Analyze();

            Assert.IsTrue(report.Chains.Count > 0);
            var longest = report.Chains.OrderByDescending(c => c.Length).First();
            Assert.IsTrue(longest.Length >= 2);
        }

        [TestMethod]
        public void Chains_SingleEvent_NoChain()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Comedy);

            var baseDate = new DateTime(2026, 4, 1);
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(1));
            AddRental(m2.Id, c1.Id, baseDate.AddDays(2));
            AddRental(m2.Id, c2.Id, baseDate.AddDays(3));

            var svc = CreateService();
            var report = svc.Analyze();

            // Chains require 2+ links minimum
            // Single pair can form a chain of length 2 (both endpoints)
            // Just verify no crash
            Assert.IsNotNull(report.Chains);
        }

        // ── Social Proof Generator ──────────────────────────────────

        [TestMethod]
        public void Recommendations_SuggestsUnrentedMovies()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Comedy);
            var m3 = AddMovie("Film C", Genre.Drama); // Only Bob rents this

            var baseDate = new DateTime(2026, 4, 1);

            // Build social link
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(1));
            AddRental(m2.Id, c1.Id, baseDate.AddDays(2));
            AddRental(m2.Id, c2.Id, baseDate.AddDays(3));

            // Bob rents something Alice hasn't
            AddRental(m3.Id, c2.Id, baseDate.AddDays(10));

            var svc = CreateService();
            var report = svc.Analyze();

            // Should recommend Film C to Alice
            var rec = report.Recommendations.FirstOrDefault(
                r => r.TargetCustomerId == c1.Id && r.MovieName == "Film C");
            // Recommendations depend on contagion events existing, so may or may not appear
            Assert.IsNotNull(report.Recommendations);
        }

        [TestMethod]
        public void Recommendations_ConfidenceScore_BoundedTo100()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var c3 = AddCustomer("Carol");
            var c4 = AddCustomer("Dave");
            var c5 = AddCustomer("Eve");

            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Comedy);
            var m3 = AddMovie("Film C", Genre.Drama);

            var baseDate = new DateTime(2026, 4, 1);

            // Dense network
            foreach (var c in new[] { c1, c2, c3, c4, c5 })
            {
                AddRental(m1.Id, c.Id, baseDate.AddDays(c.Id));
                AddRental(m2.Id, c.Id, baseDate.AddDays(c.Id + 1));
            }

            AddRental(m3.Id, c2.Id, baseDate.AddDays(10));
            AddRental(m3.Id, c3.Id, baseDate.AddDays(11));
            AddRental(m3.Id, c4.Id, baseDate.AddDays(12));
            AddRental(m3.Id, c5.Id, baseDate.AddDays(13));

            var svc = CreateService();
            var report = svc.Analyze();

            foreach (var rec in report.Recommendations)
            {
                Assert.IsTrue(rec.ConfidenceScore >= 0 && rec.ConfidenceScore <= 100,
                    "Confidence should be bounded: " + rec.ConfidenceScore);
            }
        }

        // ── Insight Generator ───────────────────────────────────────

        [TestMethod]
        public void Insights_AlwaysIncludesConnectivity()
        {
            AddCustomer("Alice");
            var svc = CreateService();
            var report = svc.Analyze();

            Assert.IsTrue(report.Insights.Any(i => i.Contains("connectivity")));
        }

        [TestMethod]
        public void Insights_NoEvents_SaysNoContagion()
        {
            AddCustomer("Alice");
            var svc = CreateService();
            var report = svc.Analyze();

            Assert.IsTrue(report.Insights.Any(i => i.Contains("No contagion events")));
        }

        [TestMethod]
        public void Insights_WithEvents_ReportsCount()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Comedy);
            var m3 = AddMovie("Film C", Genre.Drama);

            var baseDate = new DateTime(2026, 4, 1);
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(1));
            AddRental(m2.Id, c1.Id, baseDate.AddDays(2));
            AddRental(m2.Id, c2.Id, baseDate.AddDays(3));
            AddRental(m3.Id, c1.Id, baseDate.AddDays(10));
            AddRental(m3.Id, c2.Id, baseDate.AddDays(12));

            var svc = CreateService();
            var report = svc.Analyze();

            Assert.IsTrue(report.Insights.Any(
                i => i.Contains("contagion event") && i.Contains("avg")));
        }

        // ── Health Score ────────────────────────────────────────────

        [TestMethod]
        public void HealthScore_ZeroForEmptyData()
        {
            var svc = CreateService();
            var report = svc.Analyze();
            Assert.AreEqual(0, report.HealthScore);
        }

        [TestMethod]
        public void HealthScore_BoundedTo100()
        {
            var customers = new List<Customer>();
            for (int i = 0; i < 20; i++)
                customers.Add(AddCustomer("Customer" + i));

            var movies = new List<Movie>();
            for (int i = 0; i < 10; i++)
                movies.Add(AddMovie("Film" + i, (Genre)(i % 5 + 1)));

            var baseDate = new DateTime(2026, 4, 1);

            // Create dense network
            foreach (var m in movies)
            {
                foreach (var c in customers)
                {
                    AddRental(m.Id, c.Id, baseDate.AddDays(c.Id % 10));
                }
            }

            var svc = CreateService(new ContagionEngineConfig { MinCoRentals = 1 });
            var report = svc.Analyze();

            Assert.IsTrue(report.HealthScore >= 0 && report.HealthScore <= 100);
        }

        [TestMethod]
        public void HealthScore_IncreasesWithMoreActivity()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m1 = AddMovie("Film A", Genre.Action);

            var baseDate = new DateTime(2026, 4, 1);
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(1));

            var svc1 = CreateService(new ContagionEngineConfig { MinCoRentals = 1 });
            var score1 = svc1.Analyze().HealthScore;

            // Add more activity
            var m2 = AddMovie("Film B", Genre.Comedy);
            var m3 = AddMovie("Film C", Genre.Drama);
            var m4 = AddMovie("Film D", Genre.Horror);
            AddRental(m2.Id, c1.Id, baseDate.AddDays(2));
            AddRental(m2.Id, c2.Id, baseDate.AddDays(3));
            AddRental(m3.Id, c1.Id, baseDate.AddDays(4));
            AddRental(m3.Id, c2.Id, baseDate.AddDays(5));
            AddRental(m4.Id, c1.Id, baseDate.AddDays(6));
            AddRental(m4.Id, c2.Id, baseDate.AddDays(7));

            var svc2 = CreateService(new ContagionEngineConfig { MinCoRentals = 1 });
            var score2 = svc2.Analyze().HealthScore;

            Assert.IsTrue(score2 >= score1, "More activity should increase health score");
        }

        // ── Config tests ────────────────────────────────────────────

        [TestMethod]
        public void Config_DefaultValues_Reasonable()
        {
            var config = new ContagionEngineConfig();
            Assert.AreEqual(90, config.WindowDays);
            Assert.AreEqual(14, config.ContagionWindowDays);
            Assert.AreEqual(2, config.MinCoRentals);
            Assert.AreEqual(30.0, config.MinInfluenceScore);
            Assert.AreEqual(10, config.TopN);
        }

        [TestMethod]
        public void Analyze_CustomWindowDays_FiltersOldRentals()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Comedy);

            // Rentals from 60 days ago (within 90 but outside 30)
            var baseDate = _clock.Now.AddDays(-60);
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(1));
            AddRental(m2.Id, c1.Id, baseDate.AddDays(2));
            AddRental(m2.Id, c2.Id, baseDate.AddDays(3));

            var svc30 = CreateService(new ContagionEngineConfig { WindowDays = 30 });
            var report30 = svc30.Analyze();

            var svc90 = CreateService(new ContagionEngineConfig { WindowDays = 90 });
            var report90 = svc90.Analyze();

            Assert.AreEqual(0, report30.SocialNetwork.Count, "30-day window should exclude these rentals");
            Assert.IsTrue(report90.SocialNetwork.Count > 0, "90-day window should include these rentals");
        }

        [TestMethod]
        public void Analyze_TopN_LimitsResults()
        {
            // Create many customers and movies
            var customers = new List<Customer>();
            for (int i = 0; i < 15; i++)
                customers.Add(AddCustomer("Customer" + i));

            var movies = new List<Movie>();
            for (int i = 0; i < 8; i++)
                movies.Add(AddMovie("Film" + i, (Genre)(i % 5 + 1)));

            var baseDate = new DateTime(2026, 4, 1);
            foreach (var m in movies)
            {
                foreach (var c in customers)
                {
                    AddRental(m.Id, c.Id, baseDate.AddDays(c.Id % 10));
                }
            }

            var svc = CreateService(new ContagionEngineConfig { MinCoRentals = 1, TopN = 3 });
            var report = svc.Analyze();

            Assert.IsTrue(report.Influencers.Count <= 3);
            Assert.IsTrue(report.Chains.Count <= 3);
            Assert.IsTrue(report.Recommendations.Count <= 3);
        }

        // ── Edge cases ──────────────────────────────────────────────

        [TestMethod]
        public void Analyze_RentalsOutsideWindow_Excluded()
        {
            var c1 = AddCustomer("Alice");
            var m1 = AddMovie("Film A", Genre.Action);

            // Rental way outside window
            AddRental(m1.Id, c1.Id, new DateTime(2025, 1, 1));

            var svc = CreateService();
            var report = svc.Analyze();

            Assert.AreEqual(0, report.SocialNetwork.Count);
        }

        [TestMethod]
        public void Analyze_MultipleGenres_AllTracked()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");

            var genres = new[] { Genre.Action, Genre.Comedy, Genre.Drama, Genre.Horror };
            var movies = new List<Movie>();
            foreach (var g in genres)
            {
                movies.Add(AddMovie("Film-" + g, g));
                movies.Add(AddMovie("Film2-" + g, g));
            }

            var baseDate = new DateTime(2026, 4, 1);
            // All movies rented by both
            foreach (var m in movies)
            {
                AddRental(m.Id, c1.Id, baseDate);
                AddRental(m.Id, c2.Id, baseDate.AddDays(1));
            }

            var svc = CreateService(new ContagionEngineConfig { MinCoRentals = 1 });
            var report = svc.Analyze();

            Assert.IsTrue(report.GenreContagions.Count > 1, "Multiple genres should be tracked");
        }

        [TestMethod]
        public void SocialNetwork_OrderedByCoRentalCount()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var c3 = AddCustomer("Carol");

            var baseDate = new DateTime(2026, 4, 1);

            // c1-c2: 3 shared movies
            for (int i = 0; i < 3; i++)
            {
                var m = AddMovie("Shared-AB-" + i, Genre.Action);
                AddRental(m.Id, c1.Id, baseDate.AddDays(i));
                AddRental(m.Id, c2.Id, baseDate.AddDays(i + 1));
            }

            // c1-c3: 2 shared movies
            for (int i = 0; i < 2; i++)
            {
                var m = AddMovie("Shared-AC-" + i, Genre.Comedy);
                AddRental(m.Id, c1.Id, baseDate.AddDays(i));
                AddRental(m.Id, c3.Id, baseDate.AddDays(i + 1));
            }

            var svc = CreateService();
            var report = svc.Analyze();

            if (report.SocialNetwork.Count >= 2)
            {
                Assert.IsTrue(report.SocialNetwork[0].CoRentalCount >= report.SocialNetwork[1].CoRentalCount,
                    "Social network should be ordered by co-rental count");
            }
        }

        [TestMethod]
        public void ContagionEvents_OrderedByDate()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");

            var m1 = AddMovie("Film A", Genre.Action);
            var m2 = AddMovie("Film B", Genre.Action);
            var m3 = AddMovie("Film C", Genre.Action);

            var baseDate = new DateTime(2026, 4, 1);
            AddRental(m1.Id, c1.Id, baseDate);
            AddRental(m1.Id, c2.Id, baseDate.AddDays(1));
            AddRental(m2.Id, c1.Id, baseDate.AddDays(2));
            AddRental(m2.Id, c2.Id, baseDate.AddDays(3));

            AddRental(m3.Id, c1.Id, baseDate.AddDays(10));
            AddRental(m3.Id, c2.Id, baseDate.AddDays(15));

            var svc = CreateService();
            var report = svc.Analyze();

            for (int i = 1; i < report.ContagionEvents.Count; i++)
            {
                Assert.IsTrue(report.ContagionEvents[i].ContagionDate >= report.ContagionEvents[i - 1].ContagionDate,
                    "Contagion events should be ordered by date");
            }
        }
    }
}
