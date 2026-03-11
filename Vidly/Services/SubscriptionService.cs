using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages subscription rental plans: subscribe, cancel, pause/resume,
    /// upgrade/downgrade, billing, and usage tracking.
    /// </summary>
    public class SubscriptionService
    {
        private readonly ISubscriptionRepository _subscriptionRepo;
        private readonly ICustomerRepository _customerRepo;
        private readonly IClock _clock;

        /// <summary>Maximum days a subscription can be paused.</summary>
        public const int MaxPauseDays = 30;

        /// <summary>Days of grace before a past-due subscription is expired.</summary>
        public const int PastDueGraceDays = 7;

        private static readonly Dictionary<SubscriptionPlanType, SubscriptionPlan> Plans =
            new Dictionary<SubscriptionPlanType, SubscriptionPlan>
            {
                {
                    SubscriptionPlanType.Basic,
                    new SubscriptionPlan
                    {
                        PlanType = SubscriptionPlanType.Basic,
                        Name = "Basic",
                        MonthlyPrice = 4.99m,
                        RentalsPerMonth = 2,
                        OverageDiscountPercent = 0,
                        FreeReservations = false,
                        ExtraGraceDays = 0,
                        MaxPausesPerYear = 1
                    }
                },
                {
                    SubscriptionPlanType.Standard,
                    new SubscriptionPlan
                    {
                        PlanType = SubscriptionPlanType.Standard,
                        Name = "Standard",
                        MonthlyPrice = 9.99m,
                        RentalsPerMonth = 5,
                        OverageDiscountPercent = 10,
                        FreeReservations = true,
                        ExtraGraceDays = 1,
                        MaxPausesPerYear = 2
                    }
                },
                {
                    SubscriptionPlanType.Premium,
                    new SubscriptionPlan
                    {
                        PlanType = SubscriptionPlanType.Premium,
                        Name = "Premium",
                        MonthlyPrice = 19.99m,
                        RentalsPerMonth = 0, // unlimited
                        OverageDiscountPercent = 25,
                        FreeReservations = true,
                        ExtraGraceDays = 3,
                        MaxPausesPerYear = 3
                    }
                }
            };

        public SubscriptionService(
            ISubscriptionRepository subscriptionRepo,
            ICustomerRepository customerRepo,
            IClock clock = null)
        {
            _subscriptionRepo = subscriptionRepo
                ?? throw new ArgumentNullException("subscriptionRepo");
            _customerRepo = customerRepo
                ?? throw new ArgumentNullException("customerRepo");
            _clock = clock ?? new SystemClock();
        }

        /// <summary>
        /// Get the plan configuration for a plan type.
        /// </summary>
        public static SubscriptionPlan GetPlan(SubscriptionPlanType planType)
        {
            SubscriptionPlan plan;
            if (!Plans.TryGetValue(planType, out plan))
                throw new ArgumentException("Unknown plan type: " + planType);
            return plan;
        }

        /// <summary>
        /// Get all available plans.
        /// </summary>
        public static IReadOnlyList<SubscriptionPlan> GetAvailablePlans()
        {
            return Plans.Values.OrderBy(p => p.MonthlyPrice).ToList().AsReadOnly();
        }

        /// <summary>
        /// Subscribe a customer to a plan.
        /// </summary>
        /// <returns>The new subscription.</returns>
        /// <exception cref="InvalidOperationException">Customer already has an active subscription.</exception>
        public CustomerSubscription Subscribe(int customerId, SubscriptionPlanType planType)
        {
            var customer = _customerRepo.GetById(customerId);
            if (customer == null)
                throw new ArgumentException("Customer not found: " + customerId);

            var existing = _subscriptionRepo.GetByCustomerId(customerId);
            if (existing != null)
                throw new InvalidOperationException(
                    "Customer already has an active subscription (ID " + existing.Id + "). " +
                    "Cancel or let it expire first.");

            var plan = GetPlan(planType);
            var now = _clock.Now;

            var subscription = new CustomerSubscription
            {
                CustomerId = customerId,
                PlanType = planType,
                Status = SubscriptionStatus.Active,
                StartDate = now,
                CurrentPeriodStart = now,
                CurrentPeriodEnd = now.AddMonths(1),
                RentalsUsedThisPeriod = 0,
                TotalBilled = plan.MonthlyPrice,
                BillingHistory = new List<SubscriptionBillingEvent>
                {
                    new SubscriptionBillingEvent
                    {
                        EventType = "charge",
                        Amount = plan.MonthlyPrice,
                        Timestamp = now,
                        Description = plan.Name + " plan - initial charge"
                    }
                }
            };

            _subscriptionRepo.Add(subscription);
            return subscription;
        }

        /// <summary>
        /// Cancel a subscription immediately. The subscription status changes to
        /// Cancelled right away, blocking further rentals. When the current billing
        /// period ends, <see cref="ProcessRenewals"/> transitions it to Expired.
        /// </summary>
        public CustomerSubscription Cancel(int subscriptionId, string reason)
        {
            var sub = _subscriptionRepo.GetById(subscriptionId);
            if (sub == null)
                throw new ArgumentException("Subscription not found: " + subscriptionId);

            if (sub.Status == SubscriptionStatus.Cancelled)
                throw new InvalidOperationException("Subscription is already cancelled.");

            sub.Status = SubscriptionStatus.Cancelled;
            sub.CancelledDate = _clock.Now;

            _subscriptionRepo.AddBillingEvent(subscriptionId, new SubscriptionBillingEvent
            {
                EventType = "cancellation",
                Amount = 0m,
                Description = "Cancelled" + (string.IsNullOrEmpty(reason) ? "" : ": " + reason)
            });

            _subscriptionRepo.Update(sub);
            return sub;
        }

        /// <summary>
        /// Pause a subscription. The customer won't be billed while paused.
        /// Max pause duration is 30 days; plan limits apply.
        /// </summary>
        public CustomerSubscription Pause(int subscriptionId)
        {
            var sub = _subscriptionRepo.GetById(subscriptionId);
            if (sub == null)
                throw new ArgumentException("Subscription not found: " + subscriptionId);

            if (sub.Status != SubscriptionStatus.Active)
                throw new InvalidOperationException(
                    "Only active subscriptions can be paused. Current status: " + sub.Status);

            var plan = GetPlan(sub.PlanType);
            if (sub.PausesUsedThisYear >= plan.MaxPausesPerYear)
                throw new InvalidOperationException(
                    "Maximum pauses reached (" + plan.MaxPausesPerYear + "/year for " + plan.Name + " plan).");

            sub.Status = SubscriptionStatus.Paused;
            sub.PausedDate = _clock.Now;
            sub.PausesUsedThisYear++;

            _subscriptionRepo.AddBillingEvent(subscriptionId, new SubscriptionBillingEvent
            {
                EventType = "pause",
                Amount = 0m,
                Description = "Subscription paused (pause " + sub.PausesUsedThisYear + "/" + plan.MaxPausesPerYear + ")"
            });

            _subscriptionRepo.Update(sub);
            return sub;
        }

        /// <summary>
        /// Resume a paused subscription.
        /// </summary>
        public CustomerSubscription Resume(int subscriptionId)
        {
            var sub = _subscriptionRepo.GetById(subscriptionId);
            if (sub == null)
                throw new ArgumentException("Subscription not found: " + subscriptionId);

            if (sub.Status != SubscriptionStatus.Paused)
                throw new InvalidOperationException("Subscription is not paused.");

            // Calculate how many days were paused and extend the period
            var pauseDays = (int)(_clock.Now - sub.PausedDate.Value).TotalDays;
            if (pauseDays > MaxPauseDays)
                pauseDays = MaxPauseDays;

            sub.Status = SubscriptionStatus.Active;
            sub.PausedDate = null;
            sub.CurrentPeriodEnd = sub.CurrentPeriodEnd.AddDays(pauseDays);

            _subscriptionRepo.AddBillingEvent(subscriptionId, new SubscriptionBillingEvent
            {
                EventType = "resume",
                Amount = 0m,
                Description = "Subscription resumed (paused " + pauseDays + " days, period extended)"
            });

            _subscriptionRepo.Update(sub);
            return sub;
        }

        /// <summary>
        /// Change plan (upgrade or downgrade). Takes effect immediately.
        /// Prorated credit is applied for the remaining days on the old plan.
        /// </summary>
        public CustomerSubscription ChangePlan(int subscriptionId, SubscriptionPlanType newPlanType)
        {
            var sub = _subscriptionRepo.GetById(subscriptionId);
            if (sub == null)
                throw new ArgumentException("Subscription not found: " + subscriptionId);

            if (sub.Status != SubscriptionStatus.Active)
                throw new InvalidOperationException("Can only change plan on active subscriptions.");

            if (sub.PlanType == newPlanType)
                throw new InvalidOperationException("Already on the " + newPlanType + " plan.");

            var oldPlan = GetPlan(sub.PlanType);
            var newPlan = GetPlan(newPlanType);
            var now = _clock.Now;

            // Prorate: credit remaining days on old plan, charge full new plan
            var totalDays = (sub.CurrentPeriodEnd - sub.CurrentPeriodStart).TotalDays;
            var remainingDays = (sub.CurrentPeriodEnd - now).TotalDays;
            decimal proratedCredit = 0m;
            if (totalDays > 0 && remainingDays > 0)
            {
                proratedCredit = Math.Round(
                    oldPlan.MonthlyPrice * (decimal)(remainingDays / totalDays), 2);
            }

            var netCharge = newPlan.MonthlyPrice - proratedCredit;
            if (netCharge < 0) netCharge = 0;

            sub.PlanType = newPlanType;
            sub.CurrentPeriodStart = now;
            sub.CurrentPeriodEnd = now.AddMonths(1);
            sub.RentalsUsedThisPeriod = 0;
            sub.TotalBilled += netCharge;

            var direction = newPlan.MonthlyPrice > oldPlan.MonthlyPrice ? "Upgrade" : "Downgrade";
            _subscriptionRepo.AddBillingEvent(subscriptionId, new SubscriptionBillingEvent
            {
                EventType = "plan_change",
                Amount = netCharge,
                Description = direction + " from " + oldPlan.Name + " to " + newPlan.Name +
                              " (credit $" + proratedCredit.ToString("F2") + ", charge $" + netCharge.ToString("F2") + ")"
            });

            _subscriptionRepo.Update(sub);
            return sub;
        }

        /// <summary>
        /// Record a rental against the subscription's usage counter.
        /// Throws if the subscription has reached its per-period rental limit.
        /// </summary>
        /// <returns>Remaining rentals this period (-1 = unlimited).</returns>
        /// <exception cref="InvalidOperationException">
        /// Rental limit reached for the current billing period.
        /// </exception>
        public int RecordRental(int subscriptionId)
        {
            var sub = _subscriptionRepo.GetById(subscriptionId);
            if (sub == null)
                throw new ArgumentException("Subscription not found: " + subscriptionId);

            if (sub.Status != SubscriptionStatus.Active)
                throw new InvalidOperationException("Subscription is not active.");

            var plan = GetPlan(sub.PlanType);

            // Enforce per-period rental limit (0 = unlimited)
            if (plan.RentalsPerMonth > 0 && sub.RentalsUsedThisPeriod >= plan.RentalsPerMonth)
                throw new InvalidOperationException(
                    "Rental limit reached (" + plan.RentalsPerMonth +
                    "/" + plan.Name + " plan). Upgrade your plan or wait for the next billing period.");

            sub.RentalsUsedThisPeriod++;
            _subscriptionRepo.Update(sub);

            return plan.RentalsPerMonth == 0 ? -1 : plan.RentalsPerMonth - sub.RentalsUsedThisPeriod;
        }

        /// <summary>
        /// Process renewals for all subscriptions whose current period has ended.
        /// Call this daily from a scheduled job.
        /// </summary>
        /// <returns>Summary of processed subscriptions.</returns>
        public RenewalSummary ProcessRenewals()
        {
            var now = _clock.Now;
            var all = _subscriptionRepo.GetAll();
            int renewed = 0;
            int expired = 0;
            int pastDue = 0;

            foreach (var sub in all)
            {
                if (sub.CurrentPeriodEnd > now)
                    continue;

                if (sub.Status == SubscriptionStatus.Active)
                {
                    // Renew
                    var plan = GetPlan(sub.PlanType);
                    sub.CurrentPeriodStart = now;
                    sub.CurrentPeriodEnd = now.AddMonths(1);
                    sub.RentalsUsedThisPeriod = 0;
                    sub.TotalBilled += plan.MonthlyPrice;

                    _subscriptionRepo.AddBillingEvent(sub.Id, new SubscriptionBillingEvent
                    {
                        EventType = "charge",
                        Amount = plan.MonthlyPrice,
                        Description = plan.Name + " plan - auto-renewal"
                    });

                    _subscriptionRepo.Update(sub);
                    renewed++;
                }
                else if (sub.Status == SubscriptionStatus.PastDue)
                {
                    var daysPastDue = (now - sub.CurrentPeriodEnd).TotalDays;
                    if (daysPastDue > PastDueGraceDays)
                    {
                        sub.Status = SubscriptionStatus.Expired;
                        _subscriptionRepo.AddBillingEvent(sub.Id, new SubscriptionBillingEvent
                        {
                            EventType = "expiration",
                            Amount = 0m,
                            Description = "Subscription expired after " + (int)daysPastDue + " days past due"
                        });
                        _subscriptionRepo.Update(sub);
                        expired++;
                    }
                    else
                    {
                        pastDue++;
                    }
                }
                else if (sub.Status == SubscriptionStatus.Paused)
                {
                    // Auto-expire pauses exceeding MaxPauseDays
                    if (sub.PausedDate.HasValue)
                    {
                        var pausedDays = (now - sub.PausedDate.Value).TotalDays;
                        if (pausedDays > MaxPauseDays)
                        {
                            sub.Status = SubscriptionStatus.Active;
                            sub.PausedDate = null;
                            sub.CurrentPeriodStart = now;
                            sub.CurrentPeriodEnd = now.AddMonths(1);
                            sub.RentalsUsedThisPeriod = 0;

                            var plan = GetPlan(sub.PlanType);
                            sub.TotalBilled += plan.MonthlyPrice;

                            _subscriptionRepo.AddBillingEvent(sub.Id, new SubscriptionBillingEvent
                            {
                                EventType = "charge",
                                Amount = plan.MonthlyPrice,
                                Description = plan.Name + " plan - auto-resumed after max pause"
                            });

                            _subscriptionRepo.Update(sub);
                            renewed++;
                        }
                }
                }
                else if (sub.Status == SubscriptionStatus.Cancelled)
                {
                    // Cancelled subscriptions that have reached their period end
                    // should transition to Expired for proper lifecycle tracking
                    sub.Status = SubscriptionStatus.Expired;
                    _subscriptionRepo.AddBillingEvent(sub.Id, new SubscriptionBillingEvent
                    {
                        EventType = "expiration",
                        Amount = 0m,
                        Description = "Cancelled subscription period ended"
                    });
                    _subscriptionRepo.Update(sub);
                    expired++;
                }
            }

            return new RenewalSummary
            {
                ProcessedAt = now,
                Renewed = renewed,
                Expired = expired,
                StillPastDue = pastDue
            };
        }

        /// <summary>
        /// Get usage statistics for a subscription.
        /// </summary>
        public SubscriptionUsage GetUsage(int subscriptionId)
        {
            var sub = _subscriptionRepo.GetById(subscriptionId);
            if (sub == null)
                throw new ArgumentException("Subscription not found: " + subscriptionId);

            var plan = GetPlan(sub.PlanType);
            var daysInPeriod = (sub.CurrentPeriodEnd - sub.CurrentPeriodStart).TotalDays;
            var daysElapsed = (_clock.Now - sub.CurrentPeriodStart).TotalDays;
            if (daysElapsed < 0) daysElapsed = 0;
            if (daysElapsed > daysInPeriod) daysElapsed = daysInPeriod;

            return new SubscriptionUsage
            {
                SubscriptionId = subscriptionId,
                PlanName = plan.Name,
                RentalsUsed = sub.RentalsUsedThisPeriod,
                RentalsAllowed = plan.RentalsPerMonth,
                IsUnlimited = plan.RentalsPerMonth == 0,
                DaysRemainingInPeriod = Math.Max(0, (int)(daysInPeriod - daysElapsed)),
                PeriodProgressPercent = daysInPeriod > 0
                    ? (int)Math.Round(daysElapsed / daysInPeriod * 100)
                    : 0,
                TotalBilled = sub.TotalBilled,
                Status = sub.Status,
                PausesRemaining = Math.Max(0, plan.MaxPausesPerYear - sub.PausesUsedThisYear)
            };
        }

        /// <summary>
        /// Get a subscription by customer ID (active/paused/past-due only).
        /// </summary>
        public CustomerSubscription GetByCustomerId(int customerId)
        {
            return _subscriptionRepo.GetByCustomerId(customerId);
        }

        /// <summary>
        /// Get revenue breakdown across all subscriptions.
        /// </summary>
        public SubscriptionRevenue GetRevenueBreakdown()
        {
            var all = _subscriptionRepo.GetAll();
            var activeByPlan = new Dictionary<string, int>();
            decimal mrr = 0m;
            decimal totalRevenue = 0m;

            foreach (var sub in all)
            {
                totalRevenue += sub.TotalBilled;
                if (sub.Status == SubscriptionStatus.Active
                    || sub.Status == SubscriptionStatus.Paused)
                {
                    var plan = GetPlan(sub.PlanType);
                    var key = plan.Name;
                    activeByPlan.TryGetValue(key, out var _c1);
                    activeByPlan[key] = _c1 + 1;

                    if (sub.Status == SubscriptionStatus.Active)
                        mrr += plan.MonthlyPrice;
                }
            }

            return new SubscriptionRevenue
            {
                MonthlyRecurringRevenue = mrr,
                TotalLifetimeRevenue = totalRevenue,
                ActiveSubscribersByPlan = activeByPlan,
                TotalSubscribers = all.Count(s =>
                    s.Status == SubscriptionStatus.Active
                    || s.Status == SubscriptionStatus.Paused),
                ChurnedSubscribers = all.Count(s =>
                    s.Status == SubscriptionStatus.Cancelled
                    || s.Status == SubscriptionStatus.Expired)
            };
        }
    }

    /// <summary>Summary of a renewal processing batch.</summary>
    public class RenewalSummary
    {
        public DateTime ProcessedAt { get; set; }
        public int Renewed { get; set; }
        public int Expired { get; set; }
        public int StillPastDue { get; set; }
    }

    /// <summary>Current usage stats for a subscription.</summary>
    public class SubscriptionUsage
    {
        public int SubscriptionId { get; set; }
        public string PlanName { get; set; }
        public int RentalsUsed { get; set; }
        public int RentalsAllowed { get; set; }
        public bool IsUnlimited { get; set; }
        public int DaysRemainingInPeriod { get; set; }
        public int PeriodProgressPercent { get; set; }
        public decimal TotalBilled { get; set; }
        public SubscriptionStatus Status { get; set; }
        public int PausesRemaining { get; set; }
    }

    /// <summary>Revenue breakdown for subscriptions.</summary>
    public class SubscriptionRevenue
    {
        public decimal MonthlyRecurringRevenue { get; set; }
        public decimal TotalLifetimeRevenue { get; set; }
        public Dictionary<string, int> ActiveSubscribersByPlan { get; set; }
        public int TotalSubscribers { get; set; }
        public int ChurnedSubscribers { get; set; }
    }
}
