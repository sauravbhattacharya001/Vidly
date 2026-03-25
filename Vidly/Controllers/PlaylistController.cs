using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class PlaylistController : Controller
    {
        private readonly IPlaylistRepository _playlistRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IMovieRepository _movieRepository;

        public PlaylistController()
            : this(
                new InMemoryPlaylistRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryMovieRepository())
        {
        }

        public PlaylistController(
            IPlaylistRepository playlistRepository,
            ICustomerRepository customerRepository,
            IMovieRepository movieRepository)
        {
            _playlistRepository = playlistRepository
                ?? throw new ArgumentNullException(nameof(playlistRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        // GET: Playlist
        public ActionResult Index(int? customerId, string message, bool? error)
        {
            var viewModel = new PlaylistIndexViewModel
            {
                Customers = _customerRepository.GetAll(),
                SelectedCustomerId = customerId,
                PublicPlaylists = _playlistRepository.GetPublicPlaylists(10),
                StatusMessage = message,
                IsError = error ?? false
            };

            if (customerId.HasValue)
            {
                var customer = _customerRepository.GetById(customerId.Value);
                if (customer == null)
                    return HttpNotFound("Customer not found.");

                viewModel.SelectedCustomerName = customer.Name;
                viewModel.CustomerPlaylists = _playlistRepository.GetByCustomer(customerId.Value);
            }

            return View(viewModel);
        }

        // GET: Playlist/Detail/5
        public ActionResult Detail(int id, int? customerId)
        {
            var playlist = _playlistRepository.GetById(id);
            if (playlist == null)
                return HttpNotFound("Playlist not found.");

            // Increment view count for non-owners
            if (!customerId.HasValue || customerId.Value != playlist.CreatedByCustomerId)
                playlist.ViewCount++;

            var viewModel = new PlaylistDetailViewModel
            {
                Playlist = playlist,
                IsOwner = customerId.HasValue && customerId.Value == playlist.CreatedByCustomerId,
                ViewerCustomerId = customerId
            };

            return View(viewModel);
        }

        // GET: Playlist/Create
        public ActionResult Create(int? customerId)
        {
            var viewModel = new PlaylistCreateViewModel
            {
                Customers = _customerRepository.GetAll(),
                Playlist = new Playlist
                {
                    CreatedByCustomerId = customerId ?? 0,
                    IsPublic = true
                }
            };

            return View(viewModel);
        }

        // POST: Playlist/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(Playlist playlist)
        {
            if (playlist.CreatedByCustomerId <= 0)
                ModelState.AddModelError("CreatedByCustomerId", "Please select a customer.");

            if (string.IsNullOrWhiteSpace(playlist.Name))
                ModelState.AddModelError("Name", "Playlist name is required.");

            if (!ModelState.IsValid)
            {
                var viewModel = new PlaylistCreateViewModel
                {
                    Customers = _customerRepository.GetAll(),
                    Playlist = playlist
                };
                return View(viewModel);
            }

            var customer = _customerRepository.GetById(playlist.CreatedByCustomerId);
            if (customer == null)
                return HttpNotFound("Customer not found.");

            playlist.CreatedByCustomerName = customer.Name;
            _playlistRepository.Add(playlist);

            return RedirectToAction("Detail", new { id = playlist.Id, customerId = playlist.CreatedByCustomerId });
        }

        // GET: Playlist/AddMovie/5
        public ActionResult AddMovie(int id, int? customerId)
        {
            var playlist = _playlistRepository.GetById(id);
            if (playlist == null)
                return HttpNotFound("Playlist not found.");

            var viewModel = new PlaylistAddMovieViewModel
            {
                PlaylistId = id,
                PlaylistName = playlist.Name,
                AvailableMovies = _movieRepository.GetAll()
            };

            return View(viewModel);
        }

        // POST: Playlist/AddMovie
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddMovie(int playlistId, int movieId, string note, int? customerId)
        {
            var playlist = _playlistRepository.GetById(playlistId);
            if (playlist == null)
                return HttpNotFound("Playlist not found.");

            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                return HttpNotFound("Movie not found.");

            var entry = new PlaylistEntry
            {
                MovieId = movieId,
                MovieName = movie.Name,
                MovieGenre = movie.Genre,
                MovieRating = movie.Rating,
                Note = note
            };

            try
            {
                _playlistRepository.AddEntry(playlistId, entry);
                return RedirectToAction("Detail", new
                {
                    id = playlistId,
                    customerId
                });
            }
            catch (InvalidOperationException ex)
            {
                return RedirectToAction("Index", new
                {
                    customerId,
                    message = ex.Message,
                    error = true
                });
            }
        }

        // POST: Playlist/RemoveMovie
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RemoveMovie(int playlistId, int entryId, int? customerId)
        {
            try
            {
                _playlistRepository.RemoveEntry(playlistId, entryId);
                return RedirectToAction("Detail", new { id = playlistId, customerId });
            }
            catch (KeyNotFoundException)
            {
                return RedirectToAction("Index", new
                {
                    customerId,
                    message = "Entry not found.",
                    error = true
                });
            }
        }

        // POST: Playlist/MoveMovie
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult MoveMovie(int playlistId, int entryId, int newPosition, int? customerId)
        {
            try
            {
                _playlistRepository.MoveEntry(playlistId, entryId, newPosition);
                return RedirectToAction("Detail", new { id = playlistId, customerId });
            }
            catch (KeyNotFoundException)
            {
                return RedirectToAction("Index", new
                {
                    customerId,
                    message = "Entry not found.",
                    error = true
                });
            }
        }

        // POST: Playlist/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, int? customerId)
        {
            try
            {
                _playlistRepository.Delete(id);
                return RedirectToAction("Index", new
                {
                    customerId,
                    message = "Playlist deleted."
                });
            }
            catch (KeyNotFoundException)
            {
                return RedirectToAction("Index", new
                {
                    customerId,
                    message = "Playlist not found.",
                    error = true
                });
            }
        }

        // POST: Playlist/Fork/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Fork(int id, int customerId)
        {
            var original = _playlistRepository.GetById(id);
            if (original == null)
                return HttpNotFound("Playlist not found.");

            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                return HttpNotFound("Customer not found.");

            var fork = new Playlist
            {
                Name = original.Name + " (copy)",
                Description = original.Description,
                CreatedByCustomerId = customerId,
                CreatedByCustomerName = customer.Name,
                IsPublic = false
            };

            _playlistRepository.Add(fork);

            // Copy entries
            foreach (var entry in original.Entries)
            {
                var newEntry = new PlaylistEntry
                {
                    MovieId = entry.MovieId,
                    MovieName = entry.MovieName,
                    MovieGenre = entry.MovieGenre,
                    MovieRating = entry.MovieRating,
                    Note = entry.Note
                };
                _playlistRepository.AddEntry(fork.Id, newEntry);
            }

            original.ForkCount++;

            return RedirectToAction("Detail", new { id = fork.Id, customerId });
        }
    }
}
