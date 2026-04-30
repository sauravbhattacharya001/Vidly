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
    public class FrictionDetectorServiceTests
    {
        private InMemoryRentalRepository _rentals;
        private InMemoryMovieRepository _movies;
        private InMemoryCustomerRepository _customers;
        private TestClock _clock;
        private FrictionDetectorService _service;

        [TestInitialize]
        public void Setup()
        {
            _rentals = new InMemoryRentalRepository();
            _movies = new InMemoryMovieRepository();
            _customers = new InMemoryCustomerRepository();
            _clock = new TestClock(new DateTime(2025, 6, 1));
            _service = new FrictionDetectorService(_rentals, _movies, _customers, _clock);
        }

        // ── Constructor Tests ────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new FrictionDetectorService(null, _movies, _customers, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new FrictionDetectorService(_rentals, null, _customers, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new FrictionDetectorService(_rentals, _movies, null, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullClock_Throws()
        {
            new FrictionDetectorService(_rentals, _movies, _customers, null);
        }

        // ── No Data Tests ────────────────────────────────────────────

        [TestMethod]
        public void GenerateReport_NoCustomers_ReturnsEmptyReport()
        {
            var report = _service.GenerateReport();

            Assert.IsNotNull(report);
            Assert.AreEqual(0, report.TotalCustomersAnalyzed);
            Assert.AreEqual(0, report.CustomersWithFriction);
            Assert.AreEqual(100.0, report.StoreHealthScore);
        }

        [TestMethod]
        public void AnalyzeCustomer_NonExistent_ReturnsEmptyProfile()
        {
            var profile = _service.AnalyzeCustomer(999);

            Assert.IsNotNull(profile);
            Assert.AreEqual(0, profile.OverallFrictionScore);
            Assert.AreEqual("Low", profile.RiskLevel);
            Assert.AreEqual(0, profile.FrictionPoints.Count);
        }

        [TestMethod]
        public void AnalyzeCustomer_NoRentals_ZeroFriction()
        {
            _customers.Add(new Customer { Id = 1, Name = "Alice", MembershipType = MembershipType.Basic });

            var profile = _service.AnalyzeCustomer(1);

            Assert.AreEqual(0, profile.OverallFrictionScore);
            Assert.AreEqual(0, profile.FrictionPoints.Count);
        }

        // ── New Customer Drop Detection ──────────────────────────────

        [TestMethod]
        public void DetectsNewCustomerDrop_SingleRentalLongAgo()
        {
            _customers.Add(new Customer { Id = 1, Name = "Bob", MembershipType = MembershipType.Basic });
            _movies.Add(new Movie { Id = 1, Name = "Movie A", Genre = Genre.Action });
            _rentals.Add(new Rental
            {
                Id = 1, CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2025, 3, 1),
                DueDate = new DateTime(2025, 3, 8),
                ReturnDate = new DateTime(2025, 3, 7),
                DailyRate = 3.99m, Status = RentalStatus.Returned
            });

            var profile = _service.AnalyzeCustomer(1);

            var dropPoint = profile.FrictionPoints
                .FirstOrDefault(fp => fp.Category == FrictionCategory.NewCustomerDrop);
            Assert.IsNotNull(dropPoint, "Should detect new customer drop");
            Assert.IsTrue(dropPoint.Score > 0);
        }

        [TestMethod]
        public void NoNewCustomerDrop_ActiveNewCustomer()
        {
            _customers.Add(new Customer { Id = 1, Name = "Charlie", MembershipType = MembershipType.Basic });
            _movies.Add(new Movie { Id = 1, Name = "Movie A", Genre = Genre.Comedy });
            // Recent rental - within 30 days
            _rentals.Add(new Rental
            {
                Id = 1, CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2025, 5, 20),
                DueDate = new DateTime(2025, 5, 27),
                ReturnDate = new DateTime(2025, 5, 25),
                DailyRate = 3.99m, Status = RentalStatus.Returned
            });

            var profile = _service.AnalyzeCustomer(1);

            var dropPoint = profile.FrictionPoints
                .FirstOrDefault(fp => fp.Category == FrictionCategory.NewCustomerDrop);
            Assert.IsNull(dropPoint, "Active new customer should not trigger drop detection");
        }

        // ── Genre Lock Detection ─────────────────────────────────────

        [TestMethod]
        public void DetectsGenreLock_AllSameGenre()
        {
            _customers.Add(new Customer { Id = 1, Name = "Dana", MembershipType = MembershipType.Silver });
            for (int i = 1; i <= 6; i++)
            {
                _movies.Add(new Movie { Id = i, Name = "Action " + i, Genre = Genre.Action });
            }
            for (int i = 1; i <= 6; i++)
            {
                _rentals.Add(new Rental
                {
                    Id = i, CustomerId = 1, MovieId = i,
                    RentalDate = new DateTime(2025, 1, 1).AddDays(i * 7),
                    DueDate = new DateTime(2025, 1, 1).AddDays(i * 7 + 7),
                    ReturnDate = new DateTime(2025, 1, 1).AddDays(i * 7 + 5),
                    DailyRate = 3.99m, Status = RentalStatus.Returned
                });
            }

            var profile = _service.AnalyzeCustomer(1);

            var genreLock = profile.FrictionPoints
                .FirstOrDefault(fp => fp.Category == FrictionCategory.GenreLock);
            Assert.IsNotNull(genreLock, "Should detect genre lock");
            Assert.IsTrue(genreLock.Score > 0);
        }

        [TestMethod]
        public void NoGenreLock_DiverseGenres()
        {
            _customers.Add(new Customer { Id = 1, Name = "Eve", MembershipType = MembershipType.Basic });
            var genres = new[] { Genre.Action, Genre.Comedy, Genre.Drama, Genre.Horror, Genre.SciFi };
            for (int i = 1; i <= 5; i++)
            {
                _movies.Add(new Movie { Id = i, Name = "Movie " + i, Genre = genres[i - 1] });
                _rentals.Add(new Rental
                {
                    Id = i, CustomerId = 1, MovieId = i,
                    RentalDate = new DateTime(2025, 1, 1).AddDays(i * 7),
                    DueDate = new DateTime(2025, 1, 1).AddDays(i * 7 + 7),
                    ReturnDate = new DateTime(2025, 1, 1).AddDays(i * 7 + 5),
                    DailyRate = 3.99m, Status = RentalStatus.Returned
                });
            }

            var profile = _service.AnalyzeCustomer(1);

            var genreLock = profile.FrictionPoints
                .FirstOrDefault(fp => fp.Category == FrictionCategory.GenreLock);
            Assert.IsNull(genreLock, "Diverse genres should not trigger genre lock");
        }

        // ── Overdue Pattern Detection ────────────────────────────────

        [TestMethod]
        public void DetectsOverduePattern_MultipleOverdueReturns()
        {
            _customers.Add(new Customer { Id = 1, Name = "Frank", MembershipType = MembershipType.Basic });
            _movies.Add(new Movie { Id = 1, Name = "Movie A", Genre = Genre.Drama });

            for (int i = 1; i <= 5; i++)
            {
                _rentals.Add(new Rental
                {
                    Id = i, CustomerId = 1, MovieId = 1,
                    RentalDate = new DateTime(2025, 1, 1).AddDays(i * 14),
                    DueDate = new DateTime(2025, 1, 1).AddDays(i * 14 + 7),
                    ReturnDate = new DateTime(2025, 1, 1).AddDays(i * 14 + 12), // 5 days late
                    DailyRate = 3.99m, Status = RentalStatus.Returned
                });
            }

            var profile = _service.AnalyzeCustomer(1);

            var overduePoint = profile.FrictionPoints
                .FirstOrDefault(fp => fp.Category == FrictionCategory.Overdue);
            Assert.IsNotNull(overduePoint, "Should detect overdue pattern");
        }

        // ── Frequency Gap Detection ──────────────────────────────────

        [TestMethod]
        public void DetectsFrequencyGap_LongInactivity()
        {
            _customers.Add(new Customer { Id = 1, Name = "Grace", MembershipType = MembershipType.Gold });
            _movies.Add(new Movie { Id = 1, Name = "Movie A", Genre = Genre.Comedy });

            // Regular weekly rentals then big gap
            for (int i = 1; i <= 5; i++)
            {
                _rentals.Add(new Rental
                {
                    Id = i, CustomerId = 1, MovieId = 1,
                    RentalDate = new DateTime(2025, 1, 1).AddDays(i * 7),
                    DueDate = new DateTime(2025, 1, 1).AddDays(i * 7 + 7),
                    ReturnDate = new DateTime(2025, 1, 1).AddDays(i * 7 + 5),
                    DailyRate = 3.99m, Status = RentalStatus.Returned
                });
            }
            // Last rental was Feb 5, clock is June 1 => ~116 day gap vs 7 day avg

            var profile = _service.AnalyzeCustomer(1);

            var freqPoint = profile.FrictionPoints
                .FirstOrDefault(fp => fp.Category == FrictionCategory.Frequency);
            Assert.IsNotNull(freqPoint, "Should detect frequency gap");
            Assert.IsTrue(freqPoint.Score > 50, "Large gap should produce high score");
        }

        // ── Pricing Friction Detection ───────────────────────────────

        [TestMethod]
        public void DetectsPriceShock_RecentPriceIncrease()
        {
            _customers.Add(new Customer { Id = 1, Name = "Henry", MembershipType = MembershipType.Basic });
            _movies.Add(new Movie { Id = 1, Name = "Cheap Movie", Genre = Genre.Action });
            _movies.Add(new Movie { Id = 2, Name = "Expensive Movie", Genre = Genre.Action });

            // Older rentals at low price
            for (int i = 1; i <= 4; i++)
            {
                _rentals.Add(new Rental
                {
                    Id = i, CustomerId = 1, MovieId = 1,
                    RentalDate = new DateTime(2025, 1, 1).AddDays(i * 7),
                    DueDate = new DateTime(2025, 1, 1).AddDays(i * 7 + 7),
                    ReturnDate = new DateTime(2025, 1, 1).AddDays(i * 7 + 5),
                    DailyRate = 2.00m, Status = RentalStatus.Returned
                });
            }
            // Recent rentals at high price
            for (int i = 5; i <= 8; i++)
            {
                _rentals.Add(new Rental
                {
                    Id = i, CustomerId = 1, MovieId = 2,
                    RentalDate = new DateTime(2025, 3, 1).AddDays((i - 5) * 7),
                    DueDate = new DateTime(2025, 3, 1).AddDays((i - 5) * 7 + 7),
                    ReturnDate = new DateTime(2025, 3, 1).AddDays((i - 5) * 7 + 5),
                    DailyRate = 5.99m, Status = RentalStatus.Returned
                });
            }

            var profile = _service.AnalyzeCustomer(1);

            var pricePoint = profile.FrictionPoints
                .FirstOrDefault(fp => fp.Category == FrictionCategory.Pricing);
            Assert.IsNotNull(pricePoint, "Should detect price shock");
        }

        // ── Return Delay Detection ───────────────────────────────────

        [TestMethod]
        public void DetectsReturnDelay_ConsistentlyLate()
        {
            _customers.Add(new Customer { Id = 1, Name = "Iris", MembershipType = MembershipType.Basic });
            _movies.Add(new Movie { Id = 1, Name = "Movie A", Genre = Genre.Thriller });

            for (int i = 1; i <= 5; i++)
            {
                _rentals.Add(new Rental
                {
                    Id = i, CustomerId = 1, MovieId = 1,
                    RentalDate = new DateTime(2025, 1, 1).AddDays(i * 14),
                    DueDate = new DateTime(2025, 1, 1).AddDays(i * 14 + 7),
                    ReturnDate = new DateTime(2025, 1, 1).AddDays(i * 14 + 11), // 4 days late
                    DailyRate = 3.99m, Status = RentalStatus.Returned
                });
            }

            var profile = _service.AnalyzeCustomer(1);

            var delayPoint = profile.FrictionPoints
                .FirstOrDefault(fp => fp.Category == FrictionCategory.ReturnDelay);
            Assert.IsNotNull(delayPoint, "Should detect consistent return delays");
            Assert.IsTrue(delayPoint.Evidence.Contains("4"), "Should mention avg delay");
        }

        // ── High Cost Abandonment ────────────────────────────────────

        [TestMethod]
        public void DetectsHighCostAbandonment_ExpensiveLastRental()
        {
            _customers.Add(new Customer { Id = 1, Name = "Jack", MembershipType = MembershipType.Basic });
            _movies.Add(new Movie { Id = 1, Name = "Cheap Movie", Genre = Genre.Comedy });
            _movies.Add(new Movie { Id = 2, Name = "Pricey Movie", Genre = Genre.SciFi });

            // Normal rentals
            for (int i = 1; i <= 4; i++)
            {
                _rentals.Add(new Rental
                {
                    Id = i, CustomerId = 1, MovieId = 1,
                    RentalDate = new DateTime(2025, 1, 1).AddDays(i * 7),
                    DueDate = new DateTime(2025, 1, 1).AddDays(i * 7 + 7),
                    ReturnDate = new DateTime(2025, 1, 1).AddDays(i * 7 + 5),
                    DailyRate = 2.00m, Status = RentalStatus.Returned
                });
            }
            // Expensive last rental (high daily rate + returned late)
            _rentals.Add(new Rental
            {
                Id = 5, CustomerId = 1, MovieId = 2,
                RentalDate = new DateTime(2025, 2, 15),
                DueDate = new DateTime(2025, 2, 22),
                ReturnDate = new DateTime(2025, 3, 1), // 7 days late
                DailyRate = 7.99m, LateFee = 10.00m, Status = RentalStatus.Returned
            });

            var profile = _service.AnalyzeCustomer(1);

            var costPoint = profile.FrictionPoints
                .FirstOrDefault(fp => fp.Category == FrictionCategory.HighCostAbandonment);
            Assert.IsNotNull(costPoint, "Should detect high cost abandonment");
        }

        // ── Availability Friction ────────────────────────────────────

        [TestMethod]
        public void DetectsAvailabilityFriction_OverdueHolding()
        {
            _customers.Add(new Customer { Id = 1, Name = "Kate", MembershipType = MembershipType.Basic });
            _movies.Add(new Movie { Id = 1, Name = "Popular Movie", Genre = Genre.Action });

            _rentals.Add(new Rental
            {
                Id = 1, CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2025, 5, 1),
                DueDate = new DateTime(2025, 5, 8),
                DailyRate = 3.99m, Status = RentalStatus.Active // overdue since clock is June 1
            });

            var profile = _service.AnalyzeCustomer(1);

            var availPoint = profile.FrictionPoints
                .FirstOrDefault(fp => fp.Category == FrictionCategory.Availability);
            Assert.IsNotNull(availPoint, "Should detect availability friction from overdue holding");
        }

        // ── Report & Heatmap Tests ───────────────────────────────────

        [TestMethod]
        public void GenerateReport_WithFrictionCustomers_ReturnsValidReport()
        {
            SetupMultipleCustomersWithFriction();

            var report = _service.GenerateReport();

            Assert.IsNotNull(report);
            Assert.IsTrue(report.TotalCustomersAnalyzed > 0);
            Assert.IsTrue(report.CustomersWithFriction > 0);
            Assert.IsTrue(report.StoreHealthScore >= 0 && report.StoreHealthScore <= 100);
            Assert.IsNotNull(report.Heatmap);
            Assert.IsNotNull(report.TopFrictionCustomers);
            Assert.IsNotNull(report.StoreWideRecommendations);
            Assert.IsNotNull(report.Insights);
        }

        [TestMethod]
        public void GetHeatmap_ReturnsCategories()
        {
            SetupMultipleCustomersWithFriction();

            var heatmap = _service.GetHeatmap();

            Assert.IsNotNull(heatmap);
            Assert.IsTrue(heatmap.CategoryCounts.Count > 0);
            Assert.IsTrue(heatmap.StoreWideFrictionIndex > 0);
        }

        [TestMethod]
        public void GetTrends_ReturnsCorrectPeriodCount()
        {
            SetupMultipleCustomersWithFriction();

            var trends = _service.GetTrends(7, 4);

            Assert.AreEqual(4, trends.Count);
            foreach (var trend in trends)
            {
                Assert.IsTrue((trend.PeriodEnd - trend.PeriodStart).TotalDays == 7);
            }
        }

        [TestMethod]
        public void Recommendations_GeneratedForEachFrictionPoint()
        {
            _customers.Add(new Customer { Id = 1, Name = "Reco", MembershipType = MembershipType.Basic });
            _movies.Add(new Movie { Id = 1, Name = "Movie A", Genre = Genre.Action });

            // Create new customer drop scenario
            _rentals.Add(new Rental
            {
                Id = 1, CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2025, 3, 1),
                DueDate = new DateTime(2025, 3, 8),
                ReturnDate = new DateTime(2025, 3, 7),
                DailyRate = 3.99m, Status = RentalStatus.Returned
            });

            var profile = _service.AnalyzeCustomer(1);

            if (profile.FrictionPoints.Count > 0)
            {
                Assert.IsTrue(profile.Recommendations.Count > 0,
                    "Should generate recommendations for friction points");
                Assert.IsTrue(profile.Recommendations.All(r => r.ExpectedImpact > 0),
                    "All recommendations should have positive expected impact");
            }
        }

        [TestMethod]
        public void SeverityClassification_CorrectThresholds()
        {
            _customers.Add(new Customer { Id = 1, Name = "Sev", MembershipType = MembershipType.Basic });
            _movies.Add(new Movie { Id = 1, Name = "Movie A", Genre = Genre.Action });

            // Create a scenario with very high friction (long gap)
            for (int i = 1; i <= 4; i++)
            {
                _rentals.Add(new Rental
                {
                    Id = i, CustomerId = 1, MovieId = 1,
                    RentalDate = new DateTime(2025, 1, 1).AddDays(i * 3),
                    DueDate = new DateTime(2025, 1, 1).AddDays(i * 3 + 7),
                    ReturnDate = new DateTime(2025, 1, 1).AddDays(i * 3 + 5),
                    DailyRate = 3.99m, Status = RentalStatus.Returned
                });
            }
            // Last rental Jan 13, clock is June 1 => massive gap vs 3-day average

            var profile = _service.AnalyzeCustomer(1);

            var freqPoint = profile.FrictionPoints
                .FirstOrDefault(fp => fp.Category == FrictionCategory.Frequency);
            if (freqPoint != null)
            {
                Assert.IsTrue(freqPoint.Severity == FrictionSeverity.Critical ||
                              freqPoint.Severity == FrictionSeverity.High,
                    "Very large frequency gap should have high/critical severity");
            }
        }

        [TestMethod]
        public void RiskLevel_CorrectClassification()
        {
            _customers.Add(new Customer { Id = 1, Name = "Risk", MembershipType = MembershipType.Basic });
            _movies.Add(new Movie { Id = 1, Name = "Movie A", Genre = Genre.Drama });

            // Many friction signals: overdue, late returns, big gap
            for (int i = 1; i <= 5; i++)
            {
                _rentals.Add(new Rental
                {
                    Id = i, CustomerId = 1, MovieId = 1,
                    RentalDate = new DateTime(2025, 1, 1).AddDays(i * 7),
                    DueDate = new DateTime(2025, 1, 1).AddDays(i * 7 + 3),
                    ReturnDate = new DateTime(2025, 1, 1).AddDays(i * 7 + 10), // 7 days late
                    DailyRate = 3.99m, Status = RentalStatus.Returned
                });
            }

            var profile = _service.AnalyzeCustomer(1);

            Assert.IsTrue(profile.OverallFrictionScore > 0);
            Assert.IsTrue(
                profile.RiskLevel == "Medium" ||
                profile.RiskLevel == "High" ||
                profile.RiskLevel == "Critical",
                "Customer with multiple friction signals should have elevated risk");
        }

        // ── Edge Cases ───────────────────────────────────────────────

        [TestMethod]
        public void NoFriction_HappyCustomer()
        {
            _customers.Add(new Customer { Id = 1, Name = "Happy", MembershipType = MembershipType.Gold });
            var genres = new[] { Genre.Action, Genre.Comedy, Genre.Drama, Genre.Horror, Genre.SciFi, Genre.Thriller };
            for (int i = 1; i <= 6; i++)
            {
                _movies.Add(new Movie { Id = i, Name = "Movie " + i, Genre = genres[i - 1] });
            }

            // Regular rentals, on time, diverse genres, recent activity
            for (int i = 1; i <= 6; i++)
            {
                _rentals.Add(new Rental
                {
                    Id = i, CustomerId = 1, MovieId = i,
                    RentalDate = new DateTime(2025, 4, 1).AddDays(i * 7),
                    DueDate = new DateTime(2025, 4, 1).AddDays(i * 7 + 7),
                    ReturnDate = new DateTime(2025, 4, 1).AddDays(i * 7 + 5),
                    DailyRate = 3.99m, Status = RentalStatus.Returned
                });
            }

            var profile = _service.AnalyzeCustomer(1);

            // Happy customer may have some minor friction but should be low
            Assert.IsTrue(profile.OverallFrictionScore < 50,
                "Happy customer should have low friction score");
        }

        [TestMethod]
        public void StoreHealthScore_InverseOfFriction()
        {
            _customers.Add(new Customer { Id = 1, Name = "Healthy", MembershipType = MembershipType.Basic });
            _movies.Add(new Movie { Id = 1, Name = "Movie A", Genre = Genre.Action });
            _rentals.Add(new Rental
            {
                Id = 1, CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2025, 5, 25),
                DueDate = new DateTime(2025, 6, 1),
                ReturnDate = new DateTime(2025, 5, 30),
                DailyRate = 3.99m, Status = RentalStatus.Returned
            });

            var report = _service.GenerateReport();

            // Store with minimal friction should have high health
            Assert.IsTrue(report.StoreHealthScore >= 50);
        }

        // ── Helper Methods ───────────────────────────────────────────

        private void SetupMultipleCustomersWithFriction()
        {
            _movies.Add(new Movie { Id = 1, Name = "Action Movie", Genre = Genre.Action });
            _movies.Add(new Movie { Id = 2, Name = "Comedy Movie", Genre = Genre.Comedy });

            // Customer 1: new customer drop (1 rental, long ago)
            _customers.Add(new Customer { Id = 1, Name = "Dropper", MembershipType = MembershipType.Basic });
            _rentals.Add(new Rental
            {
                Id = 1, CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2025, 2, 1),
                DueDate = new DateTime(2025, 2, 8),
                ReturnDate = new DateTime(2025, 2, 7),
                DailyRate = 3.99m, Status = RentalStatus.Returned
            });

            // Customer 2: overdue holder
            _customers.Add(new Customer { Id = 2, Name = "Holder", MembershipType = MembershipType.Silver });
            _rentals.Add(new Rental
            {
                Id = 2, CustomerId = 2, MovieId = 1,
                RentalDate = new DateTime(2025, 5, 1),
                DueDate = new DateTime(2025, 5, 8),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            // Customer 3: genre locked (all action)
            _customers.Add(new Customer { Id = 3, Name = "Locked", MembershipType = MembershipType.Basic });
            for (int i = 10; i <= 16; i++)
            {
                _rentals.Add(new Rental
                {
                    Id = i, CustomerId = 3, MovieId = 1,
                    RentalDate = new DateTime(2025, 1, 1).AddDays((i - 10) * 10),
                    DueDate = new DateTime(2025, 1, 1).AddDays((i - 10) * 10 + 7),
                    ReturnDate = new DateTime(2025, 1, 1).AddDays((i - 10) * 10 + 5),
                    DailyRate = 3.99m, Status = RentalStatus.Returned
                });
            }
        }
    }
}
