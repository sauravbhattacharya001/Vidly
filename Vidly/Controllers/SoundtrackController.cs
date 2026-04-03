using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;
using static Vidly.Services.MovieSoundtrackService;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Soundtrack Discovery — explore soundtrack profiles by genre,
    /// generate mood-based playlists, get per-movie soundtrack suggestions,
    /// and take soundtrack trivia quizzes.
    /// </summary>
    public class SoundtrackController : Controller
    {
        private readonly MovieSoundtrackService _service;

        public SoundtrackController()
            : this(new InMemoryMovieRepository())
        {
        }

        public SoundtrackController(IMovieRepository movieRepository)
        {
            _service = new MovieSoundtrackService(
                movieRepository ?? throw new ArgumentNullException(nameof(movieRepository)));
        }

        // GET: Soundtrack
        public ActionResult Index()
        {
            var stats = _service.GetCatalogStats();
            var profiles = _service.GetAllProfiles();
            var moods = _service.GetAllMoods();

            ViewBag.Stats = stats;
            ViewBag.Profiles = profiles;
            ViewBag.Moods = moods;
            return View();
        }

        // GET: Soundtrack/Genre/Action
        public ActionResult Genre(string id)
        {
            if (!Enum.TryParse<Models.Genre>(id, true, out var genre))
                return HttpNotFound("Unknown genre.");

            var profile = _service.GetGenreProfile(genre);
            if (profile == null) return HttpNotFound();

            ViewBag.Profile = profile;
            return View();
        }

        // GET: Soundtrack/Mood/Energetic
        public ActionResult Mood(string id)
        {
            if (!Enum.TryParse<MovieSoundtrackService.Mood>(id, true, out var mood))
                return HttpNotFound("Unknown mood.");

            var playlist = _service.GenerateMoodPlaylist(mood);
            ViewBag.Playlist = playlist;
            return View();
        }

        // GET: Soundtrack/Movie/5
        public ActionResult Movie(int id)
        {
            var suggestion = _service.GetSuggestionForMovie(id);
            if (suggestion == null)
                return HttpNotFound("Movie not found.");

            ViewBag.Suggestion = suggestion;
            return View();
        }

        // GET: Soundtrack/Mixtape
        public ActionResult Mixtape()
        {
            var mixtape = _service.GenerateMovieNightMixtape();
            ViewBag.Mixtape = mixtape;
            return View();
        }

        // GET: Soundtrack/Quiz
        public ActionResult Quiz()
        {
            var questions = _service.GenerateQuiz(5);
            ViewBag.Questions = questions;
            return View();
        }
    }
}
