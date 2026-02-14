using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class MoviesController : Controller
    {
        // In-memory movie store (replace with DbContext for production use).
        // Guarded by _moviesLock for thread-safe concurrent access. (fixes #4)
        private static readonly List<Movie> _movies = new List<Movie>
        {
            new Movie { Id = 1, Name = "Shrek!", ReleaseDate = new DateTime(2001, 5, 18) },
            new Movie { Id = 2, Name = "The Godfather", ReleaseDate = new DateTime(1972, 3, 24) },
            new Movie { Id = 3, Name = "Toy Story", ReleaseDate = new DateTime(1995, 11, 22) }
        };

        private static readonly object _moviesLock = new object();
        private static readonly Random _random = new Random();

        // GET: Movies/Random
        public ActionResult Random()
        {
            Movie movie;
            lock (_moviesLock)
            {
                if (!_movies.Any())
                    return HttpNotFound("No movies available.");

                movie = _movies[_random.Next(_movies.Count)];
            }

            var customers = new List<Customer>
            {
                new Customer {Name = "Customer 1"},
                new Customer {Name = "Customer 2"}
            };

            var viewModel = new RandomMovieViewModel
            {
                Movie = movie,
                Customers = customers
            };

            return View(viewModel);
        }

        // GET: Movies/Create
        public ActionResult Create()
        {
            return View("Edit", new Movie());
        }

        // POST: Movies/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(Movie movie)
        {
            if (!ModelState.IsValid)
                return View("Edit", movie);

            lock (_moviesLock)
            {
                movie.Id = _movies.Any() ? _movies.Max(m => m.Id) + 1 : 1;
                _movies.Add(movie);
            }

            return RedirectToAction("Index");
        }

        // GET: Movies/Edit/5
        public ActionResult Edit(int id)
        {
            Movie movie;
            lock (_moviesLock)
            {
                movie = _movies.SingleOrDefault(m => m.Id == id);
            }

            if (movie == null)
                return HttpNotFound();

            return View(movie);
        }

        // POST: Movies/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(Movie movie)
        {
            if (!ModelState.IsValid)
                return View(movie);

            lock (_moviesLock)
            {
                var movieInStore = _movies.SingleOrDefault(m => m.Id == movie.Id);

                if (movieInStore == null)
                    return HttpNotFound();

                movieInStore.Name = movie.Name;
                movieInStore.ReleaseDate = movie.ReleaseDate;
            }

            return RedirectToAction("Index");
        }

        [Route("movies/released/{year:range(1888,2100)}/{month:regex(\\d{2}):range(1,12)}")]
        public ActionResult ByReleaseDate(int year, int month)
        {
            List<Movie> matches;
            lock (_moviesLock)
            {
                matches = _movies
                    .Where(m => m.ReleaseDate.HasValue
                             && m.ReleaseDate.Value.Year == year
                             && m.ReleaseDate.Value.Month == month)
                    .OrderBy(m => m.ReleaseDate)
                    .ToList();
            }

            ViewBag.Year = year;
            ViewBag.Month = month;

            return View(matches);
        }

        // POST: Movies/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            lock (_moviesLock)
            {
                var movie = _movies.SingleOrDefault(m => m.Id == id);

                if (movie == null)
                    return HttpNotFound();

                _movies.Remove(movie);
            }

            return RedirectToAction("Index");
        }

        // GET: Movies
        public ActionResult Index(int? pageIndex, string sortBy)
        {
            var page = pageIndex ?? 1;
            var sort = string.IsNullOrWhiteSpace(sortBy) ? "Name" : sortBy;

            List<Movie> snapshot;
            lock (_moviesLock)
            {
                snapshot = _movies.ToList(); // snapshot under lock
            }

            IEnumerable<Movie> sorted;
            if (string.Equals(sort, "Name", StringComparison.OrdinalIgnoreCase))
                sorted = snapshot.OrderBy(m => m.Name);
            else
                sorted = snapshot.OrderBy(m => m.Id);

            return View(sorted.ToList());
        }
    }
}
