using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Taste DNA — autonomous preference fingerprinting with radar chart,
    /// personality archetype classification, and proactive recommendations.
    /// </summary>
    public class TasteDnaController : Controller
    {
        private readonly TasteDnaService _service;

        public TasteDnaController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository(),
                   new InMemoryCustomerRepository())
        {
        }

        public TasteDnaController(
            IMovieRepository movieRepo,
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo)
        {
            _service = new TasteDnaService(movieRepo, rentalRepo, customerRepo);
        }

        // GET: TasteDna
        public ActionResult Index()
        {
            var fleet = _service.BuildFleet();
            return View(fleet);
        }

        // GET: TasteDna/Profile/5
        public ActionResult Profile(int id)
        {
            var profile = _service.BuildProfile(id);
            if (profile == null)
                return HttpNotFound();
            return View(profile);
        }

        // GET: TasteDna/ProfileJson/5
        public ActionResult ProfileJson(int id)
        {
            var profile = _service.BuildProfile(id);
            if (profile == null)
                return HttpNotFound();
            return Json(profile, JsonRequestBehavior.AllowGet);
        }
    }
}
