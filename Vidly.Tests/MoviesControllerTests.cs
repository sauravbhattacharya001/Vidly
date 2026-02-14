using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class MoviesControllerTests
    {
        /// <summary>
        /// Verifies that Index returns a ViewResult with a list of movies.
        /// The static movie store is pre-populated with 3 movies.
        /// </summary>
        [TestMethod]
        public void Index_ReturnsViewWithMovies()
        {
            var controller = new MoviesController();

            var result = controller.Index(null, null) as ViewResult;

            Assert.IsNotNull(result, "Index should return a ViewResult.");
            var movies = result.Model as List<Movie>;
            Assert.IsNotNull(movies, "Model should be a List<Movie>.");
            Assert.IsTrue(movies.Count >= 3,
                "Should contain at least the 3 pre-seeded movies.");
        }

        /// <summary>
        /// Verifies that Index sorts by Name by default.
        /// </summary>
        [TestMethod]
        public void Index_DefaultSort_ByName()
        {
            var controller = new MoviesController();

            var result = controller.Index(null, null) as ViewResult;
            var movies = result?.Model as List<Movie>;

            Assert.IsNotNull(movies);
            for (int i = 1; i < movies.Count; i++)
            {
                Assert.IsTrue(
                    string.Compare(movies[i - 1].Name, movies[i].Name, StringComparison.Ordinal) <= 0,
                    $"Movies should be sorted by name: '{movies[i - 1].Name}' should come before '{movies[i].Name}'.");
            }
        }

        /// <summary>
        /// Verifies that Random returns a ViewResult with a RandomMovieViewModel.
        /// </summary>
        [TestMethod]
        public void Random_ReturnsViewModelWithMovieAndCustomers()
        {
            var controller = new MoviesController();

            var result = controller.Random() as ViewResult;

            Assert.IsNotNull(result, "Random should return a ViewResult.");
            var vm = result.Model as RandomMovieViewModel;
            Assert.IsNotNull(vm, "Model should be a RandomMovieViewModel.");
            Assert.IsNotNull(vm.Movie, "ViewModel should contain a Movie.");
            Assert.IsNotNull(vm.Customers, "ViewModel should contain a Customers list.");
            Assert.AreEqual(2, vm.Customers.Count, "Should have 2 customers.");
        }

        /// <summary>
        /// Verifies that Edit returns HttpNotFound for a non-existent movie ID.
        /// </summary>
        [TestMethod]
        public void Edit_InvalidId_ReturnsHttpNotFound()
        {
            var controller = new MoviesController();

            var result = controller.Edit(99999);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult),
                "Edit with invalid ID should return HttpNotFoundResult.");
        }

        /// <summary>
        /// Verifies that Edit returns the correct movie for a valid ID.
        /// </summary>
        [TestMethod]
        public void Edit_ValidId_ReturnsMovieView()
        {
            var controller = new MoviesController();

            var result = controller.Edit(1) as ViewResult;

            Assert.IsNotNull(result, "Edit with valid ID should return a ViewResult.");
            var movie = result.Model as Movie;
            Assert.IsNotNull(movie, "Model should be a Movie.");
            Assert.AreEqual(1, movie.Id);
        }

        /// <summary>
        /// Verifies that ByReleaseDate filters correctly by year and month.
        /// </summary>
        [TestMethod]
        public void ByReleaseDate_FiltersCorrectly()
        {
            var controller = new MoviesController();

            // Movie ID 1 = "Shrek!" released 2001-05-18
            var result = controller.ByReleaseDate(2001, 5) as ViewResult;

            Assert.IsNotNull(result, "ByReleaseDate should return a ViewResult.");
            var movies = result.Model as List<Movie>;
            Assert.IsNotNull(movies);
            Assert.IsTrue(movies.All(m =>
                m.ReleaseDate.HasValue &&
                m.ReleaseDate.Value.Year == 2001 &&
                m.ReleaseDate.Value.Month == 5),
                "All returned movies should match the year/month filter.");
        }

        /// <summary>
        /// Verifies that ByReleaseDate returns empty list for a date with no movies.
        /// </summary>
        [TestMethod]
        public void ByReleaseDate_NoMatches_ReturnsEmptyList()
        {
            var controller = new MoviesController();

            var result = controller.ByReleaseDate(1900, 1) as ViewResult;

            Assert.IsNotNull(result);
            var movies = result.Model as List<Movie>;
            Assert.IsNotNull(movies);
            Assert.AreEqual(0, movies.Count,
                "Should return empty list when no movies match the date.");
        }

        /// <summary>
        /// Verifies that Create GET returns an Edit view with an empty movie.
        /// </summary>
        [TestMethod]
        public void Create_Get_ReturnsEditViewWithEmptyMovie()
        {
            var controller = new MoviesController();

            var result = controller.Create() as ViewResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Edit", result.ViewName,
                "Create should render the Edit view.");
            var movie = result.Model as Movie;
            Assert.IsNotNull(movie);
            Assert.AreEqual(0, movie.Id,
                "New movie should have default Id of 0.");
        }
    }
}
