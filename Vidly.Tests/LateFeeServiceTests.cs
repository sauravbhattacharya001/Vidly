using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class LateFeeServiceTests
    {
        private LateFeeService _svc;

        [TestInitialize]
        public void Setup()
        {
            _svc = new LateFeeService();
        }

        // ── GetAllPolicies ───────────────────────────────────────────

        [TestMethod]
        public void GetAllPolicies_ReturnsNonEmpty_SortedByActiveDescThenName()
        {
            var policies = _svc.GetAllPolicies();
            Assert.IsTrue(policies.Count >= 3, "Should have at least the 3 seed policies");

            // Active policies should come first
            var firstInactive = policies.FindIndex(p => !p.IsActive);
            if (firstInactive >= 0)
            {
                for (int i = firstInactive; i < policies.Count; i++)
                    Assert.IsFalse(policies[i].IsActive,
                        "All inactive policies should be grouped after active ones");
            }
        }

        // ── GetPolicy ────────────────────────────────────────────────

        [TestMethod]
        public void GetPolicy_ExistingId_ReturnsPolicy()
        {
            var policy = _svc.GetPolicy(1);
            Assert.IsNotNull(policy);
            Assert.AreEqual("Standard", policy.Name);
        }

        [TestMethod]
        public void GetPolicy_NonExistentId_ReturnsNull()
        {
            Assert.IsNull(_svc.GetPolicy(9999));
        }

        // ── GetActivePolicy ──────────────────────────────────────────

        [TestMethod]
        public void GetActivePolicy_ReturnsFirstActivePolicy()
        {
            var active = _svc.GetActivePolicy();
            Assert.IsNotNull(active);
            Assert.IsTrue(active.IsActive);
        }

        // ── SavePolicy (create) ──────────────────────────────────────

        [TestMethod]
        public void SavePolicy_NewPolicy_AssignsIdAndAddsToList()
        {
            var before = _svc.GetAllPolicies().Count;
            var newPolicy = new LateFeePolicy
            {
                Name = "Test Policy",
                Strategy = LateFeeStrategy.FlatFee,
                FlatFeeAmount = 5.00m,
                GracePeriodDays = 0,
                MaxFeeCap = 0
            };

            _svc.SavePolicy(newPolicy);

            Assert.IsTrue(newPolicy.Id > 0, "Should assign a non-zero id");
            Assert.AreEqual(before + 1, _svc.GetAllPolicies().Count);
            Assert.IsNotNull(_svc.GetPolicy(newPolicy.Id));
        }

        // ── SavePolicy (update) ──────────────────────────────────────

        [TestMethod]
        public void SavePolicy_ExistingPolicy_UpdatesFields()
        {
            var policy = _svc.GetPolicy(1);
            var originalName = policy.Name;

            _svc.SavePolicy(new LateFeePolicy
            {
                Id = 1,
                Name = "Updated Standard",
                Strategy = LateFeeStrategy.PerDay,
                PerDayRate = 2.00m,
                GracePeriodDays = 2,
                MaxFeeCap = 30.00m,
                IsActive = true
            });

            var updated = _svc.GetPolicy(1);
            Assert.AreEqual("Updated Standard", updated.Name);
            Assert.AreEqual(2.00m, updated.PerDayRate);
            Assert.AreEqual(2, updated.GracePeriodDays);

            // Restore for other tests
            _svc.SavePolicy(new LateFeePolicy
            {
                Id = 1,
                Name = originalName,
                Strategy = LateFeeStrategy.PerDay,
                PerDayRate = RentalPolicyConstants.LateFeePerDay,
                GracePeriodDays = RentalPolicyConstants.BaseReturnGracePeriodDays,
                MaxFeeCap = RentalPolicyConstants.MaxLateFeeCap,
                IsActive = true
            });
        }

        // ── DeletePolicy ─────────────────────────────────────────────

        [TestMethod]
        public void DeletePolicy_ExistingId_RemovesAndReturnsTrue()
        {
            var p = new LateFeePolicy
            {
                Name = "Deletable",
                Strategy = LateFeeStrategy.FlatFee,
                FlatFeeAmount = 1m
            };
            _svc.SavePolicy(p);
            int id = p.Id;

            Assert.IsTrue(_svc.DeletePolicy(id));
            Assert.IsNull(_svc.GetPolicy(id));
        }

        [TestMethod]
        public void DeletePolicy_NonExistentId_ReturnsFalse()
        {
            Assert.IsFalse(_svc.DeletePolicy(9999));
        }

        // ── CalculateEstimate — Standard (PerDay) ────────────────────

        [TestMethod]
        public void CalculateEstimate_Standard_WithinGracePeriod_ZeroFee()
        {
            // Standard policy has GracePeriodDays = BaseReturnGracePeriodDays (1)
            var est = _svc.CalculateEstimate(1, 1);
            Assert.AreEqual(0m, est.Fee, "1 day overdue with 1-day grace = no fee");
            Assert.AreEqual(0, est.ChargeableDays);
        }

        [TestMethod]
        public void CalculateEstimate_Standard_BeyondGrace_ChargesPerDay()
        {
            // 5 days overdue, 1 day grace = 4 chargeable days * 1.50
            var est = _svc.CalculateEstimate(1, 5);
            Assert.AreEqual(4, est.ChargeableDays);
            Assert.AreEqual(4 * RentalPolicyConstants.LateFeePerDay, est.Fee);
            Assert.AreEqual("Standard", est.PolicyName);
        }

        [TestMethod]
        public void CalculateEstimate_Standard_HitsMaxCap()
        {
            // Enough days to exceed the MaxLateFeeCap (25.00)
            // 1.50 per day * 18 chargeable days = 27.00 > 25.00 cap
            var est = _svc.CalculateEstimate(1, 20); // 20 - 1 grace = 19 * 1.50 = 28.50, capped at 25
            Assert.AreEqual(RentalPolicyConstants.MaxLateFeeCap, est.Fee);
            Assert.IsTrue(est.WasCapped);
        }

        [TestMethod]
        public void CalculateEstimate_ZeroDaysOverdue_ZeroFee()
        {
            var est = _svc.CalculateEstimate(1, 0);
            Assert.AreEqual(0m, est.Fee);
        }

        [TestMethod]
        public void CalculateEstimate_NegativeDays_ZeroFee()
        {
            var est = _svc.CalculateEstimate(1, -3);
            Assert.AreEqual(0m, est.Fee);
        }

        // ── CalculateEstimate — Premium Member ───────────────────────

        [TestMethod]
        public void CalculateEstimate_Premium_LowerRateAndHigherGrace()
        {
            // Premium: 0.75/day, 3-day grace, 15.00 cap
            var est = _svc.CalculateEstimate(2, 6);
            // 6 - 3 = 3 chargeable days * 0.75 = 2.25
            Assert.AreEqual(3, est.ChargeableDays);
            Assert.AreEqual(2.25m, est.Fee);
        }

        [TestMethod]
        public void CalculateEstimate_Premium_HitsCap()
        {
            // 0.75/day, 3 grace, cap 15.00 → 20 chargeable = 15.00
            var est = _svc.CalculateEstimate(2, 24);
            Assert.AreEqual(15.00m, est.Fee);
            Assert.IsTrue(est.WasCapped);
        }

        // ── CalculateEstimate — Graduated (Tiered) ───────────────────

        [TestMethod]
        public void CalculateEstimate_Tiered_CorrectBreakdown()
        {
            // Graduated policy (id=3): 1-day grace
            // Tiers: days 1-3 @ 1.00, days 4-7 @ 2.00, days 8+ @ 3.00
            // 10 days overdue → 9 chargeable days
            // Tier 1: 3 days * 1.00 = 3.00
            // Tier 2: 4 days * 2.00 = 8.00
            // Tier 3: 2 days * 3.00 = 6.00
            // Total: 17.00
            var est = _svc.CalculateEstimate(3, 10);
            Assert.AreEqual(9, est.ChargeableDays);
            Assert.AreEqual(17.00m, est.Fee);
            Assert.AreEqual(LateFeeStrategy.Tiered, est.Strategy);
            Assert.IsTrue(est.TierBreakdowns.Count >= 3,
                "Should have breakdown for all 3 tiers");
        }

        [TestMethod]
        public void CalculateEstimate_Tiered_OnlyFirstTier()
        {
            // 3 days overdue, 1 grace = 2 chargeable
            // All in tier 1 (days 1-3) @ 1.00 = 2.00
            var est = _svc.CalculateEstimate(3, 3);
            Assert.AreEqual(2, est.ChargeableDays);
            Assert.AreEqual(2.00m, est.Fee);
        }

        [TestMethod]
        public void CalculateEstimate_Tiered_HitsCap()
        {
            // Cap is 50.00 for graduated policy
            // Need enough days: tier1 (3*1) + tier2 (4*2) + tier3 (n*3)
            // 3 + 8 + 3n > 50 → n > 13 → 14 days in tier 3
            // Total chargeable: 3 + 4 + 14 = 21, plus 1 grace = 22 days overdue
            var est = _svc.CalculateEstimate(3, 22);
            Assert.AreEqual(50.00m, est.Fee);
            Assert.IsTrue(est.WasCapped);
        }

        // ── CalculateEstimate — Nonexistent policy falls back ────────

        [TestMethod]
        public void CalculateEstimate_InvalidPolicyId_FallsBackToActivePolicy()
        {
            var est = _svc.CalculateEstimate(9999, 5);
            Assert.IsNotNull(est);
            Assert.IsTrue(est.Fee >= 0);
        }

        // ── BuildSchedule ────────────────────────────────────────────

        [TestMethod]
        public void BuildSchedule_ReturnsCorrectNumberOfEntries()
        {
            var schedule = _svc.BuildSchedule(1, 15);
            Assert.AreEqual(15, schedule.Count);
            Assert.AreEqual(1, schedule[0].DaysOverdue);
            Assert.AreEqual(15, schedule[14].DaysOverdue);
        }

        [TestMethod]
        public void BuildSchedule_FeesAreMonotonicallyNonDecreasing()
        {
            var schedule = _svc.BuildSchedule(1, 30);
            for (int i = 1; i < schedule.Count; i++)
            {
                Assert.IsTrue(schedule[i].Fee >= schedule[i - 1].Fee,
                    $"Fee at day {schedule[i].DaysOverdue} should be >= day {schedule[i - 1].DaysOverdue}");
            }
        }

        [TestMethod]
        public void BuildSchedule_DefaultMaxDays_Returns30()
        {
            var schedule = _svc.BuildSchedule(1);
            Assert.AreEqual(30, schedule.Count);
        }

        // ── FlatFee strategy via custom policy ───────────────────────

        [TestMethod]
        public void CalculateEstimate_FlatFee_ChargesFixedAmount()
        {
            var flat = new LateFeePolicy
            {
                Name = "Flat Test",
                Strategy = LateFeeStrategy.FlatFee,
                FlatFeeAmount = 10.00m,
                GracePeriodDays = 0,
                MaxFeeCap = 0
            };
            _svc.SavePolicy(flat);

            var est = _svc.CalculateEstimate(flat.Id, 15);
            Assert.AreEqual(10.00m, est.Fee, "Flat fee should be the same regardless of days");

            // Cleanup
            _svc.DeletePolicy(flat.Id);
        }
    }
}
