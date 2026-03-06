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

        // GET: Movies/Random
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

        // GET: Movies/Details/5
        public ActionResult Details(int id)
        {
            var movie = _movieRepository.GetById(id);

            if (movie == null)
                return HttpNotFound();

            return View(movie);
        }

        // GET: Movies/Create
        public ActionResult Create()
        {
            return View("Edit", new Movie());
        }

        // POST: Movies/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Exclude = "Id")] Movie movie)
        {
            if (!ModelState.IsValid)
                return View("Edit", movie);

            _movieRepository.Add(movie);

            return RedirectToAction("Index");
        }

        // GET: Movies/Edit/5
        public ActionResult Edit(int id)
        {
            var movie = _movieRepository.GetById(id);

            if (movie == null)
                return HttpNotFound();

            return View(movie);
        }

        // POST: Movies/Edit/5
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

        // POST: Movies/Delete/5
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

        // GET: Movies
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
