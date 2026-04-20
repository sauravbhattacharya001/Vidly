using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Smart Churn Predictor Dashboard — interactive customer churn risk
    /// analysis with risk distribution, factor breakdown, retention
    /// recommendations, and winnable-customer targeting.
    /// </summary>
    public class ChurnPredictorController : Controller
    {
        private readonly ChurnPredictorService _service;

        public ChurnPredictorController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository(),
                   new InMemoryCustomerRepository())
        {
        }

        public ChurnPredictorController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository)
        {
            _service = new ChurnPredictorService(customerRepository, rentalRepository, movieRepository);
        }

        // GET: ChurnPredictor
        public ActionResult Index()
        {
            var summary = _service.GetSummary(DateTime.Now, topN: 20);
            return View(summary);
        }

        // GET: ChurnPredictor/Customer/5
        public ActionResult Customer(int id)
        {
            try
            {
                var profile = _service.Analyze(id, DateTime.Now);
                return Json(new
                {
                    success = true,
                    profile = new
                    {
                        profile.CustomerId,
                        profile.CustomerName,
                        MembershipType = profile.MembershipType.ToString(),
                        profile.RiskScore,
                        RiskLevel = profile.RiskLevel.ToString(),
                        profile.DaysSinceLastRental,
                        profile.TotalRentals,
                        TotalSpend = profile.TotalSpend.ToString("F2"),
                        profile.AvgDaysBetweenRentals,
                        profile.FrequencyTrend,
                        profile.LateReturnRate,
                        profile.GenreDiversity,
                        profile.RetentionActions,
                        Factors = new
                        {
                            profile.Factors.RecencyScore,
                            profile.Factors.FrequencyDeclineScore,
                            profile.Factors.EngagementScore,
                            profile.Factors.LateReturnScore,
                            profile.Factors.DiversityScore
                        }
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (ArgumentException ex)
            {
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // GET: ChurnPredictor/Winnable
        public ActionResult Winnable()
        {
            var customers = _service.GetWinnableCustomers(DateTime.Now);
            return Json(customers, JsonRequestBehavior.AllowGet);
        }

        // GET: ChurnPredictor/ByRisk?level=High
        public ActionResult ByRisk(string level = "High")
        {
            ChurnRisk riskLevel;
            if (!Enum.TryParse(level, true, out riskLevel))
                return Json(new { error = "Invalid risk level" }, JsonRequestBehavior.AllowGet);

            var customers = _service.GetByRiskLevel(riskLevel, DateTime.Now);
            return Json(customers, JsonRequestBehavior.AllowGet);
        }
    }
}
