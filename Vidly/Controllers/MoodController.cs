using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    public class MoodController : Controller
    {
        private readonly MoodMatcherService _moodService;

        public MoodController()
            : this(new InMemoryMovieRepository())
        {
        }

        public MoodController(IMovieRepository movieRepository)
        {
            _moodService = new MoodMatcherService(
                movieRepository ?? throw new ArgumentNullException(nameof(movieRepository)));
        }

        // GET: Mood
        public ActionResult Index()
        {
            var moods = _moodService.GetAllMoods();
            return View(moods);
        }

        // GET: Mood/Match/1
        public ActionResult Match(int id)
        {
            if (!Enum.IsDefined(typeof(Mood), id))
                return HttpNotFound("Unknown mood.");

            var mood = (Mood)id;
            var result = _moodService.GetRecommendations(mood);

            if (result.SelectedMood == null)
                return HttpNotFound("Mood profile not found.");

            return View(result);
        }
    }
}
