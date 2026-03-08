using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Repository interface for subscription management.
    /// </summary>
    public interface ISubscriptionRepository
    {
        IReadOnlyList<CustomerSubscription> GetAll();
        CustomerSubscription GetById(int id);
        CustomerSubscription GetByCustomerId(int customerId);
        IReadOnlyList<CustomerSubscription> GetByStatus(SubscriptionStatus status);
        void Add(CustomerSubscription subscription);
        void Update(CustomerSubscription subscription);
        void AddBillingEvent(int subscriptionId, SubscriptionBillingEvent evt);
    }
}
