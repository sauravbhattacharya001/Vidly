using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Poster Creator — design custom movie posters with Canvas editor.
    /// Choose a movie or start from scratch, pick layouts, colors, and download as PNG.
    /// </summary>
    public class PosterController : Controller
    {
        private readonly IMovieRepository _movieRepository;

        public PosterController()
            : this(new InMemoryMovieRepository())
        {
        }

        public PosterController(IMovieRepository movieRepository)
        {
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        // GET: Poster
        public ActionResult Index()
        {
            var viewModel = new PosterViewModel
            {
                Movies = _movieRepository.GetAll()
            };

            return View(viewModel);
        }
    }
}
