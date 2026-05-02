using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Competitive Intelligence Engine — autonomous market analysis,
    /// opportunity detection, and strategic recommendations.
    /// </summary>
    public class CompetitiveIntelController : Controller
    {
        private readonly CompetitiveIntelService _service;

        public CompetitiveIntelController()
            : this(
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository(),
                new SystemClock())
        {
        }

        public CompetitiveIntelController(
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo,
            ICustomerRepository customerRepo,
            IClock clock)
        {
            if (rentalRepo == null) throw new ArgumentNullException("rentalRepo");
            if (movieRepo == null) throw new ArgumentNullException("movieRepo");
            if (customerRepo == null) throw new ArgumentNullException("customerRepo");
            if (clock == null) throw new ArgumentNullException("clock");
            _service = new CompetitiveIntelService(rentalRepo, movieRepo, customerRepo, clock);
        }

        // GET: CompetitiveIntel/Dashboard
        public ActionResult Dashboard()
        {
            return Json(_service.GetDashboard(), JsonRequestBehavior.AllowGet);
        }

        // GET: CompetitiveIntel/Position
        public ActionResult Position()
        {
            return Json(_service.AnalyzePosition(), JsonRequestBehavior.AllowGet);
        }

        // GET: CompetitiveIntel/Opportunities
        public ActionResult Opportunities()
        {
            return Json(_service.ScanOpportunities(), JsonRequestBehavior.AllowGet);
        }

        // GET: CompetitiveIntel/Threats
        public ActionResult Threats()
        {
            return Json(_service.DetectThreats(), JsonRequestBehavior.AllowGet);
        }

        // GET: CompetitiveIntel/Recommendations
        public ActionResult Recommendations()
        {
            return Json(_service.GetRecommendations(), JsonRequestBehavior.AllowGet);
        }

        // GET: CompetitiveIntel/Health
        public ActionResult Health()
        {
            return Json(_service.GetHealthScore(), JsonRequestBehavior.AllowGet);
        }
    }
}
