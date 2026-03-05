using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class StaffPerformanceServiceTests
    {
        #region Helpers

        private StaffPerformanceService _service;
        private StaffMember _alice;
        private StaffMember _bob;
        private StaffMember _carol;
        private readonly DateTime _periodStart = new DateTime(2026, 3, 1);
        private readonly DateTime _periodEnd = new DateTime(2026, 3, 8);

        [TestInitialize]
        public void SetUp()
        {
            _service = new StaffPerformanceService();
            _alice = _service.AddStaff("Alice Johnson", StaffRole.Clerk);
            _bob = _service.AddStaff("Bob Smith", StaffRole.SeniorClerk);
            _carol = _service.AddStaff("Carol Lee", StaffRole.ShiftLead, new DateTime(2025, 1, 15));
        }

        private StaffTransaction RecordRental(int staffId, decimal revenue = 5m,
            int duration = 120, int? rating = null, bool upsell = false, bool upsellOk = false,
            DateTime? ts = null)
        {
            return _service.RecordTransaction(staffId, customerId: 100,
                type: StaffTransactionType.Rental, revenue: revenue,
                durationSeconds: duration, movieId: 1,
                upsellAttempted: upsell, upsellAccepted: upsellOk,
                satisfactionRating: rating, timestamp: ts ?? _periodStart.AddHours(10));
        }

        private void SeedAliceTransactions()
        {
            for (int i = 0; i < 5; i++)
            {
                RecordRental(_alice.Id, revenue: 10m, duration: 100, rating: 5,
                    upsell: true, upsellOk: true,
                    ts: _periodStart.AddDays(i).AddHours(10));
            }
            for (int i = 0; i < 3; i++)
            {
                RecordRental(_alice.Id, revenue: 5m, duration: 150, rating: 4,
                    upsell: true, upsellOk: false,
                    ts: _periodStart.AddDays(i).AddHours(14));
            }
        }

        private void SeedBobTransactions()
        {
            for (int i = 0; i < 3; i++)
            {
                RecordRental(_bob.Id, revenue: 3m, duration: 200, rating: 3,
                    ts: _periodStart.AddDays(i).AddHours(10));
            }
        }

        #endregion

        // ══════════════════════════════════════════════════════
        //  Constructor
        // ══════════════════════════════════════════════════════

        [TestMethod]
        public void Constructor_DefaultWeights_Succeeds()
        {
            var svc = new StaffPerformanceService();
            Assert.IsNotNull(svc);
        }

        [TestMethod]
        public void Constructor_CustomWeights_Normalizes()
        {
            var svc = new StaffPerformanceService(2, 2, 2, 2, 2);
            Assert.IsNotNull(svc);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_NegativeWeight_Throws()
        {
            new StaffPerformanceService(-1, 1, 1, 1, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_AllZeroWeights_Throws()
        {
            new StaffPerformanceService(0, 0, 0, 0, 0);
        }

        // ══════════════════════════════════════════════════════
        //  Staff CRUD
        // ══════════════════════════════════════════════════════

        [TestMethod]
        public void AddStaff_ValidName_ReturnsStaff()
        {
            var dave = _service.AddStaff("Dave", StaffRole.Clerk);
            Assert.IsNotNull(dave);
            Assert.AreEqual("Dave", dave.Name);
            Assert.IsTrue(dave.Id > 0);
            Assert.IsTrue(dave.IsActive);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void AddStaff_EmptyName_Throws()
        {
            _service.AddStaff("", StaffRole.Clerk);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void AddStaff_WhitespaceName_Throws()
        {
            _service.AddStaff("   ", StaffRole.Clerk);
        }

        [TestMethod]
        public void AddStaff_TrimsName()
        {
            var s = _service.AddStaff("  Dave  ", StaffRole.Clerk);
            Assert.AreEqual("Dave", s.Name);
        }

        [TestMethod]
        public void AddStaff_CustomHireDate()
        {
            var hire = new DateTime(2024, 6, 1);
            var s = _service.AddStaff("Eve", StaffRole.Manager, hire);
            Assert.AreEqual(hire, s.HireDate);
        }

        [TestMethod]
        public void GetStaff_ExistingId_ReturnsStaff()
        {
            var result = _service.GetStaff(_alice.Id);
            Assert.IsNotNull(result);
            Assert.AreEqual("Alice Johnson", result.Name);
        }

        [TestMethod]
        public void GetStaff_NonExistentId_ReturnsNull()
        {
            Assert.IsNull(_service.GetStaff(999));
        }

        [TestMethod]
        public void ListStaff_ActiveOnly_ExcludesDeactivated()
        {
            _service.DeactivateStaff(_bob.Id);
            var active = _service.ListStaff(activeOnly: true);
            Assert.IsFalse(active.Any(s => s.Id == _bob.Id));
            Assert.AreEqual(2, active.Count);
        }

        [TestMethod]
        public void ListStaff_IncludeInactive_ReturnsAll()
        {
            _service.DeactivateStaff(_bob.Id);
            var all = _service.ListStaff(activeOnly: false);
            Assert.AreEqual(3, all.Count);
        }

        [TestMethod]
        public void ListStaff_SortedByName()
        {
            var list = _service.ListStaff();
            Assert.AreEqual("Alice Johnson", list[0].Name);
            Assert.AreEqual("Bob Smith", list[1].Name);
            Assert.AreEqual("Carol Lee", list[2].Name);
        }

        [TestMethod]
        public void DeactivateStaff_ExistingId_ReturnsTrue()
        {
            Assert.IsTrue(_service.DeactivateStaff(_alice.Id));
            Assert.IsFalse(_service.GetStaff(_alice.Id).IsActive);
        }

        [TestMethod]
        public void DeactivateStaff_NonExistent_ReturnsFalse()
        {
            Assert.IsFalse(_service.DeactivateStaff(999));
        }

        [TestMethod]
        public void ReactivateStaff_AfterDeactivation_Succeeds()
        {
            _service.DeactivateStaff(_alice.Id);
            Assert.IsTrue(_service.ReactivateStaff(_alice.Id));
            Assert.IsTrue(_service.GetStaff(_alice.Id).IsActive);
        }

        [TestMethod]
        public void ReactivateStaff_NonExistent_ReturnsFalse()
        {
            Assert.IsFalse(_service.ReactivateStaff(999));
        }

        // ══════════════════════════════════════════════════════
        //  Transaction Recording
        // ══════════════════════════════════════════════════════

        [TestMethod]
        public void RecordTransaction_Valid_ReturnsTransaction()
        {
            var txn = RecordRental(_alice.Id, revenue: 10m, rating: 5);
            Assert.IsNotNull(txn);
            Assert.IsTrue(txn.Id > 0);
            Assert.AreEqual(_alice.Id, txn.StaffId);
            Assert.AreEqual("Alice Johnson", txn.StaffName);
            Assert.AreEqual(10m, txn.Revenue);
            Assert.AreEqual(5, txn.SatisfactionRating);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RecordTransaction_InvalidStaff_Throws()
        {
            _service.RecordTransaction(999, 100, StaffTransactionType.Rental);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RecordTransaction_NegativeRevenue_Throws()
        {
            _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.Rental, revenue: -5m);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RecordTransaction_NegativeDuration_Throws()
        {
            _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.Rental, durationSeconds: -1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RecordTransaction_InvalidRating_Throws()
        {
            _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.Rental, satisfactionRating: 6);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RecordTransaction_ZeroRating_Throws()
        {
            _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.Rental, satisfactionRating: 0);
        }

        [TestMethod]
        public void RecordTransaction_UpsellAcceptedWithoutAttempt_ForceFalse()
        {
            var txn = _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.Rental,
                upsellAttempted: false, upsellAccepted: true);
            Assert.IsFalse(txn.UpsellAccepted);
        }

        [TestMethod]
        public void RecordTransaction_CustomTimestamp()
        {
            var ts = new DateTime(2026, 1, 15, 14, 30, 0);
            var txn = _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.Rental, timestamp: ts);
            Assert.AreEqual(ts, txn.Timestamp);
        }

        [TestMethod]
        public void RecordTransaction_FeedbackTrimmed()
        {
            var txn = _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.Rental,
                satisfactionRating: 4, feedbackComment: "  Great service!  ");
            Assert.AreEqual("Great service!", txn.FeedbackComment);
        }

        [TestMethod]
        public void GetTransactions_FiltersByStaffAndDate()
        {
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 1, 10, 0, 0));
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 5, 10, 0, 0));
            RecordRental(_bob.Id, ts: new DateTime(2026, 3, 3, 10, 0, 0));

            var aliceTxns = _service.GetTransactions(_alice.Id);
            Assert.AreEqual(2, aliceTxns.Count);

            var filtered = _service.GetTransactions(_alice.Id,
                from: new DateTime(2026, 3, 3), to: new DateTime(2026, 3, 6));
            Assert.AreEqual(1, filtered.Count);
        }

        [TestMethod]
        public void GetTransactions_OrderedByTimestampDescending()
        {
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 1));
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 5));
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 3));

            var txns = _service.GetTransactions(_alice.Id);
            Assert.IsTrue(txns[0].Timestamp >= txns[1].Timestamp);
            Assert.IsTrue(txns[1].Timestamp >= txns[2].Timestamp);
        }

        // ══════════════════════════════════════════════════════
        //  Performance Report
        // ══════════════════════════════════════════════════════

        [TestMethod]
        public void GenerateReport_ValidStaff_ReturnsReport()
        {
            SeedAliceTransactions();
            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);

            Assert.AreEqual(_alice.Id, report.StaffId);
            Assert.AreEqual("Alice Johnson", report.StaffName);
            Assert.AreEqual(8, report.TotalTransactions);
            Assert.AreEqual(8, report.RentalCount);
        }

        [TestMethod]
        public void GenerateReport_CalculatesRevenue()
        {
            SeedAliceTransactions();
            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);

            // 5 * $10 + 3 * $5 = $65
            Assert.AreEqual(65m, report.TotalRevenue);
            Assert.AreEqual(65m / 8, report.AverageRevenuePerTransaction);
        }

        [TestMethod]
        public void GenerateReport_CalculatesSpeed()
        {
            SeedAliceTransactions();
            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);

            Assert.IsTrue(report.AverageTransactionSeconds > 0);
            Assert.AreEqual(100, report.FastestTransactionSeconds);
            Assert.AreEqual(150, report.SlowestTransactionSeconds);
        }

        [TestMethod]
        public void GenerateReport_CalculatesUpsell()
        {
            SeedAliceTransactions();
            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);

            Assert.AreEqual(8, report.UpsellAttempts);
            Assert.AreEqual(5, report.UpsellSuccesses);
            Assert.IsTrue(report.UpsellConversionRate > 0.5);
        }

        [TestMethod]
        public void GenerateReport_CalculatesSatisfaction()
        {
            SeedAliceTransactions();
            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);

            Assert.AreEqual(8, report.TotalRatings);
            Assert.IsTrue(report.AverageSatisfactionRating > 4.0);
            Assert.AreEqual(5, report.FiveStarCount);
            Assert.AreEqual(0, report.OneStarCount);
        }

        [TestMethod]
        public void GenerateReport_CalculatesGrade()
        {
            SeedAliceTransactions();
            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);

            Assert.IsNotNull(report.Grade);
            Assert.AreNotEqual("N/A", report.Grade);
            Assert.IsTrue(report.PerformanceScore > 0);
        }

        [TestMethod]
        public void GenerateReport_NoTransactions_ReturnsNA()
        {
            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);

            Assert.AreEqual("N/A", report.Grade);
            Assert.AreEqual(0, report.TotalTransactions);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GenerateReport_InvalidStaff_Throws()
        {
            _service.GenerateReport(999, _periodStart, _periodEnd);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GenerateReport_InvalidPeriod_Throws()
        {
            _service.GenerateReport(_alice.Id, _periodEnd, _periodStart);
        }

        [TestMethod]
        public void GenerateReport_IdentifiesStrengths()
        {
            SeedAliceTransactions();
            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);

            Assert.IsTrue(report.Strengths.Count > 0);
            Assert.IsTrue(report.Strengths.Any(s => s.Contains("satisfaction")));
        }

        [TestMethod]
        public void GenerateReport_IdentifiesImprovementAreas()
        {
            // Bob has low satisfaction (3.0 avg)
            SeedBobTransactions();
            var report = _service.GenerateReport(_bob.Id, _periodStart, _periodEnd);

            Assert.IsTrue(report.ImprovementAreas.Count > 0);
        }

        [TestMethod]
        public void GenerateReport_TransactionTypeCounts()
        {
            _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.Rental,
                revenue: 5m, timestamp: _periodStart.AddHours(10));
            _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.Return,
                revenue: 0m, timestamp: _periodStart.AddHours(11));
            _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.GiftCardSale,
                revenue: 25m, timestamp: _periodStart.AddHours(12));
            _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.MembershipUpgrade,
                revenue: 15m, timestamp: _periodStart.AddHours(13));

            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);

            Assert.AreEqual(1, report.RentalCount);
            Assert.AreEqual(1, report.ReturnCount);
            Assert.AreEqual(1, report.GiftCardSaleCount);
            Assert.AreEqual(1, report.MembershipUpgradeCount);
        }

        [TestMethod]
        public void GenerateReport_NoRatings_ImprovementNote()
        {
            RecordRental(_alice.Id, revenue: 5m, duration: 100, rating: null,
                ts: _periodStart.AddHours(10));

            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);

            Assert.IsTrue(report.ImprovementAreas.Any(a => a.Contains("No customer ratings")));
        }

        [TestMethod]
        public void GenerateReport_NoUpsellAttempts_ImprovementNote()
        {
            RecordRental(_alice.Id, upsell: false, ts: _periodStart.AddHours(10));
            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);

            Assert.IsTrue(report.ImprovementAreas.Any(a => a.Contains("upsell")));
        }

        [TestMethod]
        public void GenerateReport_OneStarWarning()
        {
            for (int i = 0; i < 5; i++)
            {
                RecordRental(_alice.Id, rating: 1, ts: _periodStart.AddDays(i).AddHours(10));
            }
            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);

            Assert.IsTrue(report.ImprovementAreas.Any(a => a.Contains("1-star")));
        }

        // ══════════════════════════════════════════════════════
        //  Leaderboard
        // ══════════════════════════════════════════════════════

        [TestMethod]
        public void GetLeaderboard_RanksStaffByScore()
        {
            SeedAliceTransactions();
            SeedBobTransactions();

            var board = _service.GetLeaderboard(_periodStart, _periodEnd);

            Assert.AreEqual(2, board.Count);
            Assert.AreEqual(1, board[0].Rank);
            Assert.AreEqual(2, board[1].Rank);
            Assert.IsTrue(board[0].Score >= board[1].Score);
        }

        [TestMethod]
        public void GetLeaderboard_ExcludesStaffWithNoTransactions()
        {
            SeedAliceTransactions();
            var board = _service.GetLeaderboard(_periodStart, _periodEnd);

            Assert.AreEqual(1, board.Count);
            Assert.AreEqual(_alice.Id, board[0].StaffId);
        }

        [TestMethod]
        public void GetLeaderboard_ExcludesDeactivatedStaff()
        {
            SeedAliceTransactions();
            SeedBobTransactions();
            _service.DeactivateStaff(_alice.Id);

            var board = _service.GetLeaderboard(_periodStart, _periodEnd);
            Assert.IsFalse(board.Any(e => e.StaffId == _alice.Id));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetLeaderboard_InvalidPeriod_Throws()
        {
            _service.GetLeaderboard(_periodEnd, _periodStart);
        }

        [TestMethod]
        public void GetLeaderboard_IncludesGrades()
        {
            SeedAliceTransactions();
            var board = _service.GetLeaderboard(_periodStart, _periodEnd);
            Assert.IsNotNull(board[0].Grade);
        }

        // ══════════════════════════════════════════════════════
        //  Team Summary
        // ══════════════════════════════════════════════════════

        [TestMethod]
        public void GetTeamSummary_AggregatesAllStaff()
        {
            SeedAliceTransactions();
            SeedBobTransactions();

            var summary = _service.GetTeamSummary(_periodStart, _periodEnd);

            Assert.AreEqual(2, summary.ActiveStaffCount);
            Assert.AreEqual(11, summary.TotalTransactions);
            Assert.AreEqual(74m, summary.TotalRevenue); // 65 + 9
        }

        [TestMethod]
        public void GetTeamSummary_CalculatesAverages()
        {
            SeedAliceTransactions();
            SeedBobTransactions();

            var summary = _service.GetTeamSummary(_periodStart, _periodEnd);

            Assert.AreEqual(5.5, summary.AvgTransactionsPerStaff, 0.01);
            Assert.IsTrue(summary.AvgRevenuePerStaff > 0);
            Assert.IsTrue(summary.TeamSatisfactionAvg > 0);
        }

        [TestMethod]
        public void GetTeamSummary_HasTopPerformer()
        {
            SeedAliceTransactions();
            SeedBobTransactions();

            var summary = _service.GetTeamSummary(_periodStart, _periodEnd);

            Assert.IsNotNull(summary.TopPerformer);
            Assert.AreEqual(_alice.Id, summary.TopPerformer.StaffId);
        }

        [TestMethod]
        public void GetTeamSummary_TransactionBreakdown()
        {
            SeedAliceTransactions();
            var summary = _service.GetTeamSummary(_periodStart, _periodEnd);

            Assert.IsTrue(summary.TransactionBreakdown.ContainsKey(StaffTransactionType.Rental));
        }

        [TestMethod]
        public void GetTeamSummary_ToTextReport_NotEmpty()
        {
            SeedAliceTransactions();
            var summary = _service.GetTeamSummary(_periodStart, _periodEnd);

            var text = summary.ToTextReport();
            Assert.IsTrue(text.Contains("TEAM PERFORMANCE REPORT"));
            Assert.IsTrue(text.Contains("Alice Johnson"));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetTeamSummary_InvalidPeriod_Throws()
        {
            _service.GetTeamSummary(_periodEnd, _periodStart);
        }

        [TestMethod]
        public void GetTeamSummary_NoTransactions_ReturnsEmpty()
        {
            var summary = _service.GetTeamSummary(_periodStart, _periodEnd);
            Assert.AreEqual(0, summary.TotalTransactions);
            Assert.AreEqual(0, summary.ActiveStaffCount);
        }

        // ══════════════════════════════════════════════════════
        //  Period Comparison
        // ══════════════════════════════════════════════════════

        [TestMethod]
        public void ComparePerformance_ReturnsDelta()
        {
            // Period 1: low performance
            RecordRental(_alice.Id, revenue: 5m, duration: 200, rating: 3,
                ts: new DateTime(2026, 2, 1, 10, 0, 0));

            // Period 2: better performance
            for (int i = 0; i < 5; i++)
            {
                RecordRental(_alice.Id, revenue: 15m, duration: 90, rating: 5,
                    upsell: true, upsellOk: true,
                    ts: new DateTime(2026, 3, 1 + i, 10, 0, 0));
            }

            var result = _service.ComparePerformance(_alice.Id,
                new DateTime(2026, 2, 1), new DateTime(2026, 2, 8),
                new DateTime(2026, 3, 1), new DateTime(2026, 3, 8));

            Assert.AreEqual(_alice.Id, (int)result["staffId"]);
            Assert.AreEqual(4, (int)result["transactionsDelta"]); // 5 - 1
            Assert.IsTrue((bool)result["improved"]);
        }

        [TestMethod]
        public void ComparePerformance_DetectsDecline()
        {
            // Period 1: high performance
            for (int i = 0; i < 5; i++)
            {
                RecordRental(_alice.Id, revenue: 20m, rating: 5,
                    ts: new DateTime(2026, 2, 1 + i, 10, 0, 0));
            }

            // Period 2: declined
            RecordRental(_alice.Id, revenue: 3m, rating: 2,
                ts: new DateTime(2026, 3, 1, 10, 0, 0));

            var result = _service.ComparePerformance(_alice.Id,
                new DateTime(2026, 2, 1), new DateTime(2026, 2, 8),
                new DateTime(2026, 3, 1), new DateTime(2026, 3, 8));

            Assert.IsFalse((bool)result["improved"]);
        }

        // ══════════════════════════════════════════════════════
        //  Hourly Activity
        // ══════════════════════════════════════════════════════

        [TestMethod]
        public void GetHourlyActivity_CorrectCounts()
        {
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 1, 10, 0, 0));
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 1, 10, 30, 0));
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 1, 14, 0, 0));

            var hourly = _service.GetHourlyActivity(_alice.Id, new DateTime(2026, 3, 1));

            Assert.AreEqual(2, hourly[10]);
            Assert.AreEqual(1, hourly[14]);
            Assert.AreEqual(0, hourly[8]);
            Assert.AreEqual(24, hourly.Count);
        }

        [TestMethod]
        public void GetHourlyActivity_EmptyDay_AllZeros()
        {
            var hourly = _service.GetHourlyActivity(_alice.Id, new DateTime(2026, 3, 1));
            Assert.IsTrue(hourly.Values.All(v => v == 0));
        }

        [TestMethod]
        public void GetPeakHours_ReturnsTopN()
        {
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 1, 10, 0, 0));
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 1, 10, 30, 0));
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 1, 10, 45, 0));
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 1, 14, 0, 0));
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 1, 14, 30, 0));
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 1, 16, 0, 0));

            var peaks = _service.GetPeakHours(_alice.Id,
                new DateTime(2026, 3, 1), new DateTime(2026, 3, 2), topN: 2);

            Assert.AreEqual(2, peaks.Count);
            Assert.AreEqual(10, peaks[0].Key); // hour 10 with 3 txns
            Assert.AreEqual(3, peaks[0].Value);
        }

        // ══════════════════════════════════════════════════════
        //  Feedback
        // ══════════════════════════════════════════════════════

        [TestMethod]
        public void GetFeedback_FiltersByRating()
        {
            RecordRental(_alice.Id, rating: 5, ts: _periodStart.AddHours(1));
            RecordRental(_alice.Id, rating: 2, ts: _periodStart.AddHours(2));
            RecordRental(_alice.Id, rating: 4, ts: _periodStart.AddHours(3));

            var negative = _service.GetFeedback(_alice.Id, maxRating: 3);
            Assert.AreEqual(1, negative.Count);
            Assert.AreEqual(2, negative[0].SatisfactionRating);
        }

        [TestMethod]
        public void GetFeedback_FiltersByDate()
        {
            RecordRental(_alice.Id, rating: 5, ts: new DateTime(2026, 3, 1, 10, 0, 0));
            RecordRental(_alice.Id, rating: 4, ts: new DateTime(2026, 3, 5, 10, 0, 0));

            var feedback = _service.GetFeedback(_alice.Id,
                from: new DateTime(2026, 3, 3), to: new DateTime(2026, 3, 6));
            Assert.AreEqual(1, feedback.Count);
        }

        [TestMethod]
        public void GetFeedback_ExcludesUnrated()
        {
            RecordRental(_alice.Id, rating: null, ts: _periodStart.AddHours(1));
            RecordRental(_alice.Id, rating: 5, ts: _periodStart.AddHours(2));

            var feedback = _service.GetFeedback(_alice.Id);
            Assert.AreEqual(1, feedback.Count);
        }

        [TestMethod]
        public void GetRatingDistribution_CorrectCounts()
        {
            RecordRental(_alice.Id, rating: 5, ts: _periodStart.AddHours(1));
            RecordRental(_alice.Id, rating: 5, ts: _periodStart.AddHours(2));
            RecordRental(_alice.Id, rating: 3, ts: _periodStart.AddHours(3));
            RecordRental(_alice.Id, rating: 1, ts: _periodStart.AddHours(4));

            var dist = _service.GetRatingDistribution(_alice.Id);

            Assert.AreEqual(5, dist.Count);
            Assert.AreEqual(1, dist[1]);
            Assert.AreEqual(0, dist[2]);
            Assert.AreEqual(1, dist[3]);
            Assert.AreEqual(0, dist[4]);
            Assert.AreEqual(2, dist[5]);
        }

        // ══════════════════════════════════════════════════════
        //  Streaks & Trends
        // ══════════════════════════════════════════════════════

        [TestMethod]
        public void GetFiveStarStreak_ConsecutiveFiveStars()
        {
            RecordRental(_alice.Id, rating: 4, ts: _periodStart.AddHours(1));
            RecordRental(_alice.Id, rating: 5, ts: _periodStart.AddHours(2));
            RecordRental(_alice.Id, rating: 5, ts: _periodStart.AddHours(3));
            RecordRental(_alice.Id, rating: 5, ts: _periodStart.AddHours(4));

            var streak = _service.GetFiveStarStreak(_alice.Id);
            Assert.AreEqual(3, streak);
        }

        [TestMethod]
        public void GetFiveStarStreak_BrokenByNonFive()
        {
            RecordRental(_alice.Id, rating: 5, ts: _periodStart.AddHours(1));
            RecordRental(_alice.Id, rating: 4, ts: _periodStart.AddHours(2));
            RecordRental(_alice.Id, rating: 5, ts: _periodStart.AddHours(3));

            var streak = _service.GetFiveStarStreak(_alice.Id);
            Assert.AreEqual(1, streak);
        }

        [TestMethod]
        public void GetFiveStarStreak_NoRatings_ReturnsZero()
        {
            Assert.AreEqual(0, _service.GetFiveStarStreak(_alice.Id));
        }

        [TestMethod]
        public void GetDailyTrend_CorrectCounts()
        {
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 1, 10, 0, 0));
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 1, 14, 0, 0));
            RecordRental(_alice.Id, ts: new DateTime(2026, 3, 3, 10, 0, 0));

            var trend = _service.GetDailyTrend(_alice.Id,
                new DateTime(2026, 3, 1), new DateTime(2026, 3, 3));

            Assert.AreEqual(3, trend.Count); // 3 days
            Assert.AreEqual(2, trend[0].Value); // March 1
            Assert.AreEqual(0, trend[1].Value); // March 2
            Assert.AreEqual(1, trend[2].Value); // March 3
        }

        // ══════════════════════════════════════════════════════
        //  Scoring & Grading
        // ══════════════════════════════════════════════════════

        [TestMethod]
        public void Score_HighPerformer_HighScore()
        {
            // High volume, high revenue, fast, great satisfaction, good upsell
            for (int i = 0; i < 20; i++)
            {
                RecordRental(_alice.Id, revenue: 40m, duration: 60, rating: 5,
                    upsell: true, upsellOk: true,
                    ts: _periodStart.AddDays(i % 7).AddHours(10 + i % 8));
            }

            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);
            Assert.IsTrue(report.PerformanceScore >= 70);
            Assert.IsTrue(report.Grade == "A" || report.Grade == "B");
        }

        [TestMethod]
        public void Score_LowPerformer_LowScore()
        {
            // 1 transaction, low revenue, slow, bad rating, no upsell
            RecordRental(_alice.Id, revenue: 2m, duration: 600, rating: 1,
                ts: _periodStart.AddHours(10));

            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);
            Assert.IsTrue(report.PerformanceScore < 50);
        }

        [TestMethod]
        public void Score_CustomWeights_ChangesResult()
        {
            var satisfactionOnly = new StaffPerformanceService(0, 0, 1, 0, 0);
            var volumeOnly = new StaffPerformanceService(1, 0, 0, 0, 0);

            var alice1 = satisfactionOnly.AddStaff("Alice", StaffRole.Clerk);
            var alice2 = volumeOnly.AddStaff("Alice", StaffRole.Clerk);

            // 1 transaction with perfect rating
            satisfactionOnly.RecordTransaction(alice1.Id, 100, StaffTransactionType.Rental,
                revenue: 5m, satisfactionRating: 5,
                timestamp: _periodStart.AddHours(10));
            volumeOnly.RecordTransaction(alice2.Id, 100, StaffTransactionType.Rental,
                revenue: 5m, satisfactionRating: 5,
                timestamp: _periodStart.AddHours(10));

            var r1 = satisfactionOnly.GenerateReport(alice1.Id, _periodStart, _periodEnd);
            var r2 = volumeOnly.GenerateReport(alice2.Id, _periodStart, _periodEnd);

            // Satisfaction-only should score high (5/5 = 100 satisfaction),
            // volume-only should score low (1 txn / 7 days is low volume)
            Assert.IsTrue(r1.PerformanceScore > r2.PerformanceScore);
        }

        // ══════════════════════════════════════════════════════
        //  Edge Cases
        // ══════════════════════════════════════════════════════

        [TestMethod]
        public void GenerateReport_ZeroDurationTransactions_Handled()
        {
            RecordRental(_alice.Id, duration: 0, ts: _periodStart.AddHours(10));
            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);

            Assert.AreEqual(0, report.AverageTransactionSeconds);
            Assert.AreEqual(1, report.TotalTransactions);
        }

        [TestMethod]
        public void GenerateReport_AllTransactionTypes()
        {
            _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.Rental,
                timestamp: _periodStart.AddHours(1));
            _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.Return,
                timestamp: _periodStart.AddHours(2));
            _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.Reservation,
                timestamp: _periodStart.AddHours(3));
            _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.GiftCardSale,
                revenue: 25m, timestamp: _periodStart.AddHours(4));
            _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.MembershipUpgrade,
                revenue: 10m, timestamp: _periodStart.AddHours(5));

            var report = _service.GenerateReport(_alice.Id, _periodStart, _periodEnd);
            Assert.AreEqual(5, report.TotalTransactions);
            Assert.AreEqual(1, report.ReservationCount);
        }

        [TestMethod]
        public void StaffMember_TenureDays_Computed()
        {
            Assert.IsTrue(_carol.TenureDays > 0);
        }

        [TestMethod]
        public void TeamSummary_UpsellRate_Correct()
        {
            _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.Rental,
                upsellAttempted: true, upsellAccepted: true,
                timestamp: _periodStart.AddHours(1));
            _service.RecordTransaction(_alice.Id, 100, StaffTransactionType.Rental,
                upsellAttempted: true, upsellAccepted: false,
                timestamp: _periodStart.AddHours(2));

            var summary = _service.GetTeamSummary(_periodStart, _periodEnd);
            Assert.AreEqual(0.5, summary.TeamUpsellRate, 0.01);
        }

        [TestMethod]
        public void Leaderboard_SatisfactionAvgForUnrated_IsZero()
        {
            RecordRental(_alice.Id, rating: null, ts: _periodStart.AddHours(10));
            var board = _service.GetLeaderboard(_periodStart, _periodEnd);
            Assert.AreEqual(0, board[0].SatisfactionAvg);
        }
    }
}

