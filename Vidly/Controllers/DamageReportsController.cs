using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Damage report dashboard — track disc condition, file damage
    /// reports, view analytics, and manage replacement needs.
    /// </summary>
    public class DamageReportsController : Controller
    {
        private readonly DamageReportService _service;

        public DamageReportsController()
            : this(new DamageReportService(
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository()))
        {
        }

        public DamageReportsController(DamageReportService service)
        {
            _service = service
                ?? throw new ArgumentNullException(nameof(service));
        }

        // GET: DamageReports
        public ActionResult Index(string tab = "overview")
        {
            var viewModel = new DamageReportViewModel
            {
                Analytics = _service.GetAnalytics(),
                RecentReports = _service.GetAllReports(),
                MoviesNeedingReplacement = _service.GetMoviesNeedingReplacement(),
                FlaggedCustomers = _service.GetFlaggedCustomers(),
                ActiveTab = tab
            };

            return View(viewModel);
        }

        // POST: DamageReports/File
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult File(
            int movieId,
            int customerId,
            DiscCondition conditionBefore,
            DiscCondition conditionAfter,
            DamageType damageType,
            string notes)
        {
            try
            {
                _service.FileReport(
                    movieId, customerId,
                    conditionBefore, conditionAfter,
                    damageType, notes);

                TempData["Success"] = "Damage report filed successfully.";
            }
            catch (ArgumentException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index", new { tab = "reports" });
        }

        // POST: DamageReports/Collect/5
        [HttpPost]
        public ActionResult Collect(int id)
        {
            if (_service.CollectCharge(id))
                TempData["Success"] = "Charge marked as collected.";
            else
                TempData["Error"] = "Report not found.";

            return RedirectToAction("Index", new { tab = "reports" });
        }

        // POST: DamageReports/Replace/5
        [HttpPost]
        public ActionResult Replace(int id)
        {
            if (_service.MarkReplaced(id))
                TempData["Success"] = "Disc marked as replaced.";
            else
                TempData["Error"] = "Report not found.";

            return RedirectToAction("Index", new { tab = "replacements" });
        }

        // GET: DamageReports/Movie/5
        public ActionResult Movie(int id)
        {
            var summary = _service.GetMovieDamageSummary(id);
            if (summary == null)
                return HttpNotFound();

            ViewBag.Reports = _service.GetReportsByMovie(id);
            return View(summary);
        }

        // GET: DamageReports/Customer/5
        public ActionResult Customer(int id)
        {
            var profile = _service.GetCustomerProfile(id);
            if (profile == null)
                return HttpNotFound();

            ViewBag.Reports = _service.GetReportsByCustomer(id);
            return View(profile);
        }
    }
}
