using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Cultural Moment Detector — autonomous detection of movie cultural
    /// relevance events with proactive promotion/stocking recommendations.
    /// </summary>
    public class CulturalMomentController : Controller
    {
        private readonly CulturalMomentService _service;

        public CulturalMomentController()
            : this(
                new InMemoryMovieRepository(),
                new InMemoryRentalRepository(),
                new SystemClock())
        {
        }

        public CulturalMomentController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            IClock clock,
            CulturalMomentConfig config = null)
        {
            if (movieRepository == null) throw new ArgumentNullException("movieRepository");
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (clock == null) throw new ArgumentNullException("clock");
            _service = new CulturalMomentService(rentalRepository, movieRepository, clock, config);
        }

        /// <summary>
        /// GET /CulturalMoment — Full cultural moment report.
        /// </summary>
        [HttpGet]
        public ActionResult Index()
        {
            var report = _service.Analyze();
            return Json(new
            {
                report.GeneratedAt,
                report.CulturalPulseScore,
                report.TotalMomentsDetected,
                report.MomentsByType,
                Moments = report.Moments.Select(m => new
                {
                    m.MovieId,
                    m.MovieName,
                    Genre = m.Genre.HasValue ? m.Genre.Value.ToString() : null,
                    m.MomentType,
                    m.Description,
                    m.RelevanceScore,
                    m.DetectedAt,
                    m.RecommendedAction,
                    m.Priority
                }),
                GenreMomentum = report.GenreMomentum.Select(g => new
                {
                    Genre = g.Genre.ToString(),
                    g.RecentVelocity,
                    g.HistoricalBaseline,
                    g.MomentumRatio,
                    g.Trend
                }),
                report.Insights
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// GET /CulturalMoment/Top?count=10 — Top moments by relevance.
        /// </summary>
        [HttpGet]
        public ActionResult Top(int count = 10)
        {
            var moments = _service.GetTopMoments(count);
            return Json(new
            {
                Count = moments.Count,
                Moments = moments.Select(m => new
                {
                    m.MovieId,
                    m.MovieName,
                    Genre = m.Genre.HasValue ? m.Genre.Value.ToString() : null,
                    m.MomentType,
                    m.Description,
                    m.RelevanceScore,
                    m.RecommendedAction,
                    m.Priority
                })
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// GET /CulturalMoment/GenreMomentum — Genre momentum analysis.
        /// </summary>
        [HttpGet]
        public ActionResult GenreMomentum()
        {
            var momentum = _service.GetGenreMomentum();
            return Json(new
            {
                Genres = momentum.Select(g => new
                {
                    Genre = g.Genre.ToString(),
                    g.RecentVelocity,
                    g.HistoricalBaseline,
                    g.MomentumRatio,
                    g.Trend
                })
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// GET /CulturalMoment/ByType?type=Anniversary — Filter by moment type.
        /// </summary>
        [HttpGet]
        public ActionResult ByType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return Json(new { Error = "Type parameter is required" }, JsonRequestBehavior.AllowGet);

            var moments = _service.GetMomentsByType(type);
            return Json(new
            {
                Type = type,
                Count = moments.Count,
                Moments = moments.Select(m => new
                {
                    m.MovieId,
                    m.MovieName,
                    Genre = m.Genre.HasValue ? m.Genre.Value.ToString() : null,
                    m.MomentType,
                    m.Description,
                    m.RelevanceScore,
                    m.RecommendedAction,
                    m.Priority
                })
            }, JsonRequestBehavior.AllowGet);
        }
    }
}
