using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Catalog Velocity Engine — autonomous movie lifecycle phase tracking,
    /// velocity scoring, and action recommendations.
    /// </summary>
    public class CatalogVelocityController : Controller
    {
        private readonly CatalogVelocityService _service;

        public CatalogVelocityController()
            : this(
                new InMemoryMovieRepository(),
                new InMemoryRentalRepository(),
                new SystemClock())
        {
        }

        public CatalogVelocityController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            IClock clock,
            VelocityEngineConfig config = null)
        {
            if (movieRepository == null) throw new ArgumentNullException("movieRepository");
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (clock == null) throw new ArgumentNullException("clock");
            _service = new CatalogVelocityService(movieRepository, rentalRepository, clock, config);
        }

        /// <summary>
        /// GET /CatalogVelocity — Full velocity dashboard.
        /// </summary>
        [HttpGet]
        public ActionResult Index()
        {
            var report = _service.Analyze();
            return Json(new
            {
                report.GeneratedAt,
                report.WindowDays,
                report.CatalogHealthScore,
                report.AverageVelocity,
                PhaseDistribution = report.PhaseDistribution.ToDictionary(
                    kv => kv.Key.ToString(), kv => kv.Value),
                UrgentActions = report.UrgentActions.Select(FormatProfile),
                GenreBreakdown = report.GenreBreakdown.Select(g => new
                {
                    Genre = g.Genre.ToString(),
                    g.MovieCount,
                    g.AverageVelocity,
                    g.HotCount,
                    g.DormantCount,
                    g.HealthScore
                }),
                RecentTransitions = report.RecentTransitions.Select(t => new
                {
                    t.MovieId,
                    t.MovieName,
                    From = t.FromPhase.ToString(),
                    To = t.ToPhase.ToString(),
                    t.Trigger
                }),
                report.Insights,
                TotalMovies = report.Profiles.Count
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// GET /CatalogVelocity/Movie/5 — Single movie velocity profile.
        /// </summary>
        [HttpGet]
        public ActionResult Movie(int id)
        {
            var profile = _service.GetMovieVelocity(id);
            if (profile == null)
                return HttpNotFound();

            return Json(FormatProfile(profile), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// GET /CatalogVelocity/Phase/Hot — All movies in a specific phase.
        /// </summary>
        [HttpGet]
        public ActionResult Phase(string id)
        {
            CatalogPhase phase;
            if (!Enum.TryParse(id, true, out phase))
                return new HttpStatusCodeResult(400, "Invalid phase. Valid: NewArrival, Hot, Steady, Declining, Dormant, Resurgent");

            var profiles = _service.GetByPhase(phase);
            return Json(new
            {
                Phase = phase.ToString(),
                Count = profiles.Count,
                Movies = profiles.Select(FormatProfile)
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// GET /CatalogVelocity/Actions — All pending action recommendations sorted by confidence.
        /// </summary>
        [HttpGet]
        public ActionResult Actions()
        {
            var queue = _service.GetActionQueue();
            return Json(new
            {
                TotalActions = queue.Count,
                Actions = queue.Select(p => new
                {
                    p.MovieId,
                    p.MovieName,
                    Phase = p.Phase.ToString(),
                    Action = p.RecommendedAction.ToString(),
                    p.ActionConfidence,
                    p.ActionReasoning
                })
            }, JsonRequestBehavior.AllowGet);
        }

        private object FormatProfile(MovieVelocityProfile p)
        {
            return new
            {
                p.MovieId,
                p.MovieName,
                Genre = p.Genre.HasValue ? p.Genre.Value.ToString() : null,
                Phase = p.Phase.ToString(),
                PreviousPhase = p.PreviousPhase.HasValue ? p.PreviousPhase.Value.ToString() : null,
                p.VelocityScore,
                p.Acceleration,
                p.RentalsInWindow,
                p.RecentRentals,
                p.PriorPeriodRentals,
                p.DaysSinceLastRental,
                p.DaysInCatalog,
                Action = p.RecommendedAction.ToString(),
                p.ActionConfidence,
                p.ActionReasoning,
                p.AtRisk,
                p.EstimatedDaysToNextPhase
            };
        }
    }
}
