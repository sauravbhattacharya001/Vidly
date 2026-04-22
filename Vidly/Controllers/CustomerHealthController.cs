using System.Web.Mvc;
using Vidly.Services;

namespace Vidly.Controllers
{
    public class CustomerHealthController : Controller
    {
        private readonly CustomerHealthService _service = new CustomerHealthService();

        public ActionResult Index()
        {
            var fleet = _service.GetFleetHealth();
            return View(fleet);
        }

        public ActionResult Details(int id)
        {
            var result = _service.GetCustomerHealth(id);
            if (result == null) return HttpNotFound();
            return View(result);
        }

        public ActionResult Alerts()
        {
            var alerts = _service.AutoMonitor();
            return View(alerts);
        }

        public ActionResult Api_FleetHealth()
        {
            return Json(_service.GetFleetHealth(), JsonRequestBehavior.AllowGet);
        }

        public ActionResult Api_CustomerHealth(int id)
        {
            var result = _service.GetCustomerHealth(id);
            if (result == null) return HttpNotFound();
            return Json(result, JsonRequestBehavior.AllowGet);
        }
    }
}
