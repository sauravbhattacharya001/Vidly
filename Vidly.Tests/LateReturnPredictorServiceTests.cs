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
        private InMemoryRentalRepository _rentalRepo;
        private InMemoryCustomerRepository _custRepo;

        [TestInitialize]
        public void Setup()
        {
            InMemoryRentalRepository.Reset();
            _rentalRepo = new InMemoryRentalRepository();
            _custRepo = new InMemoryCustomerRepository();
            _service = new LateReturnPredictorService(_rentalRepo, _custRepo);
        }

        // --- Existing tests (preserved) ---

        [TestMethod]
        public void PredictAll_ReturnsOnlyActiveRentals()
        {
            var predictions = _service.PredictAll();
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

        // ═══════════════════════════════════════════════════
        // Constructor validation
        // ═══════════════════════════════════════════════════

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new LateReturnPredictorService(null, _custRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new LateReturnPredictorService(_rentalRepo, null);
        }

        // ═══════════════════════════════════════════════════
        // Factor 1: Already overdue — scoring
        // ═══════════════════════════════════════════════════

        [TestMethod]
        public void OverdueFactor_3Days_Scores15Points()
        {
            var prediction = _service.PredictForRental(2);
            var overdueFactor = prediction.Factors.FirstOrDefault(f => f.Name == "Already Overdue");
            Assert.IsNotNull(overdueFactor, "Should have an overdue factor.");
            Assert.AreEqual(15, overdueFactor.Points, "3 days overdue = 3x5 = 15 points.");
        }

        [TestMethod]
        public void OverdueFactor_CapsAt30Points()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 3,
                CustomerName = "Test User",
                MovieId = 100,
                MovieName = "Test Movie",
                RentalDate = DateTime.Today.AddDays(-17),
                DueDate = DateTime.Today.AddDays(-10),
                DailyRate = 3.99m,
                Status = RentalStatus.Active
            });

            var all = _rentalRepo.GetAll();
            var added = all.Last();
            var prediction = _service.PredictForRental(added.Id);

            var overdueFactor = prediction.Factors.FirstOrDefault(f => f.Name == "Already Overdue");
            Assert.IsNotNull(overdueFactor);
            Assert.AreEqual(30, overdueFactor.Points, "Overdue points should cap at 30.");
        }

        [TestMethod]
        public void OverdueFactor_EstimatedFee_MatchesDaysOverdue()
        {
            var prediction = _service.PredictForRental(2);
            int daysOverdue = Math.Abs(prediction.DaysRemaining);
            Assert.AreEqual(daysOverdue * 1.50m, prediction.EstimatedLateFee,
                "Late fee = overdue days x $1.50.");
        }

        // ═══════════════════════════════════════════════════
        // Factor 2: Due soon (0, 1, 2 days remaining)
        // ═══════════════════════════════════════════════════

        [TestMethod]
        public void DueSoonFactor_DueToday_15Points()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 3, CustomerName = "Due Today",
                MovieId = 101, MovieName = "Movie A",
                RentalDate = DateTime.Today.AddDays(-7),
                DueDate = DateTime.Today,
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var added = _rentalRepo.GetAll().Last();
            var prediction = _service.PredictForRental(added.Id);
            var dueSoon = prediction.Factors.FirstOrDefault(f => f.Name == "Due Soon");
            Assert.IsNotNull(dueSoon, "Should detect due today.");
            Assert.AreEqual(15, dueSoon.Points, "Due today = 15 points.");
        }

        [TestMethod]
        public void DueSoonFactor_DueTomorrow_10Points()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 3, CustomerName = "Due Tomorrow",
                MovieId = 102, MovieName = "Movie B",
                RentalDate = DateTime.Today.AddDays(-6),
                DueDate = DateTime.Today.AddDays(1),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var added = _rentalRepo.GetAll().Last();
            var prediction = _service.PredictForRental(added.Id);
            var dueSoon = prediction.Factors.FirstOrDefault(f => f.Name == "Due Soon");
            Assert.IsNotNull(dueSoon, "Should detect due tomorrow.");
            Assert.AreEqual(10, dueSoon.Points, "Due tomorrow = 10 points.");
        }

        [TestMethod]
        public void DueSoonFactor_DueIn2Days_5Points()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 3, CustomerName = "Due In 2",
                MovieId = 103, MovieName = "Movie C",
                RentalDate = DateTime.Today.AddDays(-5),
                DueDate = DateTime.Today.AddDays(2),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var added = _rentalRepo.GetAll().Last();
            var prediction = _service.PredictForRental(added.Id);
            var dueSoon = prediction.Factors.FirstOrDefault(f => f.Name == "Due Soon");
            Assert.IsNotNull(dueSoon, "Should detect due in 2 days.");
            Assert.AreEqual(5, dueSoon.Points, "Due in 2 days = 5 points.");
        }

        [TestMethod]
        public void DueSoonFactor_DueIn4Days_NoFactor()
        {
            var prediction = _service.PredictForRental(1);
            var dueSoon = prediction.Factors.FirstOrDefault(f => f.Name == "Due Soon");
            Assert.IsNull(dueSoon, "Due in 4 days should not trigger Due Soon factor.");
        }

        // ═══════════════════════════════════════════════════
        // Factor 3: Late return history
        // ═══════════════════════════════════════════════════

        [TestMethod]
        public void LateHistoryFactor_AllLate_25Points()
        {
            for (int i = 0; i < 4; i++)
            {
                _rentalRepo.Add(new Rental
                {
                    CustomerId = 5, CustomerName = "Late Larry",
                    MovieId = 200 + i, MovieName = "Movie " + i,
                    RentalDate = DateTime.Today.AddDays(-30 - i * 7),
                    DueDate = DateTime.Today.AddDays(-23 - i * 7),
                    ReturnDate = DateTime.Today.AddDays(-20 - i * 7),
                    DailyRate = 3.99m, Status = RentalStatus.Returned
                });
            }

            _rentalRepo.Add(new Rental
            {
                CustomerId = 5, CustomerName = "Late Larry",
                MovieId = 210, MovieName = "Current Movie",
                RentalDate = DateTime.Today.AddDays(-3),
                DueDate = DateTime.Today.AddDays(4),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var active = _rentalRepo.GetAll().Last();
            var prediction = _service.PredictForRental(active.Id);
            var historyFactor = prediction.Factors.FirstOrDefault(f => f.Name == "Late Return History");
            Assert.IsNotNull(historyFactor, "Should detect late return history.");
            Assert.AreEqual(25, historyFactor.Points, "100% late rate = 25 points.");
        }

        [TestMethod]
        public void LateHistoryFactor_HalfLate_PartialPoints()
        {
            for (int i = 0; i < 4; i++)
            {
                bool isLate = i < 2;
                _rentalRepo.Add(new Rental
                {
                    CustomerId = 6, CustomerName = "Mixed Mary",
                    MovieId = 220 + i, MovieName = "Movie " + i,
                    RentalDate = DateTime.Today.AddDays(-30 - i * 7),
                    DueDate = DateTime.Today.AddDays(-23 - i * 7),
                    ReturnDate = isLate
                        ? DateTime.Today.AddDays(-20 - i * 7)
                        : DateTime.Today.AddDays(-24 - i * 7),
                    DailyRate = 3.99m, Status = RentalStatus.Returned
                });
            }

            _rentalRepo.Add(new Rental
            {
                CustomerId = 6, CustomerName = "Mixed Mary",
                MovieId = 230, MovieName = "Current Movie",
                RentalDate = DateTime.Today.AddDays(-3),
                DueDate = DateTime.Today.AddDays(4),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var active = _rentalRepo.GetAll().Last();
            var prediction = _service.PredictForRental(active.Id);
            var historyFactor = prediction.Factors.FirstOrDefault(f => f.Name == "Late Return History");
            Assert.IsNotNull(historyFactor, "Should detect partial late history.");
            Assert.IsTrue(historyFactor.Points > 0 && historyFactor.Points <= 25,
                $"Partial late history should give 1-25 points, got {historyFactor.Points}.");
        }

        // ═══════════════════════════════════════════════════
        // Factor 5: Extended hold (>10 days)
        // ═══════════════════════════════════════════════════

        [TestMethod]
        public void ExtendedHoldFactor_15Days_10Points()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 3, CustomerName = "Long Holder",
                MovieId = 250, MovieName = "Extended Movie",
                RentalDate = DateTime.Today.AddDays(-15),
                DueDate = DateTime.Today.AddDays(5),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var added = _rentalRepo.GetAll().Last();
            var prediction = _service.PredictForRental(added.Id);
            var holdFactor = prediction.Factors.FirstOrDefault(f => f.Name == "Extended Hold");
            Assert.IsNotNull(holdFactor, "15 days held should trigger Extended Hold.");
            Assert.AreEqual(10, holdFactor.Points, "15 - 10 = 5, 5x2 = 10 (capped at 10).");
        }

        [TestMethod]
        public void ExtendedHoldFactor_8Days_NoFactor()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 3, CustomerName = "Short Holder",
                MovieId = 251, MovieName = "Short Movie",
                RentalDate = DateTime.Today.AddDays(-8),
                DueDate = DateTime.Today.AddDays(5),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var added = _rentalRepo.GetAll().Last();
            var prediction = _service.PredictForRental(added.Id);
            var holdFactor = prediction.Factors.FirstOrDefault(f => f.Name == "Extended Hold");
            Assert.IsNull(holdFactor, "8 days held should NOT trigger Extended Hold.");
        }

        // ═══════════════════════════════════════════════════
        // Factor 6: New customer
        // ═══════════════════════════════════════════════════

        [TestMethod]
        public void NewCustomerFactor_NoHistory_10Points()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 7, CustomerName = "New Newbie",
                MovieId = 260, MovieName = "First Movie",
                RentalDate = DateTime.Today.AddDays(-1),
                DueDate = DateTime.Today.AddDays(6),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var added = _rentalRepo.GetAll().Last();
            var prediction = _service.PredictForRental(added.Id);
            var newFactor = prediction.Factors.FirstOrDefault(f => f.Name == "New Customer");
            Assert.IsNotNull(newFactor, "First-time customer should trigger New Customer factor.");
            Assert.AreEqual(10, newFactor.Points, "New customer factor = 10 points.");
        }

        [TestMethod]
        public void NewCustomerFactor_SuggestsWelcomeEmail()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 8, CustomerName = "Welcome Wendy",
                MovieId = 261, MovieName = "Welcome Movie",
                RentalDate = DateTime.Today.AddDays(-1),
                DueDate = DateTime.Today.AddDays(6),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var added = _rentalRepo.GetAll().Last();
            var prediction = _service.PredictForRental(added.Id);
            Assert.IsTrue(prediction.SuggestedActions.Any(a =>
                a.Contains("welcome", StringComparison.OrdinalIgnoreCase)),
                "New customer should get welcome email action.");
        }

        // ═══════════════════════════════════════════════════
        // Score capping at 100
        // ═══════════════════════════════════════════════════

        [TestMethod]
        public void RiskScore_CappedAt100()
        {
            for (int i = 0; i < 5; i++)
            {
                _rentalRepo.Add(new Rental
                {
                    CustomerId = 9, CustomerName = "Worst Case",
                    MovieId = 300 + i, MovieName = "Past " + i,
                    RentalDate = DateTime.Today.AddDays(-60 - i * 7),
                    DueDate = DateTime.Today.AddDays(-53 - i * 7),
                    ReturnDate = DateTime.Today.AddDays(-45 - i * 7),
                    DailyRate = 3.99m, Status = RentalStatus.Returned
                });
            }

            _rentalRepo.Add(new Rental
            {
                CustomerId = 9, CustomerName = "Worst Case",
                MovieId = 310, MovieName = "Other Overdue",
                RentalDate = DateTime.Today.AddDays(-14),
                DueDate = DateTime.Today.AddDays(-7),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            _rentalRepo.Add(new Rental
            {
                CustomerId = 9, CustomerName = "Worst Case",
                MovieId = 311, MovieName = "Very Overdue",
                RentalDate = DateTime.Today.AddDays(-20),
                DueDate = DateTime.Today.AddDays(-13),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var added = _rentalRepo.GetAll().Last();
            var prediction = _service.PredictForRental(added.Id);
            Assert.AreEqual(100, prediction.RiskScore,
                "Risk score should be capped at 100.");
            Assert.AreEqual(RiskLevel.Critical, prediction.Level);
        }

        // ═══════════════════════════════════════════════════
        // GenerateActions — specific scenarios
        // ═══════════════════════════════════════════════════

        [TestMethod]
        public void Actions_OverdueLongTime_SuggestsEscalation()
        {
            var prediction = _service.PredictForRental(2);
            Assert.IsTrue(prediction.SuggestedActions.Any(a =>
                a.Contains("Escalate", StringComparison.OrdinalIgnoreCase)),
                "3+ days overdue should suggest escalation.");
        }

        [TestMethod]
        public void Actions_DueToday_SuggestsReminder()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 3, CustomerName = "Reminder Rick",
                MovieId = 270, MovieName = "Due Today Movie",
                RentalDate = DateTime.Today.AddDays(-7),
                DueDate = DateTime.Today,
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var added = _rentalRepo.GetAll().Last();
            var prediction = _service.PredictForRental(added.Id);
            Assert.IsTrue(prediction.SuggestedActions.Any(a =>
                a.Contains("today", StringComparison.OrdinalIgnoreCase)),
                "Due today should suggest sending reminder.");
        }

        [TestMethod]
        public void Actions_DueTomorrow_SuggestsReminder()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 3, CustomerName = "Reminder Rita",
                MovieId = 271, MovieName = "Due Tomorrow Movie",
                RentalDate = DateTime.Today.AddDays(-6),
                DueDate = DateTime.Today.AddDays(1),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var added = _rentalRepo.GetAll().Last();
            var prediction = _service.PredictForRental(added.Id);
            Assert.IsTrue(prediction.SuggestedActions.Any(a =>
                a.Contains("tomorrow", StringComparison.OrdinalIgnoreCase)),
                "Due tomorrow should suggest sending reminder.");
        }

        [TestMethod]
        public void Actions_HighRisk_SuggestsStaffFollowUp()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 10, CustomerName = "High Risk Hank",
                MovieId = 280, MovieName = "High Risk Movie",
                RentalDate = DateTime.Today.AddDays(-20),
                DueDate = DateTime.Today.AddDays(-7),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var added = _rentalRepo.GetAll().Last();
            var prediction = _service.PredictForRental(added.Id);
            Assert.IsTrue(prediction.Level >= RiskLevel.High,
                $"Should be High or Critical risk, got {prediction.Level} (score {prediction.RiskScore}).");
            Assert.IsTrue(prediction.SuggestedActions.Any(a =>
                a.Contains("staff", StringComparison.OrdinalIgnoreCase)),
                "High risk should suggest staff follow-up.");
        }

        [TestMethod]
        public void Actions_LateHistory_SuggestsDeposit()
        {
            _rentalRepo.Add(new Rental
            {
                CustomerId = 11, CustomerName = "Deposit Dan",
                MovieId = 290, MovieName = "Past Late",
                RentalDate = DateTime.Today.AddDays(-30),
                DueDate = DateTime.Today.AddDays(-23),
                ReturnDate = DateTime.Today.AddDays(-20),
                DailyRate = 3.99m, Status = RentalStatus.Returned
            });

            _rentalRepo.Add(new Rental
            {
                CustomerId = 11, CustomerName = "Deposit Dan",
                MovieId = 291, MovieName = "Current",
                RentalDate = DateTime.Today.AddDays(-3),
                DueDate = DateTime.Today.AddDays(4),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var active = _rentalRepo.GetAll().Last();
            var prediction = _service.PredictForRental(active.Id);
            Assert.IsTrue(prediction.SuggestedActions.Any(a =>
                a.Contains("deposit", StringComparison.OrdinalIgnoreCase)),
                "Customer with late history should get deposit suggestion.");
        }

        [TestMethod]
        public void Actions_LowRisk_NoActionNeeded()
        {
            var prediction = _service.PredictForRental(1);
            if (prediction.RiskScore < 20)
            {
                Assert.IsTrue(prediction.SuggestedActions.Any(a =>
                    a.Contains("No action needed", StringComparison.OrdinalIgnoreCase) ||
                    a.Contains("on track", StringComparison.OrdinalIgnoreCase)),
                    "Low risk rental should indicate no action needed.");
            }
        }

        // ═══════════════════════════════════════════════════
        // Estimated late fees — non-overdue high risk
        // ═══════════════════════════════════════════════════

        [TestMethod]
        public void EstimatedFee_HighRiskNotOverdue_EstimatesFutureFee()
        {
            for (int i = 0; i < 3; i++)
            {
                _rentalRepo.Add(new Rental
                {
                    CustomerId = 12, CustomerName = "Risky Ricky",
                    MovieId = 400 + i, MovieName = "Past " + i,
                    RentalDate = DateTime.Today.AddDays(-40 - i * 7),
                    DueDate = DateTime.Today.AddDays(-33 - i * 7),
                    ReturnDate = DateTime.Today.AddDays(-28 - i * 7),
                    DailyRate = 3.99m, Status = RentalStatus.Returned
                });
            }

            _rentalRepo.Add(new Rental
            {
                CustomerId = 12, CustomerName = "Risky Ricky",
                MovieId = 410, MovieName = "Current",
                RentalDate = DateTime.Today.AddDays(-7),
                DueDate = DateTime.Today,
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var added = _rentalRepo.GetAll().Last();
            var prediction = _service.PredictForRental(added.Id);
            if (prediction.RiskScore >= 45)
            {
                Assert.IsTrue(prediction.EstimatedLateFee > 0,
                    "High risk non-overdue rental should estimate future late fee.");
            }
        }

        // ═══════════════════════════════════════════════════
        // Prediction metadata
        // ═══════════════════════════════════════════════════

        [TestMethod]
        public void Prediction_HasCorrectMetadata()
        {
            var prediction = _service.PredictForRental(1);
            Assert.AreEqual(1, prediction.RentalId);
            Assert.AreEqual(1, prediction.CustomerId);
            Assert.AreEqual("John Smith", prediction.CustomerName);
            Assert.AreEqual(1, prediction.MovieId);
            Assert.AreEqual("Shrek!", prediction.MovieName);
        }

        [TestMethod]
        public void Prediction_DaysRemaining_PositiveForFutureDate()
        {
            var prediction = _service.PredictForRental(1);
            Assert.AreEqual(4, prediction.DaysRemaining);
        }

        [TestMethod]
        public void Prediction_DaysRemaining_NegativeForOverdue()
        {
            var prediction = _service.PredictForRental(2);
            Assert.AreEqual(-3, prediction.DaysRemaining);
        }

        // ═══════════════════════════════════════════════════
        // GetSummary — edge cases
        // ═══════════════════════════════════════════════════

        [TestMethod]
        public void GetSummary_TotalEstimatedLateFees_SumsCorrectly()
        {
            var summary = _service.GetSummary();
            var predictions = _service.PredictAll();
            decimal expected = predictions.Sum(p => p.EstimatedLateFee);
            Assert.AreEqual(expected, summary.TotalEstimatedLateFees,
                "Summary late fees should equal sum of individual predictions.");
        }

        [TestMethod]
        public void GetSummary_CountsMatchTotal()
        {
            var summary = _service.GetSummary();
            int countSum = summary.LowRisk + summary.MediumRisk +
                           summary.HighRisk + summary.CriticalRisk;
            Assert.AreEqual(summary.TotalActiveRentals, countSum,
                "Risk level counts must sum to total active rentals.");
        }
    }
}
