using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Smart Movie Retirement Planner — autonomous end-of-life analysis for movies
    /// based on rental decline, dormancy, age, and genre health. Recommends archive,
    /// discount, or bundle actions and suggests replacement acquisitions.
    /// </summary>
    public class RetirementController : Controller
    {
        private readonly RetirementPlannerService _service;

        public RetirementController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository(),
                   new InMemoryCustomerRepository())
        {
        }

        public RetirementController(
            IMovieRepository movieRepo,
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo)
        {
            _service = new RetirementPlannerService(movieRepo, rentalRepo, customerRepo);
        }

        // GET: Retirement
        public ActionResult Index()
        {
            var plan = _service.GeneratePlan();
            return View(plan);
        }

        // GET: Retirement/PlanJson
        public ActionResult PlanJson()
        {
            var plan = _service.GeneratePlan();
            return Json(plan, JsonRequestBehavior.AllowGet);
        }

        // GET: Retirement/Urgency/Immediate
        public ActionResult Urgency(string id)
        {
            var candidates = _service.GetCandidatesByUrgency(id ?? "Immediate");
            ViewBag.Urgency = id ?? "Immediate";
            return View(candidates);
        }

        // GET: Retirement/GenreHealth
        public ActionResult GenreHealth()
        {
            var health = _service.GetGenreHealth();
            return View(health);
        }

        // GET: Retirement/GenreHealthJson
        public ActionResult GenreHealthJson()
        {
            var health = _service.GetGenreHealth();
            return Json(health, JsonRequestBehavior.AllowGet);
        }
    }
}
