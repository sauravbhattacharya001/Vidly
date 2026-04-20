using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    public class CatalogGapController : Controller
    {
        private readonly CatalogGapService _service;

        public CatalogGapController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository())
        {
        }

        public CatalogGapController(IMovieRepository movieRepository, IRentalRepository rentalRepository)
        {
            _service = new CatalogGapService(movieRepository, rentalRepository);
        }

        public ActionResult Index()
        {
            var dashboard = _service.GetDashboard();
            return View(dashboard);
        }

        [HttpGet]
        public ActionResult Api()
        {
            var dashboard = _service.GetDashboard();
            return Json(dashboard, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public ActionResult Recommendations()
        {
            var dashboard = _service.GetDashboard();
            return Json(dashboard.Recommendations, JsonRequestBehavior.AllowGet);
        }
    }
}
