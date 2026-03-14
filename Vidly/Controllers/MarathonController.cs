using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class MarathonController : Controller
    {
        private readonly IMovieRepository _movieRepository;
        private readonly MarathonPlannerService _plannerService;

        public MarathonController()
            : this(new InMemoryMovieRepository())
        {
        }

        public MarathonController(IMovieRepository movieRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _plannerService = new MarathonPlannerService(movieRepository);
        }

        // GET: Marathon
        public ActionResult Index()
        {
            var viewModel = new MarathonViewModel
            {
                AvailableMovies = _movieRepository.GetAll(),
                Request = new MarathonRequest()
            };
            return View(viewModel);
        }

        // POST: Marathon/Plan
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Plan(MarathonRequest request)
        {
            if (request == null || request.MovieIds == null || !request.MovieIds.Any())
            {
                TempData["Error"] = "Please select at least one movie for your marathon.";
                return RedirectToAction("Index");
            }

            var plan = _plannerService.BuildPlan(request);

            var viewModel = new MarathonViewModel
            {
                AvailableMovies = _movieRepository.GetAll(),
                Request = request,
                Plan = plan
            };

            return View("Index", viewModel);
        }

        // GET: Marathon/Suggest?count=5&genre=Action
        public ActionResult Suggest(int count = 5, Genre? genre = null)
        {
            var movies = _plannerService.SuggestMovies(count, genre);
            return Json(movies.Select(m => new
            {
                m.Id,
                m.Name,
                m.Rating,
                Genre = m.Genre?.ToString(),
                ReleaseYear = m.ReleaseDate?.Year
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
