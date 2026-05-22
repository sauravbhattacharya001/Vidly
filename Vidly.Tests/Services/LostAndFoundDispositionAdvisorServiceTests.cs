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
    public class LostAndFoundDispositionAdvisorServiceTests
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

        private sealed class FakeRepo : ILostAndFoundRepository
        {
            private readonly List<LostItem> _items = new List<LostItem>();
            private readonly Dictionary<int, List<LostItemClaim>> _claims =
                new Dictionary<int, List<LostItemClaim>>();
            private int _nextId = 1;

            public LostItem Seed(LostItem it)
            {
                if (it.Id == 0) it.Id = _nextId++;
                else _nextId = Math.Max(_nextId, it.Id + 1);
                _items.Add(it);
                return it;
            }

            public void SeedClaim(LostItemClaim c)
            {
                if (!_claims.TryGetValue(c.ItemId, out var list))
                {
                    list = new List<LostItemClaim>();
                    _claims[c.ItemId] = list;
                }
                list.Add(c);
            }

            public IEnumerable<LostItem> GetAll() => _items.ToList();
            public IEnumerable<LostItem> GetByStatus(LostItemStatus s) =>
                _items.Where(i => i.Status == s);
            public IEnumerable<LostItem> GetByCategory(LostItemCategory c) =>
                _items.Where(i => i.Category == c);
            public LostItem GetById(int id) => _items.FirstOrDefault(i => i.Id == id);
            public void Add(LostItem item) { Seed(item); }
            public void Update(LostItem item) { }
            public void Remove(int id) { _items.RemoveAll(i => i.Id == id); }
            public IEnumerable<LostItemClaim> GetClaimsForItem(int itemId) =>
                _claims.TryGetValue(itemId, out var l) ? l : Enumerable.Empty<LostItemClaim>();
            public LostItemClaim GetClaimById(int id) =>
                _claims.Values.SelectMany(x => x).FirstOrDefault(c => c.Id == id);
            public void AddClaim(LostItemClaim claim) { SeedClaim(claim); }
            public void UpdateClaim(LostItemClaim claim) { }
            public LostAndFoundReport GetReport() => new LostAndFoundReport();
            public IEnumerable<LostItem> GetOverdueItems() => Enumerable.Empty<LostItem>();
            public IEnumerable<LostItem> Search(string q) => Enumerable.Empty<LostItem>();
        }

        private static LostItem MakeItem(
            int id,
            LostItemCategory cat,
            int ageDays,
            LostItemStatus status = LostItemStatus.Found,
            int retentionDays = 30,
            string bin = "BIN-1")
        {
            return new LostItem
            {
                Id = id,
                Description = "Item " + id,
                Category = cat,
                Status = status,
                RetentionDays = retentionDays,
                StorageBin = bin,
                FoundAt = Today.AddDays(-ageDays),
                FoundByStaffId = "S001"
            };
        }

        private static LostAndFoundDispositionAdvisorService Build(FakeRepo repo) =>
            new LostAndFoundDispositionAdvisorService(repo, new FakeClock(Today, Now));

        // ---------------------------------------------------------- Verdict ladder

        [TestMethod]
        public void OverdueDisposal_TakesPriority_OverEverythingElse()
        {
            var repo = new FakeRepo();
            repo.Seed(MakeItem(1, LostItemCategory.Other, ageDays: 60, retentionDays: 30));

            var report = Build(repo).GenerateReport();

            Assert.AreEqual(1, report.Cases.Count);
            Assert.AreEqual(LostFoundDispositionVerdict.OverdueDisposal, report.Cases[0].Verdict);
            Assert.AreEqual(LostFoundDispositionActionPriority.P0, report.Cases[0].Priority);
            Assert.IsTrue(report.Cases[0].DispositionRisk >= 50);
            Assert.IsTrue(report.Cases[0].Reasons.Any(r => r.StartsWith("overdue_by_")));
        }

        [TestMethod]
        public void StaleUnverifiedClaim_RaisesPriorityToP1()
        {
            var repo = new FakeRepo();
            repo.Seed(MakeItem(1, LostItemCategory.Other, ageDays: 5,
                status: LostItemStatus.ClaimPending));
            repo.SeedClaim(new LostItemClaim
            {
                Id = 1,
                ItemId = 1,
                CustomerId = 42,
                ClaimDate = Today.AddDays(-10),
                Verified = false,
                Rejected = false
            });

            var report = Build(repo).GenerateReport();

            Assert.AreEqual(LostFoundDispositionVerdict.UnverifiedClaimStale, report.Cases[0].Verdict);
            Assert.AreEqual(LostFoundDispositionActionPriority.P1, report.Cases[0].Priority);
        }

        [TestMethod]
        public void ExpiringSoon_BetweenRetentionWindowAndCutoff()
        {
            var repo = new FakeRepo();
            // ageDays=28, retentionDays=30 -> daysToRetention=2 <= ExpiringSoonDays(3)
            repo.Seed(MakeItem(1, LostItemCategory.Book, ageDays: 28, retentionDays: 30));

            var report = Build(repo).GenerateReport();

            Assert.AreEqual(LostFoundDispositionVerdict.ExpiringSoon, report.Cases[0].Verdict);
            Assert.AreEqual(LostFoundDispositionActionPriority.P1, report.Cases[0].Priority);
        }

        [TestMethod]
        public void NewlyFound_LowPriority_LowRisk()
        {
            var repo = new FakeRepo();
            repo.Seed(MakeItem(1, LostItemCategory.Book, ageDays: 1, retentionDays: 30));

            var report = Build(repo).GenerateReport();

            Assert.AreEqual(LostFoundDispositionVerdict.NewlyFound, report.Cases[0].Verdict);
            Assert.AreEqual(LostFoundDispositionActionPriority.P3, report.Cases[0].Priority);
            Assert.AreEqual(0, report.Cases[0].DispositionRisk);
        }

        [TestMethod]
        public void Resolved_WhenStatusClaimedOrDisposedOrDonated()
        {
            var repo = new FakeRepo();
            repo.Seed(MakeItem(1, LostItemCategory.Other, 5, LostItemStatus.Claimed));
            repo.Seed(MakeItem(2, LostItemCategory.Other, 60, LostItemStatus.Disposed));
            repo.Seed(MakeItem(3, LostItemCategory.Other, 60, LostItemStatus.Donated));

            var report = Build(repo).GenerateReport();

            Assert.IsTrue(report.Cases.All(c => c.Verdict == LostFoundDispositionVerdict.Resolved));
            Assert.IsTrue(report.Cases.All(c => c.Priority == LostFoundDispositionActionPriority.P3));
        }

        [TestMethod]
        public void HighValueCategory_AddsRiskPoints()
        {
            var repo = new FakeRepo();
            repo.Seed(MakeItem(1, LostItemCategory.Wallet, ageDays: 20, retentionDays: 30));
            repo.Seed(MakeItem(2, LostItemCategory.Book, ageDays: 20, retentionDays: 30));

            var report = Build(repo).GenerateReport();

            var wallet = report.Cases.First(c => c.ItemId == 1);
            var book = report.Cases.First(c => c.ItemId == 2);

            Assert.IsTrue(wallet.DispositionRisk > book.DispositionRisk,
                "wallet=" + wallet.DispositionRisk + " book=" + book.DispositionRisk);
            Assert.IsTrue(wallet.Reasons.Contains("high_value_category"));
        }

        // ---------------------------------------------------------- Playbook

        [TestMethod]
        public void Playbook_IncludesDisposeP0_ForOverdueItems()
        {
            var repo = new FakeRepo();
            repo.Seed(MakeItem(1, LostItemCategory.Other, ageDays: 45, retentionDays: 30));
            repo.Seed(MakeItem(2, LostItemCategory.Other, ageDays: 50, retentionDays: 30));

            var report = Build(repo).GenerateReport();

            var dispose = report.Playbook
                .FirstOrDefault(a => a.Id == "DISPOSE_OR_DONATE_OVERDUE_ITEMS");
            Assert.IsNotNull(dispose);
            Assert.AreEqual(LostFoundDispositionActionPriority.P0, dispose.Priority);
            CollectionAssert.AreEqual(new[] { 1, 2 }, dispose.TargetItemIds);
        }

        [TestMethod]
        public void Playbook_IncludesExpediteVerification_ForStaleClaims()
        {
            var repo = new FakeRepo();
            repo.Seed(MakeItem(1, LostItemCategory.Other, ageDays: 5,
                status: LostItemStatus.ClaimPending));
            repo.SeedClaim(new LostItemClaim
            {
                Id = 1,
                ItemId = 1,
                CustomerId = 1,
                ClaimDate = Today.AddDays(-14),
                Verified = false,
                Rejected = false
            });

            var report = Build(repo).GenerateReport();

            Assert.IsTrue(report.Playbook.Any(a => a.Id == "EXPEDITE_CLAIM_VERIFICATION"
                                                    && a.Priority == LostFoundDispositionActionPriority.P1));
        }

        [TestMethod]
        public void Playbook_HealthyFallback_WhenNothingFound()
        {
            var repo = new FakeRepo();
            repo.Seed(MakeItem(1, LostItemCategory.Book, ageDays: 1));
            repo.Seed(MakeItem(2, LostItemCategory.Book, ageDays: 0));

            var report = Build(repo).GenerateReport();

            Assert.AreEqual(1, report.Playbook.Count);
            Assert.AreEqual("PORTFOLIO_HEALTHY", report.Playbook[0].Id);
            Assert.AreEqual(LostFoundDispositionActionPriority.P3, report.Playbook[0].Priority);
        }

        [TestMethod]
        public void Playbook_OrderedByPriorityThenId()
        {
            var repo = new FakeRepo();
            // Overdue (P0)
            repo.Seed(MakeItem(1, LostItemCategory.Other, ageDays: 60, retentionDays: 30));
            // ExpiringSoon (P1)
            repo.Seed(MakeItem(2, LostItemCategory.Book, ageDays: 28, retentionDays: 30));
            // High-value at risk (P2)
            repo.Seed(MakeItem(3, LostItemCategory.Wallet, ageDays: 50, retentionDays: 30));

            var report = Build(repo).GenerateReport();

            for (int i = 1; i < report.Playbook.Count; i++)
            {
                Assert.IsTrue(report.Playbook[i - 1].Priority <= report.Playbook[i].Priority,
                    "out of order at " + i);
            }
        }

        // ---------------------------------------------------------- Risk appetite

        [TestMethod]
        public void RiskAppetite_Cautious_HigherRisk_Than_Aggressive()
        {
            var repo = new FakeRepo();
            repo.Seed(MakeItem(1, LostItemCategory.Wallet, ageDays: 20, retentionDays: 30));

            var cautious = Build(repo).GenerateReport(new LostFoundDispositionOptions
            {
                RiskAppetite = LostFoundDispositionAppetite.Cautious
            });
            var aggressive = Build(repo).GenerateReport(new LostFoundDispositionOptions
            {
                RiskAppetite = LostFoundDispositionAppetite.Aggressive
            });

            Assert.IsTrue(cautious.Cases[0].DispositionRisk > aggressive.Cases[0].DispositionRisk);
        }

        [TestMethod]
        public void RiskAppetite_Aggressive_TrimsP3_WhenP0OrP1Present()
        {
            var repo = new FakeRepo();
            repo.Seed(MakeItem(1, LostItemCategory.Other, ageDays: 60, retentionDays: 30));

            var report = Build(repo).GenerateReport(new LostFoundDispositionOptions
            {
                RiskAppetite = LostFoundDispositionAppetite.Aggressive
            });

            Assert.IsFalse(report.Playbook.Any(a => a.Priority == LostFoundDispositionActionPriority.P3));
        }

        [TestMethod]
        public void RiskAppetite_Cautious_AppendsPolicyReview_OnBadGrade()
        {
            var repo = new FakeRepo();
            for (int i = 1; i <= 6; i++)
                repo.Seed(MakeItem(i, LostItemCategory.Other, ageDays: 60, retentionDays: 30));

            var report = Build(repo).GenerateReport(new LostFoundDispositionOptions
            {
                RiskAppetite = LostFoundDispositionAppetite.Cautious
            });

            Assert.IsTrue(report.Playbook.Any(a => a.Id == "REVIEW_DISPOSITION_POLICY"));
            Assert.IsTrue(report.Summary.Grade == 'F' || report.Summary.Grade == 'D'
                          || report.Summary.Grade == 'C');
        }

        // ---------------------------------------------------------- Summary

        [TestMethod]
        public void Summary_HeadlineLadder_Critical_WhenManyOverdue()
        {
            var repo = new FakeRepo();
            for (int i = 1; i <= 6; i++)
                repo.Seed(MakeItem(i, LostItemCategory.Other, ageDays: 60, retentionDays: 30));

            var report = Build(repo).GenerateReport();

            Assert.AreEqual(LostFoundDispositionHeadline.BacklogCritical, report.Summary.HeadlineVerdict);
            Assert.AreEqual('F', report.Summary.Grade);
        }

        [TestMethod]
        public void Summary_HeadlineLadder_Healthy_WhenAllNew()
        {
            var repo = new FakeRepo();
            repo.Seed(MakeItem(1, LostItemCategory.Book, ageDays: 0));
            repo.Seed(MakeItem(2, LostItemCategory.Book, ageDays: 1));

            var report = Build(repo).GenerateReport();

            Assert.AreEqual(LostFoundDispositionHeadline.PortfolioHealthy, report.Summary.HeadlineVerdict);
            Assert.AreEqual('A', report.Summary.Grade);
        }

        [TestMethod]
        public void Summary_ResolutionRate_CountsResolvedItems()
        {
            var repo = new FakeRepo();
            repo.Seed(MakeItem(1, LostItemCategory.Other, 5, LostItemStatus.Claimed));
            repo.Seed(MakeItem(2, LostItemCategory.Other, 5, LostItemStatus.Donated));
            repo.Seed(MakeItem(3, LostItemCategory.Other, 5, LostItemStatus.Found));
            repo.Seed(MakeItem(4, LostItemCategory.Other, 5, LostItemStatus.Found));

            var report = Build(repo).GenerateReport();

            Assert.AreEqual(0.5, report.Summary.ResolutionRate, 0.0001);
        }

        // ---------------------------------------------------------- Output / determinism

        [TestMethod]
        public void ToMarkdown_ContainsAllSections()
        {
            var repo = new FakeRepo();
            repo.Seed(MakeItem(1, LostItemCategory.Wallet, ageDays: 60, retentionDays: 30));

            var md = Build(repo).GenerateReport().ToMarkdown();

            StringAssert.Contains(md, "## Summary");
            StringAssert.Contains(md, "## Top cases");
            StringAssert.Contains(md, "## Playbook");
            StringAssert.Contains(md, "## Insights");
            StringAssert.Contains(md, "DISPOSE_OR_DONATE_OVERDUE_ITEMS");
        }

        [TestMethod]
        public void ToText_OmitsMarkdownHeaders()
        {
            var repo = new FakeRepo();
            repo.Seed(MakeItem(1, LostItemCategory.Book, ageDays: 1));

            var text = Build(repo).GenerateReport().ToText();

            Assert.IsFalse(text.Contains("## "));
            StringAssert.Contains(text, "Summary");
            StringAssert.Contains(text, "Playbook");
        }

        [TestMethod]
        public void GenerateReport_NeverMutatesInputItems()
        {
            var repo = new FakeRepo();
            var seeded = repo.Seed(MakeItem(1, LostItemCategory.Other, ageDays: 60, retentionDays: 30));
            var originalStatus = seeded.Status;
            var originalAt = seeded.FoundAt;

            Build(repo).GenerateReport();

            Assert.AreEqual(originalStatus, seeded.Status);
            Assert.AreEqual(originalAt, seeded.FoundAt);
            Assert.IsNull(seeded.DisposalDate);
        }

        [TestMethod]
        public void GenerateReport_EmptyRepo_ReturnsHealthyPortfolio()
        {
            var repo = new FakeRepo();

            var report = Build(repo).GenerateReport();

            Assert.AreEqual(0, report.Cases.Count);
            Assert.AreEqual(LostFoundDispositionHeadline.PortfolioHealthy, report.Summary.HeadlineVerdict);
            Assert.AreEqual(1, report.Playbook.Count);
            Assert.AreEqual("PORTFOLIO_HEALTHY", report.Playbook[0].Id);
            Assert.IsTrue(report.Summary.Insights.Contains("NO_LOST_ITEMS"));
        }

        [TestMethod]
        public void GenerateReport_OrdersCasesByPriorityThenRiskThenId()
        {
            var repo = new FakeRepo();
            repo.Seed(MakeItem(1, LostItemCategory.Book, ageDays: 1));                  // P3
            repo.Seed(MakeItem(2, LostItemCategory.Other, ageDays: 60, retentionDays: 30)); // P0
            repo.Seed(MakeItem(3, LostItemCategory.Book, ageDays: 28, retentionDays: 30)); // P1

            var report = Build(repo).GenerateReport();

            Assert.AreEqual(2, report.Cases[0].ItemId);
            Assert.AreEqual(3, report.Cases[1].ItemId);
            Assert.AreEqual(1, report.Cases[2].ItemId);
        }

        [TestMethod]
        public void GenerateReport_RespectsTopCasesLimit()
        {
            var repo = new FakeRepo();
            for (int i = 1; i <= 10; i++)
                repo.Seed(MakeItem(i, LostItemCategory.Book, ageDays: 1));

            var report = Build(repo).GenerateReport(new LostFoundDispositionOptions { TopCases = 3 });

            Assert.AreEqual(3, report.Cases.Count);
        }

        [TestMethod]
        public void ConstructorNullRepo_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new LostAndFoundDispositionAdvisorService(null));
        }

        [TestMethod]
        public void DefaultConstructor_UsesInMemoryRepo_DoesNotThrow()
        {
            var svc = new LostAndFoundDispositionAdvisorService();
            var report = svc.GenerateReport();
            Assert.IsNotNull(report);
            Assert.IsNotNull(report.Summary);
        }
    }
}
