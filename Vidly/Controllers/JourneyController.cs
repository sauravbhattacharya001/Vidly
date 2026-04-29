using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Customer Journey Orchestrator — autonomous lifecycle stage tracking.
    /// Classifies customers through 8 stages, detects transitions,
    /// generates personalized interventions, and surfaces proactive alerts.
    /// </summary>
    public class JourneyController : Controller
    {
        private readonly JourneyOrchestratorService _service;

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public JourneyController()
            : this(
                new InMemoryCustomerRepository(),
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new SystemClock())
        {
        }

        /// <summary>
        /// Constructor injection for testability and future DI container use.
        /// </summary>
        public JourneyController(
            ICustomerRepository customerRepository,
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            IClock clock)
        {
            if (customerRepository == null) throw new ArgumentNullException("customerRepository");
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (movieRepository == null) throw new ArgumentNullException("movieRepository");
            if (clock == null) throw new ArgumentNullException("clock");
            _service = new JourneyOrchestratorService(
                customerRepository, rentalRepository, movieRepository, clock);
        }

        // GET: Journey
        public ActionResult Index()
        {
            var dashboard = _service.GetDashboard();
            return View(dashboard);
        }

        // GET: Journey/Dashboard
        public ActionResult Dashboard()
        {
            var dashboard = _service.GetDashboard();
            return Json(dashboard, JsonRequestBehavior.AllowGet);
        }

        // GET: Journey/Customer/5
        public ActionResult Customer(int id)
        {
            try
            {
                var profile = _service.ClassifyCustomer(id);
                return Json(profile, JsonRequestBehavior.AllowGet);
            }
            catch (ArgumentException ex)
            {
                return HttpNotFound(ex.Message);
            }
        }

        // GET: Journey/FullJourney/5
        public ActionResult FullJourney(int id)
        {
            try
            {
                var map = _service.GetFullJourney(id);
                return Json(map, JsonRequestBehavior.AllowGet);
            }
            catch (ArgumentException ex)
            {
                return HttpNotFound(ex.Message);
            }
        }

        // GET: Journey/Interventions/5
        public ActionResult Interventions(int id)
        {
            try
            {
                var interventions = _service.GetInterventions(id);
                return Json(interventions, JsonRequestBehavior.AllowGet);
            }
            catch (ArgumentException ex)
            {
                return HttpNotFound(ex.Message);
            }
        }

        // GET: Journey/Alerts
        public ActionResult Alerts()
        {
            var alerts = _service.GetAlerts();
            return Json(alerts, JsonRequestBehavior.AllowGet);
        }

        // GET: Journey/Transitions
        public ActionResult Transitions()
        {
            var dashboard = _service.GetDashboard();
            return Json(dashboard.TransitionMatrix, JsonRequestBehavior.AllowGet);
        }
    }
}
