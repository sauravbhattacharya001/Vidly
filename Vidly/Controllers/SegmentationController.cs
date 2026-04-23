using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Smart Customer Segmentation — autonomous RFM-based segmentation with
    /// migration tracking, health scoring, and proactive campaign recommendations.
    /// </summary>
    public class SegmentationController : Controller
    {
        private readonly SegmentationService _service;

        public SegmentationController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository(),
                   new InMemoryCustomerRepository())
        {
        }

        public SegmentationController(
            IMovieRepository movieRepo,
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo)
        {
            _service = new SegmentationService(movieRepo, rentalRepo, customerRepo);
        }

        // GET: Segmentation
        public ActionResult Index()
        {
            var fleet = _service.BuildFleet();
            return View(fleet);
        }

        // GET: Segmentation/FleetJson
        public ActionResult FleetJson()
        {
            var fleet = _service.BuildFleet();
            return Json(fleet, JsonRequestBehavior.AllowGet);
        }

        // GET: Segmentation/CustomerJson/5
        public ActionResult CustomerJson(int id)
        {
            var seg = _service.BuildCustomerSegment(id);
            if (seg == null)
                return HttpNotFound();
            return Json(seg, JsonRequestBehavior.AllowGet);
        }
    }
}
