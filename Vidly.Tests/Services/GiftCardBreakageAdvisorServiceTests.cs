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
    public class GiftCardBreakageAdvisorServiceTests
    {
        private static readonly DateTime Today = new DateTime(2026, 5, 20);
        private static readonly DateTime Now = new DateTime(2026, 5, 20, 12, 0, 0);

        private sealed class FakeClock : IClock
        {
            private readonly DateTime _today;
            private readonly DateTime _now;
            public FakeClock(DateTime today, DateTime now) { _today = today; _now = now; }
            public DateTime Now => _now;
            public DateTime Today => _today;
        }

        private sealed class FakeGiftCardRepo : IGiftCardRepository
        {
            private readonly List<GiftCard> _cards = new List<GiftCard>();
            public void Seed(GiftCard c) => _cards.Add(c);
            public IReadOnlyList<GiftCard> GetAll() => _cards;
            public GiftCard GetById(int id) => _cards.FirstOrDefault(c => c.Id == id);
            public GiftCard GetByCode(string code) => _cards.FirstOrDefault(c => c.Code == code);
            public void Add(GiftCard giftCard) => _cards.Add(giftCard);
            public void Update(GiftCard giftCard) { }
            public void AddTransaction(int giftCardId, GiftCardTransaction t) { }
        }

        private static GiftCard MakeCard(
            int id,
            decimal originalValue,
            decimal balance,
            int ageDays,
            DateTime? expiration = null,
            bool isActive = true,
            int? lastRedemptionDaysAgo = null,
            bool everRedeemed = true)
        {
            var card = new GiftCard
            {
                Id = id,
                Code = "GIFT-" + id.ToString("D4"),
                OriginalValue = originalValue,
                Balance = balance,
                IsActive = isActive,
                ExpirationDate = expiration,
                CreatedDate = Today.AddDays(-ageDays),
                PurchaserName = "P" + id,
                RecipientName = "R" + id
            };
            card.Transactions.Add(new GiftCardTransaction
            {
                Id = id * 100,
                GiftCardId = id,
                Type = GiftCardTransactionType.InitialLoad,
                Amount = originalValue,
                BalanceAfter = originalValue,
                Description = "Initial load",
                Date = card.CreatedDate
            });
            if (everRedeemed && lastRedemptionDaysAgo.HasValue)
            {
                card.Transactions.Add(new GiftCardTransaction
                {
                    Id = id * 100 + 1,
                    GiftCardId = id,
                    Type = GiftCardTransactionType.Redemption,
                    Amount = Math.Max(0m, originalValue - balance),
                    BalanceAfter = balance,
                    Description = "Used",
                    Date = Today.AddDays(-lastRedemptionDaysAgo.Value)
                });
            }
            return card;
        }

        private static GiftCardBreakageAdvisorService BuildService(out FakeGiftCardRepo repo)
        {
            repo = new FakeGiftCardRepo();
            return new GiftCardBreakageAdvisorService(repo, new FakeClock(Today, Now));
        }

        [TestMethod]
        public void EmptyPortfolio_GeneratesInsufficientDataInsight()
        {
            var svc = BuildService(out _);
            var r = svc.GenerateReport();
            Assert.AreEqual(0, r.Summary.TotalCards);
            Assert.AreEqual(100, r.Summary.OverallScore);
            Assert.AreEqual('A', r.Summary.Grade);
            CollectionAssert.Contains(r.Summary.Insights, "INSUFFICIENT_DATA");
            Assert.AreEqual(1, r.Playbook.Count);
            Assert.AreEqual("PORTFOLIO_HEALTHY", r.Playbook[0].Id);
        }

        [TestMethod]
        public void FullyRedeemed_CardIsHealthy_NoLiability()
        {
            var svc = BuildService(out var repo);
            repo.Seed(MakeCard(1, 100m, 0m, ageDays: 200, lastRedemptionDaysAgo: 10));
            var r = svc.GenerateReport();
            Assert.AreEqual(1, r.Cases.Count);
            Assert.AreEqual(GiftCardBreakageVerdict.Healthy, r.Cases[0].Verdict);
            Assert.AreEqual(0m, r.Cases[0].LiabilityAmount);
            Assert.AreEqual('A', r.Summary.Grade);
        }

        [TestMethod]
        public void ExpiredCardWithBalance_GetsExpiredVerdictAndP0()
        {
            var svc = BuildService(out var repo);
            repo.Seed(MakeCard(2, 100m, 60m, ageDays: 400,
                expiration: Today.AddDays(-10), lastRedemptionDaysAgo: 200));
            var r = svc.GenerateReport();
            var c = r.Cases.Single(x => x.CardId == 2);
            Assert.AreEqual(GiftCardBreakageVerdict.Expired, c.Verdict);
            Assert.AreEqual(GiftCardBreakageActionPriority.P0, c.Priority);
            Assert.IsTrue(c.BreakageRisk >= 30, "risk should jump for Expired, got " + c.BreakageRisk);
            Assert.IsTrue(r.Playbook.Any(p => p.Id == "WRITE_OFF_EXPIRED"));
        }

        [TestMethod]
        public void DormantCard_GetsDormantVerdict()
        {
            var svc = BuildService(out var repo);
            repo.Seed(MakeCard(3, 50m, 30m, ageDays: 400, lastRedemptionDaysAgo: 200));
            var r = svc.GenerateReport();
            var c = r.Cases.Single(x => x.CardId == 3);
            Assert.AreEqual(GiftCardBreakageVerdict.Dormant, c.Verdict);
            Assert.AreEqual(GiftCardBreakageActionPriority.P1, c.Priority);
        }

        [TestMethod]
        public void AbandonedHighValue_GetsP0()
        {
            var svc = BuildService(out var repo);
            // High balance ($75), never redeemed, ~400 days old, no expiration.
            repo.Seed(MakeCard(4, 100m, 75m, ageDays: 400, everRedeemed: false));
            var r = svc.GenerateReport();
            var c = r.Cases.Single(x => x.CardId == 4);
            Assert.AreEqual(GiftCardBreakageVerdict.AbandonedHighValue, c.Verdict);
            Assert.AreEqual(GiftCardBreakageActionPriority.P0, c.Priority);
            Assert.IsTrue(r.Playbook.Any(p => p.Id == "OUTREACH_ABANDONED_HIGH_VALUE"));
        }

        [TestMethod]
        public void ExpiringSoon_DetectedAndExtendActionFires()
        {
            var svc = BuildService(out var repo);
            repo.Seed(MakeCard(5, 50m, 25m, ageDays: 30,
                expiration: Today.AddDays(10), lastRedemptionDaysAgo: 15));
            var r = svc.GenerateReport();
            var c = r.Cases.Single(x => x.CardId == 5);
            Assert.AreEqual(GiftCardBreakageVerdict.ExpiringSoon, c.Verdict);
            Assert.IsTrue(r.Playbook.Any(p => p.Id == "EXTEND_EXPIRY_EXPIRING_SOON"));
        }

        [TestMethod]
        public void PartiallyRedeemed_DetectedWhenInBand()
        {
            var svc = BuildService(out var repo);
            // 50% redeemed ($100 original, $50 left), used recently
            repo.Seed(MakeCard(6, 100m, 50m, ageDays: 90, lastRedemptionDaysAgo: 60));
            var r = svc.GenerateReport();
            var c = r.Cases.Single(x => x.CardId == 6);
            Assert.AreEqual(GiftCardBreakageVerdict.PartiallyRedeemed, c.Verdict);
            Assert.AreEqual(GiftCardBreakageActionPriority.P2, c.Priority);
        }

        [TestMethod]
        public void NeverRedeemed_AgedCard_AccumulatesRisk()
        {
            var svc = BuildService(out var repo);
            // Never redeemed, 250 days old, $40 balance (below high-value threshold).
            repo.Seed(MakeCard(7, 40m, 40m, ageDays: 250, everRedeemed: false));
            var r = svc.GenerateReport();
            var c = r.Cases.Single(x => x.CardId == 7);
            CollectionAssert.Contains(c.Reasons, "NEVER_REDEEMED");
            Assert.IsTrue(c.BreakageRisk > 0);
        }

        [TestMethod]
        public void RiskAppetite_IsMonotonic()
        {
            // Cautious >= Balanced >= Aggressive on the same dormant card.
            var dormant = MakeCard(8, 50m, 40m, ageDays: 400, lastRedemptionDaysAgo: 220);

            var svcA = BuildService(out var repoA); repoA.Seed(dormant);
            var svcB = BuildService(out var repoB);
            repoB.Seed(MakeCard(8, 50m, 40m, ageDays: 400, lastRedemptionDaysAgo: 220));
            var svcC = BuildService(out var repoC);
            repoC.Seed(MakeCard(8, 50m, 40m, ageDays: 400, lastRedemptionDaysAgo: 220));

            int cautious = svcA.GenerateReport(new GiftCardBreakageOptions
            { RiskAppetite = GiftCardBreakageAppetite.Cautious }).Cases[0].BreakageRisk;
            int balanced = svcB.GenerateReport(new GiftCardBreakageOptions
            { RiskAppetite = GiftCardBreakageAppetite.Balanced }).Cases[0].BreakageRisk;
            int aggressive = svcC.GenerateReport(new GiftCardBreakageOptions
            { RiskAppetite = GiftCardBreakageAppetite.Aggressive }).Cases[0].BreakageRisk;

            Assert.IsTrue(cautious >= balanced, "cautious " + cautious + " < balanced " + balanced);
            Assert.IsTrue(balanced >= aggressive, "balanced " + balanced + " < aggressive " + aggressive);
        }

        [TestMethod]
        public void Playbook_NoDuplicateIds()
        {
            var svc = BuildService(out var repo);
            for (int i = 0; i < 5; i++)
                repo.Seed(MakeCard(100 + i, 50m, 30m, ageDays: 400, lastRedemptionDaysAgo: 220));
            for (int i = 0; i < 3; i++)
                repo.Seed(MakeCard(200 + i, 50m, 20m, ageDays: 50,
                    expiration: Today.AddDays(15), lastRedemptionDaysAgo: 25));
            var r = svc.GenerateReport();
            var ids = r.Playbook.Select(p => p.Id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "duplicate playbook ids");
        }

        [TestMethod]
        public void Playbook_OrderedP0First()
        {
            var svc = BuildService(out var repo);
            repo.Seed(MakeCard(10, 100m, 60m, ageDays: 400,
                expiration: Today.AddDays(-5), lastRedemptionDaysAgo: 200)); // Expired
            for (int i = 0; i < 4; i++)
                repo.Seed(MakeCard(20 + i, 50m, 30m, ageDays: 400,
                    lastRedemptionDaysAgo: 220)); // Dormant
            var r = svc.GenerateReport();
            Assert.IsTrue(r.Playbook.Count >= 2);
            // The first item must be the highest priority.
            var first = r.Playbook[0].Priority;
            foreach (var a in r.Playbook)
                Assert.IsTrue((int)first <= (int)a.Priority);
        }

        [TestMethod]
        public void Formatter_RendersAllSections()
        {
            var svc = BuildService(out var repo);
            repo.Seed(MakeCard(11, 100m, 80m, ageDays: 400, everRedeemed: false));
            var r = svc.GenerateReport();
            string md = r.ToMarkdown();
            string txt = r.ToText();
            StringAssert.Contains(md, "## Summary");
            StringAssert.Contains(md, "## Top cases");
            StringAssert.Contains(md, "## Playbook");
            StringAssert.Contains(md, "## Insights");
            StringAssert.Contains(txt, "Summary");
            StringAssert.Contains(txt, "Playbook");
            StringAssert.Contains(txt, "Insights");
        }

        [TestMethod]
        public void Insights_AlwaysNonEmpty()
        {
            // Healthy single-card portfolio should still produce at least one insight.
            var svc = BuildService(out var repo);
            repo.Seed(MakeCard(12, 100m, 0m, ageDays: 200, lastRedemptionDaysAgo: 5));
            var r = svc.GenerateReport();
            Assert.IsTrue(r.Summary.Insights.Count >= 1);
        }

        [TestMethod]
        public void TopCases_RespectsLimit()
        {
            var svc = BuildService(out var repo);
            for (int i = 0; i < 30; i++)
                repo.Seed(MakeCard(1000 + i, 50m, 30m, ageDays: 400, lastRedemptionDaysAgo: 220));
            var r = svc.GenerateReport(new GiftCardBreakageOptions { TopCases = 5 });
            Assert.AreEqual(5, r.Cases.Count);
            Assert.AreEqual(30, r.Summary.TotalCards);
        }
    }
}
