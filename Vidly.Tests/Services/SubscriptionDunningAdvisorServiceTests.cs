using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Services;

namespace Vidly.Tests.Services
{
    [TestClass]
    public class SubscriptionDunningAdvisorServiceTests
    {
        private static readonly DateTime Now = new DateTime(2026, 5, 22, 12, 0, 0, DateTimeKind.Utc);

        private static SubscriptionDunningAdvisorService NewService()
        {
            return new SubscriptionDunningAdvisorService(() => Now);
        }

        private static SubscriptionDunningSnapshot Snap(
            int id,
            int attempts = 1,
            int daysSinceFail = 1,
            SubscriptionDunningFailureReason reason = SubscriptionDunningFailureReason.InsufficientFunds,
            SubscriptionDunningTier tier = SubscriptionDunningTier.Standard,
            decimal mrr = 12.99m,
            decimal ltv = 200m,
            int tenure = 6,
            bool cardExpired = false,
            bool backup = false,
            bool emailEng = false,
            int rentals30 = 1,
            bool ticket = false,
            bool prevPaused = false,
            string name = null)
        {
            return new SubscriptionDunningSnapshot
            {
                SubscriptionId = id,
                CustomerId = 1000 + id,
                CustomerName = name ?? ("Cust " + id),
                Tier = tier,
                MonthlyRevenue = mrr,
                LifetimeRevenue = ltv,
                TenureMonths = tenure,
                LastFailedAt = Now.AddDays(-daysSinceFail),
                FailedAttempts = attempts,
                LastFailureReason = reason,
                CardExpired = cardExpired,
                HasBackupPaymentMethod = backup,
                RecentEmailEngagement = emailEng,
                RentalsLast30Days = rentals30,
                HasOpenBillingTicket = ticket,
                PreviouslyAutoPauseRecovered = prevPaused
            };
        }

        [TestMethod]
        public void GenerateReport_EmptyInput_ReturnsEmptyButValidReport()
        {
            var svc = NewService();
            var report = svc.GenerateReport(null);

            Assert.AreEqual(Now, report.GeneratedAt);
            Assert.AreEqual(0, report.Cases.Count);
            Assert.AreEqual(0, report.Summary.TotalSubscriptions);
            // Empty portfolio = perfect score.
            Assert.AreEqual('A', report.Summary.Grade);
            Assert.AreEqual(SubscriptionDunningHeadline.PortfolioHealthy, report.Summary.HeadlineVerdict);
            Assert.IsTrue(report.Summary.Insights.Contains("EMPTY_PORTFOLIO"));
            Assert.AreEqual(1, report.Playbook.Count);
            Assert.AreEqual("PORTFOLIO_HEALTHY", report.Playbook[0].Id);
        }

        [TestMethod]
        public void GenerateReport_AllCurrent_ReportsHealthyAndAllCurrentInsight()
        {
            var svc = NewService();
            var report = svc.GenerateReport(new[]
            {
                Snap(1, attempts: 0, reason: SubscriptionDunningFailureReason.Unknown),
                Snap(2, attempts: 0, reason: SubscriptionDunningFailureReason.Unknown),
            });
            Assert.AreEqual(2, report.Summary.CurrentSubscriptions);
            Assert.AreEqual('A', report.Summary.Grade);
            Assert.IsTrue(report.Summary.Insights.Contains("ALL_CURRENT"));
            // Recovery probability is 1.0 with no delinquent accounts.
            Assert.AreEqual(1.0, report.Summary.DunningRecoveryProbability, 1e-9);
        }

        [TestMethod]
        public void GenerateReport_FraudBlock_EscalatesToPauseHoldAndFraudReviewP0()
        {
            var svc = NewService();
            var report = svc.GenerateReport(new[]
            {
                Snap(1, attempts: 1, reason: SubscriptionDunningFailureReason.FraudBlock)
            });
            var c = report.Cases.Single();
            Assert.AreEqual(SubscriptionDunningVerdict.PauseHold, c.Verdict);
            Assert.AreEqual(SubscriptionDunningActionPriority.P0, c.Priority);
            Assert.IsTrue(c.Reasons.Contains("FRAUD_BLOCK"));

            var fraud = report.Playbook.SingleOrDefault(a => a.Id == "FRAUD_REVIEW");
            Assert.IsNotNull(fraud);
            Assert.AreEqual(SubscriptionDunningActionPriority.P0, fraud.Priority);
            CollectionAssert.AreEqual(new[] { 1 }, fraud.TargetSubscriptionIds.ToArray());
            Assert.IsTrue(report.Summary.Insights.Contains("FRAUD_REVIEW_REQUIRED"));
        }

        [TestMethod]
        public void GenerateReport_DisputedCharge_ForcesTerminalChargeAndWriteDown()
        {
            var svc = NewService();
            var report = svc.GenerateReport(new[]
            {
                Snap(1, attempts: 2, reason: SubscriptionDunningFailureReason.Disputed)
            });
            var c = report.Cases.Single();
            Assert.AreEqual(SubscriptionDunningVerdict.TerminalCharge, c.Verdict);
            Assert.AreEqual(SubscriptionDunningActionPriority.P0, c.Priority);
            Assert.IsTrue(report.Playbook.Any(a => a.Id == "WRITE_DOWN_TERMINAL"));
        }

        [TestMethod]
        public void GenerateReport_CycleExhausted_ProducesForceCancelP0()
        {
            var svc = NewService();
            var report = svc.GenerateReport(new[]
            {
                Snap(1, attempts: 4, daysSinceFail: 15)
            });
            var c = report.Cases.Single();
            Assert.AreEqual(SubscriptionDunningVerdict.ForceCancel, c.Verdict);
            Assert.AreEqual(SubscriptionDunningActionPriority.P0, c.Priority);
            Assert.IsTrue(c.Reasons.Contains("DUNNING_CYCLE_EXHAUSTED"));
            Assert.IsTrue(report.Playbook.Any(a => a.Id == "FORCE_CANCEL_CYCLE_EXHAUSTED"));
        }

        [TestMethod]
        public void GenerateReport_InsufficientFunds_PremiumLongTenure_RoutesToDowngradeOffer()
        {
            var svc = NewService();
            var report = svc.GenerateReport(new[]
            {
                Snap(
                    1,
                    attempts: 2,
                    daysSinceFail: 3,
                    reason: SubscriptionDunningFailureReason.InsufficientFunds,
                    tier: SubscriptionDunningTier.Premium,
                    tenure: 36)
            });
            var c = report.Cases.Single();
            Assert.AreEqual(SubscriptionDunningVerdict.DowngradeOffer, c.Verdict);
            Assert.IsTrue(c.Reasons.Contains("PRICE_SENSITIVITY_SIGNAL"));
            Assert.IsTrue(report.Playbook.Any(a => a.Id == "OFFER_DOWNGRADE"));
        }

        [TestMethod]
        public void GenerateReport_AutoPauseGuard_DoesNotPauseTwice()
        {
            var svc = NewService();
            var report = svc.GenerateReport(new[]
            {
                Snap(
                    1,
                    attempts: 3,
                    daysSinceFail: 5,
                    reason: SubscriptionDunningFailureReason.IssuerDecline,
                    prevPaused: true)
            });
            var c = report.Cases.Single();
            // Was previously auto-paused, so guard kicks in - never lands on PauseHold for non-fraud.
            Assert.AreNotEqual(SubscriptionDunningVerdict.PauseHold, c.Verdict);
            Assert.AreEqual(SubscriptionDunningVerdict.UrgentRetry, c.Verdict);
            Assert.IsTrue(c.Reasons.Contains("PREVIOUSLY_AUTO_PAUSED"));
        }

        [TestMethod]
        public void GenerateReport_FraudOverridesPauseReuseGuard()
        {
            var svc = NewService();
            var report = svc.GenerateReport(new[]
            {
                Snap(
                    1,
                    attempts: 1,
                    reason: SubscriptionDunningFailureReason.FraudBlock,
                    prevPaused: true)
            });
            var c = report.Cases.Single();
            // Fraud must always pause for review, even with prior auto-pause.
            Assert.AreEqual(SubscriptionDunningVerdict.PauseHold, c.Verdict);
        }

        [TestMethod]
        public void GenerateReport_CardExpiredCluster_TriggersCardUpdaterCampaign()
        {
            var svc = NewService();
            var report = svc.GenerateReport(new[]
            {
                Snap(1, attempts: 1, cardExpired: true, reason: SubscriptionDunningFailureReason.ExpiredCard),
                Snap(2, attempts: 1, cardExpired: true, reason: SubscriptionDunningFailureReason.ExpiredCard),
                Snap(3, attempts: 1, cardExpired: true, reason: SubscriptionDunningFailureReason.ExpiredCard),
            });
            Assert.IsTrue(report.Playbook.Any(a => a.Id == "CARD_UPDATER_CAMPAIGN"));
            Assert.IsTrue(report.Summary.Insights.Contains("CARD_HYGIENE_CAMPAIGN_NEEDED"));
            Assert.AreEqual(3, report.Summary.CardIssuesCount);
        }

        [TestMethod]
        public void GenerateReport_AppetiteMonotonicity_CautiousRiskGteBalancedGteAggressive()
        {
            var svc = NewService();
            var snap = new[] { Snap(1, attempts: 2, daysSinceFail: 4) };

            int rCautious = svc.GenerateReport(snap, new SubscriptionDunningOptions
                { RiskAppetite = SubscriptionDunningAppetite.Cautious }).Cases[0].DunningRisk;
            int rBalanced = svc.GenerateReport(snap, new SubscriptionDunningOptions
                { RiskAppetite = SubscriptionDunningAppetite.Balanced }).Cases[0].DunningRisk;
            int rAggressive = svc.GenerateReport(snap, new SubscriptionDunningOptions
                { RiskAppetite = SubscriptionDunningAppetite.Aggressive }).Cases[0].DunningRisk;

            Assert.IsTrue(rCautious >= rBalanced, "cautious >= balanced expected");
            Assert.IsTrue(rBalanced >= rAggressive, "balanced >= aggressive expected");
        }

        [TestMethod]
        public void GenerateReport_AggressiveAppetite_TrimsP2WhenP0OrP1Present()
        {
            var svc = NewService();
            var report = svc.GenerateReport(
                new[]
                {
                    Snap(1, attempts: 4, daysSinceFail: 15), // P0 ForceCancel
                    Snap(2, attempts: 1) // would create P2 soft reminder
                },
                new SubscriptionDunningOptions
                {
                    RiskAppetite = SubscriptionDunningAppetite.Aggressive
                });

            Assert.IsFalse(report.Playbook.Any(a => a.Priority == SubscriptionDunningActionPriority.P2),
                "aggressive should trim P2 when P0/P1 actions exist");
        }

        [TestMethod]
        public void GenerateReport_CautiousAppetiteOnPoorGrade_AppendsPortfolioReview()
        {
            var svc = NewService();
            // Build a deliberately bad portfolio so grade is C/D/F.
            var snaps = new List<SubscriptionDunningSnapshot>();
            for (int i = 1; i <= 5; i++)
            {
                snaps.Add(Snap(i, attempts: 4, daysSinceFail: 15));
            }
            var report = svc.GenerateReport(snaps, new SubscriptionDunningOptions
            {
                RiskAppetite = SubscriptionDunningAppetite.Cautious
            });
            Assert.IsTrue(report.Summary.Grade == 'C' || report.Summary.Grade == 'D' || report.Summary.Grade == 'F',
                "expected non-healthy grade for stress portfolio");
            Assert.IsTrue(report.Playbook.Any(a => a.Id == "SCHEDULE_PORTFOLIO_REVIEW"));
        }

        [TestMethod]
        public void GenerateReport_RecommendedActions_AreVerdictSpecific()
        {
            var svc = NewService();
            var report = svc.GenerateReport(new[]
            {
                Snap(1, attempts: 0, reason: SubscriptionDunningFailureReason.Unknown), // Current
                Snap(2, attempts: 4, daysSinceFail: 15), // ForceCancel
                Snap(3, attempts: 1, reason: SubscriptionDunningFailureReason.FraudBlock) // PauseHold
            });
            var byId = report.Cases.ToDictionary(c => c.SubscriptionId);
            Assert.IsTrue(byId[1].RecommendedAction.StartsWith("No action"));
            Assert.IsTrue(byId[2].RecommendedAction.StartsWith("Cancel"));
            Assert.IsTrue(byId[3].RecommendedAction.Contains("fraud-review"));
        }

        [TestMethod]
        public void GenerateReport_MrrAtRiskAndRecoverable_SumOnlyDelinquentSnapshots()
        {
            var svc = NewService();
            var report = svc.GenerateReport(new[]
            {
                Snap(1, attempts: 0, reason: SubscriptionDunningFailureReason.Unknown, mrr: 10m),
                Snap(2, attempts: 2, mrr: 20m), // delinquent, recoverable
                Snap(3, attempts: 4, daysSinceFail: 15, mrr: 30m) // ForceCancel - not recoverable
            });
            Assert.AreEqual(50m, report.Summary.TotalMrrAtRisk);
            Assert.AreEqual(20m, report.Summary.RecoverableMrrEstimate);
        }

        [TestMethod]
        public void GenerateReport_StableOrdering_PriorityThenRiskThenRevenueThenId()
        {
            var svc = NewService();
            var report = svc.GenerateReport(new[]
            {
                Snap(3, attempts: 1), // P2/P3
                Snap(1, attempts: 4, daysSinceFail: 15), // P0
                Snap(2, attempts: 4, daysSinceFail: 15)  // P0
            });
            // P0 cases must come first, then ordered by id ascending given equal risk/revenue.
            Assert.AreEqual(1, report.Cases[0].SubscriptionId);
            Assert.AreEqual(2, report.Cases[1].SubscriptionId);
            Assert.AreEqual(3, report.Cases[2].SubscriptionId);
        }

        [TestMethod]
        public void GenerateReport_TopCases_RespectsCap()
        {
            var svc = NewService();
            var snaps = new List<SubscriptionDunningSnapshot>();
            for (int i = 1; i <= 10; i++) snaps.Add(Snap(i, attempts: 1));
            var report = svc.GenerateReport(snaps, new SubscriptionDunningOptions { TopCases = 3 });
            Assert.AreEqual(3, report.Cases.Count);
            Assert.AreEqual(10, report.Summary.TotalSubscriptions);
        }

        [TestMethod]
        public void GenerateReport_ToMarkdown_ContainsAllRequiredSections()
        {
            var svc = NewService();
            var report = svc.GenerateReport(new[]
            {
                Snap(1, attempts: 2)
            });
            var md = report.ToMarkdown();
            Assert.IsTrue(md.Contains("## Summary"));
            Assert.IsTrue(md.Contains("## Top cases"));
            Assert.IsTrue(md.Contains("## Playbook"));
            Assert.IsTrue(md.Contains("## Insights"));
        }

        [TestMethod]
        public void GenerateReport_NeverMutatesInputSnapshots()
        {
            var svc = NewService();
            var snap = Snap(1, attempts: 2);
            var beforeAttempts = snap.FailedAttempts;
            var beforeReason = snap.LastFailureReason;
            var beforeCardExpired = snap.CardExpired;

            svc.GenerateReport(new[] { snap });

            Assert.AreEqual(beforeAttempts, snap.FailedAttempts);
            Assert.AreEqual(beforeReason, snap.LastFailureReason);
            Assert.AreEqual(beforeCardExpired, snap.CardExpired);
        }

        [TestMethod]
        public void GenerateReport_Determinism_RepeatedCallsProduceIdenticalOutput()
        {
            var svc = NewService();
            var snaps = new[]
            {
                Snap(1, attempts: 2),
                Snap(2, attempts: 4, daysSinceFail: 15),
                Snap(3, attempts: 1, cardExpired: true, reason: SubscriptionDunningFailureReason.ExpiredCard)
            };
            var a = svc.GenerateReport(snaps).ToMarkdown();
            var b = svc.GenerateReport(snaps).ToMarkdown();
            Assert.AreEqual(a, b);
        }
    }
}
