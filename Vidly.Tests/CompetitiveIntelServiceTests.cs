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
    public class CompetitiveIntelServiceTests
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

        private CompetitiveIntelService CreateService()
        {
            return new CompetitiveIntelService(_rentalRepo, _movieRepo, _customerRepo, _clock);
        }

        private CompetitiveIntelService CreateServiceWithClock(TestClock clock)
        {
            return new CompetitiveIntelService(_rentalRepo, _movieRepo, _customerRepo, clock);
        }

        private Movie AddMovie(string name, Genre genre, DateTime? release = null)
        {
            var m = new Movie { Name = name, Genre = genre, ReleaseDate = release };
            _movieRepo.Add(m);
            // Add mutates m.Id in place
            return m;
        }

        private void AddRental(int movieId, DateTime rentalDate, decimal dailyRate = 3.99m, int custId = 1)
        {
            var r = new Rental
            {
                MovieId = movieId,
                CustomerId = custId,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(7),
                ReturnDate = rentalDate.AddDays(5),
                DailyRate = dailyRate,
                Status = RentalStatus.Returned
            };
            _rentalRepo.Add(r);
        }

        // ------------------------------------------------------------------
        //  Constructor Validation
        // ------------------------------------------------------------------

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new CompetitiveIntelService(null, _movieRepo, _customerRepo, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new CompetitiveIntelService(_rentalRepo, null, _customerRepo, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new CompetitiveIntelService(_rentalRepo, _movieRepo, null, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullClock_Throws()
        {
            new CompetitiveIntelService(_rentalRepo, _movieRepo, _customerRepo, null);
        }

        // ------------------------------------------------------------------
        //  Dashboard
        // ------------------------------------------------------------------

        [TestMethod]
        public void GetDashboard_ReturnsAllSections()
        {
            AddMovie("Action Hero", Genre.Action);

            var svc = CreateService();
            var dash = svc.GetDashboard();

            Assert.IsNotNull(dash.PositionMap);
            Assert.IsNotNull(dash.Opportunities);
            Assert.IsNotNull(dash.Threats);
            Assert.IsNotNull(dash.Recommendations);
            Assert.IsNotNull(dash.Benchmarks);
            Assert.IsNotNull(dash.HealthScore);
            Assert.IsNotNull(dash.AutonomousInsights);
        }

        [TestMethod]
        public void GetDashboard_EmptyRepo_ReturnsNonNull()
        {
            var svc = CreateService();
            var dash = svc.GetDashboard();

            Assert.IsNotNull(dash);
            Assert.IsNotNull(dash.PositionMap);
        }

        // ------------------------------------------------------------------
        //  Benchmark Generation
        // ------------------------------------------------------------------

        [TestMethod]
        public void GenerateBenchmarks_Creates4CompetitorsPerGenre()
        {
            AddMovie("Film A", Genre.Action);
            AddMovie("Film B", Genre.Comedy);

            var svc = CreateService();
            var benchmarks = svc.GenerateBenchmarks();

            var actionBm = benchmarks.Where(b => b.Genre == Genre.Action).ToList();
            Assert.AreEqual(4, actionBm.Count);

            var comedyBm = benchmarks.Where(b => b.Genre == Genre.Comedy).ToList();
            Assert.AreEqual(4, comedyBm.Count);
        }

        [TestMethod]
        public void GenerateBenchmarks_SeedData_ProducesBenchmarks()
        {
            var svc = CreateService();
            var benchmarks = svc.GenerateBenchmarks();
            Assert.IsTrue(benchmarks.Count >= 4, "Seed data should produce benchmarks.");
        }

        [TestMethod]
        public void GenerateBenchmarks_CompetitorNamesAreKnown()
        {
            AddMovie("X", Genre.Action);
            var svc = CreateService();
            var names = svc.GenerateBenchmarks().Select(b => b.CompetitorName).Distinct().ToList();

            CollectionAssert.Contains(names, "StreamFlix");
            CollectionAssert.Contains(names, "MovieVault");
            CollectionAssert.Contains(names, "CineRent");
            CollectionAssert.Contains(names, "QuickFlicks");
        }

        [TestMethod]
        public void GenerateBenchmarks_RatesVaryByCompetitor()
        {
            AddMovie("X", Genre.Action);
            AddRental(1, new DateTime(2026, 4, 1), 5.00m);

            var svc = CreateService();
            var rates = svc.GenerateBenchmarks()
                .Where(b => b.Genre == Genre.Action)
                .Select(b => b.AvgDailyRate)
                .Distinct().ToList();

            Assert.IsTrue(rates.Count > 1, "Different competitors should have different rates.");
        }

        [TestMethod]
        public void GenerateBenchmarks_SatisfactionWithinRange()
        {
            AddMovie("X", Genre.Action);
            var svc = CreateService();
            foreach (var bm in svc.GenerateBenchmarks())
            {
                Assert.IsTrue(bm.CustomerSatisfaction >= 0 && bm.CustomerSatisfaction <= 5);
            }
        }

        // ------------------------------------------------------------------
        //  Position Analysis
        // ------------------------------------------------------------------

        [TestMethod]
        public void AnalyzePosition_ReturnsAssessmentsForEachGenre()
        {
            AddMovie("A1", Genre.Action);
            AddMovie("C1", Genre.Comedy);

            var svc = CreateService();
            var positions = svc.AnalyzePosition();

            Assert.IsTrue(positions.Count >= 2);
            Assert.IsTrue(positions.Any(p => p.Genre == Genre.Action));
            Assert.IsTrue(positions.Any(p => p.Genre == Genre.Comedy));
        }

        [TestMethod]
        public void AnalyzePosition_IncludesAssessmentText()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();

            foreach (var pos in svc.AnalyzePosition())
            {
                Assert.IsFalse(string.IsNullOrEmpty(pos.Assessment));
            }
        }

        [TestMethod]
        public void AnalyzePosition_MarketAvgPriceIsPositive()
        {
            AddMovie("A1", Genre.Action);
            AddRental(1, new DateTime(2026, 4, 1), 4.00m);

            var svc = CreateService();
            var action = svc.AnalyzePosition().FirstOrDefault(p => p.Genre == Genre.Action);

            Assert.IsNotNull(action);
            Assert.IsTrue(action.MarketAvgPrice > 0);
        }

        [TestMethod]
        public void AnalyzePosition_MarketPositionIsValid()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();

            foreach (var pos in svc.AnalyzePosition())
            {
                Assert.IsTrue(Enum.IsDefined(typeof(MarketPosition), pos.Position));
            }
        }

        [TestMethod]
        public void AnalyzePosition_CatalogCountsArePopulated()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            var action = svc.AnalyzePosition().FirstOrDefault(p => p.Genre == Genre.Action);

            Assert.IsNotNull(action);
            Assert.IsTrue(action.OurCatalogCount > 0);
            Assert.IsTrue(action.AvgCompetitorCatalogCount > 0);
        }

        // ------------------------------------------------------------------
        //  Opportunity Scanning
        // ------------------------------------------------------------------

        [TestMethod]
        public void ScanOpportunities_OrderedByRevenueImpact()
        {
            AddMovie("A1", Genre.Action);
            AddMovie("C1", Genre.Comedy);

            var svc = CreateService();
            var opps = svc.ScanOpportunities();

            for (int i = 1; i < opps.Count; i++)
            {
                Assert.IsTrue(opps[i - 1].EstimatedRevenueImpact >= opps[i].EstimatedRevenueImpact);
            }
        }

        [TestMethod]
        public void ScanOpportunities_SeasonalWindow_InWinter()
        {
            var winterClock = new TestClock(new DateTime(2026, 1, 15));
            var svc = CreateServiceWithClock(winterClock);
            AddMovie("D1", Genre.Drama);

            var opps = svc.ScanOpportunities();
            Assert.IsTrue(opps.Any(o => o.Type == OpportunityType.SeasonalWindow),
                "Winter season should trigger seasonal window.");
        }

        [TestMethod]
        public void ScanOpportunities_SeasonalWindow_InSummer()
        {
            var summerClock = new TestClock(new DateTime(2026, 7, 15));
            var svc = CreateServiceWithClock(summerClock);
            AddMovie("A1", Genre.Action);

            var opps = svc.ScanOpportunities();
            Assert.IsTrue(opps.Any(o => o.Type == OpportunityType.SeasonalWindow),
                "Summer should trigger seasonal window.");
        }

        [TestMethod]
        public void ScanOpportunities_SeasonalWindow_InFall()
        {
            var fallClock = new TestClock(new DateTime(2026, 10, 15));
            var svc = CreateServiceWithClock(fallClock);
            AddMovie("H1", Genre.Horror);

            var opps = svc.ScanOpportunities();
            Assert.IsTrue(opps.Any(o => o.Type == OpportunityType.SeasonalWindow),
                "October should trigger seasonal window.");
        }

        [TestMethod]
        public void ScanOpportunities_DemandSurge_OnVelocitySpike()
        {
            var m = AddMovie("Hot Film", Genre.Horror);
            for (int i = 0; i < 5; i++)
                AddRental(m.Id, new DateTime(2026, 4, 15).AddDays(i));
            AddRental(m.Id, new DateTime(2026, 3, 15));

            var svc = CreateService();
            var opps = svc.ScanOpportunities();
            Assert.IsTrue(opps.Any(o => o.Type == OpportunityType.DemandSurge && o.Genre == Genre.Horror));
        }

        [TestMethod]
        public void ScanOpportunities_CompetitorWeakness_Detected()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            var opps = svc.ScanOpportunities();
            Assert.IsTrue(opps.Any(o => o.Type == OpportunityType.CompetitorWeakness),
                "QuickFlicks (satisfaction 3.3) should trigger competitor weakness.");
        }

        [TestMethod]
        public void ScanOpportunities_AllHaveValidConfidence()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            foreach (var opp in svc.ScanOpportunities())
            {
                Assert.IsTrue(opp.ConfidencePercent >= 0 && opp.ConfidencePercent <= 100);
            }
        }

        [TestMethod]
        public void ScanOpportunities_AllHaveTitle()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            foreach (var opp in svc.ScanOpportunities())
            {
                Assert.IsFalse(string.IsNullOrEmpty(opp.Title));
            }
        }

        // ------------------------------------------------------------------
        //  Threat Detection
        // ------------------------------------------------------------------

        [TestMethod]
        public void DetectThreats_OrderedBySeverity()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            var threats = svc.DetectThreats();

            for (int i = 1; i < threats.Count; i++)
            {
                Assert.IsTrue(threats[i - 1].Level >= threats[i].Level);
            }
        }

        [TestMethod]
        public void DetectThreats_HighSatisfactionCompetitor_Detected()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            var threats = svc.DetectThreats();
            Assert.IsTrue(threats.Any(t => t.Source == "StreamFlix"),
                "StreamFlix (satisfaction 4.1) should be detected as quality threat.");
        }

        [TestMethod]
        public void DetectThreats_AllHaveCounterMoves()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            foreach (var threat in svc.DetectThreats())
            {
                Assert.IsNotNull(threat.CounterMoves);
                Assert.IsTrue(threat.CounterMoves.Count > 0);
            }
        }

        [TestMethod]
        public void DetectThreats_AllHaveUrgency()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            foreach (var threat in svc.DetectThreats())
            {
                Assert.IsFalse(string.IsNullOrEmpty(threat.Urgency));
            }
        }

        [TestMethod]
        public void DetectThreats_ExpensivePricing_GeneratesPriceThreat()
        {
            var m = AddMovie("Pricey", Genre.SciFi);
            AddRental(m.Id, new DateTime(2026, 4, 1), 20.00m);

            var svc = CreateService();
            var threats = svc.DetectThreats();
            Assert.IsTrue(threats.Any(t => t.Source == "Market pricing"),
                "Overpriced genre should trigger price threat.");
        }

        // ------------------------------------------------------------------
        //  Recommendations
        // ------------------------------------------------------------------

        [TestMethod]
        public void GetRecommendations_ReturnsNonEmpty()
        {
            AddMovie("A1", Genre.Action);
            AddMovie("C1", Genre.Comedy);
            var svc = CreateService();
            Assert.IsTrue(svc.GetRecommendations().Count > 0);
        }

        [TestMethod]
        public void GetRecommendations_OrderedByExpectedRevenue()
        {
            AddMovie("A1", Genre.Action);
            AddMovie("C1", Genre.Comedy);
            var svc = CreateService();
            var recs = svc.GetRecommendations();

            for (int i = 1; i < recs.Count; i++)
            {
                Assert.IsTrue(recs[i - 1].ExpectedRevenueChange >= recs[i].ExpectedRevenueChange);
            }
        }

        [TestMethod]
        public void GetRecommendations_IncludeImplementation()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            foreach (var rec in svc.GetRecommendations())
            {
                Assert.IsFalse(string.IsNullOrEmpty(rec.Implementation));
            }
        }

        [TestMethod]
        public void GetRecommendations_ValidMoves()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            foreach (var rec in svc.GetRecommendations())
            {
                Assert.IsTrue(Enum.IsDefined(typeof(StrategicMove), rec.Move));
            }
        }

        // ------------------------------------------------------------------
        //  Health Scoring
        // ------------------------------------------------------------------

        [TestMethod]
        public void GetHealthScore_AllDimensionsInRange()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            var health = svc.GetHealthScore();

            Assert.IsTrue(health.Overall >= 0 && health.Overall <= 100);
            Assert.IsTrue(health.PricingStrength >= 0 && health.PricingStrength <= 100);
            Assert.IsTrue(health.CatalogCoverage >= 0 && health.CatalogCoverage <= 100);
            Assert.IsTrue(health.OpportunityCapture >= 0 && health.OpportunityCapture <= 100);
            Assert.IsTrue(health.ThreatResilience >= 0 && health.ThreatResilience <= 100);
        }

        [TestMethod]
        public void GetHealthScore_GradeIsAssigned()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            var health = svc.GetHealthScore();

            Assert.IsFalse(string.IsNullOrEmpty(health.Grade));
        }

        [TestMethod]
        public void GetHealthScore_GradeFormatIsValid()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            var health = svc.GetHealthScore();

            var validGrades = new HashSet<string>
            {
                "A+", "A", "A-", "B+", "B", "B-",
                "C+", "C", "C-", "D+", "D", "D-", "F"
            };
            Assert.IsTrue(validGrades.Contains(health.Grade),
                $"Grade '{health.Grade}' is not a valid letter grade.");
        }

        // ------------------------------------------------------------------
        //  Insights
        // ------------------------------------------------------------------

        [TestMethod]
        public void Dashboard_InsightsGenerated()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            var dash = svc.GetDashboard();

            Assert.IsTrue(dash.AutonomousInsights.Count >= 3,
                "Should generate at least 3 insights.");
        }

        [TestMethod]
        public void Dashboard_InsightsContainHealthScore()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            var dash = svc.GetDashboard();

            Assert.IsTrue(dash.AutonomousInsights.Any(i => i.Contains("health score")));
        }

        [TestMethod]
        public void Dashboard_InsightsContainPositionInfo()
        {
            AddMovie("A1", Genre.Action);
            var svc = CreateService();
            var dash = svc.GetDashboard();

            Assert.IsTrue(dash.AutonomousInsights.Any(
                i => i.Contains("position") || i.Contains("genre")));
        }

        // ------------------------------------------------------------------
        //  Edge Cases
        // ------------------------------------------------------------------

        [TestMethod]
        public void AnalyzePosition_NoRentals_UsesDefaultRate()
        {
            AddMovie("A1", Genre.SciFi);
            var svc = CreateService();
            var scifi = svc.AnalyzePosition().FirstOrDefault(p => p.Genre == Genre.SciFi);

            Assert.IsNotNull(scifi);
            Assert.IsTrue(scifi.OurAvgPrice > 0, "Should use default rate when no rentals.");
        }

        [TestMethod]
        public void FullPipeline_IntegrationTest()
        {
            var m1 = AddMovie("Action A", Genre.Action);
            var m2 = AddMovie("Comedy B", Genre.Comedy);
            var m3 = AddMovie("Drama C", Genre.Drama);

            AddRental(m1.Id, new DateTime(2026, 4, 10), 4.50m);
            AddRental(m2.Id, new DateTime(2026, 4, 12), 3.00m);
            AddRental(m3.Id, new DateTime(2026, 4, 15), 5.00m);

            var svc = CreateService();
            var dash = svc.GetDashboard();

            Assert.IsTrue(dash.PositionMap.Count > 0);
            Assert.IsTrue(dash.Benchmarks.Count > 0);
            Assert.IsTrue(dash.HealthScore.Overall >= 0);
            Assert.IsTrue(dash.AutonomousInsights.Count >= 3);
        }
    }
}
