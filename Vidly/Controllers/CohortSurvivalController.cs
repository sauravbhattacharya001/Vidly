using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Customer Cohort Survival Engine — autonomous retention analysis using
    /// Kaplan-Meier survival estimation. Tracks monthly cohorts, computes
    /// survival curves, detects retention cliffs, and compares cohort performance.
    /// </summary>
    public class CohortSurvivalController : Controller
    {
        private readonly CohortSurvivalService _service;

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public CohortSurvivalController()
            : this(
                new InMemoryCustomerRepository(),
                new InMemoryRentalRepository(),
                new SystemClock())
        {
        }

        /// <summary>
        /// Constructor injection for testability.
        /// </summary>
        public CohortSurvivalController(
            ICustomerRepository customerRepository,
            IRentalRepository rentalRepository,
            IClock clock)
        {
            if (customerRepository == null) throw new ArgumentNullException("customerRepository");
            if (rentalRepository == null) throw new ArgumentNullException("rentalRepository");
            if (clock == null) throw new ArgumentNullException("clock");
            _service = new CohortSurvivalService(customerRepository, rentalRepository, clock);
        }

        // GET: CohortSurvival
        public ActionResult Index()
        {
            var report = _service.GenerateReport();
            return View(report);
        }

        // GET: CohortSurvival/Report
        public ActionResult Report()
        {
            var report = _service.GenerateReport();
            return Json(new
            {
                report.GeneratedAt,
                report.OverallRetentionHealth,
                CohortCount = report.Cohorts.Count,
                report.Insights,
                Cohorts = report.Cohorts.ConvertAll(c => new
                {
                    c.Label,
                    c.InitialSize,
                    c.MonthsTracked,
                    c.FinalSurvivalRate,
                    c.MedianSurvivalMonth,
                    c.HealthGrade,
                    CliffCount = c.RetentionCliffs.Count
                })
            }, JsonRequestBehavior.AllowGet);
        }

        // GET: CohortSurvival/Cohort?label=2025-01
        public ActionResult Cohort(string label)
        {
            if (string.IsNullOrEmpty(label))
                return Json(new { error = "Cohort label is required (e.g. 2025-01)" }, JsonRequestBehavior.AllowGet);

            var detail = _service.GetCohortDetail(label);
            if (detail == null)
                return Json(new { error = $"Cohort '{label}' not found" }, JsonRequestBehavior.AllowGet);

            return Json(new
            {
                detail.Cohort.Label,
                detail.Cohort.InitialSize,
                detail.Cohort.MonthsTracked,
                detail.Cohort.FinalSurvivalRate,
                detail.Cohort.MedianSurvivalMonth,
                detail.Cohort.HealthGrade,
                SurvivalCurve = detail.Cohort.SurvivalCurve,
                RetentionCliffs = detail.Cohort.RetentionCliffs,
                TopRetained = detail.TopRetainedCustomers,
                Churned = detail.ChurnedCustomers
            }, JsonRequestBehavior.AllowGet);
        }

        // GET: CohortSurvival/Health
        public ActionResult Health()
        {
            var report = _service.GenerateReport();
            return Json(report.OverallRetentionHealth, JsonRequestBehavior.AllowGet);
        }

        // GET: CohortSurvival/Compare
        public ActionResult Compare()
        {
            var report = _service.GenerateReport();
            return Json(new
            {
                Comparisons = report.Comparisons,
                report.Insights
            }, JsonRequestBehavior.AllowGet);
        }
    }
}
