using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Smart Fraud Detector Dashboard — autonomous fraud pattern detection
    /// with velocity checks, behavioral anomaly scoring, risk tiering,
    /// and an interactive investigation interface.
    /// </summary>
    public class FraudDetectorController : Controller
    {
        private readonly FraudDetectorService _service;

        public FraudDetectorController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository(),
                   new InMemoryCustomerRepository())
        {
        }

        public FraudDetectorController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository)
        {
            _service = new FraudDetectorService(customerRepository, rentalRepository, movieRepository);
        }

        // GET: FraudDetector
        public ActionResult Index()
        {
            var summary = _service.GetSummary(DateTime.Now, topN: 20);
            return View(summary);
        }

        // GET: FraudDetector/Customer/5
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
                        profile.MembershipType,
                        profile.RiskScore,
                        profile.RiskTier,
                        profile.TotalRentals,
                        profile.ActiveRentals,
                        profile.LastRentalDate,
                        signals = profile.Signals.Select(s => new
                        {
                            s.RuleId,
                            s.RuleName,
                            s.Description,
                            severity = s.Severity.ToString(),
                            severityLevel = (int)s.Severity,
                            s.Confidence,
                            s.Evidence
                        })
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (ArgumentException ex)
            {
                return Json(new { success = false, error = ex.Message },
                    JsonRequestBehavior.AllowGet);
            }
        }
    }
}
