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
    public class LateReturnPredictorServiceTests
    {
        private LateReturnPredictorService _service;

        [TestInitialize]
        public void Setup()
        {
            InMemoryRentalRepository.Reset();
            _service = new LateReturnPredictorService(
                new InMemoryRentalRepository(),
                new InMemoryCustomerRepository());
        }

        [TestMethod]
        public void PredictAll_ReturnsOnlyActiveRentals()
        {
            var predictions = _service.PredictAll();
            // Seed has 2 active (one overdue), 1 returned
            Assert.AreEqual(2, predictions.Count);
        }

        [TestMethod]
        public void PredictAll_OrderedByRiskScoreDescending()
        {
            var predictions = _service.PredictAll();
            for (int i = 1; i < predictions.Count; i++)
            {
                Assert.IsTrue(predictions[i - 1].RiskScore >= predictions[i].RiskScore,
                    "Predictions should be ordered by risk score descending.");
            }
        }

        [TestMethod]
        public void PredictForRental_OverdueRental_HighRiskScore()
        {
            // Rental 2 in seed data is overdue by 3 days
            var prediction = _service.PredictForRental(2);
            Assert.IsTrue(prediction.RiskScore >= 15,
                "Overdue rental should have significant risk score.");
            Assert.IsTrue(prediction.DaysRemaining < 0,
                "Days remaining should be negative for overdue rental.");
        }

        [TestMethod]
        public void PredictForRental_ActiveRental_HasFactors()
        {
            var prediction = _service.PredictForRental(1);
            Assert.IsNotNull(prediction.Factors);
            Assert.IsNotNull(prediction.SuggestedActions);
            Assert.IsTrue(prediction.SuggestedActions.Count > 0,
                "Should always have at least one suggested action.");
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void PredictForRental_NonexistentRental_Throws()
        {
            _service.PredictForRental(999);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void PredictForRental_ReturnedRental_Throws()
        {
            _service.PredictForRental(3);
        }

        [TestMethod]
        public void GetSummary_ReturnsCorrectCounts()
        {
            var summary = _service.GetSummary();
            Assert.AreEqual(2, summary.TotalActiveRentals);
            Assert.AreEqual(2,
                summary.LowRisk + summary.MediumRisk + summary.HighRisk + summary.CriticalRisk,
                "Risk level counts should sum to total active rentals.");
        }

        [TestMethod]
        public void GetSummary_AverageRiskScore_IsReasonable()
        {
            var summary = _service.GetSummary();
            Assert.IsTrue(summary.AverageRiskScore >= 0 && summary.AverageRiskScore <= 100,
                "Average risk score should be 0-100.");
        }

        [TestMethod]
        public void PredictForRental_OverdueRental_HasEstimatedLateFee()
        {
            var prediction = _service.PredictForRental(2);
            Assert.IsTrue(prediction.EstimatedLateFee > 0,
                "Overdue rental should have estimated late fee.");
        }

        [TestMethod]
        public void PredictForRental_OverdueRental_SuggestsOverdueNotice()
        {
            var prediction = _service.PredictForRental(2);
            Assert.IsTrue(prediction.SuggestedActions.Any(a =>
                a.Contains("overdue", StringComparison.OrdinalIgnoreCase)),
                "Overdue rental should suggest sending overdue notice.");
        }

        [TestMethod]
        public void RiskLevel_CorrectMapping()
        {
            // Score >= 70 = Critical, >= 45 = High, >= 20 = Medium, < 20 = Low
            var predictions = _service.PredictAll();
            foreach (var p in predictions)
            {
                var expected = p.RiskScore switch
                {
                    >= 70 => RiskLevel.Critical,
                    >= 45 => RiskLevel.High,
                    >= 20 => RiskLevel.Medium,
                    _ => RiskLevel.Low
                };
                Assert.AreEqual(expected, p.Level,
                    $"Rental {p.RentalId} with score {p.RiskScore} should be {expected}.");
            }
        }

        [TestMethod]
        public void PredictAll_FactorPointsDoNotExceedScore()
        {
            var predictions = _service.PredictAll();
            foreach (var p in predictions)
            {
                int factorSum = p.Factors.Sum(f => f.Points);
                Assert.IsTrue(factorSum <= 100,
                    $"Factor points sum ({factorSum}) should not exceed 100.");
            }
        }
    }
}
