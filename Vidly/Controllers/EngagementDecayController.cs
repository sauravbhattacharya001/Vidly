using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Customer Engagement Decay Engine — autonomous engagement decay detection
    /// with re-engagement prediction and proactive intervention recommendations.
    /// </summary>
    public class EngagementDecayController : Controller
    {
        private readonly EngagementDecayService _service;

        /// <summary>Default constructor using in-memory repositories.</summary>
        public EngagementDecayController()
            : this(
                new InMemoryRentalRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryMovieRepository(),
                new SystemClock())
        {
        }

        /// <summary>Testable constructor accepting repository interfaces.</summary>
        public EngagementDecayController(
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo,
            IMovieRepository movieRepo,
            IClock clock)
        {
            if (rentalRepo == null) throw new ArgumentNullException("rentalRepo");
            if (customerRepo == null) throw new ArgumentNullException("customerRepo");
            if (movieRepo == null) throw new ArgumentNullException("movieRepo");
            if (clock == null) throw new ArgumentNullException("clock");
            _service = new EngagementDecayService(rentalRepo, customerRepo, movieRepo, clock);
        }

        /// <summary>GET /EngagementDecay — Full engagement decay report.</summary>
        [HttpGet]
        public ActionResult Index()
        {
            var report = _service.GenerateReport();
            return Json(new
            {
                report.GeneratedAt,
                report.EngagementDecayScore,
                report.FleetHealth,
                TotalProfiles = report.Profiles.Count,
                WindowCount = report.Windows.Count,
                InterventionCount = report.Interventions.Count,
                report.Profiles,
                report.Windows,
                report.Interventions,
                report.TrendHistory,
                report.Insights
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>GET /EngagementDecay/Profile/{id} — Single customer profile.</summary>
        [HttpGet]
        public new ActionResult Profile(int id)
        {
            var profile = _service.GetProfile(id);
            if (profile == null)
                return Json(new { error = "Customer not found" }, JsonRequestBehavior.AllowGet);

            return Json(profile, JsonRequestBehavior.AllowGet);
        }

        /// <summary>GET /EngagementDecay/Windows — Re-engagement windows.</summary>
        [HttpGet]
        public ActionResult Windows()
        {
            var windows = _service.GetReengagementWindows();
            return Json(new
            {
                Count = windows.Count,
                Windows = windows
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>GET /EngagementDecay/Interventions — Prioritized interventions.</summary>
        [HttpGet]
        public ActionResult Interventions()
        {
            var interventions = _service.GetInterventions();
            return Json(new
            {
                Count = interventions.Count,
                Interventions = interventions
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>GET /EngagementDecay/Fleet — Fleet health only.</summary>
        [HttpGet]
        public ActionResult Fleet()
        {
            var health = _service.GetFleetHealth();
            return Json(health, JsonRequestBehavior.AllowGet);
        }

        /// <summary>GET /EngagementDecay/AtRisk — AtRisk + Churned profiles only.</summary>
        [HttpGet]
        public ActionResult AtRisk()
        {
            var report = _service.GenerateReport();
            var atRisk = report.Profiles
                .Where(p => p.CurrentPhase == EngagementPhase.AtRisk || p.CurrentPhase == EngagementPhase.Churned)
                .OrderByDescending(p => p.DaysSinceLastRental)
                .ToList();

            return Json(new
            {
                Count = atRisk.Count,
                AtRiskProfiles = atRisk
            }, JsonRequestBehavior.AllowGet);
        }
    }
}
