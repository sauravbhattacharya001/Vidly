using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Soundtrack Manager — browse, search, rate, and discover movie soundtracks.
    /// </summary>
    public class SoundtrackController : Controller
    {
        private readonly ISoundtrackRepository _soundtrackRepository;
        private readonly IMovieRepository _movieRepository;

        public SoundtrackController()
            : this(new InMemorySoundtrackRepository(), new InMemoryMovieRepository())
        {
        }

        public SoundtrackController(
            ISoundtrackRepository soundtrackRepository,
            IMovieRepository movieRepository)
        {
            _soundtrackRepository = soundtrackRepository
                ?? throw new ArgumentNullException(nameof(soundtrackRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        /// <summary>
        /// GET /Soundtrack — main soundtrack browser with optional movie filter and search.
        /// </summary>
        public ActionResult Index(int? movieId, string q)
        {
            var tracks = !string.IsNullOrWhiteSpace(q)
                ? _soundtrackRepository.Search(q)
                : movieId.HasValue
                    ? _soundtrackRepository.GetByMovieId(movieId.Value)
                    : _soundtrackRepository.GetAll();

            var viewModel = new SoundtrackViewModel
            {
                Tracks = tracks,
                TopRated = _soundtrackRepository.GetTopRated(5),
                Movies = _movieRepository.GetAll(),
                FilterMovieId = movieId,
                SearchQuery = q
            };

            return View(viewModel);
        }

        /// <summary>
        /// POST /Soundtrack/Add — add a new track to a movie's soundtrack.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Add(int movieId, string title, string artist,
            int durationMinutes, int durationSeconds, int trackNumber, string genre)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                TempData["Error"] = "Track title is required.";
                return RedirectToAction("Index");
            }

            if (string.IsNullOrWhiteSpace(artist))
            {
                TempData["Error"] = "Artist name is required.";
                return RedirectToAction("Index");
            }

            var movie = _movieRepository.GetAll().FirstOrDefault(m => m.Id == movieId);
            if (movie == null)
            {
                TempData["Error"] = "Invalid movie selected.";
                return RedirectToAction("Index");
            }

            var track = new SoundtrackTrack
            {
                MovieId = movieId,
                MovieName = movie.Name,
                Title = title.Trim(),
                Artist = artist.Trim(),
                DurationSeconds = durationMinutes * 60 + durationSeconds,
                TrackNumber = trackNumber > 0 ? trackNumber : 1,
                Genre = genre?.Trim()
            };

            _soundtrackRepository.Add(track);
            TempData["Success"] = $"Added \"{track.Title}\" to {movie.Name} soundtrack!";
            return RedirectToAction("Index", new { movieId });
        }

        /// <summary>
        /// POST /Soundtrack/Rate — rate a track (1-5 stars).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Rate(int id, int stars)
        {
            if (stars < 1 || stars > 5)
            {
                TempData["Error"] = "Rating must be between 1 and 5 stars.";
                return RedirectToAction("Index");
            }

            _soundtrackRepository.Rate(id, stars);
            TempData["Success"] = "Thanks for rating!";
            return RedirectToAction("Index");
        }

        /// <summary>
        /// POST /Soundtrack/Delete — remove a track.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            _soundtrackRepository.Delete(id);
            TempData["Success"] = "Track removed.";
            return RedirectToAction("Index");
        }
    }
}
