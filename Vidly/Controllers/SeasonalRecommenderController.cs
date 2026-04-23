using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Smart Seasonal Recommender — analyzes rental patterns by season and
    /// recommends movies optimized for the current time of year.
    /// </summary>
    public class SeasonalRecommenderController : Controller
    {
        private readonly SeasonalRecommenderService _service;

        public SeasonalRecommenderController()
            : this(new InMemoryMovieRepository(), new InMemoryRentalRepository())
        {
        }

        public SeasonalRecommenderController(
            IMovieRepository movieRepo,
            IRentalRepository rentalRepo)
        {
            _service = new SeasonalRecommenderService(movieRepo, rentalRepo);
        }

        // GET: SeasonalRecommender
        public ActionResult Index()
        {
            var profile = _service.BuildProfile();
            return View(profile);
        }

        // GET: SeasonalRecommender/Season/Summer
        public ActionResult Season(string id)
        {
            if (string.IsNullOrEmpty(id))
                return RedirectToAction("Index");
            var profile = _service.BuildProfile(id);
            return View("Index", profile);
        }

        // GET: SeasonalRecommender/Json
        public ActionResult Json()
        {
            var profile = _service.BuildProfile();
            return base.Json(profile, JsonRequestBehavior.AllowGet);
        }
    }
}
