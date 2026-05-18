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
    public class LateReturnEscalationServiceTests
    {
        private InMemoryTestRentalRepo _rentals;
        private InMemoryTestCustomerRepo _customers;
        private FakeClock _clock;
        private LateReturnEscalationService _service;

        [TestInitialize]
        public void SetUp()
        {
            _rentals = new InMemoryTestRentalRepo();
            _customers = new InMemoryTestCustomerRepo();
            _clock = new FakeClock(new DateTime(2026, 5, 18));
            _service = new LateReturnEscalationService(_rentals, _customers, _clock);
        }

        // ── Helpers ─────────────────────────────────────────────

        private Customer C(int id, string name = "Cust", string email = "c@x.com",
                           string phone = "555-0100", DateTime? since = null,
                           MembershipType mt = MembershipType.Basic)
        {
            var c = new Customer
            {
                Id = id,
                Name = name + id,
                Email = email,
                Phone = phone,
                MemberSince = since ?? new DateTime(2020, 1, 1),
                MembershipType = mt
            };
            _customers.Add(c);
            return c;
        }

        private Rental R(int id, int customerId, DateTime rentalDate, DateTime dueDate,
                         DateTime? returnDate = null, decimal dailyRate = 2.00m,
                         decimal lateFee = 0m, string movie = "M", string custName = "Cust")
        {
            var status = returnDate.HasValue ? RentalStatus.Returned :
                         (_clock.Today > dueDate ? RentalStatus.Overdue : RentalStatus.Active);
            var r = new Rental
            {
                Id = id,
                CustomerId = customerId,
                CustomerName = custName,
                MovieId = id * 10,
                MovieName = movie + id,
                RentalDate = rentalDate,
                DueDate = dueDate,
                ReturnDate = returnDate,
                DailyRate = dailyRate,
                LateFee = lateFee,
                Status = status
            };
            _rentals.Add(r);
            return r;
        }

        // ── Tests ───────────────────────────────────────────────

        [TestMethod]
        public void Report_NoRentals_IsHealthy()
        {
            var rep = _service.GenerateReport();
            Assert.AreEqual(0, rep.Cases.Count);
            Assert.AreEqual('A', rep.Summary.Grade);
            StringAssert.Contains(rep.Summary.Headline.ToLower(), "healthy");
            Assert.AreEqual(1, rep.Playbook.Count);
            Assert.AreEqual("PORTFOLIO_HEALTHY", rep.Playbook[0].Id);
            CollectionAssert.Contains(rep.Summary.Insights, "HEALTHY_PORTFOLIO");
        }

        [TestMethod]
        public void Report_OneTwoDayLate_BasicMember_IsGentleReminder()
        {
            C(1, mt: MembershipType.Basic);
            R(1, 1, _clock.Today.AddDays(-7), _clock.Today.AddDays(-2), dailyRate: 1.00m);
            var rep = _service.GenerateReport();
            Assert.AreEqual(1, rep.Cases.Count);
            var c = rep.Cases[0];
            Assert.AreEqual(EscalationVerdict.GentleReminder, c.Verdict);
            // 2-day late + basic member → low/medium priority bucket
            Assert.IsTrue(c.Priority == EscalationPriority.P3 || c.Priority == EscalationPriority.P2);
        }

        [TestMethod]
        public void Report_HighValue_FiresHighValueSignal()
        {
            C(1);
            // 10 days late at $5/day → projected $5*10*3 = $150 ≥ $25 threshold
            R(1, 1, _clock.Today.AddDays(-20), _clock.Today.AddDays(-10), dailyRate: 5.00m);
            var rep = _service.GenerateReport();
            var c = rep.Cases.Single();
            Assert.IsTrue(c.Signals.Any(s => s.Code == "HIGH_VALUE_RENTAL"));
            Assert.IsTrue(c.Verdict >= EscalationVerdict.FirmReminder);
        }

        [TestMethod]
        public void Report_ThirtyFiveDaysLate_IsCollectionsP0()
        {
            C(1);
            R(1, 1, _clock.Today.AddDays(-45), _clock.Today.AddDays(-35), dailyRate: 3.00m);
            var rep = _service.GenerateReport();
            var c = rep.Cases.Single();
            Assert.AreEqual(EscalationVerdict.CollectionsHandoff, c.Verdict);
            Assert.AreEqual(EscalationPriority.P0, c.Priority);
        }

        [TestMethod]
        public void Report_ChronicOffender_ForcesP0_AndGradeFWith3()
        {
            // 3 chronic offenders, each with 4 late returns in past year, currently overdue.
            for (int cid = 1; cid <= 3; cid++)
            {
                C(cid);
                // 4 returned-late rentals in past year
                for (int i = 0; i < 4; i++)
                {
                    var rented = _clock.Today.AddDays(-200 - i * 10);
                    var due = rented.AddDays(5);
                    var ret = due.AddDays(3); // late
                    R(100 + cid * 10 + i, cid, rented, due, returnDate: ret);
                }
                // current overdue rental
                R(200 + cid, cid, _clock.Today.AddDays(-10), _clock.Today.AddDays(-5));
            }
            var rep = _service.GenerateReport();
            Assert.AreEqual(3, rep.Cases.Count);
            foreach (var c in rep.Cases)
            {
                Assert.IsTrue(c.Signals.Any(s => s.Code == "CHRONIC_OFFENDER"),
                    "Expected CHRONIC_OFFENDER on rental " + c.RentalId);
                Assert.AreEqual(EscalationPriority.P0, c.Priority);
            }
            Assert.AreEqual('F', rep.Summary.Grade);
            CollectionAssert.Contains(rep.Summary.Insights, "MANY_CHRONIC_OFFENDERS");
        }

        [TestMethod]
        public void RiskAppetite_CautiousAtLeastAsSevereAsAggressive()
        {
            // 5-day-late basic member, no other history.
            C(1, mt: MembershipType.Basic);
            R(1, 1, _clock.Today.AddDays(-12), _clock.Today.AddDays(-5), dailyRate: 2.00m);

            var aggressive = new LateReturnEscalationService(_rentals, _customers, _clock,
                new LateReturnEscalationConfig { RiskAppetite = LateReturnRiskAppetite.Aggressive }).GenerateReport();
            var balanced = _service.GenerateReport();
            var cautious = new LateReturnEscalationService(_rentals, _customers, _clock,
                new LateReturnEscalationConfig { RiskAppetite = LateReturnRiskAppetite.Cautious }).GenerateReport();

            // Score monotonicity for the single case.
            int agg = aggressive.Cases[0].Score;
            int bal = balanced.Cases[0].Score;
            int cau = cautious.Cases[0].Score;
            Assert.IsTrue(agg <= bal, "aggressive(" + agg + ") <= balanced(" + bal + ")");
            Assert.IsTrue(bal <= cau, "balanced(" + bal + ") <= cautious(" + cau + ")");

            // Verdict severity monotonicity.
            Assert.IsTrue((int)cautious.Cases[0].Verdict >= (int)aggressive.Cases[0].Verdict);
        }

        [TestMethod]
        public void Report_MultipleActiveOverdues_ForcesServiceFreezeMin()
        {
            C(1);
            // Two short-overdue rentals for same customer.
            R(1, 1, _clock.Today.AddDays(-5), _clock.Today.AddDays(-2));
            R(2, 1, _clock.Today.AddDays(-5), _clock.Today.AddDays(-3));
            var rep = _service.GenerateReport();
            Assert.AreEqual(2, rep.Cases.Count);
            foreach (var c in rep.Cases)
            {
                Assert.IsTrue(c.Verdict >= EscalationVerdict.ServiceFreeze,
                    "Expected ≥ServiceFreeze, got " + c.Verdict);
                Assert.IsTrue(c.Signals.Any(s => s.Code == "MULTIPLE_ACTIVE_OVERDUES"));
            }
        }

        [TestMethod]
        public void Report_NoContactInfo_FiresSignal_AndEscalateActionWhen2Plus()
        {
            C(1, email: "", phone: "");
            C(2, email: "", phone: "");
            R(1, 1, _clock.Today.AddDays(-8), _clock.Today.AddDays(-3));
            R(2, 2, _clock.Today.AddDays(-8), _clock.Today.AddDays(-3));
            var rep = _service.GenerateReport();
            foreach (var c in rep.Cases)
                Assert.IsTrue(c.Signals.Any(s => s.Code == "NO_CONTACT_INFO"));
            Assert.IsTrue(rep.Playbook.Any(a => a.Id == "ESCALATE_NO_CONTACT"));
        }

        [TestMethod]
        public void Report_ColdTrail_FiresSignal_AndAuditActionWhen3Plus()
        {
            for (int i = 1; i <= 3; i++)
            {
                C(i);
                // 25 days overdue, $0 late fee.
                R(i, i, _clock.Today.AddDays(-30), _clock.Today.AddDays(-25),
                  dailyRate: 1.00m, lateFee: 0m);
            }
            var rep = _service.GenerateReport();
            foreach (var c in rep.Cases)
                Assert.IsTrue(c.Signals.Any(s => s.Code == "COLD_TRAIL"));
            Assert.IsTrue(rep.Playbook.Any(a => a.Id == "AUDIT_COLD_TRAIL"));
        }

        [TestMethod]
        public void RenderJson_IsDeterministic()
        {
            C(1);
            C(2);
            R(1, 1, _clock.Today.AddDays(-20), _clock.Today.AddDays(-10), dailyRate: 5.00m, lateFee: 15.00m);
            R(2, 2, _clock.Today.AddDays(-5), _clock.Today.AddDays(-2));

            var rep1 = _service.GenerateReport();
            var rep2 = _service.GenerateReport();
            var json1 = _service.RenderJson(rep1);
            var json2 = _service.RenderJson(rep2);
            Assert.AreEqual(json1, json2);
        }

        [TestMethod]
        public void RenderMarkdown_HeaderTablesAndPipeEscaping()
        {
            C(1, name: "Pipe|Name");
            R(1, 1, _clock.Today.AddDays(-10), _clock.Today.AddDays(-3), movie: "A|B");
            var rep = _service.GenerateReport();
            var md = _service.RenderMarkdown(rep);
            StringAssert.Contains(md, "# Late Return Escalation");
            StringAssert.Contains(md, "| Priority |");
            StringAssert.Contains(md, "Pipe\\|Name");
            StringAssert.Contains(md, "A\\|B1");
        }

        [TestMethod]
        public void RenderText_NullThrows()
        {
            Assert.ThrowsException<ArgumentNullException>(() => _service.RenderText(null));
        }

        [TestMethod]
        public void RenderMarkdown_NullThrows()
        {
            Assert.ThrowsException<ArgumentNullException>(() => _service.RenderMarkdown(null));
        }

        [TestMethod]
        public void RenderJson_NullThrows()
        {
            Assert.ThrowsException<ArgumentNullException>(() => _service.RenderJson(null));
        }

        [TestMethod]
        public void Ctor_NullArgs_Throw()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => new LateReturnEscalationService(null, _customers, _clock));
            Assert.ThrowsException<ArgumentNullException>(
                () => new LateReturnEscalationService(_rentals, null, _clock));
            Assert.ThrowsException<ArgumentNullException>(
                () => new LateReturnEscalationService(_rentals, _customers, null));
        }

        [TestMethod]
        public void Playbook_IsP0FirstAndDeduped()
        {
            // Build a portfolio with at least P0 + P1 actions.
            for (int i = 1; i <= 4; i++)
            {
                C(i);
                R(i, i, _clock.Today.AddDays(-50), _clock.Today.AddDays(-35), dailyRate: 5.00m);
            }
            var rep = _service.GenerateReport();
            // P0-first ordering.
            for (int i = 1; i < rep.Playbook.Count; i++)
                Assert.IsTrue((int)rep.Playbook[i].Priority >= (int)rep.Playbook[i - 1].Priority,
                    "Playbook not priority-ordered");
            // Dedup by Id.
            var ids = rep.Playbook.Select(a => a.Id).ToList();
            CollectionAssert.AllItemsAreUnique(ids);
            Assert.IsTrue(rep.Playbook.Any(a => a.Id == "OPEN_COLLECTIONS_BATCH"));
        }

        [TestMethod]
        public void ReturnedRentals_AreNotInReport()
        {
            C(1);
            // Returned (late) rental in the past.
            R(1, 1, _clock.Today.AddDays(-30), _clock.Today.AddDays(-20),
              returnDate: _clock.Today.AddDays(-10));
            // Active, not yet overdue.
            R(2, 1, _clock.Today.AddDays(-2), _clock.Today.AddDays(3));
            var rep = _service.GenerateReport();
            Assert.AreEqual(0, rep.Cases.Count);
        }

        [TestMethod]
        public void Evaluate_SingleNotOverdue_ReturnsNoneVerdict()
        {
            C(1);
            var r = R(1, 1, _clock.Today.AddDays(-2), _clock.Today.AddDays(3));
            var ec = _service.Evaluate(r);
            Assert.AreEqual(EscalationVerdict.None, ec.Verdict);
            Assert.AreEqual("NO_ACTION", ec.RecommendedActionId);
        }

        [TestMethod]
        public void Evaluate_NullRental_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => _service.Evaluate(null));
        }

        [TestMethod]
        public void LoyalGoldFirstTime_GentlerThanBasic()
        {
            // Two 4-day-late rentals, identical except membership.
            C(1, mt: MembershipType.Basic);
            C(2, mt: MembershipType.Gold);
            R(1, 1, _clock.Today.AddDays(-10), _clock.Today.AddDays(-4), dailyRate: 2.00m);
            R(2, 2, _clock.Today.AddDays(-10), _clock.Today.AddDays(-4), dailyRate: 2.00m);
            var rep = _service.GenerateReport();
            var basic = rep.Cases.Single(c => c.CustomerId == 1);
            var gold = rep.Cases.Single(c => c.CustomerId == 2);
            Assert.IsTrue(gold.Signals.Any(s => s.Code == "LOYAL_CUSTOMER_GRACE"));
            Assert.IsTrue((int)gold.Verdict <= (int)basic.Verdict,
                "Gold verdict (" + gold.Verdict + ") should be ≤ basic verdict (" + basic.Verdict + ")");
            Assert.IsTrue(gold.Score <= basic.Score);
        }

        [TestMethod]
        public void HighDollarsAtRiskInsight_Fires()
        {
            for (int i = 1; i <= 5; i++)
            {
                C(i);
                R(i, i, _clock.Today.AddDays(-30), _clock.Today.AddDays(-20),
                  dailyRate: 5.00m); // 5 * 20 * 5 = $500
            }
            var rep = _service.GenerateReport();
            CollectionAssert.Contains(rep.Summary.Insights, "HIGH_DOLLARS_AT_RISK");
            Assert.IsTrue(rep.Summary.TotalDollarsAtRisk > 200m);
        }

        // ── Test doubles ────────────────────────────────────────

        private class FakeClock : IClock
        {
            public FakeClock(DateTime today) { _today = today; }
            private readonly DateTime _today;
            public DateTime Now => _today;
            public DateTime Today => _today;
        }

        private class InMemoryTestCustomerRepo : ICustomerRepository
        {
            private readonly List<Customer> _data = new List<Customer>();
            public void Add(Customer entity) => _data.Add(entity);
            public void Remove(int id) => _data.RemoveAll(c => c.Id == id);
            public IReadOnlyList<Customer> GetAll() => _data.AsReadOnly();
            public Customer GetById(int id) => _data.FirstOrDefault(c => c.Id == id);
            public void Update(Customer entity) { }
            public IReadOnlyList<Customer> Search(string query, MembershipType? membershipType) =>
                _data.AsReadOnly();
            public IReadOnlyList<Customer> GetByMemberSince(int year, int month) =>
                new List<Customer>().AsReadOnly();
            public CustomerStats GetStats() => new CustomerStats { TotalCustomers = _data.Count };
        }

        private class InMemoryTestRentalRepo : IRentalRepository
        {
            private readonly List<Rental> _data = new List<Rental>();
            public void Add(Rental entity) => _data.Add(entity);
            public void Remove(int id) => _data.RemoveAll(r => r.Id == id);
            public IReadOnlyList<Rental> GetAll() => _data.AsReadOnly();
            public Rental GetById(int id) => _data.FirstOrDefault(r => r.Id == id);
            public void Update(Rental entity) { }
            public IReadOnlyList<Rental> GetByCustomer(int customerId) =>
                _data.Where(r => r.CustomerId == customerId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) =>
                _data.Where(r => r.CustomerId == customerId && r.Status != RentalStatus.Returned)
                     .ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetByMovie(int movieId) =>
                _data.Where(r => r.MovieId == movieId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetOverdue() =>
                _data.Where(r => r.IsOverdue).ToList().AsReadOnly();
            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) =>
                _data.AsReadOnly();
            public Rental ReturnRental(int rentalId) => GetById(rentalId);
            public bool IsMovieRentedOut(int movieId) => false;
            public Rental Checkout(Rental rental) => rental;
            public Rental Checkout(Rental rental, int maxConcurrentRentals) => rental;
            public Rental ExtendRental(int rentalId, int days) => GetById(rentalId);
            public bool IsExtended(int rentalId) => false;
            public RentalStats GetStats() => new RentalStats { TotalRentals = _data.Count };
        }
    }
}
