using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    public class PricingController : Controller
    {
        private readonly PricingEngineService _service;

        public PricingController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository(),
                   new InMemoryCustomerRepository())
        {
        }

        public PricingController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository)
        {
            _service = new PricingEngineService(
                rentalRepository, movieRepository, customerRepository, new SystemClock());
        }

        public ActionResult Index()
        {
            var dashboard = _service.GetDashboard();
            return View(dashboard);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ToggleRule(int id)
        {
            _service.ToggleRule(id);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddRule(PricingRule rule)
        {
            if (!string.IsNullOrWhiteSpace(rule.Name) && rule.Multiplier > 0)
            {
                _service.AddRule(rule);
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RemoveRule(int id)
        {
            _service.RemoveRule(id);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RunAutopilot()
        {
            _service.RunAutopilot();
            return RedirectToAction("Index");
        }

        [HttpGet]
        public ActionResult Calculate(int movieId)
        {
            var price = _service.CalculatePrice(movieId);
            return Json(new { movieId, price }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public ActionResult Heatmap()
        {
            var data = _service.GetDemandHeatmap();
            return Json(data, JsonRequestBehavior.AllowGet);
        }
    }
}
