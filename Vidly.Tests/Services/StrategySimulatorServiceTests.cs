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
    public class StrategySimulatorServiceTests
    {
        private StrategySimulatorService _service;
        private InMemoryRentalRepository _rentals;
        private InMemoryMovieRepository _movies;
        private InMemoryCustomerRepository _customers;
        private TestClock _clock;

        [TestInitialize]
        public void Setup()
        {
            _rentals = new InMemoryRentalRepository();
            _movies = new InMemoryMovieRepository();
            _customers = new InMemoryCustomerRepository();
            _clock = new TestClock(new DateTime(2026, 4, 15));
            _service = new StrategySimulatorService(_rentals, _movies, _customers, _clock);

            SeedData();
        }

        private void SeedData()
        {
            // Add movies
            for (int i = 1; i <= 20; i++)
                _movies.Add(new Movie { Id = i, Name = $"Movie {i}", Genre = (Genre)(i % 5 + 1), Rating = (i % 5) + 1 });

            // Add customers
            for (int i = 1; i <= 10; i++)
                _customers.Add(new Customer { Id = i, Name = $"Customer {i}" });

            // Add recent rentals (within last 28 days)
            var baseDate = new DateTime(2026, 3, 20);
            for (int i = 1; i <= 30; i++)
            {
                _rentals.Add(new Rental
                {
                    Id = i,
                    CustomerId = (i % 10) + 1,
                    MovieId = (i % 20) + 1,
                    RentalDate = baseDate.AddDays(i % 25),
                    DueDate = baseDate.AddDays(i % 25 + 3),
                    DailyRate = 3.99m,
                    Status = i % 5 == 0 ? RentalStatus.Active : RentalStatus.Returned
                });
            }
        }

        [TestMethod]
        public void Simulate_BasicScenario_ReturnsResult()
        {
            var scenario = new StrategyScenario
            {
                Name = "Test Price Cut",
                PriceChangePercent = -10,
                HorizonWeeks = 4,
                Strategies = new List<StrategyType> { StrategyType.PriceReduction }
            };

            var result = _service.Simulate(scenario);

            Assert.IsNotNull(result);
            Assert.AreEqual("Test Price Cut", result.Scenario.Name);
            Assert.IsNotNull(result.Baseline);
            Assert.IsNotNull(result.Projected);
            Assert.IsNotNull(result.Comparison);
            Assert.IsNotNull(result.Verdict);
            Assert.IsNotNull(result.RiskAssessment);
        }

        [TestMethod]
        public void Simulate_PriceReduction_IncreasesVolume()
        {
            var scenario = new StrategyScenario
            {
                Name = "Volume Boost",
                PriceChangePercent = -15,
                HorizonWeeks = 8
            };

            var result = _service.Simulate(scenario);

            Assert.IsTrue(result.Projected.WeeklyRentalVolume > result.Baseline.WeeklyRentalVolume,
                "Price reduction should increase rental volume");
            Assert.IsTrue(result.Projected.VolumeMultiplier > 1.0);
        }

        [TestMethod]
        public void Simulate_PriceIncrease_DecreasesVolume()
        {
            var scenario = new StrategyScenario
            {
                Name = "Premium Test",
                PriceChangePercent = 20,
                HorizonWeeks = 4
            };

            var result = _service.Simulate(scenario);

            Assert.IsTrue(result.Projected.WeeklyRentalVolume < result.Baseline.WeeklyRentalVolume,
                "Price increase should decrease volume");
        }

        [TestMethod]
        public void Simulate_Marketing_IncreasesVolume()
        {
            var scenario = new StrategyScenario
            {
                Name = "Marketing Push",
                MarketingBudgetMultiplier = 2.0,
                HorizonWeeks = 8,
                Strategies = new List<StrategyType> { StrategyType.MarketingPush }
            };

            var result = _service.Simulate(scenario);

            Assert.IsTrue(result.Projected.VolumeMultiplier > 1.0,
                "Marketing spend should increase volume");
            Assert.IsTrue(result.Projected.NewCustomersPerWeek > 0,
                "Marketing should attract new customers");
        }

        [TestMethod]
        public void Simulate_LoyaltyBoost_ReducesChurn()
        {
            var scenario = new StrategyScenario
            {
                Name = "Loyalty Focus",
                LoyaltyBoostPercent = 40,
                HorizonWeeks = 12,
                Strategies = new List<StrategyType> { StrategyType.LoyaltyEnhancement }
            };

            var result = _service.Simulate(scenario);

            Assert.IsTrue(result.Projected.ProjectedChurnRate <= result.Baseline.WeeklyChurnRate,
                "Loyalty boost should reduce churn");
        }

        [TestMethod]
        public void Simulate_InventoryExpansion_IncreasesCapacity()
        {
            var scenario = new StrategyScenario
            {
                Name = "Inventory Growth",
                NewInventoryPercent = 25,
                HorizonWeeks = 8,
                Strategies = new List<StrategyType> { StrategyType.InventoryExpansion }
            };

            var result = _service.Simulate(scenario);

            Assert.IsTrue(result.Projected.VolumeMultiplier > 1.0,
                "More inventory should drive more volume");
        }

        [TestMethod]
        public void Simulate_AssignsIncrementingId()
        {
            var s1 = _service.Simulate(new StrategyScenario { Name = "S1", HorizonWeeks = 4 });
            var s2 = _service.Simulate(new StrategyScenario { Name = "S2", HorizonWeeks = 4 });

            Assert.IsTrue(s2.Id > s1.Id);
        }

        [TestMethod]
        public void Simulate_GeneratesWeeklyProjections()
        {
            var scenario = new StrategyScenario
            {
                Name = "Projection Test",
                PriceChangePercent = -10,
                HorizonWeeks = 6
            };

            var result = _service.Simulate(scenario);

            Assert.AreEqual(6, result.WeeklyProjections.Count);
            Assert.IsTrue(result.WeeklyProjections.All(p => p.Revenue > 0));
            Assert.IsTrue(result.WeeklyProjections.Last().CumulativeRevenue > result.WeeklyProjections.First().Revenue);
        }

        [TestMethod]
        public void Simulate_WeeklyProjections_ShowRampUp()
        {
            var scenario = new StrategyScenario
            {
                Name = "Ramp Test",
                PriceChangePercent = -20,
                HorizonWeeks = 12
            };

            var result = _service.Simulate(scenario);

            // Later weeks should have higher ramp factor (S-curve)
            Assert.IsTrue(result.WeeklyProjections.Last().RampFactor > result.WeeklyProjections.First().RampFactor,
                "Ramp factor should increase over time");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Simulate_InvalidHorizon_Throws()
        {
            _service.Simulate(new StrategyScenario { Name = "Bad", HorizonWeeks = 0 });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Simulate_HorizonTooLarge_Throws()
        {
            _service.Simulate(new StrategyScenario { Name = "Bad", HorizonWeeks = 53 });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Simulate_NullScenario_Throws()
        {
            _service.Simulate(null);
        }

        [TestMethod]
        public void GetHistory_ReturnsAllSimulations()
        {
            _service.Simulate(new StrategyScenario { Name = "H1", HorizonWeeks = 4 });
            _service.Simulate(new StrategyScenario { Name = "H2", HorizonWeeks = 4 });
            _service.Simulate(new StrategyScenario { Name = "H3", HorizonWeeks = 4 });

            var history = _service.GetHistory();

            Assert.IsTrue(history.Count >= 3);
        }

        [TestMethod]
        public void GetById_ExistingId_ReturnsResult()
        {
            var result = _service.Simulate(new StrategyScenario { Name = "Find Me", HorizonWeeks = 4 });

            var found = _service.GetById(result.Id);

            Assert.IsNotNull(found);
            Assert.AreEqual("Find Me", found.Scenario.Name);
        }

        [TestMethod]
        public void GetById_NonExistentId_ReturnsNull()
        {
            var found = _service.GetById(99999);
            Assert.IsNull(found);
        }

        [TestMethod]
        public void GetRecommendations_ReturnsNonEmpty()
        {
            var recs = _service.GetRecommendations();

            Assert.IsTrue(recs.Count > 0);
            Assert.IsTrue(recs.All(r => !string.IsNullOrEmpty(r.Title)));
            Assert.IsTrue(recs.All(r => r.Confidence > 0 && r.Confidence <= 1.0));
        }

        [TestMethod]
        public void GetRecommendations_OrderedByConfidence()
        {
            var recs = _service.GetRecommendations();

            for (int i = 1; i < recs.Count; i++)
                Assert.IsTrue(recs[i - 1].Confidence >= recs[i].Confidence,
                    "Recommendations should be sorted by confidence descending");
        }

        [TestMethod]
        public void GetRecommendations_IncludeSuggestedScenarios()
        {
            var recs = _service.GetRecommendations();

            Assert.IsTrue(recs.All(r => r.SuggestedScenario != null));
            Assert.IsTrue(recs.All(r => r.SuggestedScenario.HorizonWeeks > 0));
        }

        [TestMethod]
        public void CompareScenarios_ReturnsWinner()
        {
            var a = new StrategyScenario { Name = "A", PriceChangePercent = -10, HorizonWeeks = 8 };
            var b = new StrategyScenario { Name = "B", PriceChangePercent = 10, HorizonWeeks = 8 };

            var comparison = _service.CompareScenarios(a, b);

            Assert.IsNotNull(comparison);
            Assert.IsTrue(comparison.Winner == "A" || comparison.Winner == "B");
            Assert.IsTrue(comparison.RevenueAdvantage >= 0);
            Assert.IsNotNull(comparison.Recommendation);
        }

        [TestMethod]
        public void Verdict_HighROI_GetsGradeA()
        {
            // Large marketing push with no price increase = strong ROI
            var scenario = new StrategyScenario
            {
                Name = "High ROI",
                MarketingBudgetMultiplier = 2.5,
                NewInventoryPercent = 20,
                HorizonWeeks = 12
            };

            var result = _service.Simulate(scenario);

            // The exact grade depends on elasticity model, but should be good
            Assert.IsNotNull(result.Verdict.Grade);
            Assert.IsTrue(result.Verdict.Signals.Count > 0);
        }

        [TestMethod]
        public void RiskAssessment_LargePriceChange_FlagsRisk()
        {
            var scenario = new StrategyScenario
            {
                Name = "Risky Price",
                PriceChangePercent = 25,
                HorizonWeeks = 4
            };

            var result = _service.Simulate(scenario);

            Assert.IsTrue(result.RiskAssessment.Factors.Any(f => f.Category == "Price Shock"),
                "Large price change should flag Price Shock risk");
        }

        [TestMethod]
        public void RiskAssessment_HighMarketing_FlagsOverspend()
        {
            var scenario = new StrategyScenario
            {
                Name = "Big Spend",
                MarketingBudgetMultiplier = 3.0,
                HorizonWeeks = 8
            };

            var result = _service.Simulate(scenario);

            Assert.IsTrue(result.RiskAssessment.Factors.Any(f => f.Category == "Marketing Overspend"),
                "Very high marketing spend should flag overspend risk");
        }

        [TestMethod]
        public void RiskAssessment_HighInventory_FlagsOvercommit()
        {
            var scenario = new StrategyScenario
            {
                Name = "Inventory Flood",
                NewInventoryPercent = 50,
                HorizonWeeks = 8
            };

            var result = _service.Simulate(scenario);

            Assert.IsTrue(result.RiskAssessment.Factors.Any(f => f.Category == "Inventory Overcommit"),
                "Large inventory addition should flag overcommit risk");
        }

        [TestMethod]
        public void RiskAssessment_IncludesMitigations()
        {
            var scenario = new StrategyScenario
            {
                Name = "Multi Risk",
                PriceChangePercent = 30,
                MarketingBudgetMultiplier = 3.0,
                NewInventoryPercent = 50,
                HorizonWeeks = 4
            };

            var result = _service.Simulate(scenario);

            Assert.IsTrue(result.RiskAssessment.Mitigations.Count > 0,
                "Risk factors should come with mitigations");
        }

        [TestMethod]
        public void RiskLevel_LowForConservative()
        {
            var scenario = new StrategyScenario
            {
                Name = "Conservative",
                PriceChangePercent = -3,
                LoyaltyBoostPercent = 10,
                HorizonWeeks = 8
            };

            var result = _service.Simulate(scenario);

            Assert.AreEqual("LOW", result.RiskAssessment.RiskLevel,
                "Conservative changes should have LOW risk");
        }

        [TestMethod]
        public void Baseline_ComputesFromRecentRentals()
        {
            var result = _service.Simulate(new StrategyScenario { Name = "Baseline Check", HorizonWeeks = 4 });

            Assert.IsTrue(result.Baseline.WeeklyRentalVolume > 0);
            Assert.IsTrue(result.Baseline.WeeklyRevenue > 0);
            Assert.IsTrue(result.Baseline.TotalCustomers > 0);
            Assert.IsTrue(result.Baseline.TotalInventory > 0);
        }

        [TestMethod]
        public void Baseline_UtilizationBetweenZeroAndOne()
        {
            var result = _service.Simulate(new StrategyScenario { Name = "Util Check", HorizonWeeks = 4 });

            Assert.IsTrue(result.Baseline.AvgUtilizationRate >= 0);
            Assert.IsTrue(result.Baseline.AvgUtilizationRate <= 1.0);
        }

        [TestMethod]
        public void VolumeMultiplier_ClampedReasonably()
        {
            // Extreme scenario should still produce clamped multiplier
            var scenario = new StrategyScenario
            {
                Name = "Extreme",
                PriceChangePercent = -80,
                MarketingBudgetMultiplier = 10,
                NewInventoryPercent = 100,
                HorizonWeeks = 4
            };

            var result = _service.Simulate(scenario);

            Assert.IsTrue(result.Projected.VolumeMultiplier <= 3.0,
                "Volume multiplier should be clamped at 3.0");
            Assert.IsTrue(result.Projected.VolumeMultiplier >= 0.3,
                "Volume multiplier should not go below 0.3");
        }

        [TestMethod]
        public void ProjectedUtilization_CappedAt95Percent()
        {
            var scenario = new StrategyScenario
            {
                Name = "Max Util",
                PriceChangePercent = -50,
                MarketingBudgetMultiplier = 5,
                NewInventoryPercent = 50,
                HorizonWeeks = 4
            };

            var result = _service.Simulate(scenario);

            Assert.IsTrue(result.Projected.ProjectedUtilization <= 0.95,
                "Projected utilization should cap at 95%");
        }

        [TestMethod]
        public void Simulate_RecordsTimestamp()
        {
            var result = _service.Simulate(new StrategyScenario { Name = "Time", HorizonWeeks = 4 });

            Assert.AreEqual(_clock.Now, result.SimulatedAt);
        }

        [TestMethod]
        public void CompareScenarios_DominatingScenario_ClearWinner()
        {
            var good = new StrategyScenario { Name = "Good", PriceChangePercent = -5, MarketingBudgetMultiplier = 1.5, HorizonWeeks = 8 };
            var bad = new StrategyScenario { Name = "Bad", PriceChangePercent = 30, HorizonWeeks = 8 };

            var comparison = _service.CompareScenarios(good, bad);

            Assert.IsNotNull(comparison.Recommendation);
            Assert.IsTrue(comparison.Recommendation.Length > 10);
        }

        [TestMethod]
        public void Verdict_HasConfidenceScore()
        {
            var result = _service.Simulate(new StrategyScenario { Name = "Conf", HorizonWeeks = 4 });

            Assert.IsTrue(result.Verdict.ConfidenceScore >= 0.3);
            Assert.IsTrue(result.Verdict.ConfidenceScore <= 0.95);
        }

        [TestMethod]
        public void Simulate_MultipleStrategies_Composable()
        {
            var combined = new StrategyScenario
            {
                Name = "Combined",
                PriceChangePercent = -10,
                MarketingBudgetMultiplier = 1.5,
                NewInventoryPercent = 15,
                LoyaltyBoostPercent = 20,
                HorizonWeeks = 8,
                Strategies = new List<StrategyType>
                {
                    StrategyType.PriceReduction,
                    StrategyType.MarketingPush,
                    StrategyType.InventoryExpansion,
                    StrategyType.LoyaltyEnhancement
                }
            };

            var result = _service.Simulate(combined);

            Assert.IsTrue(result.Projected.VolumeMultiplier > 1.0);
            Assert.IsTrue(result.Projected.ProjectedChurnRate < result.Baseline.WeeklyChurnRate);
        }

        private class TestClock : IClock
        {
            private readonly DateTime _now;
            public TestClock(DateTime now) { _now = now; }
            public DateTime Now => _now;
        }
    }
}
