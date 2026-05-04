using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Rental Streak Engine — autonomous customer engagement streak
    /// tracking with proactive intervention recommendations.
    /// </summary>
    public class StreakController : Controller
    {
        private readonly RentalStreakService _service;

        /// <summary>
        /// Default constructor using in-memory repositories.
        /// </summary>
        public StreakController()
            : this(
                new InMemoryRentalRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryMovieRepository(),
                new SystemClock())
        {
        }

        /// <summary>
        /// Testable constructor accepting repository interfaces.
        /// </summary>
        public StreakController(
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo,
            IMovieRepository movieRepo,
            IClock clock,
            StreakConfig config = null)
        {
            if (rentalRepo == null) throw new ArgumentNullException("rentalRepo");
            if (customerRepo == null) throw new ArgumentNullException("customerRepo");
            if (movieRepo == null) throw new ArgumentNullException("movieRepo");
            if (clock == null) throw new ArgumentNullException("clock");
            _service = new RentalStreakService(rentalRepo, customerRepo, movieRepo, clock, config);
        }

        /// <summary>
        /// GET /Streak — Full streak report.
        /// </summary>
        [HttpGet]
        public ActionResult Index()
        {
            var report = _service.Analyze();
            return Json(new
            {
                report.GeneratedAt,
                report.OverallEngagementScore,
                report.FleetHealth,
                TotalStreaks = report.Streaks.Count,
                AtRiskCount = report.AtRiskStreaks.Count,
                MilestoneCount = report.Milestones.Count,
                report.Streaks,
                report.AtRiskStreaks,
                report.Milestones,
                report.Recommendations,
                report.Insights
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// GET /Streak/Customer/{id} — Single customer streak detail.
        /// </summary>
        [HttpGet]
        public ActionResult Customer(int id)
        {
            var streaks = _service.CalculateStreaks();
            var customer = streaks.FirstOrDefault(s => s.CustomerId == id);
            if (customer == null)
                return Json(new { error = "Customer not found" }, JsonRequestBehavior.AllowGet);

            return Json(customer, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// GET /Streak/AtRisk — At-risk streaks only.
        /// </summary>
        [HttpGet]
        public ActionResult AtRisk()
        {
            var atRisk = _service.DetectAtRiskStreaks();
            return Json(new
            {
                Count = atRisk.Count,
                AtRiskStreaks = atRisk
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// GET /Streak/Milestones — Recent milestones.
        /// </summary>
        [HttpGet]
        public ActionResult Milestones()
        {
            var milestones = _service.DetectMilestones();
            return Json(new
            {
                Count = milestones.Count,
                Milestones = milestones
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// GET /Streak/Fleet — Fleet health only.
        /// </summary>
        [HttpGet]
        public ActionResult Fleet()
        {
            var health = _service.CalculateFleetHealth();
            return Json(health, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// GET /Streak/Leaderboard — Top 10 customers by streak length.
        /// </summary>
        [HttpGet]
        public ActionResult Leaderboard()
        {
            var streaks = _service.CalculateStreaks();
            var top = streaks
                .Where(s => s.CurrentStreakWeeks > 0)
                .OrderByDescending(s => s.CurrentStreakWeeks)
                .ThenByDescending(s => s.EngagementScore)
                .Take(10)
                .Select((s, i) => new
                {
                    Rank = i + 1,
                    s.CustomerId,
                    s.CustomerName,
                    s.CurrentStreakWeeks,
                    s.LongestStreakWeeks,
                    s.EngagementScore,
                    s.HasActiveStreak
                })
                .ToList();

            return Json(new { Leaderboard = top }, JsonRequestBehavior.AllowGet);
        }
    }
}
