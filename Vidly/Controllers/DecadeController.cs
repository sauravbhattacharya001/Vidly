using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Browse the movie catalog organized by decade. See stats,
    /// genre breakdowns, and top-rated films for each era.
    /// </summary>
    public class DecadeController : Controller
    {
        private readonly DecadeExplorerService _decadeService;

        public DecadeController()
            : this(new InMemoryMovieRepository())
        {
        }

        public DecadeController(IMovieRepository movieRepository)
        {
            _decadeService = new DecadeExplorerService(
                movieRepository ?? throw new ArgumentNullException(nameof(movieRepository)));
        }

        // GET: Decade
        public ActionResult Index()
        {
            var decades = _decadeService.GetAllDecades();
            return View(decades);
        }

        // GET: Decade/Browse/1990
        public ActionResult Browse(int id)
        {
            // Normalize to decade start (e.g. 1993 → 1990)
            var decade = (id / 10) * 10;
            var detail = _decadeService.GetDecade(decade);

            if (detail == null)
                return HttpNotFound("No movies found for the " + decade + "s.");

            return View(detail);
        }
    }
}
