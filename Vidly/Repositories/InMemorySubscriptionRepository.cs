using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Thread-safe in-memory subscription repository with seed data.
    /// </summary>
    public class InMemorySubscriptionRepository : ISubscriptionRepository
    {
        private static readonly object _lock = new object();
        private static readonly List<CustomerSubscription> _subscriptions = new List<CustomerSubscription>();
        private static int _nextId = 1;
        private static int _nextEventId = 1;
        private static bool _seeded;

        public InMemorySubscriptionRepository()
        {
            lock (_lock)
            {
                if (!_seeded)
                {
                    Seed();
                    _seeded = true;
                }
            }
        }

        private void Seed()
        {
            var sub1 = new CustomerSubscription
            {
                Id = _nextId++,
                CustomerId = 1,
                PlanType = SubscriptionPlanType.Standard,
                Status = SubscriptionStatus.Active,
                StartDate = DateTime.Today.AddMonths(-3),
                CurrentPeriodStart = DateTime.Today.AddDays(-15),
                CurrentPeriodEnd = DateTime.Today.AddDays(15),
                RentalsUsedThisPeriod = 2,
                TotalBilled = 29.97m,
                BillingHistory = new List<SubscriptionBillingEvent>
                {
                    new SubscriptionBillingEvent
                    {
                        Id = _nextEventId++,
                        SubscriptionId = 1,
                        EventType = "charge",
                        Amount = 9.99m,
                        Timestamp = DateTime.Today.AddMonths(-3),
                        Description = "Standard plan - initial charge"
                    },
                    new SubscriptionBillingEvent
                    {
                        Id = _nextEventId++,
                        SubscriptionId = 1,
                        EventType = "charge",
                        Amount = 9.99m,
                        Timestamp = DateTime.Today.AddMonths(-2),
                        Description = "Standard plan - renewal"
                    },
                    new SubscriptionBillingEvent
                    {
                        Id = _nextEventId++,
                        SubscriptionId = 1,
                        EventType = "charge",
                        Amount = 9.99m,
                        Timestamp = DateTime.Today.AddMonths(-1),
                        Description = "Standard plan - renewal"
                    }
                }
            };

            var sub2 = new CustomerSubscription
            {
                Id = _nextId++,
                CustomerId = 2,
                PlanType = SubscriptionPlanType.Premium,
                Status = SubscriptionStatus.Active,
                StartDate = DateTime.Today.AddMonths(-1),
                CurrentPeriodStart = DateTime.Today.AddDays(-5),
                CurrentPeriodEnd = DateTime.Today.AddDays(25),
                RentalsUsedThisPeriod = 8,
                TotalBilled = 19.99m,
                BillingHistory = new List<SubscriptionBillingEvent>
                {
                    new SubscriptionBillingEvent
                    {
                        Id = _nextEventId++,
                        SubscriptionId = 2,
                        EventType = "charge",
                        Amount = 19.99m,
                        Timestamp = DateTime.Today.AddMonths(-1),
                        Description = "Premium plan - initial charge"
                    }
                }
            };

            _subscriptions.Add(sub1);
            _subscriptions.Add(sub2);
        }

        public IReadOnlyList<CustomerSubscription> GetAll()
        {
            lock (_lock)
            {
                return _subscriptions.ToList().AsReadOnly();
            }
        }

        public CustomerSubscription GetById(int id)
        {
            lock (_lock)
            {
                return _subscriptions.FirstOrDefault(s => s.Id == id);
            }
        }

        public CustomerSubscription GetByCustomerId(int customerId)
        {
            lock (_lock)
            {
                return _subscriptions
                    .Where(s => s.Status != SubscriptionStatus.Cancelled
                             && s.Status != SubscriptionStatus.Expired)
                    .FirstOrDefault(s => s.CustomerId == customerId);
            }
        }

        public IReadOnlyList<CustomerSubscription> GetByStatus(SubscriptionStatus status)
        {
            lock (_lock)
            {
                return _subscriptions.Where(s => s.Status == status).ToList().AsReadOnly();
            }
        }

        public void Add(CustomerSubscription subscription)
        {
            lock (_lock)
            {
                subscription.Id = _nextId++;
                _subscriptions.Add(subscription);
            }
        }

        public void Update(CustomerSubscription subscription)
        {
            lock (_lock)
            {
                var idx = _subscriptions.FindIndex(s => s.Id == subscription.Id);
                if (idx >= 0)
                    _subscriptions[idx] = subscription;
            }
        }

        public void AddBillingEvent(int subscriptionId, SubscriptionBillingEvent evt)
        {
            lock (_lock)
            {
                var sub = _subscriptions.FirstOrDefault(s => s.Id == subscriptionId);
                if (sub != null)
                {
                    evt.Id = _nextEventId++;
                    evt.SubscriptionId = subscriptionId;
                    sub.BillingHistory.Add(evt);
                }
            }
        }
    }
}
