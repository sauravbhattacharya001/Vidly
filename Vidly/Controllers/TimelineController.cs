using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Interactive visual timeline of movies plotted by release year
    /// with genre color-coding and filtering.
    /// </summary>
    public class TimelineController : Controller
    {
        private readonly TimelineService _timelineService;

        public TimelineController()
            : this(new InMemoryMovieRepository())
        {
        }

        public TimelineController(IMovieRepository movieRepository)
        {
            _timelineService = new TimelineService(
                movieRepository ?? throw new ArgumentNullException(nameof(movieRepository)));
        }

        // GET: Timeline?genre=Action
        public ActionResult Index(string genre = null)
        {
            Genre? filterGenre = null;
            if (!string.IsNullOrEmpty(genre) && Enum.TryParse<Genre>(genre, true, out var parsed))
            {
                filterGenre = parsed;
            }

            var viewModel = _timelineService.BuildTimeline(filterGenre);
            return View(viewModel);
        }
    }
}
