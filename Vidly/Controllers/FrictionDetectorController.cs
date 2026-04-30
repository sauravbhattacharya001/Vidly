using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Rental Friction Detector — autonomous detection of friction points in the
    /// customer rental journey. Identifies availability constraints, price shocks,
    /// overdue patterns, frequency gaps, genre lock, return delays, new customer
    /// drop-off, and high-cost abandonment. Generates targeted recommendations.
    /// </summary>
    public class FrictionDetectorController : Controller
    {
        private readonly FrictionDetectorService _service;

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public FrictionDetectorController()
            : this(
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository(),
                new SystemClock())
        {
        }

        /// <summary>
        /// Constructor injection for testability and future DI container use.
        /// </summary>
        public FrictionDetectorController(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IClock clock)
        {
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (movieRepository == null) throw new ArgumentNullException("movieRepository");
            if (customerRepository == null) throw new ArgumentNullException("customerRepository");
            if (clock == null) throw new ArgumentNullException("clock");
            _service = new FrictionDetectorService(rentalRepository, movieRepository, customerRepository, clock);
        }

        // GET: FrictionDetector
        public ActionResult Index()
        {
            var report = _service.GenerateReport();
            return View(report);
        }

        // GET: FrictionDetector/Report
        public ActionResult Report()
        {
            var report = _service.GenerateReport();
            return Json(report, JsonRequestBehavior.AllowGet);
        }

        // GET: FrictionDetector/Customer/5
        public ActionResult Customer(int id)
        {
            var profile = _service.AnalyzeCustomer(id);
            return Json(profile, JsonRequestBehavior.AllowGet);
        }

        // GET: FrictionDetector/Heatmap
        public ActionResult Heatmap()
        {
            var heatmap = _service.GetHeatmap();
            return Json(heatmap, JsonRequestBehavior.AllowGet);
        }

        // GET: FrictionDetector/Trends?periodDays=7&periods=8
        public ActionResult Trends(int periodDays = 7, int periods = 8)
        {
            var trends = _service.GetTrends(periodDays, periods);
            return Json(trends, JsonRequestBehavior.AllowGet);
        }
    }
}
