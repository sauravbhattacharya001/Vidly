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
    public class RefundFraudTriageServiceTests
    {
        // ── Test doubles ──────────────────────────────────────────

        private FakeRentalRepo _rentalRepo;
        private FakeCustomerRepo _customerRepo;
        private FixedClock _clock;
        private DateTime _today;
        private List<RefundRequest> _refundLedger;
        private RefundFraudTriageService _service;
        private int _nextRefundId;
        private int _nextRentalId;
        private int _nextCustomerId;

        [TestInitialize]
        public void Setup()
        {
            _rentalRepo = new FakeRentalRepo();
            _customerRepo = new FakeCustomerRepo();
            _today = new DateTime(2026, 5, 20);
            _clock = new FixedClock(_today);
            _refundLedger = new List<RefundRequest>();
            _nextRefundId = 1;
            _nextRentalId = 1;
            _nextCustomerId = 1;
            _service = new RefundFraudTriageService(
                _rentalRepo,
                _customerRepo,
                customerId => _refundLedger
                    .Where(r => r.CustomerId == customerId)
                    .ToList(),
                _clock);
        }

        // ── Fixtures ──────────────────────────────────────────────

        private Customer AddCustomer(
            string name = "Alice",
            MembershipType tier = MembershipType.Silver,
            int memberDaysAgo = 365)
        {
            var c = new Customer
            {
                Id = _nextCustomerId++,
                Name = name,
                MembershipType = tier,
                MemberSince = _today.AddDays(-memberDaysAgo),
            };
            _customerRepo.Customers.Add(c);
            return c;
        }

        private Rental AddRental(int customerId, decimal dailyRate = 3.00m,
            int rentalDaysAgo = 7, RentalStatus status = RentalStatus.Returned)
        {
            var r = new Rental
            {
                Id = _nextRentalId++,
                CustomerId = customerId,
                CustomerName = _customerRepo.GetById(customerId)?.Name,
                MovieId = 100,
                MovieName = "Test Movie",
                RentalDate = _today.AddDays(-rentalDaysAgo),
                DueDate = _today.AddDays(-rentalDaysAgo + 5),
                ReturnDate = status == RentalStatus.Returned
                    ? _today.AddDays(-rentalDaysAgo + 3) : (DateTime?)null,
                DailyRate = dailyRate,
                Status = status,
            };
            _rentalRepo.Rentals.Add(r);
            return r;
        }

        private RefundRequest BuildRequest(
            int customerId, int rentalId,
            decimal amount = 5.00m,
            RefundReason reason = RefundReason.DefectiveDisc,
            RefundType type = RefundType.Partial,
            int requestedDaysAgo = 0,
            RefundStatus status = RefundStatus.Pending,
            bool addToLedger = false)
        {
            var customer = _customerRepo.GetById(customerId);
            var rental = _rentalRepo.GetById(rentalId);
            var req = new RefundRequest
            {
                Id = _nextRefundId++,
                CustomerId = customerId,
                CustomerName = customer?.Name,
                RentalId = rentalId,
                MovieName = rental?.MovieName,
                Reason = reason,
                Type = type,
                OriginalAmount = amount,
                RefundAmount = amount,
                RequestedDate = _today.AddDays(-requestedDaysAgo),
                Status = status,
            };
            if (addToLedger) _refundLedger.Add(req);
            return req;
        }

        // ── Construction guards ───────────────────────────────────

        [TestMethod]
        public void Constructor_NullArgs_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new RefundFraudTriageService(
                null, _customerRepo, id => new List<RefundRequest>(), _clock));
            Assert.ThrowsException<ArgumentNullException>(() => new RefundFraudTriageService(
                _rentalRepo, null, id => new List<RefundRequest>(), _clock));
            Assert.ThrowsException<ArgumentNullException>(() => new RefundFraudTriageService(
                _rentalRepo, _customerRepo, null, _clock));
            Assert.ThrowsException<ArgumentNullException>(() => new RefundFraudTriageService(
                _rentalRepo, _customerRepo, id => new List<RefundRequest>(), null));
        }

        [TestMethod]
        public void Triage_NullRequest_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => _service.Triage(null));
        }

        // ── Happy path ────────────────────────────────────────────

        [TestMethod]
        public void Triage_LowRiskSmallRefund_AutoApprovesWithMinimalScore()
        {
            var c = AddCustomer(memberDaysAgo: 720, tier: MembershipType.Gold);
            // Give the customer rental history so THIN_RENTAL_HISTORY does not fire.
            AddRental(c.Id);
            AddRental(c.Id);
            var rental = AddRental(c.Id, rentalDaysAgo: 2);
            var req = BuildRequest(c.Id, rental.Id,
                amount: 4.00m, reason: RefundReason.BillingError);

            var t = _service.Triage(req);

            Assert.AreEqual(RefundFraudVerdict.AutoApprove, t.Verdict);
            Assert.IsTrue(t.Score < _service.Config.StandardReviewThreshold);
            Assert.AreEqual(RefundFraudRiskBand.Minimal, t.RiskBand);
            Assert.IsTrue(t.Actions.Any(a => a.Code == "AUTO_APPROVE_ELIGIBLE"));
            Assert.AreEqual(c.Name, t.CustomerName);
            Assert.AreEqual(req.Id, t.RefundRequestId);
        }

        // ── Velocity ─────────────────────────────────────────────

        [TestMethod]
        public void Triage_HighRefundVelocity_RaisesScoreAndCriticalSignal()
        {
            var c = AddCustomer();
            AddRental(c.Id);
            var rental = AddRental(c.Id);
            // 3 prior refund requests inside the velocity window.
            for (int i = 0; i < 3; i++)
            {
                BuildRequest(c.Id, rental.Id, amount: 5.00m,
                    requestedDaysAgo: 5 + i,
                    status: RefundStatus.Processed,
                    addToLedger: true);
            }
            var req = BuildRequest(c.Id, rental.Id, amount: 5.00m);

            var t = _service.Triage(req);

            Assert.IsTrue(t.Signals.Any(s => s.Code == "VELOCITY_HIGH" && s.Severity == "critical"));
            Assert.IsTrue(t.Score >= 25);
        }

        [TestMethod]
        public void Triage_ModerateVelocity_GetsWarnSignal()
        {
            var c = AddCustomer();
            AddRental(c.Id);
            var rental = AddRental(c.Id);
            for (int i = 0; i < 2; i++)
                BuildRequest(c.Id, rental.Id,
                    requestedDaysAgo: 5 + i,
                    status: RefundStatus.Processed,
                    addToLedger: true);
            var req = BuildRequest(c.Id, rental.Id);

            var t = _service.Triage(req);

            Assert.IsTrue(t.Signals.Any(s => s.Code == "VELOCITY_MODERATE"));
            Assert.IsFalse(t.Signals.Any(s => s.Code == "VELOCITY_HIGH"));
        }

        // ── Denial history ───────────────────────────────────────

        [TestMethod]
        public void Triage_HighDenialRate_ContributesCriticalSignal()
        {
            var c = AddCustomer();
            AddRental(c.Id);
            var rental = AddRental(c.Id);
            // Prior history: 3 denied, 1 approved → 75% denial rate.
            // Place these OUTSIDE the velocity window (30d) so velocity doesn't add noise.
            BuildRequest(c.Id, rental.Id, requestedDaysAgo: 60,
                status: RefundStatus.Denied, addToLedger: true);
            BuildRequest(c.Id, rental.Id, requestedDaysAgo: 90,
                status: RefundStatus.Denied, addToLedger: true);
            BuildRequest(c.Id, rental.Id, requestedDaysAgo: 120,
                status: RefundStatus.Denied, addToLedger: true);
            BuildRequest(c.Id, rental.Id, requestedDaysAgo: 150,
                status: RefundStatus.Approved, addToLedger: true);

            var req = BuildRequest(c.Id, rental.Id);
            var t = _service.Triage(req);

            Assert.IsTrue(t.Signals.Any(s => s.Code == "HIGH_DENIAL_RATE"));
            Assert.IsTrue(t.Actions.Any(a => a.Code == "REVIEW_DENIAL_HISTORY"));
        }

        // ── Timing ───────────────────────────────────────────────

        [TestMethod]
        public void Triage_LateRefundRequest_ContributesWarnSignal()
        {
            var c = AddCustomer();
            AddRental(c.Id);
            var rental = AddRental(c.Id, rentalDaysAgo: 20);

            var req = BuildRequest(c.Id, rental.Id, requestedDaysAgo: 0);
            var t = _service.Triage(req);

            Assert.IsTrue(t.Signals.Any(s => s.Code == "LATE_REFUND_REQUEST"));
        }

        [TestMethod]
        public void Triage_SameDayDefectiveDiscOnReturnedRental_WarnSignal()
        {
            var c = AddCustomer();
            AddRental(c.Id);
            // Rental created today, already returned (a same-day rental+return scenario).
            var rental = AddRental(c.Id, rentalDaysAgo: 0, status: RentalStatus.Returned);
            rental.ReturnDate = _today;
            var req = BuildRequest(c.Id, rental.Id,
                requestedDaysAgo: 0, reason: RefundReason.DefectiveDisc);

            var t = _service.Triage(req);

            Assert.IsTrue(t.Signals.Any(s => s.Code == "SAME_DAY_DEFECT_CLAIM"));
        }

        // ── Amount tiers ─────────────────────────────────────────

        [TestMethod]
        public void Triage_LargeAmount_AddsLargeAmountSignal()
        {
            var c = AddCustomer();
            AddRental(c.Id);
            var rental = AddRental(c.Id);
            var req = BuildRequest(c.Id, rental.Id, amount: 75.00m);

            var t = _service.Triage(req);

            Assert.IsTrue(t.Signals.Any(s => s.Code == "LARGE_AMOUNT"));
        }

        [TestMethod]
        public void Triage_AmountAboveAutoApproveCap_NoAutoApproveEvenIfLowScore()
        {
            var c = AddCustomer(memberDaysAgo: 720, tier: MembershipType.Gold);
            AddRental(c.Id);
            AddRental(c.Id);
            var rental = AddRental(c.Id, rentalDaysAgo: 2);
            // 20-dollar refund: above the $10 auto-approve cap, below medium tier ($25).
            var req = BuildRequest(c.Id, rental.Id,
                amount: 20.00m, reason: RefundReason.BillingError);

            var t = _service.Triage(req);

            // Score should be low (no warn signals expected), but verdict must NOT auto-approve.
            Assert.AreNotEqual(RefundFraudVerdict.AutoApprove, t.Verdict);
        }

        // ── New customer ─────────────────────────────────────────

        [TestMethod]
        public void Triage_NewCustomerFullRefund_CriticalSignalAndIdentityVerifyAction()
        {
            var c = AddCustomer(memberDaysAgo: 3, tier: MembershipType.Basic);
            var rental = AddRental(c.Id, rentalDaysAgo: 1);
            var req = BuildRequest(c.Id, rental.Id, amount: 30.00m,
                type: RefundType.Full, reason: RefundReason.WrongMovie);

            var t = _service.Triage(req);

            Assert.IsTrue(t.Signals.Any(s => s.Code == "NEW_CUSTOMER_FULL_REFUND"));
            Assert.IsTrue(t.Actions.Any(a => a.Code == "VERIFY_CUSTOMER_IDENTITY"));
        }

        // ── Repeat reason pattern ────────────────────────────────

        [TestMethod]
        public void Triage_RepeatedSameReason_CriticalSignalAndInvestigateAction()
        {
            var c = AddCustomer();
            AddRental(c.Id);
            var rental = AddRental(c.Id);
            // 3 prior same-reason refunds within the repeat window, but outside the
            // velocity window so velocity stays at LIGHT (no compounding).
            for (int i = 0; i < 3; i++)
            {
                BuildRequest(c.Id, rental.Id,
                    requestedDaysAgo: 40 + i * 30,
                    reason: RefundReason.DefectiveDisc,
                    status: RefundStatus.Processed,
                    addToLedger: true);
            }
            var req = BuildRequest(c.Id, rental.Id, reason: RefundReason.DefectiveDisc);

            var t = _service.Triage(req);

            Assert.IsTrue(t.Signals.Any(s => s.Code == "REPEAT_REASON_PATTERN"));
            Assert.IsTrue(t.Actions.Any(a => a.Code == "INVESTIGATE_REASON_PATTERN"));
        }

        // ── Vague reason ─────────────────────────────────────────

        [TestMethod]
        public void Triage_OtherReason_AddsVagueReasonSignal()
        {
            var c = AddCustomer();
            AddRental(c.Id);
            var rental = AddRental(c.Id);
            var req = BuildRequest(c.Id, rental.Id, reason: RefundReason.Other);

            var t = _service.Triage(req);

            Assert.IsTrue(t.Signals.Any(s => s.Code == "VAGUE_REASON"));
        }

        // ── Membership ───────────────────────────────────────────

        [TestMethod]
        public void Triage_PlatinumCustomer_ReducesScoreViaNegativeDelta()
        {
            var basicCust = AddCustomer(name: "Basic", tier: MembershipType.Basic);
            AddRental(basicCust.Id);
            var basicRental = AddRental(basicCust.Id);
            var basicReq = BuildRequest(basicCust.Id, basicRental.Id, amount: 30.00m);
            var basicTriage = _service.Triage(basicReq);

            var platCust = AddCustomer(name: "Plat", tier: MembershipType.Platinum);
            AddRental(platCust.Id);
            var platRental = AddRental(platCust.Id);
            var platReq = BuildRequest(platCust.Id, platRental.Id, amount: 30.00m);
            var platTriage = _service.Triage(platReq);

            Assert.IsTrue(platTriage.Score < basicTriage.Score,
                $"Platinum score ({platTriage.Score}) should be < Basic score ({basicTriage.Score}).");
            Assert.IsTrue(platTriage.Signals.Any(s => s.Code == "MEMBERSHIP_PLATINUM" && s.ScoreDelta < 0));
        }

        // ── Thin history ─────────────────────────────────────────

        [TestMethod]
        public void Triage_ThinRentalHistory_AddsWarnSignal()
        {
            var c = AddCustomer();
            // Exactly 1 rental → "only 1 prior rental" path.
            var rental = AddRental(c.Id);
            var req = BuildRequest(c.Id, rental.Id);

            var t = _service.Triage(req);

            Assert.IsTrue(t.Signals.Any(s => s.Code == "THIN_RENTAL_HISTORY"));
        }

        // ── Risk appetite ────────────────────────────────────────

        [TestMethod]
        public void Triage_CautiousAppetite_ScoresHigherThanBalanced()
        {
            var customerId = SetupRiskyCustomer();
            var baselineService = _service;
            var cautiousService = new RefundFraudTriageService(
                _rentalRepo, _customerRepo,
                id => _refundLedger.Where(r => r.CustomerId == id).ToList(),
                _clock,
                new RefundFraudTriageConfig { RiskAppetite = RefundFraudRiskAppetite.Cautious });

            var rental = _rentalRepo.GetByCustomer(customerId).Last();
            var req = BuildRequest(customerId, rental.Id, amount: 30.00m);

            var balanced = baselineService.Triage(req);
            var cautious = cautiousService.Triage(req);

            Assert.IsTrue(cautious.Score >= balanced.Score);
            // Cautious adds the "second pair of eyes" P2 nudge on actionable cases.
            if (cautious.Verdict != RefundFraudVerdict.AutoApprove)
                Assert.IsTrue(cautious.Actions.Any(a => a.Code == "SECOND_PAIR_OF_EYES"));
        }

        [TestMethod]
        public void Triage_AggressiveAppetite_ScoresLowerThanBalanced()
        {
            var customerId = SetupRiskyCustomer();
            var balanced = _service;
            var aggressive = new RefundFraudTriageService(
                _rentalRepo, _customerRepo,
                id => _refundLedger.Where(r => r.CustomerId == id).ToList(),
                _clock,
                new RefundFraudTriageConfig { RiskAppetite = RefundFraudRiskAppetite.Aggressive });

            var rental = _rentalRepo.GetByCustomer(customerId).Last();
            var req = BuildRequest(customerId, rental.Id, amount: 30.00m);

            var b = balanced.Triage(req);
            var a = aggressive.Triage(req);

            Assert.IsTrue(a.Score <= b.Score);
        }

        private int SetupRiskyCustomer()
        {
            var c = AddCustomer();
            AddRental(c.Id);
            var rental = AddRental(c.Id);
            // 2 prior recent refunds → moderate velocity.
            BuildRequest(c.Id, rental.Id, requestedDaysAgo: 5,
                status: RefundStatus.Processed, addToLedger: true);
            BuildRequest(c.Id, rental.Id, requestedDaysAgo: 8,
                status: RefundStatus.Processed, addToLedger: true);
            return c.Id;
        }

        // ── Verdict thresholds ────────────────────────────────────

        [TestMethod]
        public void Triage_VeryHighRisk_BlocksWithEvidenceAndHoldActions()
        {
            var c = AddCustomer(memberDaysAgo: 2, tier: MembershipType.Basic);
            var rental = AddRental(c.Id, rentalDaysAgo: 1);
            // Stack: new + full refund + large amount + high velocity + repeat reason.
            for (int i = 0; i < 4; i++)
                BuildRequest(c.Id, rental.Id,
                    requestedDaysAgo: 1 + i,
                    reason: RefundReason.DefectiveDisc,
                    status: RefundStatus.Denied,
                    addToLedger: true);

            var req = BuildRequest(c.Id, rental.Id,
                amount: 80.00m,
                type: RefundType.Full,
                reason: RefundReason.DefectiveDisc);

            var t = _service.Triage(req);

            Assert.AreEqual(RefundFraudVerdict.Block, t.Verdict);
            Assert.IsTrue(t.Actions.Any(a => a.Code == "HOLD_PAYOUT" && a.Priority == RefundFraudActionPriority.P0));
            Assert.IsTrue(t.Actions.Any(a => a.Code == "FLAG_ACCOUNT"));
            Assert.IsTrue(t.Actions.Any(a => a.Code == "REQUEST_EVIDENCE"));
            Assert.AreEqual(RefundFraudRiskBand.High, t.RiskBand);
        }

        [TestMethod]
        public void Triage_MissingRental_AddsP0MissingRentalAction()
        {
            var c = AddCustomer();
            // Don't add a rental, but reference a non-existent rental id.
            var req = BuildRequest(c.Id, rentalId: 99999, amount: 5.00m);

            var t = _service.Triage(req);

            Assert.IsTrue(t.Actions.Any(a => a.Code == "MISSING_RENTAL"
                && a.Priority == RefundFraudActionPriority.P0));
        }

        // ── Score clamps to 0-100 ────────────────────────────────

        [TestMethod]
        public void Triage_ExtremeStacking_ScoreClampedTo100()
        {
            // 10 prior denied same-reason refunds + new customer + large full refund.
            var c = AddCustomer(memberDaysAgo: 1, tier: MembershipType.Basic);
            var rental = AddRental(c.Id, rentalDaysAgo: 0);
            for (int i = 0; i < 10; i++)
                BuildRequest(c.Id, rental.Id,
                    requestedDaysAgo: 1 + i,
                    reason: RefundReason.Other,
                    status: RefundStatus.Denied,
                    addToLedger: true);

            var req = BuildRequest(c.Id, rental.Id,
                amount: 99.00m, type: RefundType.Full,
                reason: RefundReason.Other);

            var t = _service.Triage(req);

            Assert.IsTrue(t.Score <= 100);
            Assert.IsTrue(t.Score >= 80);
            Assert.AreEqual(RefundFraudRiskBand.High, t.RiskBand);
        }

        // ── Many + summary + report ───────────────────────────────

        [TestMethod]
        public void TriageMany_AndSummarize_AggregatesCorrectly()
        {
            var c = AddCustomer(memberDaysAgo: 720, tier: MembershipType.Gold);
            AddRental(c.Id); AddRental(c.Id);
            var rental = AddRental(c.Id, rentalDaysAgo: 2);

            var lowReq = BuildRequest(c.Id, rental.Id, amount: 3.00m,
                reason: RefundReason.BillingError);

            // Build a high-risk request via fresh risky customer.
            var riskyCust = AddCustomer(memberDaysAgo: 1, tier: MembershipType.Basic);
            var riskyRental = AddRental(riskyCust.Id, rentalDaysAgo: 0);
            for (int i = 0; i < 4; i++)
                BuildRequest(riskyCust.Id, riskyRental.Id,
                    requestedDaysAgo: 1 + i,
                    reason: RefundReason.DefectiveDisc,
                    status: RefundStatus.Denied,
                    addToLedger: true);
            var highReq = BuildRequest(riskyCust.Id, riskyRental.Id,
                amount: 90.00m, type: RefundType.Full,
                reason: RefundReason.DefectiveDisc);

            var triages = _service.TriageMany(new[] { lowReq, highReq });
            var summary = _service.Summarize(triages);

            Assert.AreEqual(2, summary.TotalRequests);
            Assert.IsTrue(summary.BlockCount + summary.EnhancedReviewCount >= 1);
            Assert.IsTrue(summary.AutoApproveCount >= 1);
            Assert.IsTrue(summary.AmountAtRisk >= 90.00m);
            Assert.AreEqual(2, summary.ByBand.Values.Sum());
        }

        [TestMethod]
        public void RenderTextReport_ContainsHeaderAndVerdict()
        {
            var c = AddCustomer(memberDaysAgo: 720, tier: MembershipType.Gold);
            AddRental(c.Id); AddRental(c.Id);
            var rental = AddRental(c.Id, rentalDaysAgo: 2);
            var req = BuildRequest(c.Id, rental.Id, amount: 3.00m,
                reason: RefundReason.BillingError);

            var t = _service.Triage(req);
            var report = _service.RenderTextReport(new[] { t });

            Assert.IsTrue(report.Contains("Refund Fraud Triage Report"));
            Assert.IsTrue(report.Contains("AutoApprove"));
            Assert.IsTrue(report.Contains(c.Name));
        }

        [TestMethod]
        public void TriageMany_NullInput_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => _service.TriageMany(null));
        }

        [TestMethod]
        public void Summarize_NullInput_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => _service.Summarize(null));
        }

        [TestMethod]
        public void RenderTextReport_NullInput_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => _service.RenderTextReport(null));
        }

        // ── Test doubles ──────────────────────────────────────────

        private class FixedClock : IClock
        {
            public FixedClock(DateTime now) { Now = now; Today = now.Date; }
            public DateTime Now { get; }
            public DateTime Today { get; }
        }

        private class FakeRentalRepo : IRentalRepository
        {
            public readonly List<Rental> Rentals = new List<Rental>();
            public IReadOnlyList<Rental> GetAll() => Rentals;
            public Rental GetById(int id) => Rentals.FirstOrDefault(r => r.Id == id);
            public void Add(Rental entity) => Rentals.Add(entity);
            public void Update(Rental entity) { }
            public void Remove(int id) => Rentals.RemoveAll(r => r.Id == id);
            public IReadOnlyList<Rental> GetByCustomer(int customerId) =>
                Rentals.Where(r => r.CustomerId == customerId).ToList();
            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) =>
                Rentals.Where(r => r.CustomerId == customerId
                    && r.Status != RentalStatus.Returned).ToList();
            public IReadOnlyList<Rental> GetByMovie(int movieId) =>
                Rentals.Where(r => r.MovieId == movieId).ToList();
            public IReadOnlyList<Rental> GetOverdue() => throw new NotImplementedException();
            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) =>
                throw new NotImplementedException();
            public Rental ReturnRental(int rentalId) => throw new NotImplementedException();
            public bool IsMovieRentedOut(int movieId) => throw new NotImplementedException();
            public Rental Checkout(Rental rental) => throw new NotImplementedException();
            public Rental Checkout(Rental rental, int maxConcurrentRentals) =>
                throw new NotImplementedException();
            public Rental ExtendRental(int rentalId, int days) => throw new NotImplementedException();
            public bool IsExtended(int rentalId) => throw new NotImplementedException();
            public RentalStats GetStats() => throw new NotImplementedException();
        }

        private class FakeCustomerRepo : ICustomerRepository
        {
            public readonly List<Customer> Customers = new List<Customer>();
            public Customer GetById(int id) => Customers.FirstOrDefault(c => c.Id == id);
            public IReadOnlyList<Customer> GetAll() => Customers;
            public void Add(Customer entity) => Customers.Add(entity);
            public void Update(Customer entity) { }
            public void Remove(int id) => Customers.RemoveAll(c => c.Id == id);
            public IReadOnlyList<Customer> Search(string query, MembershipType? membershipType) =>
                throw new NotImplementedException();
            public IReadOnlyList<Customer> GetByMemberSince(int year, int month) =>
                throw new NotImplementedException();
            public CustomerStats GetStats() => throw new NotImplementedException();
        }
    }
}
