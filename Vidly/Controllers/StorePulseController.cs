using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Store Pulse Monitor — autonomous multi-signal health dashboard.
    /// Aggregates inventory, revenue, customer activity, and operations
    /// signals into a unified health score with anomaly detection and
    /// auto-generated action items.
    /// </summary>
    public class StorePulseController : Controller
    {
        private readonly StorePulseService _service;

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public StorePulseController()
            : this(
                new InMemoryMovieRepository(),
                new InMemoryRentalRepository(),
                new InMemoryCustomerRepository(),
                new SystemClock())
        {
        }

        /// <summary>
        /// Constructor injection for testability and future DI container use.
        /// </summary>
        public StorePulseController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository,
            IClock clock)
        {
            if (movieRepository == null) throw new ArgumentNullException("movieRepository");
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (customerRepository == null) throw new ArgumentNullException("customerRepository");
            if (clock == null) throw new ArgumentNullException("clock");
            _service = new StorePulseService(movieRepository, rentalRepository, customerRepository, clock);
        }

        // GET: StorePulse
        public ActionResult Index()
        {
            var report = _service.GenerateReport();
            return View(report);
        }

        // GET: StorePulse/Signal?name=Inventory+Utilization
        public ActionResult Signal(string name)
        {
            if (string.IsNullOrEmpty(name))
                return Json(new { error = "Signal name is required" }, JsonRequestBehavior.AllowGet);

            var signal = _service.GetSignal(name);
            if (signal == null)
                return Json(new { error = "Signal not found" }, JsonRequestBehavior.AllowGet);

            return Json(new
            {
                signal.Name,
                signal.Category,
                signal.Score,
                signal.Status,
                signal.Description,
                signal.Metrics
            }, JsonRequestBehavior.AllowGet);
        }

        // GET: StorePulse/Actions
        public ActionResult Actions()
        {
            var report = _service.GenerateReport();
            return Json(report.ActionItems, JsonRequestBehavior.AllowGet);
        }

        // GET: StorePulse/HealthCheck
        public ActionResult HealthCheck()
        {
            var report = _service.GetHealthCheck();
            return Json(new
            {
                report.GeneratedAt,
                report.OverallHealthScore,
                report.HealthGrade,
                trend = new
                {
                    report.Trend.Direction,
                    report.Trend.ScoreChange,
                    report.Trend.Summary
                }
            }, JsonRequestBehavior.AllowGet);
        }
    }
}
