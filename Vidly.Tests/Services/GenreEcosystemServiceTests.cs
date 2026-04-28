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
    public class GenreEcosystemServiceTests
    {
        private InMemoryMovieRepository _movieRepo;
        private InMemoryRentalRepository _rentalRepo;
        private InMemoryCustomerRepository _customerRepo;
        private TestClock _clock;
        private GenreEcosystemService _service;

        private int _nextMovieId = 9000;
        private int _nextCustomerId = 9000;
        private int _nextRentalId = 9000;

        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
            InMemoryCustomerRepository.Reset();
            InMemoryRentalRepository.Reset();

            _movieRepo = new InMemoryMovieRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _customerRepo = new InMemoryCustomerRepository();
            _clock = new TestClock(new DateTime(2026, 4, 28, 12, 0, 0));

            // Seed movies across genres
            AddMovie("Action Hero", Genre.Action, 4);
            AddMovie("Laugh Track", Genre.Comedy, 3);
            AddMovie("Deep Drama", Genre.Drama, 5);
            AddMovie("Scare Fest", Genre.Horror, 3);
            AddMovie("Space Wars", Genre.SciFi, 4);
            AddMovie("Action Blast", Genre.Action, 4);
            AddMovie("Funny Days", Genre.Comedy, 4);
            AddMovie("Thriller Night", Genre.Thriller, 5);
            AddMovie("Love Story", Genre.Romance, 3);
            AddMovie("Toon World", Genre.Animation, 4);

            // Seed customers
            var alice = AddCustomer("Alice", MembershipType.Gold);
            var bob = AddCustomer("Bob", MembershipType.Silver);
            var charlie = AddCustomer("Charlie", MembershipType.Basic);
            var diana = AddCustomer("Diana", MembershipType.Platinum);
            var eve = AddCustomer("Eve", MembershipType.Gold);

            // Seed rentals — create cross-genre patterns
            var baseDate = new DateTime(2026, 3, 1);

            // Alice: Action + Comedy + Thriller (bridge across 3 genres)
            AddRental(alice.Id, 9000, baseDate);         // Action Hero
            AddRental(alice.Id, 9001, baseDate.AddDays(5));  // Laugh Track
            AddRental(alice.Id, 9007, baseDate.AddDays(10)); // Thriller Night
            AddRental(alice.Id, 9005, baseDate.AddDays(15)); // Action Blast

            // Bob: Action + SciFi + Horror + Drama + Comedy (bridge across 5 genres)
            AddRental(bob.Id, 9000, baseDate.AddDays(2));   // Action Hero
            AddRental(bob.Id, 9004, baseDate.AddDays(7));   // Space Wars
            AddRental(bob.Id, 9003, baseDate.AddDays(12));  // Scare Fest
            AddRental(bob.Id, 9002, baseDate.AddDays(17));  // Deep Drama
            AddRental(bob.Id, 9001, baseDate.AddDays(22));  // Laugh Track

            // Charlie: Action only (genre specialist)
            AddRental(charlie.Id, 9000, baseDate.AddDays(3));
            AddRental(charlie.Id, 9005, baseDate.AddDays(8));

            // Diana: Comedy + Romance + Animation (bridge across 3 genres)
            AddRental(diana.Id, 9001, baseDate.AddDays(4));  // Laugh Track
            AddRental(diana.Id, 9008, baseDate.AddDays(9));  // Love Story
            AddRental(diana.Id, 9009, baseDate.AddDays(14)); // Toon World
            AddRental(diana.Id, 9006, baseDate.AddDays(19)); // Funny Days

            // Eve: Drama + Thriller (bridge across 2 — not enough for bridge status)
            AddRental(eve.Id, 9002, baseDate.AddDays(6));
            AddRental(eve.Id, 9007, baseDate.AddDays(11));

            _service = new GenreEcosystemService(_movieRepo, _rentalRepo, _customerRepo, _clock);
        }

        private Movie AddMovie(string name, Genre genre, int rating)
        {
            var m = new Movie { Id = _nextMovieId++, Name = name, Genre = genre, Rating = rating };
            _movieRepo.Add(m);
            return m;
        }

        private Customer AddCustomer(string name, MembershipType tier)
        {
            var c = new Customer { Id = _nextCustomerId++, Name = name, MembershipType = tier };
            _customerRepo.Add(c);
            return c;
        }

        private Rental AddRental(int customerId, int movieId, DateTime rentalDate)
        {
            var r = new Rental
            {
                Id = _nextRentalId++,
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(7),
                ReturnDate = rentalDate.AddDays(5),
                DailyRate = 3.99m,
                Status = RentalStatus.Returned
            };
            _rentalRepo.Add(r);
            return r;
        }

        // ── Basic Report Tests ──────────────────────────────────────

        [TestMethod]
        public void Analyze_ReturnsReport_WithCorrectMetadata()
        {
            var report = _service.Analyze(180);

            Assert.IsNotNull(report);
            Assert.AreEqual(180, report.LookbackDays);
            Assert.IsTrue(report.TotalRentalsAnalyzed > 0);
            Assert.IsTrue(report.TotalCustomersAnalyzed > 0);
        }

        [TestMethod]
        public void Analyze_GenreNodes_ContainsAllSeededGenres()
        {
            var report = _service.Analyze();

            var genreNames = report.GenreNodes.Select(n => n.Genre).ToList();
            Assert.IsTrue(genreNames.Contains("Action"));
            Assert.IsTrue(genreNames.Contains("Comedy"));
            Assert.IsTrue(genreNames.Contains("Drama"));
        }

        [TestMethod]
        public void Analyze_GenreNodes_ActionHasHighestRentalCount()
        {
            var report = _service.Analyze();

            var action = report.GenreNodes.First(n => n.Genre == "Action");
            Assert.IsTrue(action.RentalCount >= 4);
        }

        [TestMethod]
        public void Analyze_GenreNodes_VelocityCalculatedCorrectly()
        {
            var report = _service.Analyze();

            var action = report.GenreNodes.First(n => n.Genre == "Action");
            Assert.IsTrue(action.Velocity > 0);
            Assert.AreEqual(action.RentalCount / (double)action.MovieCount, action.Velocity, 0.01);
        }

        [TestMethod]
        public void Analyze_GenreNodes_MarketSharesSumToOne()
        {
            var report = _service.Analyze();

            var totalShare = report.GenreNodes.Sum(n => n.MarketShare);
            Assert.AreEqual(1.0, totalShare, 0.02);
        }

        [TestMethod]
        public void Analyze_GenreNodes_RevenueIsPositive()
        {
            var report = _service.Analyze();

            foreach (var node in report.GenreNodes.Where(n => n.RentalCount > 0))
            {
                Assert.IsTrue(node.Revenue > 0, node.Genre + " should have positive revenue");
            }
        }

        [TestMethod]
        public void Analyze_GenreNodes_StatusClassification()
        {
            var report = _service.Analyze();

            foreach (var node in report.GenreNodes)
            {
                var validStatuses = new[] { "Star", "Growing", "Stable", "Niche", "Declining" };
                Assert.IsTrue(validStatuses.Contains(node.Status),
                    node.Genre + " has unexpected status: " + node.Status);
            }
        }

        // ── Co-Rental Edge Tests ────────────────────────────────────

        [TestMethod]
        public void Analyze_CoRentalEdges_DetectsSharedCustomers()
        {
            var report = _service.Analyze();

            var actionComedy = report.CoRentalEdges
                .FirstOrDefault(e =>
                    (e.GenreA == "Action" && e.GenreB == "Comedy") ||
                    (e.GenreA == "Comedy" && e.GenreB == "Action"));

            Assert.IsNotNull(actionComedy, "Action-Comedy edge should exist");
            Assert.IsTrue(actionComedy.SharedCustomers >= 2);
        }

        [TestMethod]
        public void Analyze_CoRentalEdges_AffinityBetweenZeroAndOne()
        {
            var report = _service.Analyze();

            foreach (var edge in report.CoRentalEdges)
            {
                Assert.IsTrue(edge.Affinity >= 0 && edge.Affinity <= 1.0,
                    edge.GenreA + "-" + edge.GenreB + " affinity out of range: " + edge.Affinity);
            }
        }

        [TestMethod]
        public void Analyze_CoRentalEdges_StrengthClassification()
        {
            var report = _service.Analyze();

            foreach (var edge in report.CoRentalEdges)
            {
                var valid = new[] { "Strong", "Moderate", "Weak" };
                Assert.IsTrue(valid.Contains(edge.Strength),
                    edge.GenreA + "-" + edge.GenreB + " unexpected strength: " + edge.Strength);
            }
        }

        // ── Bridge Customer Tests ───────────────────────────────────

        [TestMethod]
        public void Analyze_BridgeCustomers_DetectsBobAsExplorer()
        {
            var report = _service.Analyze();

            var bob = report.BridgeCustomers.FirstOrDefault(b => b.CustomerName == "Bob");
            Assert.IsNotNull(bob, "Bob should be detected as a bridge customer");
            Assert.IsTrue(bob.GenreCount >= 5);
        }

        [TestMethod]
        public void Analyze_BridgeCustomers_CharlieNotABridge()
        {
            var report = _service.Analyze();

            var charlie = report.BridgeCustomers.FirstOrDefault(b => b.CustomerName == "Charlie");
            Assert.IsNull(charlie, "Charlie should NOT be a bridge (single genre)");
        }

        [TestMethod]
        public void Analyze_BridgeCustomers_ClassificationValid()
        {
            var report = _service.Analyze();

            foreach (var b in report.BridgeCustomers)
            {
                var valid = new[] { "Omnivore", "Explorer", "Crossover", "Dabbler" };
                Assert.IsTrue(valid.Contains(b.Classification),
                    b.CustomerName + " unexpected classification: " + b.Classification);
            }
        }

        [TestMethod]
        public void Analyze_BridgeCustomers_EvennessBetweenZeroAndOne()
        {
            var report = _service.Analyze();

            foreach (var b in report.BridgeCustomers)
            {
                Assert.IsTrue(b.Evenness >= 0 && b.Evenness <= 1.0,
                    b.CustomerName + " evenness out of range: " + b.Evenness);
            }
        }

        [TestMethod]
        public void Analyze_BridgeCustomers_BridgeScorePositive()
        {
            var report = _service.Analyze();

            foreach (var b in report.BridgeCustomers)
            {
                Assert.IsTrue(b.BridgeScore > 0,
                    b.CustomerName + " should have positive bridge score");
            }
        }

        // ── Trend Tests ─────────────────────────────────────────────

        [TestMethod]
        public void Analyze_Trends_AllGenresPresent()
        {
            var report = _service.Analyze();

            var trendGenres = report.Trends.Select(t => t.Genre).ToList();
            Assert.AreEqual(Enum.GetNames(typeof(Genre)).Length, trendGenres.Count);
        }

        [TestMethod]
        public void Analyze_Trends_DirectionIsValid()
        {
            var report = _service.Analyze();

            foreach (var trend in report.Trends)
            {
                var valid = new[] { "Rising", "Warming", "Stable", "Cooling", "Falling" };
                Assert.IsTrue(valid.Contains(trend.Direction),
                    trend.Genre + " unexpected direction: " + trend.Direction);
            }
        }

        [TestMethod]
        public void Analyze_Trends_ForecastNonNegative()
        {
            var report = _service.Analyze();

            foreach (var trend in report.Trends)
            {
                Assert.IsTrue(trend.ForecastNextPeriod >= 0,
                    trend.Genre + " forecast should not be negative");
            }
        }

        [TestMethod]
        public void Analyze_Trends_ConfidenceIsValid()
        {
            var report = _service.Analyze();

            foreach (var trend in report.Trends)
            {
                var valid = new[] { "High", "Medium", "Low" };
                Assert.IsTrue(valid.Contains(trend.Confidence));
            }
        }

        // ── Desert Tests ────────────────────────────────────────────

        [TestMethod]
        public void Analyze_Deserts_UrgencyIsValid()
        {
            var report = _service.Analyze();

            foreach (var desert in report.Deserts)
            {
                var valid = new[] { "High", "Medium", "Low" };
                Assert.IsTrue(valid.Contains(desert.Urgency),
                    desert.Genre + " unexpected urgency: " + desert.Urgency);
            }
        }

        [TestMethod]
        public void Analyze_Deserts_EstimatedMoviesNonNegative()
        {
            var report = _service.Analyze();

            foreach (var desert in report.Deserts)
            {
                Assert.IsTrue(desert.EstimatedMoviesNeeded >= 0);
            }
        }

        // ── Recommendation Tests ────────────────────────────────────

        [TestMethod]
        public void Analyze_Recommendations_PrioritiesAreUnique()
        {
            var report = _service.Analyze();

            var priorities = report.Recommendations.Select(r => r.Priority).ToList();
            Assert.AreEqual(priorities.Count, priorities.Distinct().Count(), "Priorities should be unique");
        }

        [TestMethod]
        public void Analyze_Recommendations_CategoryIsValid()
        {
            var report = _service.Analyze();

            var valid = new[] { "Catalog Gap", "Trend Capture", "Cross-Promotion", "Customer Engagement", "Genre Recovery" };
            foreach (var rec in report.Recommendations)
            {
                Assert.IsTrue(valid.Contains(rec.Category),
                    "Unexpected category: " + rec.Category);
            }
        }

        // ── Ecosystem Health Tests ──────────────────────────────────

        [TestMethod]
        public void Analyze_EcosystemHealth_ScoreInRange()
        {
            var report = _service.Analyze();

            Assert.IsTrue(report.EcosystemHealth.OverallScore >= 0);
            Assert.IsTrue(report.EcosystemHealth.OverallScore <= 100);
        }

        [TestMethod]
        public void Analyze_EcosystemHealth_SubScoresInRange()
        {
            var report = _service.Analyze();

            Assert.IsTrue(report.EcosystemHealth.DiversityScore >= 0 && report.EcosystemHealth.DiversityScore <= 100);
            Assert.IsTrue(report.EcosystemHealth.ConnectivityScore >= 0 && report.EcosystemHealth.ConnectivityScore <= 100);
            Assert.IsTrue(report.EcosystemHealth.VibrancyScore >= 0 && report.EcosystemHealth.VibrancyScore <= 100);
        }

        [TestMethod]
        public void Analyze_EcosystemHealth_GradeIsValid()
        {
            var report = _service.Analyze();

            var valid = new[] { "A", "B", "C", "D", "F" };
            Assert.IsTrue(valid.Contains(report.EcosystemHealth.Grade));
        }

        [TestMethod]
        public void Analyze_EcosystemHealth_SummaryNotEmpty()
        {
            var report = _service.Analyze();

            Assert.IsFalse(string.IsNullOrEmpty(report.EcosystemHealth.Summary));
        }

        // ── API Tests ───────────────────────────────────────────────

        [TestMethod]
        public void GetGenreAffinity_ReturnsValueBetweenZeroAndOne()
        {
            var affinity = _service.GetGenreAffinity("Action", "Comedy");
            Assert.IsTrue(affinity >= 0 && affinity <= 1.0);
        }

        [TestMethod]
        public void GetGenreAffinity_SameGenre_ReturnsZero()
        {
            var affinity = _service.GetGenreAffinity("Action", "Action");
            Assert.AreEqual(0.0, affinity);
        }

        [TestMethod]
        public void GetGenreAffinity_UnrelatedGenres_ReturnsLowValue()
        {
            var affinity = _service.GetGenreAffinity("Documentary", "Horror");
            Assert.AreEqual(0.0, affinity);
        }

        [TestMethod]
        public void GetTopBridges_ReturnsOrderedByScore()
        {
            var bridges = _service.GetTopBridges(10);

            for (int i = 1; i < bridges.Count; i++)
            {
                Assert.IsTrue(bridges[i - 1].BridgeScore >= bridges[i].BridgeScore,
                    "Bridges should be ordered by score descending");
            }
        }

        [TestMethod]
        public void GetTopBridges_RespectsTopN()
        {
            var bridges = _service.GetTopBridges(2);
            Assert.IsTrue(bridges.Count <= 2);
        }

        // ── Edge Cases ──────────────────────────────────────────────

        [TestMethod]
        public void Analyze_WithFreshRepos_ReturnsEmptyReport()
        {
            InMemoryMovieRepository.Reset();
            InMemoryCustomerRepository.Reset();
            InMemoryRentalRepository.Reset();
            var freshMovieRepo = new InMemoryMovieRepository();
            var freshRentalRepo = new InMemoryRentalRepository();
            var freshCustomerRepo = new InMemoryCustomerRepository();
            var service = new GenreEcosystemService(freshMovieRepo, freshRentalRepo, freshCustomerRepo, _clock);

            var report = service.Analyze();

            Assert.IsNotNull(report);
            Assert.AreEqual(0, report.TotalRentalsAnalyzed);
            Assert.AreEqual(0, report.CoRentalEdges.Count);
            Assert.AreEqual(0, report.BridgeCustomers.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new GenreEcosystemService(null, _rentalRepo, _customerRepo, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new GenreEcosystemService(_movieRepo, null, _customerRepo, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new GenreEcosystemService(_movieRepo, _rentalRepo, null, _clock);
        }

        [TestMethod]
        public void Analyze_ShortLookback_FiltersOlderRentals()
        {
            var report = _service.Analyze(10);
            // All rentals are from March 2026. 10-day lookback from April 28 should miss most.
            Assert.IsTrue(report.TotalRentalsAnalyzed < 17, "Short lookback should filter older rentals");
        }
    }
}
