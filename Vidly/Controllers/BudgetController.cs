using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Rental Budget Tracker — set monthly spending limits,
    /// track usage with genre breakdowns, weekly pacing,
    /// spending history, alerts, and savings tips.
    /// </summary>
    public class BudgetController : Controller
    {
        private readonly RentalBudgetService _budgetService;
        private readonly ICustomerRepository _customerRepository;

        public BudgetController()
            : this(
                new InMemoryRentalRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryMovieRepository())
        {
        }

        public BudgetController(
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository,
            IMovieRepository movieRepository)
        {
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _budgetService = new RentalBudgetService(
                rentalRepository, customerRepository, movieRepository);
        }

        // GET: Budget
        public ActionResult Index(int? customerId, int? year, int? month)
        {
            var vm = new BudgetViewModel
            {
                Customers = _customerRepository.GetCustomers().ToList(),
                AllBudgets = _budgetService.GetAllBudgetSummaries(),
                SelectedCustomerId = customerId,
                SelectedYear = year,
                SelectedMonth = month
            };

            if (customerId.HasValue)
            {
                vm.Dashboard = _budgetService.GetDashboard(customerId.Value, year, month);
            }

            return View(vm);
        }

        // POST: Budget/SetBudget
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SetBudget(int customerId, decimal monthlyLimit,
            bool alertsEnabled = true, decimal alertThreshold = 80)
        {
            try
            {
                // Convert percentage (80) to decimal (0.8)
                var threshold = alertThreshold / 100m;
                _budgetService.SetBudget(customerId, monthlyLimit, alertsEnabled, threshold);
                TempData["Success"] = $"Budget of ${monthlyLimit:F2}/month set successfully!";
            }
            catch (Exception ex) when (ex is ArgumentException || ex is KeyNotFoundException)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index", new { customerId });
        }

        // POST: Budget/RemoveBudget
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RemoveBudget(int customerId)
        {
            if (_budgetService.RemoveBudget(customerId))
                TempData["Success"] = "Budget removed.";
            else
                TempData["Error"] = "No budget found to remove.";

            return RedirectToAction("Index", new { customerId });
        }
    }
}
