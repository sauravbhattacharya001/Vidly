using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Revenue Attribution Engine — autonomous multi-touch revenue attribution
    /// that traces revenue to its driving factors (channels, genres, tiers, time,
    /// pricing rules, retention) and generates actionable insights.
    /// </summary>
    public class RevenueAttributionController : Controller
    {
        private readonly RevenueAttributionService _service;

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public RevenueAttributionController()
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
        public RevenueAttributionController(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IClock clock)
        {
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (movieRepository == null) throw new ArgumentNullException("movieRepository");
            if (customerRepository == null) throw new ArgumentNullException("customerRepository");
            if (clock == null) throw new ArgumentNullException("clock");
            _service = new RevenueAttributionService(rentalRepository, movieRepository, customerRepository, clock);
        }

        // GET: RevenueAttribution
        public ActionResult Index()
        {
            var report = _service.GenerateReport();
            return View(report);
        }

        // GET: RevenueAttribution/Report
        public ActionResult Report()
        {
            var report = _service.GenerateReport();
            return Json(new
            {
                report.GeneratedAt,
                report.TotalRevenue,
                report.AttributionHealthScore,
                report.Insights,
                ChannelCount = report.ChannelBreakdown.Count,
                GenreCount = report.GenreBreakdown.Count,
                report.ChannelBreakdown,
                report.GenreBreakdown,
                report.TierBreakdown,
                report.PricingImpacts,
                report.RetentionBreakdown
            }, JsonRequestBehavior.AllowGet);
        }

        // GET: RevenueAttribution/Channels
        public ActionResult Channels()
        {
            var channels = _service.GetChannelBreakdown();
            return Json(channels, JsonRequestBehavior.AllowGet);
        }

        // GET: RevenueAttribution/Temporal?granularity=month
        public ActionResult Temporal(string granularity = "month")
        {
            var temporal = _service.GetTemporalBreakdown(granularity);
            return Json(temporal, JsonRequestBehavior.AllowGet);
        }

        // GET: RevenueAttribution/Tiers
        public ActionResult Tiers()
        {
            var tiers = _service.GetTierAttribution();
            return Json(tiers, JsonRequestBehavior.AllowGet);
        }

        // GET: RevenueAttribution/Health
        public ActionResult Health()
        {
            var report = _service.GenerateReport();
            return Json(new
            {
                report.AttributionHealthScore,
                report.TotalRevenue,
                InsightCount = report.Insights.Count
            }, JsonRequestBehavior.AllowGet);
        }
    }
}
