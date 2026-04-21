using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Customer Win-Back Engine Dashboard — autonomous lapsed-customer
    /// detection with lapse-reason classification, personalized campaigns,
    /// and recovery probability scoring.
    /// </summary>
    public class WinBackController : Controller
    {
        private readonly WinBackService _service;

        public WinBackController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository(),
                   new InMemoryCustomerRepository())
        {
        }

        public WinBackController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository)
        {
            _service = new WinBackService(customerRepository, rentalRepository, movieRepository);
        }

        // GET: WinBack
        public ActionResult Index()
        {
            var summary = _service.Analyze();
            return View(summary);
        }

        // GET: WinBack/Customer/5
        public ActionResult Customer(int id)
        {
            try
            {
                var winBackCase = _service.AnalyzeCustomer(id);
                return Json(new
                {
                    success = true,
                    data = new
                    {
                        winBackCase.CustomerId,
                        winBackCase.CustomerName,
                        winBackCase.Email,
                        MembershipType = winBackCase.MembershipType.ToString(),
                        LapseReason = winBackCase.LapseReason.ToString(),
                        RecommendedCampaign = winBackCase.RecommendedCampaign.ToString(),
                        Status = winBackCase.Status.ToString(),
                        winBackCase.DaysSinceLastRental,
                        winBackCase.TotalLifetimeRentals,
                        TotalLifetimeSpend = winBackCase.TotalLifetimeSpend.ToString("F2"),
                        LateFeeRatio = winBackCase.LateFeeRatio.ToString("P1"),
                        winBackCase.TopGenre,
                        GenreDiversity = winBackCase.GenreDiversity.ToString("F2"),
                        WinBackProbability = winBackCase.WinBackProbability.ToString("P0"),
                        winBackCase.PersonalizedOffer,
                        winBackCase.CampaignMessages
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (ArgumentException ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // GET: WinBack/Targets?minProbability=0.5
        public ActionResult Targets(double minProbability = 0.5)
        {
            var targets = _service.GetHighProbabilityTargets(minProbability: minProbability);
            return Json(targets, JsonRequestBehavior.AllowGet);
        }
    }
}
