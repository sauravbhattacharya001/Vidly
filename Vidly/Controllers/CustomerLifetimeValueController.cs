using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Smart Customer Lifetime Value Dashboard — autonomous CLV prediction,
    /// tier segmentation, trajectory detection, and proactive value-optimisation
    /// recommendations.
    /// </summary>
    public class CustomerLifetimeValueController : Controller
    {
        private readonly CustomerLifetimeValueService _service;

        public CustomerLifetimeValueController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository(),
                   new InMemoryCustomerRepository())
        {
        }

        public CustomerLifetimeValueController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository)
        {
            _service = new CustomerLifetimeValueService(customerRepository, rentalRepository, movieRepository);
        }

        // GET: CustomerLifetimeValue
        public ActionResult Index()
        {
            var summary = _service.GetSummary(DateTime.Now);
            return View(summary);
        }

        // GET: CustomerLifetimeValue/Customer/5
        public ActionResult Customer(int id)
        {
            try
            {
                var profile = _service.GetCustomerProfile(id, DateTime.Now);
                if (profile == null)
                    return Json(new { success = false, error = "Customer not found" },
                                JsonRequestBehavior.AllowGet);

                return Json(new
                {
                    success = true,
                    profile = new
                    {
                        profile.CustomerId,
                        profile.Name,
                        profile.Email,
                        Membership = profile.Membership.ToString(),
                        profile.EstimatedClv,
                        profile.HistoricalRevenue,
                        profile.PredictedFutureRevenue,
                        profile.TotalRentals,
                        profile.AvgMonthlySpend,
                        profile.MonthsActive,
                        profile.Tier,
                        profile.Trajectory,
                        profile.RetentionProbability,
                        profile.ValueDrivers
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message },
                            JsonRequestBehavior.AllowGet);
            }
        }

        // GET: CustomerLifetimeValue/Export
        public ActionResult Export()
        {
            var summary = _service.GetSummary(DateTime.Now, topN: 1000);
            return Json(new
            {
                generated = DateTime.Now.ToString("o"),
                summary.TotalCustomers,
                summary.TotalEstimatedClv,
                summary.AverageClv,
                summary.MedianClv,
                tierBreakdown = summary.TierBreakdown,
                topCustomers = summary.TopCustomers,
                atRiskCustomers = summary.AtRiskCustomers,
                recommendations = summary.Recommendations
            }, JsonRequestBehavior.AllowGet);
        }
    }
}
