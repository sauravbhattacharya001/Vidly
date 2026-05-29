using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Services;
using static Vidly.Services.StaffTurnoverRiskAdvisorService;

namespace Vidly.Tests.Services
{
    [TestClass]
    public class StaffTurnoverRiskAdvisorServiceTests
    {
        private static readonly IClock FixedClock = new FixedTestClock(new DateTime(2026, 5, 28, 12, 0, 0));

        private class FixedTestClock : IClock
        {
            private readonly DateTime _now;
            public FixedTestClock(DateTime now) { _now = now; }
            public DateTime Now => _now;
            public DateTime Today => _now.Date;
        }

        private StaffTurnoverRiskAdvisorService CreateService() =>
            new StaffTurnoverRiskAdvisorService(FixedClock);

        private StaffSnapshot MakeStableStaff(int id = 1) => new StaffSnapshot
        {
            StaffId = id,
            Name = $"Staff_{id}",
            Role = "Associate",
            HireDate = new DateTime(2024, 1, 1),
            IsActive = true,
            RecentPerformanceScore = 85,
            PriorPerformanceScore = 83,
            RecentTransactionCount = 50,
            PriorTransactionCount = 48,
            RecentSatisfactionAvg = 4.2,
            PriorSatisfactionAvg = 4.0,
            LateArrivalsLast30Days = 1,
            AbsencesLast30Days = 0,
            ShiftSwapRequestsLast30Days = 0,
            MonthsSinceLastRaise = 8,
            MonthsSinceLastPromotion = 12,
            HasReceivedRecognitionLast90Days = true,
            WeeklyHoursAvg = 38,
            TeamWeeklyHoursAvg = 38,
            HasUpdatedResumeOnline = false,
            HasDeclinedExtraShifts = false
        };

        [TestMethod]
        public void EmptyRoster_ReturnsGradeA()
        {
            var svc = CreateService();
            var report = svc.Analyze(new List<StaffSnapshot>());
            Assert.AreEqual("A", report.Grade);
            Assert.AreEqual(0, report.TotalStaff);
            Assert.IsTrue(report.Insights.Contains("EMPTY_ROSTER"));
        }

        [TestMethod]
        public void StableStaff_VerdictStableOrThriving()
        {
            var svc = CreateService();
            var report = svc.Analyze(new[] { MakeStableStaff() });
            var a = report.Assessments[0];
            Assert.IsTrue(a.Verdict == TurnoverVerdict.Thriving || a.Verdict == TurnoverVerdict.Stable);
            Assert.AreEqual(3, a.Priority);
        }

        [TestMethod]
        public void SharpDecline_PlusResume_FlightRiskImminent()
        {
            var svc = CreateService();
            var s = MakeStableStaff();
            s.PriorPerformanceScore = 90;
            s.RecentPerformanceScore = 55;
            s.HasUpdatedResumeOnline = true;
            s.LateArrivalsLast30Days = 5;
            s.AbsencesLast30Days = 4;
            var report = svc.Analyze(new[] { s });
            var a = report.Assessments[0];
            Assert.AreEqual(TurnoverVerdict.FlightRiskImminent, a.Verdict);
            Assert.AreEqual(0, a.Priority);
            Assert.IsTrue(a.RiskScore >= 70);
        }

        [TestMethod]
        public void CompensationStagnation_ElevatedRisk()
        {
            var svc = CreateService();
            var s = MakeStableStaff();
            s.MonthsSinceLastRaise = 26;
            s.MonthsSinceLastPromotion = 38;
            s.RecentPerformanceScore = 70;
            s.PriorPerformanceScore = 82;
            s.HasReceivedRecognitionLast90Days = false;
            var report = svc.Analyze(new[] { s });
            var a = report.Assessments[0];
            Assert.IsTrue(a.Priority <= 2);
            Assert.IsTrue(a.Reasons.Contains("COMPENSATION_STAGNATION"));
        }

        [TestMethod]
        public void Overwork_Detected()
        {
            var svc = CreateService();
            var s = MakeStableStaff();
            s.WeeklyHoursAvg = 60;
            s.TeamWeeklyHoursAvg = 38;
            var report = svc.Analyze(new[] { s });
            var a = report.Assessments[0];
            Assert.IsTrue(a.Reasons.Contains("SEVERE_OVERWORK"));
        }

        [TestMethod]
        public void CautiousAppetite_HigherRiskScore()
        {
            var svc = CreateService();
            var s = MakeStableStaff();
            s.PriorPerformanceScore = 85;
            s.RecentPerformanceScore = 60;
            s.LateArrivalsLast30Days = 5;

            var balanced = svc.Analyze(new[] { s }, RiskAppetite.Balanced);
            var cautious = svc.Analyze(new[] { s }, RiskAppetite.Cautious);
            Assert.IsTrue(cautious.Assessments[0].RiskScore >= balanced.Assessments[0].RiskScore);
        }

        [TestMethod]
        public void AggressiveAppetite_LowerRiskScore()
        {
            var svc = CreateService();
            var s = MakeStableStaff();
            s.PriorPerformanceScore = 85;
            s.RecentPerformanceScore = 60;
            s.LateArrivalsLast30Days = 5;

            var balanced = svc.Analyze(new[] { s }, RiskAppetite.Balanced);
            var aggressive = svc.Analyze(new[] { s }, RiskAppetite.Aggressive);
            Assert.IsTrue(aggressive.Assessments[0].RiskScore <= balanced.Assessments[0].RiskScore);
        }

        [TestMethod]
        public void MultipleP0_GradeF()
        {
            var svc = CreateService();
            var staff = Enumerable.Range(1, 4).Select(i =>
            {
                var s = MakeStableStaff(i);
                s.PriorPerformanceScore = 90;
                s.RecentPerformanceScore = 40;
                s.HasUpdatedResumeOnline = true;
                s.AbsencesLast30Days = 6;
                s.LateArrivalsLast30Days = 9;
                return s;
            }).ToList();

            var report = svc.Analyze(staff);
            Assert.AreEqual("F", report.Grade);
            Assert.IsTrue(report.P0Count >= 3);
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "ESCALATE_TO_LEADERSHIP"));
        }

        [TestMethod]
        public void Playbook_EmergencyRetention_WhenP0Present()
        {
            var svc = CreateService();
            var s = MakeStableStaff();
            s.PriorPerformanceScore = 90;
            s.RecentPerformanceScore = 45;
            s.HasUpdatedResumeOnline = true;
            s.AbsencesLast30Days = 5;
            var report = svc.Analyze(new[] { s });
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "EMERGENCY_RETENTION_INTERVENTION"));
        }

        [TestMethod]
        public void Playbook_AggressiveTrimsP3()
        {
            var svc = CreateService();
            var s = MakeStableStaff();
            s.PriorPerformanceScore = 90;
            s.RecentPerformanceScore = 50;
            s.HasUpdatedResumeOnline = true;
            s.AbsencesLast30Days = 5;
            var report = svc.Analyze(new[] { s }, RiskAppetite.Aggressive);
            Assert.IsFalse(report.Playbook.Any(p => p.Priority == 3));
        }

        [TestMethod]
        public void Insights_WorkloadImbalance()
        {
            var svc = CreateService();
            var staff = Enumerable.Range(1, 3).Select(i =>
            {
                var s = MakeStableStaff(i);
                s.WeeklyHoursAvg = 58;
                s.TeamWeeklyHoursAvg = 38;
                return s;
            }).ToList();

            var report = svc.Analyze(staff);
            Assert.IsTrue(report.Insights.Contains("WORKLOAD_IMBALANCE_PATTERN"));
        }

        [TestMethod]
        public void InactiveStaff_Excluded()
        {
            var svc = CreateService();
            var s = MakeStableStaff();
            s.IsActive = false;
            var report = svc.Analyze(new[] { s });
            Assert.AreEqual(0, report.TotalStaff);
        }

        [TestMethod]
        public void FormatMarkdown_ContainsSections()
        {
            var svc = CreateService();
            var report = svc.Analyze(new[] { MakeStableStaff() });
            var md = svc.FormatMarkdown(report);
            Assert.IsTrue(md.Contains("## Staff Turnover Risk Report"));
            Assert.IsTrue(md.Contains("## Assessments"));
            Assert.IsTrue(md.Contains("## Playbook"));
            Assert.IsTrue(md.Contains("## Insights"));
        }

        [TestMethod]
        public void FormatText_ContainsHeadline()
        {
            var svc = CreateService();
            var report = svc.Analyze(new[] { MakeStableStaff() });
            var txt = svc.FormatText(report);
            Assert.IsTrue(txt.StartsWith("VERDICT:"));
        }

        [TestMethod]
        public void NewHire_InsufficientData()
        {
            var svc = CreateService();
            var s = MakeStableStaff();
            s.HireDate = new DateTime(2026, 5, 1); // less than 3 months tenure
            s.RecentPerformanceScore = 60;
            s.PriorPerformanceScore = 60;
            s.RecentTransactionCount = 10;
            s.PriorTransactionCount = 10;
            s.LateArrivalsLast30Days = 0;
            s.AbsencesLast30Days = 0;
            s.HasReceivedRecognitionLast90Days = true;
            var report = svc.Analyze(new[] { s });
            Assert.AreEqual(TurnoverVerdict.InsufficientData, report.Assessments[0].Verdict);
        }
    }
}
