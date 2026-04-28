using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Genre Ecosystem Analyzer — autonomous genre relationship mapping,
    /// trend detection, desert identification, and catalog recommendations.
    /// </summary>
    public class GenreEcosystemController : Controller
    {
        private readonly GenreEcosystemService _service;

        public GenreEcosystemController()
            : this(
                new InMemoryMovieRepository(),
                new InMemoryRentalRepository(),
                new InMemoryCustomerRepository(),
                new SystemClock())
        {
        }

        public GenreEcosystemController(
            IMovieRepository movieRepo,
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo,
            IClock clock)
        {
            _service = new GenreEcosystemService(
                movieRepo ?? throw new ArgumentNullException(nameof(movieRepo)),
                rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo)),
                customerRepo ?? throw new ArgumentNullException(nameof(customerRepo)),
                clock);
        }

        // GET: GenreEcosystem
        public ActionResult Index()
        {
            var report = _service.Analyze();
            return View(report);
        }

        // GET: GenreEcosystem/Report?days=180
        public ActionResult Report(int? days)
        {
            var report = _service.Analyze(days ?? 180);
            return Json(report, JsonRequestBehavior.AllowGet);
        }

        // GET: GenreEcosystem/Affinity?genreA=Action&genreB=Thriller
        public ActionResult Affinity(string genreA, string genreB)
        {
            if (string.IsNullOrEmpty(genreA) || string.IsNullOrEmpty(genreB))
                return Json(new { error = "Both genreA and genreB are required" }, JsonRequestBehavior.AllowGet);

            var affinity = _service.GetGenreAffinity(genreA, genreB);
            return Json(new { genreA, genreB, affinity }, JsonRequestBehavior.AllowGet);
        }

        // GET: GenreEcosystem/Bridges?top=10
        public ActionResult Bridges(int? top)
        {
            var bridges = _service.GetTopBridges(top ?? 10);
            return Json(bridges, JsonRequestBehavior.AllowGet);
        }
    }
}
