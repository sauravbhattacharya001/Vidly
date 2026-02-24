using System;
using System.Collections.Generic;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    public class CollectionsController : Controller
    {
        private readonly ICollectionRepository _collectionRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly CollectionService _collectionService;

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public CollectionsController()
            : this(
                new InMemoryCollectionRepository(),
                new InMemoryMovieRepository())
        {
        }

        /// <summary>
        /// Constructor injection for testability and future DI container use.
        /// </summary>
        public CollectionsController(
            ICollectionRepository collectionRepository,
            IMovieRepository movieRepository)
        {
            _collectionRepository = collectionRepository
                ?? throw new ArgumentNullException(nameof(collectionRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _collectionService = new CollectionService(collectionRepository, movieRepository);
        }

        // GET: Collections
        public ActionResult Index(string query)
        {
            var collections = string.IsNullOrWhiteSpace(query)
                ? _collectionRepository.GetPublished()
                : _collectionRepository.Search(query);

            ViewBag.Query = query;
            return View(collections);
        }

        // GET: Collections/Details/5
        public ActionResult Details(int id)
        {
            var summary = _collectionService.GetCollectionSummary(id);
            if (summary == null)
                return HttpNotFound("Collection not found.");

            return View(summary);
        }

        // GET: Collections/Create
        public ActionResult Create()
        {
            return View(new MovieCollection());
        }

        // POST: Collections/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(MovieCollection collection)
        {
            if (!ModelState.IsValid)
                return View(collection);

            try
            {
                collection.CreatedAt = DateTime.Now;
                collection.UpdatedAt = DateTime.Now;
                _collectionRepository.Add(collection);
                return RedirectToAction("Index");
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("Name", ex.Message);
                return View(collection);
            }
        }

        // GET: Collections/Edit/5
        public ActionResult Edit(int id)
        {
            var collection = _collectionRepository.GetById(id);
            if (collection == null)
                return HttpNotFound("Collection not found.");

            return View(collection);
        }

        // POST: Collections/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(MovieCollection collection)
        {
            if (!ModelState.IsValid)
                return View(collection);

            try
            {
                _collectionRepository.Update(collection);
                return RedirectToAction("Details", new { id = collection.Id });
            }
            catch (KeyNotFoundException)
            {
                return HttpNotFound("Collection not found.");
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("Name", ex.Message);
                return View(collection);
            }
        }

        // POST: Collections/AddMovie/5?movieId=2
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddMovie(int id, int movieId, string note = null)
        {
            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                return HttpNotFound("Movie not found.");

            var success = _collectionRepository.AddMovie(id, movieId, note);
            if (!success)
            {
                TempData["Error"] = "Could not add movie. Collection may not exist or movie is already in the collection.";
            }
            else
            {
                TempData["Message"] = $"'{movie.Name}' added to collection.";
            }

            return RedirectToAction("Details", new { id });
        }

        // POST: Collections/RemoveMovie/5?movieId=2
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RemoveMovie(int id, int movieId)
        {
            var success = _collectionRepository.RemoveMovie(id, movieId);
            if (!success)
            {
                TempData["Error"] = "Could not remove movie. Collection may not exist or movie is not in the collection.";
            }
            else
            {
                TempData["Message"] = "Movie removed from collection.";
            }

            return RedirectToAction("Details", new { id });
        }

        // POST: Collections/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            try
            {
                _collectionRepository.Remove(id);
                return RedirectToAction("Index");
            }
            catch (KeyNotFoundException)
            {
                return HttpNotFound("Collection not found.");
            }
        }
    }
}
