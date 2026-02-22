using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Admin revenue dashboard with KPIs, top movies/customers,
    /// genre breakdown, membership analysis, and recent activity.
    /// </summary>
    public class DashboardController : Controller
    {
        private readonly DashboardService _dashboardService;

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public DashboardController()
            : this(new DashboardService(
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository()))
        {
        }

        /// <summary>
        /// Constructor injection for testability.
        /// </summary>
        public DashboardController(DashboardService dashboardService)
        {
            _dashboardService = dashboardService
                ?? throw new ArgumentNullException(nameof(dashboardService));
        }

        // GET: Dashboard
        public ActionResult Index()
        {
            var data = _dashboardService.GetDashboard();

            var viewModel = new DashboardViewModel
            {
                Data = data
            };

            return View(viewModel);
        }
    }
}
