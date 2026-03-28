using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Gift Registry — customers create public wishlists of movies they'd like
    /// to receive as gift rentals. Friends can browse registries and fulfill items.
    /// Share a registry via its unique share code.
    /// </summary>
    public class GiftRegistryController : Controller
    {
        private readonly IGiftRegistryRepository _registryRepository;
        private readonly IMovieRepository _movieRepository;

        public GiftRegistryController()
            : this(new InMemoryGiftRegistryRepository(), new InMemoryMovieRepository()) { }

        public GiftRegistryController(
            IGiftRegistryRepository registryRepository,
            IMovieRepository movieRepository)
        {
            _registryRepository = registryRepository
                ?? throw new ArgumentNullException(nameof(registryRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        /// <summary>
        /// Browse all public gift registries.
        /// </summary>
        public ActionResult Index(string q)
        {
            var registries = _registryRepository.GetAll();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var query = q.Trim().ToLowerInvariant();
                registries = registries
                    .Where(r => (r.Name ?? "").ToLowerInvariant().Contains(query)
                             || (r.Description ?? "").ToLowerInvariant().Contains(query))
                    .ToList()
                    .AsReadOnly();
            }

            var vm = new GiftRegistryIndexViewModel
            {
                Registries = registries,
                SearchQuery = q
            };

            return View(vm);
        }

        /// <summary>
        /// View a specific registry by ID.
        /// </summary>
        public ActionResult Details(int id)
        {
            var registry = _registryRepository.GetById(id);
            if (registry == null) return HttpNotFound();

            var activeItems = registry.Items
                .Where(i => i.Status != GiftRegistryItemStatus.Removed).ToList();
            var fulfilled = activeItems.Count(i => i.Status == GiftRegistryItemStatus.Fulfilled);
            var total = activeItems.Count;

            var vm = new GiftRegistryDetailViewModel
            {
                Registry = registry,
                WantedCount = total - fulfilled,
                FulfilledCount = fulfilled,
                ProgressPercent = total > 0 ? (fulfilled * 100) / total : 0
            };

            return View(vm);
        }

        /// <summary>
        /// Look up a registry by its shareable code.
        /// </summary>
        public ActionResult Lookup(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                TempData["Error"] = "Please enter a share code.";
                return RedirectToAction("Index");
            }

            var registry = _registryRepository.GetByShareCode(code.Trim());
            if (registry == null)
            {
                TempData["Error"] = $"No registry found with code '{code.Trim()}'.";
                return RedirectToAction("Index");
            }

            return RedirectToAction("Details", new { id = registry.Id });
        }

        /// <summary>
        /// Show the create registry form.
        /// </summary>
        public ActionResult Create()
        {
            return View(new GiftRegistryCreateViewModel());
        }

        /// <summary>
        /// Create a new gift registry.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(GiftRegistryCreateViewModel model)
        {
            if (model == null) return new HttpStatusCodeResult(400);

            if (string.IsNullOrWhiteSpace(model.Name))
                ModelState.AddModelError("Name", "Registry name is required.");

            if (model.Name != null && model.Name.Length > 100)
                ModelState.AddModelError("Name", "Name cannot exceed 100 characters.");

            if (!ModelState.IsValid)
                return View(model);

            DateTime? eventDate = null;
            if (!string.IsNullOrWhiteSpace(model.EventDate) &&
                DateTime.TryParse(model.EventDate, out var parsed))
                eventDate = parsed;

            var registry = new GiftRegistry
            {
                CustomerId = 1, // Would come from auth in production
                Name = model.Name.Trim(),
                Description = model.Description?.Trim(),
                Occasion = model.Occasion,
                EventDate = eventDate,
                IsPublic = model.IsPublic
            };

            _registryRepository.Add(registry);
            TempData["Message"] = $"Registry '{registry.Name}' created! Share code: {registry.ShareCode}";
            return RedirectToAction("Details", new { id = registry.Id });
        }

        /// <summary>
        /// Show the add-item form for a registry.
        /// </summary>
        public ActionResult AddItem(int registryId)
        {
            var registry = _registryRepository.GetById(registryId);
            if (registry == null) return HttpNotFound();

            var movies = _movieRepository.GetAllMovies();
            var vm = new GiftRegistryAddItemViewModel
            {
                RegistryId = registryId,
                AvailableMovies = movies
            };

            return View(vm);
        }

        /// <summary>
        /// Add a movie to the registry.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddItem(GiftRegistryAddItemViewModel model)
        {
            if (model == null) return new HttpStatusCodeResult(400);

            var registry = _registryRepository.GetById(model.RegistryId);
            if (registry == null) return HttpNotFound();

            if (model.MovieId <= 0)
                ModelState.AddModelError("MovieId", "Please select a movie.");

            // Check for duplicates
            if (registry.Items.Any(i =>
                i.MovieId == model.MovieId &&
                i.Status != GiftRegistryItemStatus.Removed))
            {
                ModelState.AddModelError("MovieId", "This movie is already in the registry.");
            }

            if (!ModelState.IsValid)
            {
                model.AvailableMovies = _movieRepository.GetAllMovies();
                return View(model);
            }

            var movie = _movieRepository.GetMovieById(model.MovieId);
            var item = new GiftRegistryItem
            {
                MovieId = model.MovieId,
                MovieName = movie?.Name ?? "Unknown Movie",
                Note = model.Note?.Trim()
            };

            _registryRepository.AddItem(model.RegistryId, item);
            TempData["Message"] = $"'{item.MovieName}' added to the registry!";
            return RedirectToAction("Details", new { id = model.RegistryId });
        }

        /// <summary>
        /// Fulfill (gift) an item from a registry.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Fulfill(GiftRegistryFulfillViewModel model)
        {
            if (model == null) return new HttpStatusCodeResult(400);

            if (string.IsNullOrWhiteSpace(model.YourName))
            {
                TempData["Error"] = "Please enter your name to fulfill this gift.";
                return RedirectToAction("Details", new { id = model.RegistryId });
            }

            _registryRepository.FulfillItem(model.RegistryId, model.ItemId, model.YourName.Trim());
            TempData["Message"] = "Thank you for the gift! 🎁";
            return RedirectToAction("Details", new { id = model.RegistryId });
        }

        /// <summary>
        /// Remove an item from a registry (owner only).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RemoveItem(int registryId, int itemId)
        {
            _registryRepository.RemoveItem(registryId, itemId);
            TempData["Message"] = "Item removed from registry.";
            return RedirectToAction("Details", new { id = registryId });
        }

        /// <summary>
        /// Delete an entire registry.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            var registry = _registryRepository.GetById(id);
            if (registry == null) return HttpNotFound();

            _registryRepository.Remove(id);
            TempData["Message"] = $"Registry '{registry.Name}' has been deleted.";
            return RedirectToAction("Index");
        }
    }
}
