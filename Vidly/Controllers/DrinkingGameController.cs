using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Drinking Game Generator — pick a movie and difficulty to get a
    /// set of fun watching rules tailored to the movie's genre.
    /// </summary>
    public class DrinkingGameController : Controller
    {
        private readonly IMovieRepository _movieRepository;
        private readonly DrinkingGameService _service;

        public DrinkingGameController()
            : this(new InMemoryMovieRepository(), new DrinkingGameService())
        {
        }

        public DrinkingGameController(IMovieRepository movieRepository, DrinkingGameService service)
        {
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        // GET: DrinkingGame
        public ActionResult Index(int? movieId, Difficulty? difficulty)
        {
            var viewModel = new DrinkingGameViewModel
            {
                Movies = _movieRepository.GetAll(),
                SelectedMovieId = movieId,
                SelectedDifficulty = difficulty ?? Difficulty.Standard
            };

            if (movieId.HasValue)
            {
                var movie = _movieRepository.GetById(movieId.Value);
                if (movie == null)
                    return HttpNotFound("Movie not found.");

                viewModel.Game = _service.Generate(movie, viewModel.SelectedDifficulty);
            }

            return View(viewModel);
        }
    }
}
