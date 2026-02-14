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
        // In-memory movie store (replace with DbContext for production use)
        private static readonly List<Movie> _movies = new List<Movie>
        {
            new Movie { Id = 1, Name = "Shrek!" },
            new Movie { Id = 2, Name = "The Godfather" },
            new Movie { Id = 3, Name = "Toy Story" }
        };

        private static readonly Random _random = new Random();

        // GET: Movies/Random
        public ActionResult Random()
        {
            var movie = _movies[_random.Next(_movies.Count)];
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

        // GET: Movies/Edit/5
        public ActionResult Edit(int id)
        {
            var movie = _movies.SingleOrDefault(m => m.Id == id);

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

            var movieInStore = _movies.SingleOrDefault(m => m.Id == movie.Id);

            if (movieInStore == null)
                return HttpNotFound();

            movieInStore.Name = movie.Name;

            return RedirectToAction("Index");
        }

        [Route("movies/released/{year:range(1888,2100)}/{month:regex(\\d{2}):range(1,12)}")]
        public ActionResult ByReleaseDate(int year, int month)
        {
            return Content(year + "/" + month);
        }

        // GET: Movies
        public ActionResult Index(int? pageIndex, string sortBy)
        {
            var page = pageIndex ?? 1;
            var sort = string.IsNullOrWhiteSpace(sortBy) ? "Name" : sortBy;

            IEnumerable<Movie> sorted;
            if (string.Equals(sort, "Name", StringComparison.OrdinalIgnoreCase))
                sorted = _movies.OrderBy(m => m.Name);
            else
                sorted = _movies.OrderBy(m => m.Id);

            return View(sorted.ToList());
        }
    }
}
