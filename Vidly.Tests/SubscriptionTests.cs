using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class SubscriptionTests
    {
        private SubscriptionService _service;
        private ISubscriptionRepository _subRepo;
        private ICustomerRepository _custRepo;

        [TestInitialize]
        public void Setup()
        {
            _subRepo = new InMemorySubscriptionRepository();
            _custRepo = new InMemoryCustomerRepository();
            _service = new SubscriptionService(_subRepo, _custRepo);
        }

        // --- Plan configuration ---

        [TestMethod]
        public void GetAvailablePlans_ReturnsThreePlans()
        {
            var plans = SubscriptionService.GetAvailablePlans();
            Assert.AreEqual(3, plans.Count);
        }

        [TestMethod]
        public void GetAvailablePlans_OrderedByPrice()
        {
            var plans = SubscriptionService.GetAvailablePlans();
            Assert.IsTrue(plans[0].MonthlyPrice < plans[1].MonthlyPrice);
            Assert.IsTrue(plans[1].MonthlyPrice < plans[2].MonthlyPrice);
        }

        [TestMethod]
        public void GetPlan_Basic_Has2Rentals()
        {
            var plan = SubscriptionService.GetPlan(SubscriptionPlanType.Basic);
            Assert.AreEqual(2, plan.RentalsPerMonth);
            Assert.AreEqual("Basic", plan.Name);
        }

        [TestMethod]
        public void GetPlan_Premium_Unlimited()
        {
            var plan = SubscriptionService.GetPlan(SubscriptionPlanType.Premium);
            Assert.AreEqual(0, plan.RentalsPerMonth); // 0 = unlimited
        }

        // --- Subscribe ---

        [TestMethod]
        public void Subscribe_NewCustomer_CreatesActiveSubscription()
        {
            // Customer 3 has no subscription
            var sub = _service.Subscribe(3, SubscriptionPlanType.Basic);

            Assert.IsNotNull(sub);
            Assert.AreEqual(3, sub.CustomerId);
            Assert.AreEqual(SubscriptionPlanType.Basic, sub.PlanType);
            Assert.AreEqual(SubscriptionStatus.Active, sub.Status);
            Assert.AreEqual(0, sub.RentalsUsedThisPeriod);
            Assert.IsTrue(sub.TotalBilled > 0);
        }

        [TestMethod]
        public void Subscribe_SetsCorrectBillingPeriod()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Standard);

            Assert.IsTrue(sub.CurrentPeriodEnd > sub.CurrentPeriodStart);
            var days = (sub.CurrentPeriodEnd - sub.CurrentPeriodStart).TotalDays;
            Assert.IsTrue(days >= 28 && days <= 31);
        }

        [TestMethod]
        public void Subscribe_ChargesInitialBilling()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Standard);
            var plan = SubscriptionService.GetPlan(SubscriptionPlanType.Standard);

            Assert.AreEqual(plan.MonthlyPrice, sub.TotalBilled);
            Assert.AreEqual(1, sub.BillingHistory.Count);
            Assert.AreEqual("charge", sub.BillingHistory[0].EventType);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Subscribe_AlreadySubscribed_Throws()
        {
            // Customer 1 has seed subscription
            _service.Subscribe(1, SubscriptionPlanType.Premium);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Subscribe_InvalidCustomer_Throws()
        {
            _service.Subscribe(9999, SubscriptionPlanType.Basic);
        }

        // --- Cancel ---

        [TestMethod]
        public void Cancel_SetsStatusCancelled()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Basic);
            var cancelled = _service.Cancel(sub.Id, "Too expensive");

            Assert.AreEqual(SubscriptionStatus.Cancelled, cancelled.Status);
            Assert.IsNotNull(cancelled.CancelledDate);
        }

        [TestMethod]
        public void Cancel_AddsBillingEvent()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Basic);
            var initialEvents = sub.BillingHistory.Count;
            _service.Cancel(sub.Id, "Moving away");

            var updated = _subRepo.GetById(sub.Id);
            Assert.AreEqual(initialEvents + 1, updated.BillingHistory.Count);
            Assert.AreEqual("cancellation", updated.BillingHistory.Last().EventType);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Cancel_AlreadyCancelled_Throws()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Basic);
            _service.Cancel(sub.Id, "first");
            _service.Cancel(sub.Id, "second");
        }

        // --- Pause / Resume ---

        [TestMethod]
        public void Pause_ActiveSubscription_SetsPaused()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Standard);
            var paused = _service.Pause(sub.Id);

            Assert.AreEqual(SubscriptionStatus.Paused, paused.Status);
            Assert.IsNotNull(paused.PausedDate);
            Assert.AreEqual(1, paused.PausesUsedThisYear);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Pause_NotActive_Throws()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Basic);
            _service.Cancel(sub.Id, null);
            _service.Pause(sub.Id); // cancelled, not active
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Pause_ExceedsMaxPauses_Throws()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Basic);
            // Basic allows 1 pause/year
            _service.Pause(sub.Id);
            _service.Resume(sub.Id);
            _service.Pause(sub.Id); // second pause, over limit
        }

        [TestMethod]
        public void Resume_PausedSubscription_ReactivatesAndExtendsPeriod()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Standard);
            var originalEnd = sub.CurrentPeriodEnd;
            _service.Pause(sub.Id);

            var resumed = _service.Resume(sub.Id);

            Assert.AreEqual(SubscriptionStatus.Active, resumed.Status);
            Assert.IsNull(resumed.PausedDate);
            // Period should be extended (or at least not shortened)
            Assert.IsTrue(resumed.CurrentPeriodEnd >= originalEnd);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Resume_NotPaused_Throws()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Standard);
            _service.Resume(sub.Id); // not paused
        }

        // --- Change Plan ---

        [TestMethod]
        public void ChangePlan_UpgradeToPremium_UpdatesPlan()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Basic);
            var upgraded = _service.ChangePlan(sub.Id, SubscriptionPlanType.Premium);

            Assert.AreEqual(SubscriptionPlanType.Premium, upgraded.PlanType);
            Assert.AreEqual(0, upgraded.RentalsUsedThisPeriod);
        }

        [TestMethod]
        public void ChangePlan_AppliesProratedCredit()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Standard);
            var billedBefore = sub.TotalBilled;

            _service.ChangePlan(sub.Id, SubscriptionPlanType.Premium);
            var updated = _subRepo.GetById(sub.Id);

            // Should have billed something (new plan charge minus prorated credit)
            Assert.IsTrue(updated.TotalBilled > billedBefore);

            // Plan change event should be recorded
            var changeEvent = updated.BillingHistory
                .Last(e => e.EventType == "plan_change");
            Assert.IsTrue(changeEvent.Description.Contains("Upgrade"));
        }

        [TestMethod]
        public void ChangePlan_Downgrade_DescriptionSaysDowngrade()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Premium);
            _service.ChangePlan(sub.Id, SubscriptionPlanType.Basic);

            var updated = _subRepo.GetById(sub.Id);
            var evt = updated.BillingHistory.Last(e => e.EventType == "plan_change");
            Assert.IsTrue(evt.Description.Contains("Downgrade"));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ChangePlan_SamePlan_Throws()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Basic);
            _service.ChangePlan(sub.Id, SubscriptionPlanType.Basic);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ChangePlan_NotActive_Throws()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Basic);
            _service.Cancel(sub.Id, null);
            _service.ChangePlan(sub.Id, SubscriptionPlanType.Premium);
        }

        // --- Record Rental ---

        [TestMethod]
        public void RecordRental_IncrementsUsage()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Standard);
            Assert.AreEqual(0, sub.RentalsUsedThisPeriod);

            var remaining = _service.RecordRental(sub.Id);

            var updated = _subRepo.GetById(sub.Id);
            Assert.AreEqual(1, updated.RentalsUsedThisPeriod);
            Assert.AreEqual(4, remaining); // Standard = 5, used 1
        }

        [TestMethod]
        public void RecordRental_UnlimitedPlan_ReturnsMinusOne()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Premium);
            var remaining = _service.RecordRental(sub.Id);
            Assert.AreEqual(-1, remaining);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void RecordRental_PausedSubscription_Throws()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Standard);
            _service.Pause(sub.Id);
            _service.RecordRental(sub.Id);
        }

        // --- Usage ---

        [TestMethod]
        public void GetUsage_ReturnsCorrectStats()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Standard);
            _service.RecordRental(sub.Id);

            var usage = _service.GetUsage(sub.Id);

            Assert.AreEqual("Standard", usage.PlanName);
            Assert.AreEqual(1, usage.RentalsUsed);
            Assert.AreEqual(5, usage.RentalsAllowed);
            Assert.IsFalse(usage.IsUnlimited);
            Assert.IsTrue(usage.DaysRemainingInPeriod > 0);
            Assert.IsTrue(usage.PeriodProgressPercent >= 0);
        }

        [TestMethod]
        public void GetUsage_PremiumIsUnlimited()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Premium);
            var usage = _service.GetUsage(sub.Id);
            Assert.IsTrue(usage.IsUnlimited);
        }

        [TestMethod]
        public void GetUsage_ShowsPausesRemaining()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Standard);
            var usage = _service.GetUsage(sub.Id);
            Assert.AreEqual(2, usage.PausesRemaining); // Standard = 2/year
        }

        // --- Revenue ---

        [TestMethod]
        public void GetRevenueBreakdown_IncludesSeededData()
        {
            var revenue = _service.GetRevenueBreakdown();

            Assert.IsTrue(revenue.TotalSubscribers >= 2);
            Assert.IsTrue(revenue.MonthlyRecurringRevenue > 0);
            Assert.IsTrue(revenue.TotalLifetimeRevenue > 0);
            Assert.IsTrue(revenue.ActiveSubscribersByPlan.Count > 0);
        }

        [TestMethod]
        public void GetRevenueBreakdown_CancelledNotInMRR()
        {
            var sub = _service.Subscribe(3, SubscriptionPlanType.Basic);
            var mrrBefore = _service.GetRevenueBreakdown().MonthlyRecurringRevenue;

            _service.Cancel(sub.Id, null);
            var mrrAfter = _service.GetRevenueBreakdown().MonthlyRecurringRevenue;

            Assert.IsTrue(mrrAfter < mrrBefore);
        }

        // --- Model helpers ---

        [TestMethod]
        public void HasRentalsRemaining_UnderLimit_True()
        {
            var sub = new CustomerSubscription
            {
                RentalsUsedThisPeriod = 3
            };
            Assert.IsTrue(sub.HasRentalsRemaining(5));
        }

        [TestMethod]
        public void HasRentalsRemaining_AtLimit_False()
        {
            var sub = new CustomerSubscription
            {
                RentalsUsedThisPeriod = 5
            };
            Assert.IsFalse(sub.HasRentalsRemaining(5));
        }

        [TestMethod]
        public void HasRentalsRemaining_Unlimited_AlwaysTrue()
        {
            var sub = new CustomerSubscription
            {
                RentalsUsedThisPeriod = 999
            };
            Assert.IsTrue(sub.HasRentalsRemaining(0)); // 0 = unlimited
        }

        [TestMethod]
        public void IsBillable_Active_True()
        {
            var sub = new CustomerSubscription { Status = SubscriptionStatus.Active };
            Assert.IsTrue(sub.IsBillable);
        }

        [TestMethod]
        public void IsBillable_PastDue_True()
        {
            var sub = new CustomerSubscription { Status = SubscriptionStatus.PastDue };
            Assert.IsTrue(sub.IsBillable);
        }

        [TestMethod]
        public void IsBillable_Cancelled_False()
        {
            var sub = new CustomerSubscription { Status = SubscriptionStatus.Cancelled };
            Assert.IsFalse(sub.IsBillable);
        }

        [TestMethod]
        public void IsBillable_Paused_False()
        {
            var sub = new CustomerSubscription { Status = SubscriptionStatus.Paused };
            Assert.IsFalse(sub.IsBillable);
        }

        // --- Constructor null checks ---

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullSubRepo_Throws()
        {
            new SubscriptionService(null, _custRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustRepo_Throws()
        {
            new SubscriptionService(_subRepo, null);
        }
    }
}
