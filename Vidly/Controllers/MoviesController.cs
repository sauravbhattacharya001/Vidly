using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class MoviesController : Controller
    {
        private readonly IMovieRepository _movieRepository;

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
        public ActionResult Edit(int id, Movie movie)
        {
            if (movie.Id != id)
                return new HttpStatusCodeResult(400, "Movie ID mismatch.");

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
        public ActionResult Index(int? pageIndex, string sortBy)
        {
            var sort = string.IsNullOrWhiteSpace(sortBy) ? "Name" : sortBy;
            var movies = _movieRepository.GetAll();

            IEnumerable<Movie> sorted;
            if (string.Equals(sort, "Name", StringComparison.OrdinalIgnoreCase))
                sorted = movies.OrderBy(m => m.Name);
            else
                sorted = movies.OrderBy(m => m.Id);

            return View(sorted.ToList());
        }
    }
}
