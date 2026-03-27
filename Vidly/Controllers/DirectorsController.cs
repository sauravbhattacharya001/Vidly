using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Director Spotlight — browse directors, view bios, and see their
    /// filmography available in the store with stats.
    /// </summary>
    public class DirectorsController : Controller
    {
        private readonly IDirectorRepository _directorRepository;
        private readonly IMovieRepository _movieRepository;

        public DirectorsController()
            : this(new InMemoryDirectorRepository(), new InMemoryMovieRepository())
        {
        }

        public DirectorsController(
            IDirectorRepository directorRepository,
            IMovieRepository movieRepository)
        {
            _directorRepository = directorRepository
                ?? throw new ArgumentNullException(nameof(directorRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        /// <summary>
        /// GET /Directors — browse all directors with optional search.
        /// </summary>
        public ActionResult Index(string q)
        {
            var directors = string.IsNullOrWhiteSpace(q)
                ? _directorRepository.GetAll()
                : _directorRepository.Search(q);

            var viewModel = new DirectorListViewModel
            {
                Directors = directors,
                SearchQuery = q
            };

            return View(viewModel);
        }

        /// <summary>
        /// GET /Directors/Spotlight/1 — detailed view of a director with filmography.
        /// </summary>
        public ActionResult Spotlight(int id)
        {
            var director = _directorRepository.GetById(id);
            if (director == null)
                return HttpNotFound();

            var links = _directorRepository.GetMovieLinks(id);
            var movieIds = links.Select(l => l.MovieId).ToList();
            var allMovies = _movieRepository.GetAll();
            var filmography = allMovies.Where(m => movieIds.Contains(m.Id)).ToList();

            var viewModel = new DirectorSpotlightViewModel
            {
                Director = director,
                Filmography = filmography,
                TotalMoviesInStore = filmography.Count,
                AverageRating = filmography.Any(m => m.Rating.HasValue)
                    ? filmography.Where(m => m.Rating.HasValue).Average(m => m.Rating.Value)
                    : (double?)null
            };

            return View(viewModel);
        }

        /// <summary>
        /// GET /Directors/Random — JSON endpoint returning a random director (for widgets).
        /// </summary>
        public ActionResult Random()
        {
            var directors = _directorRepository.GetAll();
            if (!directors.Any())
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);

            var rng = new Random();
            var d = directors[rng.Next(directors.Count)];
            return Json(new
            {
                success = true,
                id = d.Id,
                name = d.Name,
                nationality = d.Nationality,
                knownFor = d.KnownFor
            }, JsonRequestBehavior.AllowGet);
        }
    }
}
