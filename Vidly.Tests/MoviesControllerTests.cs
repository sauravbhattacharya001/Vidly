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
        /// Verifies that Index returns a ViewResult with a MovieSearchViewModel.
        /// </summary>
        [TestMethod]
        public void Index_ReturnsViewWithMovieSearchViewModel()
        {
            var controller = new MoviesController();

            var result = controller.Index(null, null, null, null) as ViewResult;

            Assert.IsNotNull(result, "Index should return a ViewResult.");
            var vm = result.Model as MovieSearchViewModel;
            Assert.IsNotNull(vm, "Model should be a MovieSearchViewModel.");
            Assert.IsTrue(vm.Movies.Count >= 3,
                "Should contain at least the 3 pre-seeded movies.");
        }

        /// <summary>
        /// Verifies that Index sorts by Name by default.
        /// </summary>
        [TestMethod]
        public void Index_DefaultSort_ByName()
        {
            var controller = new MoviesController();

            var result = controller.Index(null, null, null, null) as ViewResult;
            var vm = result?.Model as MovieSearchViewModel;

            Assert.IsNotNull(vm);
            for (int i = 1; i < vm.Movies.Count; i++)
            {
                Assert.IsTrue(
                    string.Compare(vm.Movies[i - 1].Name, vm.Movies[i].Name, StringComparison.Ordinal) <= 0,
                    $"Movies should be sorted by name: '{vm.Movies[i - 1].Name}' should come before '{vm.Movies[i].Name}'.");
            }
        }

        /// <summary>
        /// Verifies that Index can sort by rating (descending).
        /// </summary>
        [TestMethod]
        public void Index_SortByRating_DescendingOrder()
        {
            var controller = new MoviesController();

            var result = controller.Index(null, null, null, "Rating") as ViewResult;
            var vm = result?.Model as MovieSearchViewModel;

            Assert.IsNotNull(vm);
            for (int i = 1; i < vm.Movies.Count; i++)
            {
                var prev = vm.Movies[i - 1].Rating ?? 0;
                var curr = vm.Movies[i].Rating ?? 0;
                Assert.IsTrue(prev >= curr,
                    $"Movies should be sorted by rating descending: {prev} should be >= {curr}.");
            }
        }

        /// <summary>
        /// Verifies that Index filters by genre.
        /// </summary>
        [TestMethod]
        public void Index_FilterByGenre_ReturnsMatchingMovies()
        {
            var controller = new MoviesController();

            var result = controller.Index(null, Genre.Animation, null, null) as ViewResult;
            var vm = result?.Model as MovieSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.Movies.Count >= 2,
                "Should find at least 2 Animation movies (Shrek! and Toy Story).");
            Assert.IsTrue(vm.Movies.All(m => m.Genre == Genre.Animation),
                "All returned movies should be Animation genre.");
        }

        /// <summary>
        /// Verifies that Index filters by minimum rating.
        /// </summary>
        [TestMethod]
        public void Index_FilterByMinRating_ReturnsHighRatedMovies()
        {
            var controller = new MoviesController();

            var result = controller.Index(null, null, 5, null) as ViewResult;
            var vm = result?.Model as MovieSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.Movies.Count >= 2,
                "Should find at least 2 five-star movies (The Godfather and Toy Story).");
            Assert.IsTrue(vm.Movies.All(m => m.Rating.HasValue && m.Rating.Value >= 5),
                "All returned movies should have rating >= 5.");
        }

        /// <summary>
        /// Verifies that Index searches by name substring.
        /// </summary>
        [TestMethod]
        public void Index_SearchByName_ReturnsMatchingMovies()
        {
            var controller = new MoviesController();

            var result = controller.Index("shrek", null, null, null) as ViewResult;
            var vm = result?.Model as MovieSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.AreEqual(1, vm.Movies.Count, "Should find exactly 1 movie matching 'shrek'.");
            Assert.AreEqual("Shrek!", vm.Movies[0].Name);
        }

        /// <summary>
        /// Verifies that Index shows TotalCount reflecting unfiltered count.
        /// </summary>
        [TestMethod]
        public void Index_WithFilter_ShowsTotalCount()
        {
            var controller = new MoviesController();

            var result = controller.Index("shrek", null, null, null) as ViewResult;
            var vm = result?.Model as MovieSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.TotalCount >= 3,
                "TotalCount should reflect all movies, not just filtered.");
            Assert.IsTrue(vm.Movies.Count < vm.TotalCount,
                "Filtered count should be less than total.");
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
        /// Verifies that Details returns the correct movie for a valid ID.
        /// </summary>
        [TestMethod]
        public void Details_ValidId_ReturnsMovieView()
        {
            var controller = new MoviesController();

            var result = controller.Details(1) as ViewResult;

            Assert.IsNotNull(result, "Details with valid ID should return a ViewResult.");
            var movie = result.Model as Movie;
            Assert.IsNotNull(movie, "Model should be a Movie.");
            Assert.AreEqual(1, movie.Id);
            Assert.AreEqual("Shrek!", movie.Name);
        }

        /// <summary>
        /// Verifies that Details returns HttpNotFound for a non-existent movie ID.
        /// </summary>
        [TestMethod]
        public void Details_InvalidId_ReturnsHttpNotFound()
        {
            var controller = new MoviesController();

            var result = controller.Details(99999);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult),
                "Details with invalid ID should return HttpNotFoundResult.");
        }

        /// <summary>
        /// Verifies that Details returns movie with genre and rating data.
        /// </summary>
        [TestMethod]
        public void Details_ReturnsMovieWithGenreAndRating()
        {
            var controller = new MoviesController();

            var result = controller.Details(1) as ViewResult;
            var movie = result?.Model as Movie;

            Assert.IsNotNull(movie);
            Assert.AreEqual(Genre.Animation, movie.Genre,
                "Shrek! should have Animation genre.");
            Assert.AreEqual(4, movie.Rating,
                "Shrek! should have a rating of 4.");
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

        /// <summary>
        /// Verifies that combined filters work together.
        /// </summary>
        [TestMethod]
        public void Index_CombinedFilters_NarrowsResults()
        {
            var controller = new MoviesController();

            // Search for Animation movies with rating >= 5
            var result = controller.Index(null, Genre.Animation, 5, null) as ViewResult;
            var vm = result?.Model as MovieSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.Movies.Count >= 1,
                "Should find at least Toy Story (Animation, 5 stars).");
            Assert.IsTrue(vm.Movies.All(m =>
                m.Genre == Genre.Animation && m.Rating >= 5),
                "All movies should match both genre and rating filters.");
        }

        /// <summary>
        /// Verifies that sort by genre works.
        /// </summary>
        [TestMethod]
        public void Index_SortByGenre_GroupsCorrectly()
        {
            var controller = new MoviesController();

            var result = controller.Index(null, null, null, "Genre") as ViewResult;
            var vm = result?.Model as MovieSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.Movies.Count >= 3);
        }

        /// <summary>
        /// Verifies Index preserves filter values in ViewModel.
        /// </summary>
        [TestMethod]
        public void Index_PreservesFilterValues()
        {
            var controller = new MoviesController();

            var result = controller.Index("test", Genre.Drama, 3, "Rating") as ViewResult;
            var vm = result?.Model as MovieSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.AreEqual("test", vm.Query);
            Assert.AreEqual(Genre.Drama, vm.Genre);
            Assert.AreEqual(3, vm.MinRating);
            Assert.AreEqual("Rating", vm.SortBy);
        }
    }
}
