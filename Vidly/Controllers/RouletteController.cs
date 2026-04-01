using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Roulette — spin the wheel to discover your next rental!
    /// Interactive animated wheel picker with genre and rating filters.
    /// </summary>
    public class RouletteController : Controller
    {
        private readonly RouletteService _rouletteService;

        public RouletteController()
            : this(new InMemoryMovieRepository())
        {
        }

        public RouletteController(IMovieRepository movieRepository)
        {
            _rouletteService = new RouletteService(
                movieRepository ?? throw new ArgumentNullException(nameof(movieRepository)));
        }

        // GET: Roulette
        public ActionResult Index()
        {
            var wheelMovies = _rouletteService.GetWheelMovies();

            var viewModel = new RouletteViewModel
            {
                WheelMovies = wheelMovies,
                HasSpun = false
            };

            return View(viewModel);
        }

        // POST: Roulette/Spin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Spin(Genre? genre, int? minRating)
        {
            var result = _rouletteService.Spin(genre, minRating);
            var wheelMovies = _rouletteService.GetWheelMovies(genre, minRating);

            var viewModel = new RouletteViewModel
            {
                Result = result,
                WheelMovies = wheelMovies,
                SelectedGenre = genre,
                SelectedMinRating = minRating,
                HasSpun = true
            };

            return View("Index", viewModel);
        }
    }
}
