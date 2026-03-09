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
    public class RentalInsuranceServiceTests
    {
        private InMemoryCustomerRepository _customerRepo;
        private InMemoryRentalRepository _rentalRepo;
        private RentalInsuranceService _service;

        [TestInitialize]
        public void Setup()
        {
            InMemoryCustomerRepository.Reset();
            InMemoryRentalRepository.Reset();
            _customerRepo = new InMemoryCustomerRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _service = new RentalInsuranceService(_rentalRepo, _customerRepo);
        }

        private int _nextCustId = 9000;
        private int _nextRentalId = 9000;

        private Customer AddCustomer(string name = "Test Customer")
        {
            var c = new Customer { Id = _nextCustId++, Name = name, MembershipType = MembershipType.Basic };
            _customerRepo.Add(c);
            return c;
        }

        private Rental AddRental(int customerId, decimal dailyRate = 3.99m, RentalStatus status = RentalStatus.Active)
        {
            var r = new Rental
            {
                Id = _nextRentalId++,
                CustomerId = customerId,
                MovieId = 1,
                RentalDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(7),
                DailyRate = dailyRate,
                Status = status
            };
            _rentalRepo.Add(r);
            return r;
        }

        // ── Purchase ─────────────────────────────────────────────────

        [TestMethod]
        public void Purchase_BasicTier_CreatesPolicy()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Basic);

            Assert.IsNotNull(policy);
            Assert.AreEqual(InsuranceTier.Basic, policy.Tier);
            Assert.AreEqual(InsurancePolicyStatus.Active, policy.Status);
            Assert.AreEqual(10.00m, policy.CoverageLimit);
        }

        [TestMethod]
        public void Purchase_StandardTier_CorrectCoverage()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Standard);

            Assert.AreEqual(25.00m, policy.CoverageLimit);
        }

        [TestMethod]
        public void Purchase_PremiumTier_CorrectCoverage()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Premium);

            Assert.AreEqual(50.00m, policy.CoverageLimit);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Purchase_ReturnedRental_Throws()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id, status: RentalStatus.Returned);
            _service.Purchase(rental.Id, cust.Id, InsuranceTier.Basic);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Purchase_DuplicatePolicy_Throws()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            _service.Purchase(rental.Id, cust.Id, InsuranceTier.Basic);
            _service.Purchase(rental.Id, cust.Id, InsuranceTier.Premium);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Purchase_WrongCustomer_Throws()
        {
            var cust1 = AddCustomer("Alice");
            var cust2 = AddCustomer("Bob");
            var rental = AddRental(cust1.Id);
            _service.Purchase(rental.Id, cust2.Id, InsuranceTier.Basic);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Purchase_NonexistentRental_Throws()
        {
            var cust = AddCustomer();
            _service.Purchase(99999, cust.Id, InsuranceTier.Basic);
        }

        // ── Premium Calculation ──────────────────────────────────────

        [TestMethod]
        public void CalculatePremium_BasicTier_15Percent()
        {
            var premium = _service.CalculatePremium(4.00m, InsuranceTier.Basic);
            Assert.AreEqual(0.60m, premium);
        }

        [TestMethod]
        public void CalculatePremium_StandardTier_30Percent()
        {
            var premium = _service.CalculatePremium(4.00m, InsuranceTier.Standard);
            Assert.AreEqual(1.20m, premium);
        }

        [TestMethod]
        public void CalculatePremium_PremiumTier_50Percent()
        {
            var premium = _service.CalculatePremium(4.00m, InsuranceTier.Premium);
            Assert.AreEqual(2.00m, premium);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CalculatePremium_ZeroRate_Throws()
        {
            _service.CalculatePremium(0m, InsuranceTier.Basic);
        }

        // ── Quotes ───────────────────────────────────────────────────

        [TestMethod]
        public void GetQuotes_ReturnsAllTiers()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id, dailyRate: 5.00m);
            var quotes = _service.GetQuotes(rental.Id);

            Assert.AreEqual(3, quotes.Count);
            Assert.AreEqual(0.75m, quotes[InsuranceTier.Basic]);
            Assert.AreEqual(1.50m, quotes[InsuranceTier.Standard]);
            Assert.AreEqual(2.50m, quotes[InsuranceTier.Premium]);
        }

        // ── Claims ───────────────────────────────────────────────────

        [TestMethod]
        public void FileClaim_LateFee_BasicTier_Approved()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Basic);

            var claim = _service.FileClaim(policy.Id, ClaimType.LateFee, 5.00m);

            Assert.AreEqual(ClaimStatus.Approved, claim.Status);
            Assert.AreEqual(5.00m, claim.Amount);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void FileClaim_Damage_BasicTier_Throws()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Basic);

            // Basic only covers late fees
            _service.FileClaim(policy.Id, ClaimType.Damage, 5.00m);
        }

        [TestMethod]
        public void FileClaim_Damage_StandardTier_Approved()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Standard);

            var claim = _service.FileClaim(policy.Id, ClaimType.Damage, 10.00m);
            Assert.AreEqual(ClaimStatus.Approved, claim.Status);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void FileClaim_LostDisc_StandardTier_Throws()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Standard);

            _service.FileClaim(policy.Id, ClaimType.LostDisc, 20.00m);
        }

        [TestMethod]
        public void FileClaim_LostDisc_PremiumTier_Approved()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Premium);

            var claim = _service.FileClaim(policy.Id, ClaimType.LostDisc, 30.00m);
            Assert.AreEqual(ClaimStatus.Approved, claim.Status);
        }

        [TestMethod]
        public void FileClaim_ExceedsCoverage_CapsAmount()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Basic);
            // Basic covers up to $10

            var claim = _service.FileClaim(policy.Id, ClaimType.LateFee, 15.00m);
            Assert.AreEqual(10.00m, claim.Amount); // Capped at $10
        }

        [TestMethod]
        public void FileClaim_MultipleClaims_TracksCoverage()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Standard);
            // Standard covers up to $25

            _service.FileClaim(policy.Id, ClaimType.LateFee, 10.00m);
            var claim2 = _service.FileClaim(policy.Id, ClaimType.Damage, 20.00m);
            Assert.AreEqual(15.00m, claim2.Amount); // Only $15 remaining
        }

        [TestMethod]
        public void FileClaim_ExhaustsCoverage_MarksAsClaimed()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Basic);

            _service.FileClaim(policy.Id, ClaimType.LateFee, 10.00m);
            var updated = _service.GetPolicy(policy.Id);
            Assert.AreEqual(InsurancePolicyStatus.Claimed, updated.Status);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void FileClaim_ExhaustedPolicy_Throws()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Basic);

            _service.FileClaim(policy.Id, ClaimType.LateFee, 10.00m); // Exhausts $10
            _service.FileClaim(policy.Id, ClaimType.LateFee, 1.00m);  // Should throw
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void FileClaim_ZeroAmount_Throws()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Basic);

            _service.FileClaim(policy.Id, ClaimType.LateFee, 0m);
        }

        // ── Deny Claim ───────────────────────────────────────────────

        [TestMethod]
        public void DenyClaim_ReversesPayout()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Standard);

            var claim = _service.FileClaim(policy.Id, ClaimType.LateFee, 5.00m);
            _service.DenyClaim(claim.Id, "Fraudulent");

            Assert.AreEqual(ClaimStatus.Denied, claim.Status);
            Assert.AreEqual("Fraudulent", claim.DenialReason);

            var updated = _service.GetPolicy(policy.Id);
            Assert.AreEqual(0m, updated.TotalClaimed);
        }

        [TestMethod]
        public void DenyClaim_RestoresActiveStatus()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Basic);

            var claim = _service.FileClaim(policy.Id, ClaimType.LateFee, 10.00m);
            Assert.AreEqual(InsurancePolicyStatus.Claimed, policy.Status);

            _service.DenyClaim(claim.Id, "Error");
            Assert.AreEqual(InsurancePolicyStatus.Active, policy.Status);
        }

        // ── Cancel ───────────────────────────────────────────────────

        [TestMethod]
        public void CancelPolicy_Success()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Basic);

            var cancelled = _service.CancelPolicy(policy.Id);
            Assert.AreEqual(InsurancePolicyStatus.Cancelled, cancelled.Status);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CancelPolicy_WithClaims_Throws()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Basic);
            _service.FileClaim(policy.Id, ClaimType.LateFee, 3.00m);

            _service.CancelPolicy(policy.Id);
        }

        // ── Expire ───────────────────────────────────────────────────

        [TestMethod]
        public void ExpirePolicy_SetsExpired()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Basic);

            var expired = _service.ExpirePolicy(policy.Id);
            Assert.AreEqual(InsurancePolicyStatus.Expired, expired.Status);
        }

        // ── Query ────────────────────────────────────────────────────

        [TestMethod]
        public void GetPolicyForRental_ReturnsActivePolicy()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            _service.Purchase(rental.Id, cust.Id, InsuranceTier.Standard);

            var found = _service.GetPolicyForRental(rental.Id);
            Assert.IsNotNull(found);
            Assert.AreEqual(InsuranceTier.Standard, found.Tier);
        }

        [TestMethod]
        public void GetPolicyForRental_NoPolicy_ReturnsNull()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);

            Assert.IsNull(_service.GetPolicyForRental(rental.Id));
        }

        [TestMethod]
        public void GetCustomerPolicies_ReturnsAllPolicies()
        {
            var cust = AddCustomer();
            var r1 = AddRental(cust.Id);
            var r2 = AddRental(cust.Id);
            _service.Purchase(r1.Id, cust.Id, InsuranceTier.Basic);
            _service.Purchase(r2.Id, cust.Id, InsuranceTier.Premium);

            var policies = _service.GetCustomerPolicies(cust.Id);
            Assert.AreEqual(2, policies.Count);
        }

        [TestMethod]
        public void GetClaimsForPolicy_ReturnsClaims()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Premium);
            _service.FileClaim(policy.Id, ClaimType.LateFee, 5.00m);
            _service.FileClaim(policy.Id, ClaimType.Damage, 10.00m);

            var claims = _service.GetClaimsForPolicy(policy.Id);
            Assert.AreEqual(2, claims.Count);
        }

        // ── Analytics ────────────────────────────────────────────────

        [TestMethod]
        public void GetAnalytics_EmptyStore_ReturnsDefaults()
        {
            var analytics = _service.GetAnalytics();
            Assert.AreEqual(0, analytics.TotalPolicies);
            Assert.AreEqual(0, analytics.TotalClaims);
            Assert.AreEqual(0m, analytics.TotalPremiums);
        }

        [TestMethod]
        public void GetAnalytics_WithData_CalculatesCorrectly()
        {
            var cust = AddCustomer();
            var r1 = AddRental(cust.Id, dailyRate: 4.00m);
            var r2 = AddRental(cust.Id, dailyRate: 4.00m);

            var p1 = _service.Purchase(r1.Id, cust.Id, InsuranceTier.Basic);  // $0.60
            var p2 = _service.Purchase(r2.Id, cust.Id, InsuranceTier.Premium); // $2.00

            _service.FileClaim(p1.Id, ClaimType.LateFee, 5.00m);

            var analytics = _service.GetAnalytics();
            Assert.AreEqual(2, analytics.TotalPolicies);
            Assert.AreEqual(1, analytics.ActivePolicies);
            Assert.AreEqual(2.60m, analytics.TotalPremiums);
            Assert.AreEqual(5.00m, analytics.TotalPayouts);
            Assert.AreEqual(1, analytics.TotalClaims);
            Assert.AreEqual(1, analytics.ApprovedClaims);
        }

        [TestMethod]
        public void GetAnalytics_LossRatio_Calculated()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id, dailyRate: 10.00m);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Premium); // $5.00 premium

            _service.FileClaim(policy.Id, ClaimType.LateFee, 3.00m);

            var analytics = _service.GetAnalytics();
            Assert.AreEqual(60.0m, analytics.LossRatio); // 3/5 = 60%
        }

        // ── Uptake & Frequent Claimers ───────────────────────────────

        [TestMethod]
        public void GetUptakeRate_CalculatesCorrectly()
        {
            var cust = AddCustomer();
            AddRental(cust.Id);
            AddRental(cust.Id);
            var r3 = AddRental(cust.Id);
            _service.Purchase(r3.Id, cust.Id, InsuranceTier.Basic);

            var rate = _service.GetUptakeRate();
            Assert.AreEqual(33.3m, rate); // 1 out of 3
        }

        [TestMethod]
        public void IsFrequentClaimer_Under3Claims_ReturnsFalse()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Premium);
            _service.FileClaim(policy.Id, ClaimType.LateFee, 1.00m);
            _service.FileClaim(policy.Id, ClaimType.Damage, 1.00m);

            Assert.IsFalse(_service.IsFrequentClaimer(cust.Id));
        }

        [TestMethod]
        public void IsFrequentClaimer_3OrMoreClaims_ReturnsTrue()
        {
            var cust = AddCustomer();
            var rental = AddRental(cust.Id);
            var policy = _service.Purchase(rental.Id, cust.Id, InsuranceTier.Premium);
            _service.FileClaim(policy.Id, ClaimType.LateFee, 1.00m);
            _service.FileClaim(policy.Id, ClaimType.Damage, 1.00m);
            _service.FileClaim(policy.Id, ClaimType.LostDisc, 1.00m);

            Assert.IsTrue(_service.IsFrequentClaimer(cust.Id));
        }

        [TestMethod]
        public void GetTopClaimers_SortedByCount()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var r1 = AddRental(c1.Id);
            var r2 = AddRental(c2.Id);
            var p1 = _service.Purchase(r1.Id, c1.Id, InsuranceTier.Premium);
            var p2 = _service.Purchase(r2.Id, c2.Id, InsuranceTier.Premium);

            _service.FileClaim(p1.Id, ClaimType.LateFee, 1.00m);
            _service.FileClaim(p1.Id, ClaimType.Damage, 1.00m);
            _service.FileClaim(p2.Id, ClaimType.LateFee, 1.00m);

            var top = _service.GetTopClaimers();
            Assert.AreEqual(c1.Id, top[0].Key);
            Assert.AreEqual(2, top[0].Value);
        }
    }
}
