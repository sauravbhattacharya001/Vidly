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
    public class PenaltyWaiverGovernanceAdvisorServiceTests
    {
        private static readonly DateTime Today = new DateTime(2026, 5, 20);
        private static readonly DateTime Now = new DateTime(2026, 5, 20, 12, 0, 0);

        private static PenaltyWaiverGovernanceAdvisorService BuildService(
            out InMemoryTestWaiverRepo waivers,
            out InMemoryTestRentalRepo rentals)
        {
            waivers = new InMemoryTestWaiverRepo();
            rentals = new InMemoryTestRentalRepo();
            return new PenaltyWaiverGovernanceAdvisorService(waivers, rentals, new FakeClockPwg(Today, Now));
        }

        private static PenaltyWaiver W(
            int id, int rentalId, int customerId, string customerName,
            decimal originalFee, decimal amountWaived,
            WaiverType type, int daysAgo,
            string approvedBy = "alice")
        {
            return new PenaltyWaiver
            {
                Id = id,
                RentalId = rentalId,
                CustomerName = customerName,
                MovieName = "Movie#" + rentalId,
                OriginalLateFee = originalFee,
                AmountWaived = amountWaived,
                Reason = "Test waiver " + id,
                Type = type,
                GrantedDate = Today.AddDays(-daysAgo),
                ApprovedBy = approvedBy
            };
        }

        private static Rental R(int rentalId, int customerId) =>
            new Rental { Id = rentalId, CustomerId = customerId };

        // ── Tests ────────────────────────────────────────────────

        [TestMethod]
        public void NoWaivers_HealthyReport()
        {
            var svc = BuildService(out _, out _);
            var report = svc.GenerateReport();

            Assert.AreEqual(0, report.Cases.Count);
            Assert.AreEqual(100, report.Summary.OverallScore);
            Assert.AreEqual('A', report.Summary.Grade);
            CollectionAssert.Contains(report.Summary.Insights, "INSUFFICIENT_DATA");
            Assert.AreEqual(1, report.Playbook.Count);
            Assert.AreEqual("waivers_healthy", report.Playbook[0].Id);
        }

        [TestMethod]
        public void SystemErrorPattern_GeneratesP0AndForcesGradeF()
        {
            var svc = BuildService(out var w, out var r);
            r.Add(R(101, 1)); r.Add(R(102, 1));
            w.Add(W(1, 101, 1, "Alice", 8m, 8m, WaiverType.SystemError, daysAgo: 5));
            w.Add(W(2, 102, 1, "Alice", 6m, 6m, WaiverType.SystemError, daysAgo: 2));

            var report = svc.GenerateReport();
            Assert.AreEqual(1, report.Cases.Count);
            var c = report.Cases[0];
            Assert.AreEqual(WaiverGovernanceVerdict.SystemErrorPattern, c.Verdict);
            Assert.AreEqual(WaiverGovernanceActionPriority.P0, c.Priority);
            Assert.IsTrue(c.Risk >= 80, "expected high risk; got " + c.Risk);
            Assert.IsTrue(c.Reasons.Any(x => x.StartsWith("SYSTEM_ERROR_", StringComparison.Ordinal)));
            Assert.AreEqual('F', report.Summary.Grade);
            Assert.AreEqual(1, report.Summary.P0Count);
            Assert.IsTrue(report.Playbook.Any(a => a.Id == "investigate_system_error_pattern"));
        }

        [TestMethod]
        public void ChronicAbuser_GeneratesP1()
        {
            var svc = BuildService(out var w, out var r);
            for (int i = 0; i < 5; i++)
            {
                r.Add(R(200 + i, 2));
                w.Add(W(10 + i, 200 + i, 2, "Bob", 5m, 5m, WaiverType.Partial, daysAgo: 10 + i * 3));
            }

            var report = svc.GenerateReport();
            Assert.AreEqual(1, report.Cases.Count);
            var c = report.Cases[0];
            Assert.AreEqual(WaiverGovernanceVerdict.ChronicAbuser, c.Verdict);
            Assert.AreEqual(WaiverGovernanceActionPriority.P1, c.Priority);
            Assert.AreEqual(5, c.WaiverCount);
            Assert.IsTrue(c.Reasons.Any(x => x.StartsWith("CHRONIC_WAIVERS_", StringComparison.Ordinal)));
            Assert.IsTrue(report.Playbook.Any(a => a.Id == "review_chronic_waiver_requesters"));
        }

        [TestMethod]
        public void HighDollarPattern_FiresWhenAmountExceedsThreshold()
        {
            var svc = BuildService(out var w, out var r);
            r.Add(R(300, 3));
            w.Add(W(20, 300, 3, "Carol", 75m, 75m, WaiverType.Full, daysAgo: 4));

            var report = svc.GenerateReport();
            var c = report.Cases.Single();
            Assert.AreEqual(WaiverGovernanceVerdict.HighDollarPattern, c.Verdict);
            Assert.AreEqual(WaiverGovernanceActionPriority.P2, c.Priority);
            Assert.IsTrue(c.Reasons.Any(x => x.StartsWith("HIGH_DOLLAR_", StringComparison.Ordinal)));
            Assert.IsTrue(report.Playbook.Any(a => a.Id == "audit_high_dollar_waivers"));
        }

        [TestMethod]
        public void FullWaiverConcentration_FiresOnRepeatRequesterWithAllFullWaivers()
        {
            var svc = BuildService(out var w, out var r);
            // 3 small full waivers: under high-dollar threshold so the verdict
            // can settle on FullWaiverConcentration without HighDollar dominating.
            for (int i = 0; i < 3; i++)
            {
                r.Add(R(400 + i, 4));
                w.Add(W(30 + i, 400 + i, 4, "Dave", 5m, 5m, WaiverType.Full, daysAgo: 8 + i * 2));
            }

            var report = svc.GenerateReport();
            var c = report.Cases.Single();
            Assert.AreEqual(WaiverGovernanceVerdict.FullWaiverConcentration, c.Verdict);
            Assert.AreEqual(WaiverGovernanceActionPriority.P2, c.Priority);
            Assert.IsTrue(c.Reasons.Any(x => x.StartsWith("FULL_WAIVER_PCT_", StringComparison.Ordinal)));
            Assert.IsTrue(report.Playbook.Any(a => a.Id == "calibrate_full_waiver_defaulting"));
        }

        [TestMethod]
        public void OldWaivers_OutsideWindow_AreIgnored()
        {
            var svc = BuildService(out var w, out var r);
            r.Add(R(500, 5));
            // 200 days ago — outside the 90-day audit window.
            w.Add(W(40, 500, 5, "Eve", 4m, 4m, WaiverType.Partial, daysAgo: 200));

            var report = svc.GenerateReport();
            Assert.AreEqual(0, report.Cases.Count);
        }

        [TestMethod]
        public void VolumeSpike_AndApproverConcentration_AppearInSummary()
        {
            var svc = BuildService(out var w, out var r);
            // Baseline: 1 waiver/30d (low) by alice; recent: 5 in last 7d by alice.
            r.Add(R(600, 6));
            w.Add(W(50, 600, 6, "Frank", 3m, 3m, WaiverType.Partial, daysAgo: 25));
            for (int i = 0; i < 5; i++)
            {
                r.Add(R(601 + i, 6));
                w.Add(W(60 + i, 601 + i, 6, "Frank", 3m, 3m, WaiverType.Partial, daysAgo: i));
            }

            var report = svc.GenerateReport();
            Assert.IsTrue(report.Summary.Insights.Any(x => x.StartsWith("VOLUME_SPIKE_", StringComparison.Ordinal)),
                "Expected VOLUME_SPIKE insight, got: " + string.Join(",", report.Summary.Insights));
            // Only one approver present, so APPROVER_CONCENTRATION should NOT fire
            // (requires >= 2 distinct approvers to be meaningful).
            Assert.IsFalse(report.Summary.Insights.Any(x => x.StartsWith("APPROVER_CONCENTRATION_", StringComparison.Ordinal)));
            Assert.IsTrue(report.Summary.ApproverConcentration.ContainsKey("alice"));
        }

        [TestMethod]
        public void ApproverConcentration_FiresWhenOneApproverDominates()
        {
            var svc = BuildService(out var w, out var r);
            // 5 by alice, 1 by bob → alice has 83% concentration.
            for (int i = 0; i < 5; i++)
            {
                r.Add(R(700 + i, 700 + i));
                w.Add(W(70 + i, 700 + i, 700 + i, "Cust" + i, 4m, 4m, WaiverType.Partial, daysAgo: i + 1, approvedBy: "alice"));
            }
            r.Add(R(799, 799));
            w.Add(W(99, 799, 799, "CustZ", 4m, 4m, WaiverType.Partial, daysAgo: 1, approvedBy: "bob"));

            var report = svc.GenerateReport();
            Assert.IsTrue(report.Summary.Insights.Any(x =>
                x.StartsWith("APPROVER_CONCENTRATION_", StringComparison.Ordinal) && x.EndsWith("_alice", StringComparison.Ordinal)),
                "Expected APPROVER_CONCENTRATION insight, got: " + string.Join(",", report.Summary.Insights));
        }

        [TestMethod]
        public void RenderTextReport_ContainsCoreSections()
        {
            var svc = BuildService(out var w, out var r);
            r.Add(R(800, 8));
            w.Add(W(80, 800, 8, "Grace", 10m, 10m, WaiverType.Full, daysAgo: 3));

            var report = svc.GenerateReport();
            var text = svc.RenderTextReport(report);

            StringAssert.Contains(text, "Late-Fee Waiver Governance Advisor");
            StringAssert.Contains(text, "Score:");
            StringAssert.Contains(text, "Cases (");
            StringAssert.Contains(text, "Playbook (");
            StringAssert.Contains(text, "Grace");
        }

        [TestMethod]
        public void CautiousAppetite_PromotesPriorityRelativeToBalanced()
        {
            var svc = BuildService(out var w, out var r);
            r.Add(R(900, 9));
            w.Add(W(90, 900, 9, "Hank", 4m, 4m, WaiverType.Partial, daysAgo: 1));

            var balanced = svc.GenerateReport(WaiverGovernanceAppetite.Balanced).Cases.Single();
            var cautious = svc.GenerateReport(WaiverGovernanceAppetite.Cautious).Cases.Single();

            Assert.AreEqual(WaiverGovernanceActionPriority.P3, balanced.Priority);
            Assert.AreEqual(WaiverGovernanceActionPriority.P2, cautious.Priority);
            Assert.IsTrue(cautious.Risk >= balanced.Risk);
        }

        [TestMethod]
        public void NullRentalsRepo_StillProducesGroupedCases()
        {
            var waivers = new InMemoryTestWaiverRepo();
            var svc = new PenaltyWaiverGovernanceAdvisorService(waivers, null, new FakeClockPwg(Today, Now));
            waivers.Add(W(1, 1, 0, "SoloCust", 5m, 5m, WaiverType.Partial, daysAgo: 1));
            waivers.Add(W(2, 2, 0, "SoloCust", 5m, 5m, WaiverType.Partial, daysAgo: 2));
            waivers.Add(W(3, 3, 0, "SoloCust", 5m, 5m, WaiverType.Partial, daysAgo: 3));

            var report = svc.GenerateReport();
            Assert.AreEqual(1, report.Cases.Count);
            Assert.AreEqual(3, report.Cases[0].WaiverCount);
        }

        // ── Test doubles ─────────────────────────────────────────

        private class FakeClockPwg : IClock
        {
            private readonly DateTime _today;
            private readonly DateTime _now;
            public FakeClockPwg(DateTime today, DateTime now) { _today = today; _now = now; }
            public DateTime Today => _today;
            public DateTime Now => _now;
        }

        private class InMemoryTestWaiverRepo : IPenaltyWaiverRepository
        {
            private readonly List<PenaltyWaiver> _data = new List<PenaltyWaiver>();
            public void Add(PenaltyWaiver w) => _data.Add(w);
            PenaltyWaiver IPenaltyWaiverRepository.Add(PenaltyWaiver w) { _data.Add(w); return w; }
            public IReadOnlyList<PenaltyWaiver> GetAll() => _data.AsReadOnly();
            public PenaltyWaiver GetById(int id) => _data.FirstOrDefault(w => w.Id == id);
            public IReadOnlyList<PenaltyWaiver> GetByRental(int rentalId) =>
                _data.Where(w => w.RentalId == rentalId).ToList().AsReadOnly();
            public decimal GetTotalWaivedForRental(int rentalId) =>
                _data.Where(w => w.RentalId == rentalId).Sum(w => w.AmountWaived);
            public WaiverStats GetStats() => new WaiverStats
            {
                TotalWaivers = _data.Count,
                TotalAmountWaived = _data.Sum(w => w.AmountWaived),
                FullWaivers = _data.Count(w => w.Type == WaiverType.Full),
                PartialWaivers = _data.Count(w => w.Type != WaiverType.Full)
            };
        }

        private class InMemoryTestRentalRepo : IRentalRepository
        {
            private readonly Dictionary<int, Rental> _data = new Dictionary<int, Rental>();
            public void Add(Rental r) => _data[r.Id] = r;
            public Rental GetById(int id) => _data.TryGetValue(id, out var r) ? r : null;
            public IReadOnlyList<Rental> GetAll() => _data.Values.ToList().AsReadOnly();
            void IRepository<Rental>.Add(Rental entity) => Add(entity);
            public void Update(Rental entity) { _data[entity.Id] = entity; }
            public void Remove(int id) => _data.Remove(id);

            public IReadOnlyList<Rental> GetByCustomer(int customerId) =>
                _data.Values.Where(r => r.CustomerId == customerId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) =>
                _data.Values.Where(r => r.CustomerId == customerId && r.ReturnDate == null)
                    .ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetByMovie(int movieId) =>
                _data.Values.Where(r => r.MovieId == movieId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetOverdue() =>
                new List<Rental>().AsReadOnly();
            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) =>
                new List<Rental>().AsReadOnly();
            public Rental ReturnRental(int rentalId) => GetById(rentalId);
            public bool IsMovieRentedOut(int movieId) => false;
            public Rental Checkout(Rental rental) { Add(rental); return rental; }
            public Rental Checkout(Rental rental, int maxConcurrentRentals) { Add(rental); return rental; }
            public Rental ExtendRental(int rentalId, int days) => GetById(rentalId);
            public bool IsExtended(int rentalId) => false;
            public RentalStats GetStats() => new RentalStats();
        }
    }
}
