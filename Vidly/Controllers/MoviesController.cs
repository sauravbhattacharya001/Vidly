using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Utilities;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class MoviesController : Controller
    {
        private readonly IMovieRepository _movieRepository;

        private static readonly SortHelper<Movie> _sorter = new SortHelper<Movie>(
            "name",
            new Dictionary<string, SortColumn<Movie>>
            {
                ["name"]        = new SortColumn<Movie>(m => m.Name ?? ""),
                ["rating"]      = new SortColumn<Movie>(m => m.Rating ?? 0, descending: true, thenBy: m => m.Name ?? ""),
                ["releasedate"] = new SortColumn<Movie>(m => m.ReleaseDate ?? DateTime.MinValue, descending: true, thenBy: m => m.Name ?? ""),
                ["genre"]       = new SortColumn<Movie>(m => m.Genre?.ToString() ?? "", thenBy: m => m.Name ?? ""),
                ["id"]          = new SortColumn<Movie>(m => m.Id),
            });

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// Uses the in-memory repository as the default implementation.
        /// </summary>
        public MoviesController()
            : this(new InMemoryMovieRepository())
        {
        }

        /// <summary>
        /// Constructor injection for testability and future DI container use.
        /// </summary>
        public MoviesController(IMovieRepository movieRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        /// <summary>
        /// GET: Movies/Random — Selects a random movie from the catalog and
        /// displays it alongside a sample customer list. Returns 404 when the
        /// catalog is empty.
        /// </summary>
        public ActionResult Random()
        {
            var movie = _movieRepository.GetRandom();

            if (movie == null)
                return HttpNotFound("No movies available.");

            var customers = new List<Customer>
            {
                new Customer { Name = "Customer 1" },
                new Customer { Name = "Customer 2" }
            };

            var viewModel = new RandomMovieViewModel
            {
                Movie = movie,
                Customers = customers
            };

            return View(viewModel);
        }

        /// <summary>
        /// GET: Movies/Details/{id} — Shows full details for a single movie.
        /// Returns 404 if the movie ID does not exist.
        /// </summary>
        /// <param name="id">The movie identifier.</param>
        public ActionResult Details(int id)
        {
            var movie = _movieRepository.GetById(id);

            if (movie == null)
                return HttpNotFound();

            return View(movie);
        }

        /// <summary>
        /// GET: Movies/Create — Renders the movie editor form for a new movie.
        /// </summary>
        public ActionResult Create()
        {
            return View("Edit", new Movie());
        }

        /// <summary>
        /// POST: Movies/Create — Validates and persists a new movie.
        /// Re-renders the edit form on validation failure.
        /// </summary>
        /// <param name="movie">The movie data from the form (Id excluded via Bind).</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Exclude = "Id")] Movie movie)
        {
            if (!ModelState.IsValid)
                return View("Edit", movie);

            _movieRepository.Add(movie);

            return RedirectToAction("Index");
        }

        /// <summary>
        /// GET: Movies/Edit/{id} — Renders the movie editor form for an existing movie.
        /// Returns 404 if the movie ID does not exist.
        /// </summary>
        /// <param name="id">The movie identifier.</param>
        public ActionResult Edit(int id)
        {
            var movie = _movieRepository.GetById(id);

            if (movie == null)
                return HttpNotFound();

            return View(movie);
        }

        /// <summary>
        /// POST: Movies/Edit/{id} — Updates an existing movie.
        /// Uses the route ID as authoritative to prevent over-posting attacks
        /// where an attacker modifies the hidden Id field.
        /// </summary>
        /// <param name="id">Route-based movie identifier (authoritative).</param>
        /// <param name="movie">Form-bound movie data (Id excluded via Bind).</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, [Bind(Include = "Name,ReleaseDate,Genre,Rating")] Movie movie)
        {
            // Security: Use route ID as authoritative — never trust form-submitted IDs.
            // The [Bind] attribute above excludes Id from model binding entirely,
            // preventing over-posting where an attacker modifies the hidden Id field.
            movie.Id = id;

            if (!ModelState.IsValid)
                return View(movie);

            try
            {
                _movieRepository.Update(movie);
            }
            catch (KeyNotFoundException)
            {
                return HttpNotFound();
            }

            return RedirectToAction("Index");
        }

        [Route("movies/released/{year:range(1888,2100)}/{month:regex(\\d{2}):range(1,12)}")]
        public ActionResult ByReleaseDate(int year, int month)
        {
            var matches = _movieRepository.GetByReleaseDate(year, month);

            ViewBag.Year = year;
            ViewBag.Month = month;

            return View(matches.ToList());
        }

        /// <summary>
        /// POST: Movies/Delete/{id} — Permanently removes a movie from the catalog.
        /// Returns 404 if the movie does not exist.
        /// </summary>
        /// <param name="id">The movie identifier.</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            try
            {
                _movieRepository.Remove(id);
            }
            catch (KeyNotFoundException)
            {
                return HttpNotFound();
            }

            return RedirectToAction("Index");
        }

        /// <summary>
        /// GET: Movies — Lists all movies with optional search, genre filter,
        /// minimum rating filter, and configurable sort order.
        /// </summary>
        /// <param name="query">Case-insensitive substring search on movie name.</param>
        /// <param name="genre">Optional genre filter.</param>
        /// <param name="minRating">Optional minimum star rating filter.</param>
        /// <param name="sortBy">Sort column key (name, rating, releasedate, genre, id).</param>
        public ActionResult Index(string query, Genre? genre, int? minRating, string sortBy)
        {
            var allMovies = _movieRepository.GetAll();
            var totalCount = allMovies.Count;

            // Use Search if any filter is active, otherwise return all
            IReadOnlyList<Movie> movies;
            if (!string.IsNullOrWhiteSpace(query) || genre.HasValue || minRating.HasValue)
            {
                movies = _movieRepository.Search(query, genre, minRating);
            }
            else
            {
                movies = allMovies;
            }

            // Apply sorting via declarative SortHelper (replaces switch block)
            var sort = _sorter.ResolveKey(sortBy);

            var viewModel = new MovieSearchViewModel
            {
                Movies = _sorter.Apply(movies, sort),
                Query = query,
                Genre = genre,
                MinRating = minRating,
                SortBy = sort,
                TotalCount = totalCount
            };

            return View(viewModel);
        }
    }
}
