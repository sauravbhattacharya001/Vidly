using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Smart Shelf Optimizer — autonomous shelf layout recommendation engine
    /// that analyzes co-rental patterns, genre heat, and hidden gems to
    /// recommend optimal physical store shelf placement.
    /// </summary>
    public class ShelfOptimizerController : Controller
    {
        private readonly ShelfOptimizerService _service;

        public ShelfOptimizerController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository(),
                   new InMemoryCustomerRepository())
        {
        }

        public ShelfOptimizerController(
            IMovieRepository movieRepo,
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo)
        {
            _service = new ShelfOptimizerService(movieRepo, rentalRepo, customerRepo);
        }

        // GET: ShelfOptimizer
        public ActionResult Index()
        {
            var plan = _service.GeneratePlan();
            return View(plan);
        }

        // GET: ShelfOptimizer/PlanJson
        public ActionResult PlanJson()
        {
            var plan = _service.GeneratePlan();
            return Json(plan, JsonRequestBehavior.AllowGet);
        }

        // GET: ShelfOptimizer/Zone/PrimeShelf
        public ActionResult Zone(string id)
        {
            var assignments = _service.GetByZone(id ?? "PrimeShelf");
            ViewBag.ZoneName = id ?? "PrimeShelf";
            return View(assignments);
        }

        // GET: ShelfOptimizer/Clusters
        public ActionResult Clusters()
        {
            var clusters = _service.GetClusters();
            return View(clusters);
        }

        // GET: ShelfOptimizer/ClustersJson
        public ActionResult ClustersJson()
        {
            var clusters = _service.GetClusters();
            return Json(clusters, JsonRequestBehavior.AllowGet);
        }

        // GET: ShelfOptimizer/Monitor
        public ActionResult Monitor()
        {
            var plan = _service.GeneratePlan();
            return View(plan);
        }
    }
}
