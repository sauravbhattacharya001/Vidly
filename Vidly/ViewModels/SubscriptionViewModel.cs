using System.Collections.Generic;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.ViewModels
{
    public class SubscriptionViewModel
    {
        public List<Customer> Customers { get; set; }
        public int? SelectedCustomerId { get; set; }
        public Customer SelectedCustomer { get; set; }
        public CustomerSubscription Subscription { get; set; }
        public SubscriptionUsage Usage { get; set; }
        public IReadOnlyList<SubscriptionPlan> AvailablePlans { get; set; }
        public SubscriptionRevenue Revenue { get; set; }
        public string StatusMessage { get; set; }

        public SubscriptionViewModel()
        {
            Customers = new List<Customer>();
            AvailablePlans = SubscriptionService.GetAvailablePlans();
        }
    }
}
