using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Customer Lifetime Trajectory Engine — autonomous future behavior prediction
    /// for individual customers and the entire fleet.
    /// </summary>
    public class TrajectoryController : Controller
    {
        private readonly TrajectoryEngineService _service;

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public TrajectoryController()
            : this(
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository(),
                new SystemClock())
        {
        }

        /// <summary>
        /// Constructor injection for testability.
        /// </summary>
        public TrajectoryController(
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo,
            ICustomerRepository customerRepo,
            IClock clock)
        {
            if (rentalRepo == null) throw new ArgumentNullException("rentalRepo");
            if (movieRepo == null) throw new ArgumentNullException("movieRepo");
            if (customerRepo == null) throw new ArgumentNullException("customerRepo");
            if (clock == null) throw new ArgumentNullException("clock");
            _service = new TrajectoryEngineService(rentalRepo, movieRepo, customerRepo, clock);
        }

        // GET: Trajectory
        public ActionResult Index()
        {
            var report = _service.GenerateReport();
            return View(report);
        }

        // GET: Trajectory/Report
        public ActionResult Report()
        {
            var report = _service.GenerateReport();
            return Json(new
            {
                report.GeneratedAt,
                report.TrajectoryScore,
                FleetHealth = new
                {
                    report.FleetHealth.TotalCustomers,
                    report.FleetHealth.PhaseDistribution,
                    report.FleetHealth.ChurnRiskDistribution,
                    report.FleetHealth.TotalProjectedRevenue90Days,
                    report.FleetHealth.HealthScore
                },
                Trajectories = report.Trajectories,
                Insights = report.Insights
            }, JsonRequestBehavior.AllowGet);
        }

        // GET: Trajectory/Customer/5
        public ActionResult Customer(int id)
        {
            try
            {
                var trajectory = _service.GetCustomerTrajectory(id);
                return Json(trajectory, JsonRequestBehavior.AllowGet);
            }
            catch (System.Collections.Generic.KeyNotFoundException)
            {
                return HttpNotFound();
            }
        }
    }
}
