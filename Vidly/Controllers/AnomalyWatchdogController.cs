using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Anomaly Watchdog — autonomous rental pattern anomaly detection.
    /// Runs 6 detection engines to identify suspicious behavior, fraud risks,
    /// and unusual patterns. Maintains a risk-scored customer watchlist.
    /// </summary>
    public class AnomalyWatchdogController : Controller
    {
        private readonly AnomalyWatchdogService _service;

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public AnomalyWatchdogController()
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
        public AnomalyWatchdogController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository,
            IClock clock)
        {
            if (movieRepository == null) throw new ArgumentNullException("movieRepository");
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (customerRepository == null) throw new ArgumentNullException("customerRepository");
            if (clock == null) throw new ArgumentNullException("clock");
            _service = new AnomalyWatchdogService(rentalRepository, movieRepository, customerRepository, clock);
        }

        // GET: AnomalyWatchdog
        public ActionResult Index()
        {
            var report = _service.RunFullScan();
            return View(report);
        }

        // GET: AnomalyWatchdog/Scan
        public ActionResult Scan()
        {
            var report = _service.RunFullScan();
            return Json(report, JsonRequestBehavior.AllowGet);
        }

        // GET: AnomalyWatchdog/Alerts
        public ActionResult Alerts()
        {
            var report = _service.RunFullScan();
            return Json(report.Alerts, JsonRequestBehavior.AllowGet);
        }

        // GET: AnomalyWatchdog/Watchlist
        public ActionResult Watchlist()
        {
            var watchlist = _service.GetWatchlist();
            return Json(watchlist, JsonRequestBehavior.AllowGet);
        }

        // POST: AnomalyWatchdog/Acknowledge/5
        [HttpPost]
        public ActionResult Acknowledge(int id)
        {
            _service.AcknowledgeAlert(id);
            return RedirectToAction("Index");
        }
    }
}
