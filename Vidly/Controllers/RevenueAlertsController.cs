using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Smart Revenue Alerts — proactive revenue monitoring dashboard with
    /// anomaly detection, forecasting, and autonomous recommendations.
    /// </summary>
    public class RevenueAlertsController : Controller
    {
        private readonly RevenueAlertService _service;

        public RevenueAlertsController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository(),
                   new InMemoryCustomerRepository())
        {
        }

        public RevenueAlertsController(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository)
        {
            _service = new RevenueAlertService(
                rentalRepository, movieRepository, customerRepository, new SystemClock());
        }

        // GET: RevenueAlerts
        public ActionResult Index()
        {
            var dashboard = _service.RunAnalysis();
            return View(dashboard);
        }

        // GET: RevenueAlerts/Alerts
        [HttpGet]
        public ActionResult Alerts(bool includeAcknowledged = false)
        {
            var alerts = _service.GetAlerts(includeAcknowledged);
            return Json(alerts, JsonRequestBehavior.AllowGet);
        }

        // POST: RevenueAlerts/Acknowledge/RA-0001
        [HttpPost]
        public ActionResult Acknowledge(string id)
        {
            var success = _service.AcknowledgeAlert(id);
            return Json(new { success });
        }

        // GET: RevenueAlerts/Forecast?days=7
        [HttpGet]
        public ActionResult Forecast(int days = 7)
        {
            var forecast = _service.GetForecast(days);
            return Json(forecast, JsonRequestBehavior.AllowGet);
        }

        // POST: RevenueAlerts/Analyze
        [HttpPost]
        public ActionResult Analyze()
        {
            var dashboard = _service.RunAnalysis();
            return Json(dashboard);
        }

        // GET: RevenueAlerts/Config
        [HttpGet]
        public ActionResult Config()
        {
            var config = _service.GetConfig();
            return Json(config, JsonRequestBehavior.AllowGet);
        }

        // POST: RevenueAlerts/Config
        [HttpPost]
        public ActionResult Config(AlertConfig config)
        {
            var updated = _service.ConfigureAlerts(config);
            return Json(updated);
        }

        // GET: RevenueAlerts/GenreRevenue?daysBack=30
        [HttpGet]
        public ActionResult GenreRevenue(int? daysBack = null)
        {
            var data = _service.GetGenreRevenue(daysBack);
            return Json(data, JsonRequestBehavior.AllowGet);
        }
    }
}
