using System.Collections.Generic;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.ViewModels
{
    public class BudgetViewModel
    {
        public BudgetDashboard Dashboard { get; set; }
        public IReadOnlyList<BudgetSummary> AllBudgets { get; set; }
        public List<Customer> Customers { get; set; }
        public int? SelectedCustomerId { get; set; }
        public int? SelectedYear { get; set; }
        public int? SelectedMonth { get; set; }
    }
}
