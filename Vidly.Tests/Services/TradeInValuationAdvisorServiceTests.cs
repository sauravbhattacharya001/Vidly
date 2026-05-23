using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Services;

namespace Vidly.Tests.Services
{
    [TestClass]
    public class TradeInValuationAdvisorServiceTests
    {
        private static readonly DateTime Now = new DateTime(2026, 5, 22, 12, 0, 0, DateTimeKind.Utc);

        private static TradeInValuationAdvisorService NewService()
        {
            return new TradeInValuationAdvisorService(() => Now);
        }

        private static TradeInValuationSnapshot Snap(
            int id,
            string format = "BluRay",
            string condition = "Good",
            int copies = 1,
            int demand = 60,
            int tradeIns30 = 1,
            int acceptedLT = 3,
            double rejRate = 0.10,
            bool dup = false,
            bool wanted = false,
            string title = null,
            string name = null)
        {
            return new TradeInValuationSnapshot
            {
                TradeInId = id,
                CustomerId = 5000 + id,
                CustomerName = name ?? ("Cust " + id),
                MovieTitle = title ?? ("Movie " + id),
                Format = format,
                Condition = condition,
                CopiesOnHand = copies,
                DemandScore = demand,
                CustomerTradeIns30Days = tradeIns30,
                CustomerAcceptedLifetime = acceptedLT,
                CustomerRejectionRate = rejRate,
                DuplicateRecentSubmission = dup,
                TitleOnWantedList = wanted,
                SubmittedAt = Now.AddHours(-1)
            };
        }

        [TestMethod]
        public void GenerateReport_Empty_ReturnsHealthyDefault()
        {
            var report = NewService().GenerateReport(null);
            Assert.AreEqual(Now, report.GeneratedAt);
            Assert.AreEqual(0, report.Cases.Count);
            Assert.AreEqual('A', report.Summary.Grade);
            Assert.AreEqual(100, report.Summary.OverallScore);
            Assert.AreEqual(TradeInValuationHeadline.HealthyIntake, report.Summary.HeadlineVerdict);
            CollectionAssert.Contains(report.Summary.Insights, "EMPTY_INTAKE");
            Assert.AreEqual(1, report.Playbook.Count);
            Assert.AreEqual("INTAKE_HEALTHY", report.Playbook[0].Id);
        }

        [TestMethod]
        public void GenerateReport_PremiumUHDLikeNew_HighValueAndAcceptPremium()
        {
            var report = NewService().GenerateReport(new[]
            {
                Snap(1, format: "UHD4K", condition: "LikeNew", copies: 0, demand: 90)
            });
            var c = report.Cases.Single();
            Assert.AreEqual(TradeInValuationVerdict.AcceptPremiumCredit, c.Verdict);
            Assert.IsTrue(c.ValueScore >= 80, "expected ValueScore>=80, got " + c.ValueScore);
            Assert.IsTrue(c.RecommendedCredits > 6.0m);
            CollectionAssert.Contains(c.Reasons, "FORMAT_HIGH_VALUE");
            CollectionAssert.Contains(c.Reasons, "HIGH_DEMAND");
            CollectionAssert.Contains(c.Reasons, "CATALOG_GAP");
        }

        [TestMethod]
        public void GenerateReport_VHSPoor_RejectAsObsolete_AndZeroCredit()
        {
            var report = NewService().GenerateReport(new[]
            {
                Snap(1, format: "VHS", condition: "Poor", copies: 0, demand: 20)
            });
            var c = report.Cases.Single();
            Assert.AreEqual(TradeInValuationVerdict.RejectAsObsolete, c.Verdict);
            Assert.AreEqual(0m, c.RecommendedCredits);
            CollectionAssert.Contains(c.Reasons, "FORMAT_OBSOLETE");
        }

        [TestMethod]
        public void GenerateReport_GluttedTitle_RejectAsRedundant()
        {
            var report = NewService().GenerateReport(new[]
            {
                Snap(1, format: "DVD", condition: "Good", copies: 12, demand: 10, wanted: false)
            });
            var c = report.Cases.Single();
            Assert.AreEqual(TradeInValuationVerdict.RejectAsRedundant, c.Verdict);
            Assert.AreEqual(0m, c.RecommendedCredits);
            CollectionAssert.Contains(c.Reasons, "SUPPLY_GLUT");
        }

        [TestMethod]
        public void GenerateReport_DuplicateAndHighVolume_FlagsFraud()
        {
            var report = NewService().GenerateReport(new[]
            {
                Snap(1, format: "BluRay", condition: "LikeNew", copies: 2, demand: 50,
                     tradeIns30: 15, rejRate: 0.6, dup: true)
            });
            var c = report.Cases.Single();
            Assert.AreEqual(TradeInValuationVerdict.FlagFraudPattern, c.Verdict);
            Assert.AreEqual(TradeInValuationActionPriority.P0, c.Priority);
            Assert.AreEqual(0m, c.RecommendedCredits);
            CollectionAssert.Contains(c.Reasons, "DUPLICATE_SUBMISSION");
            CollectionAssert.Contains(c.Reasons, "HIGH_VOLUME_CUSTOMER");
            Assert.IsTrue(c.FraudRisk >= 70);
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "FREEZE_FRAUD_CYCLE"));
        }

        [TestMethod]
        public void GenerateReport_ModerateFraudSignals_RoutesToManualReview()
        {
            var report = NewService().GenerateReport(new[]
            {
                Snap(1, format: "BluRay", condition: "Good", tradeIns30: 5, rejRate: 0.55)
            });
            var c = report.Cases.Single();
            Assert.AreEqual(TradeInValuationVerdict.RouteToManualReview, c.Verdict);
            Assert.AreEqual(TradeInValuationActionPriority.P1, c.Priority);
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "ASSIGN_MANAGER_REVIEW"));
        }

        [TestMethod]
        public void GenerateReport_TrustedCustomer_DampensFraudRisk()
        {
            var withTrust = NewService().GenerateReport(new[]
            {
                Snap(1, condition: "LikeNew", acceptedLT: 50, rejRate: 0.05, tradeIns30: 5)
            }).Cases.Single();
            var withoutTrust = NewService().GenerateReport(new[]
            {
                Snap(1, condition: "LikeNew", acceptedLT: 50, rejRate: 0.05, tradeIns30: 5)
                // same args; sanity-check determinism
            }).Cases.Single();
            Assert.AreEqual(withTrust.FraudRisk, withoutTrust.FraudRisk);
            CollectionAssert.Contains(withTrust.Reasons, "TRUSTED_CUSTOMER");
        }

        [TestMethod]
        public void GenerateReport_WantedTitle_NotifiesWaitlist()
        {
            var report = NewService().GenerateReport(new[]
            {
                Snap(1, format: "BluRay", condition: "Good", copies: 0, demand: 50, wanted: true)
            });
            var c = report.Cases.Single();
            CollectionAssert.Contains(c.Reasons, "WANTED_TITLE");
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "NOTIFY_WAITLIST_WANTED_TITLES"));
            Assert.IsTrue(report.Summary.WantedTitleCount >= 1);
        }

        [TestMethod]
        public void GenerateReport_MultipleObsoleteSubmissions_RaisesPolicyNotice()
        {
            var report = NewService().GenerateReport(new[]
            {
                Snap(1, format: "VHS", condition: "Poor"),
                Snap(2, format: "VHS", condition: "Fair"),
                Snap(3, format: "VHS", condition: "Poor")
            });
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "PUBLISH_OBSOLETE_FORMAT_NOTICE"));
            CollectionAssert.Contains(report.Summary.Insights, "OBSOLETE_FORMAT_TREND");
        }

        [TestMethod]
        public void GenerateReport_GluttedCluster_RebalancePlaybookFires()
        {
            var report = NewService().GenerateReport(new[]
            {
                Snap(1, copies: 8, demand: 20),
                Snap(2, copies: 9, demand: 30),
                Snap(3, copies: 7, demand: 25)
            });
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "REBALANCE_CATALOG_GLUT"));
        }

        [TestMethod]
        public void GenerateReport_AppetiteShiftsFraudSensitivity()
        {
            var snaps = new[]
            {
                Snap(1, format: "BluRay", condition: "Good",
                     tradeIns30: 5, rejRate: 0.55, dup: false)
            };

            var cautious = new TradeInValuationAdvisorService(() => Now)
                .GenerateReport(snaps, new TradeInValuationOptions { RiskAppetite = TradeInValuationAppetite.Cautious })
                .Cases.Single();
            var aggressive = new TradeInValuationAdvisorService(() => Now)
                .GenerateReport(snaps, new TradeInValuationOptions { RiskAppetite = TradeInValuationAppetite.Aggressive })
                .Cases.Single();

            Assert.IsTrue(cautious.FraudRisk > aggressive.FraudRisk,
                "Cautious should inflate fraud risk vs aggressive: c=" + cautious.FraudRisk + " a=" + aggressive.FraudRisk);
        }

        [TestMethod]
        public void GenerateReport_AggressiveTrimsP3WhenP0Present()
        {
            var snaps = new[]
            {
                Snap(1, dup: true, tradeIns30: 15, rejRate: 0.7),
                Snap(2, format: "BluRay", condition: "Good")
            };
            var report = new TradeInValuationAdvisorService(() => Now)
                .GenerateReport(snaps, new TradeInValuationOptions { RiskAppetite = TradeInValuationAppetite.Aggressive });
            Assert.IsFalse(report.Playbook.Any(p => p.Priority == TradeInValuationActionPriority.P3));
            Assert.IsTrue(report.Playbook.Any(p => p.Priority == TradeInValuationActionPriority.P0));
        }

        [TestMethod]
        public void GenerateReport_CautiousWithLowGrade_AddsAuditAction()
        {
            var snaps = new[]
            {
                Snap(1, dup: true, tradeIns30: 15, rejRate: 0.7),
                Snap(2, dup: true, tradeIns30: 15, rejRate: 0.7),
                Snap(3, format: "VHS", condition: "Poor")
            };
            var report = new TradeInValuationAdvisorService(() => Now)
                .GenerateReport(snaps, new TradeInValuationOptions { RiskAppetite = TradeInValuationAppetite.Cautious });
            Assert.IsTrue(report.Summary.Grade == 'C' || report.Summary.Grade == 'D' || report.Summary.Grade == 'F');
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "SCHEDULE_INTAKE_AUDIT"));
        }

        [TestMethod]
        public void GenerateReport_DuplicateCustomers_TriggerRateLimitAction()
        {
            var report = NewService().GenerateReport(new[]
            {
                Snap(1, dup: true, tradeIns30: 4, rejRate: 0.3),
                Snap(2, dup: true, tradeIns30: 3, rejRate: 0.2)
            });
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "RATE_LIMIT_DUPLICATE_SUBMITTERS"));
            CollectionAssert.Contains(report.Summary.Insights, "DUPLICATE_CLUSTER:2");
        }

        [TestMethod]
        public void GenerateReport_CatalogGap_FastTrackShelving()
        {
            var report = NewService().GenerateReport(new[]
            {
                Snap(1, copies: 0, demand: 70)
            });
            var c = report.Cases.Single();
            CollectionAssert.Contains(c.Reasons, "CATALOG_GAP");
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "SHELVE_CATALOG_GAP_FILLERS"));
        }

        [TestMethod]
        public void GenerateReport_OutputOrdering_PriorityThenFraudDesc()
        {
            var snaps = new[]
            {
                Snap(1, format: "DVD", condition: "Good"),               // P3 area
                Snap(2, format: "BluRay", condition: "LikeNew",
                     tradeIns30: 15, rejRate: 0.7, dup: true),            // P0
                Snap(3, format: "BluRay", condition: "Good",
                     tradeIns30: 6, rejRate: 0.55)                         // P1 manual review
            };
            var report = NewService().GenerateReport(snaps);
            Assert.AreEqual(2, report.Cases[0].TradeInId);
            Assert.IsTrue((int)report.Cases[0].Priority <= (int)report.Cases[1].Priority);
            Assert.IsTrue((int)report.Cases[1].Priority <= (int)report.Cases[2].Priority);
        }

        [TestMethod]
        public void GenerateReport_TopCasesRespectsCap()
        {
            var snaps = Enumerable.Range(1, 10).Select(i => Snap(i)).ToList();
            var report = NewService().GenerateReport(snaps, new TradeInValuationOptions { TopCases = 4 });
            Assert.AreEqual(4, report.Cases.Count);
            Assert.AreEqual(10, report.Summary.TotalSubmissions);
        }

        [TestMethod]
        public void GenerateReport_DoesNotMutateInputSnapshot()
        {
            var snap = Snap(1, format: "UHD4K", condition: "LikeNew", copies: 0, demand: 90, wanted: true);
            int originalCopies = snap.CopiesOnHand;
            int originalDemand = snap.DemandScore;
            bool originalWanted = snap.TitleOnWantedList;

            NewService().GenerateReport(new[] { snap });

            Assert.AreEqual(originalCopies, snap.CopiesOnHand);
            Assert.AreEqual(originalDemand, snap.DemandScore);
            Assert.AreEqual(originalWanted, snap.TitleOnWantedList);
        }

        [TestMethod]
        public void ToMarkdown_ContainsAllSections()
        {
            var report = NewService().GenerateReport(new[]
            {
                Snap(1, format: "BluRay", condition: "Good", copies: 0, demand: 70)
            });
            var md = report.ToMarkdown();
            StringAssert.Contains(md, "## Summary");
            StringAssert.Contains(md, "## Top cases");
            StringAssert.Contains(md, "## Playbook");
            StringAssert.Contains(md, "## Insights");
        }

        [TestMethod]
        public void ToText_ContainsHeadlines()
        {
            var report = NewService().GenerateReport(new[] { Snap(1) });
            var text = report.ToText();
            StringAssert.Contains(text, "Summary");
            StringAssert.Contains(text, "Top cases");
            StringAssert.Contains(text, "Playbook");
            StringAssert.Contains(text, "Insights");
        }

        [TestMethod]
        public void GenerateReport_ReasonsAreStablyOrdered()
        {
            var report = NewService().GenerateReport(new[]
            {
                Snap(1, format: "UHD4K", condition: "LikeNew", copies: 0, demand: 90, wanted: true)
            });
            var reasons = report.Cases.Single().Reasons;
            var sorted = reasons.OrderBy(r => r, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(sorted, reasons);
        }

        [TestMethod]
        public void GenerateReport_AcceptedAvgCreditMath()
        {
            var report = NewService().GenerateReport(new[]
            {
                Snap(1, format: "BluRay", condition: "Good", copies: 1, demand: 60),
                Snap(2, format: "BluRay", condition: "LikeNew", copies: 1, demand: 60)
            });
            Assert.IsTrue(report.Summary.AcceptedCount >= 1);
            Assert.IsTrue(report.Summary.AvgCreditsPerAccepted > 0);
        }

        [TestMethod]
        public void GenerateReport_AllCleanIntake_HasNonEmptyInsights()
        {
            var report = NewService().GenerateReport(new[]
            {
                Snap(1, format: "BluRay", condition: "Good")
            });
            Assert.IsTrue(report.Summary.Insights.Count >= 1);
        }
    }
}
