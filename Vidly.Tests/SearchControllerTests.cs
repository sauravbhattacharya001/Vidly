using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Tests
{
    [TestClass]
    public class SearchControllerTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
            InMemoryCustomerRepository.Reset();
            InMemoryRentalRepository.Reset();
        }

        [TestMethod]
        public void Index_WithQuery_ReturnsViewWithResults()
        {
            var controller = new SearchController();

            var result = controller.Index("Die") as ViewResult;

            Assert.IsNotNull(result);
            var model = result.Model as GlobalSearchResults;
            Assert.IsNotNull(model);
            Assert.AreEqual("Die", model.Query);
            Assert.IsNotNull(model.Movies);
        }

        [TestMethod]
        public void Index_NullQuery_ReturnsEmptyResults()
        {
            var controller = new SearchController();

            var result = controller.Index(null) as ViewResult;

            Assert.IsNotNull(result);
            var model = result.Model as GlobalSearchResults;
            Assert.IsNotNull(model);
            Assert.IsNull(model.Query);
            // Movies/Customers/Rentals should be null (no search performed)
            Assert.IsNull(model.Movies);
        }

        [TestMethod]
        public void Index_EmptyQuery_ReturnsEmptyResults()
        {
            var controller = new SearchController();

            var result = controller.Index("   ") as ViewResult;

            Assert.IsNotNull(result);
            var model = result.Model as GlobalSearchResults;
            Assert.IsNotNull(model);
            Assert.IsNull(model.Movies);
        }

        [TestMethod]
        public void Index_MatchingQuery_FindsMovies()
        {
            var controller = new SearchController();

            var result = controller.Index("Shawshank") as ViewResult;
            var model = result?.Model as GlobalSearchResults;

            Assert.IsNotNull(model);
            Assert.IsNotNull(model.Movies);
            Assert.IsTrue(model.Movies.Count >= 1,
                "Should find at least one movie matching 'Shawshank'.");
        }

        [TestMethod]
        public void Index_NonMatchingQuery_ReturnsNoMovies()
        {
            var controller = new SearchController();

            var result = controller.Index("zzzznonexistent") as ViewResult;
            var model = result?.Model as GlobalSearchResults;

            Assert.IsNotNull(model);
            Assert.IsNotNull(model.Movies);
            Assert.AreEqual(0, model.Movies.Count,
                "Should find no movies for a nonsense query.");
        }

        [TestMethod]
        public void Constructor_NullMovieRepo_Throws()
        {
            Assert.ThrowsException<System.ArgumentNullException>(() =>
                new SearchController(null, new InMemoryCustomerRepository(), new InMemoryRentalRepository()));
        }

        [TestMethod]
        public void Constructor_NullCustomerRepo_Throws()
        {
            Assert.ThrowsException<System.ArgumentNullException>(() =>
                new SearchController(new InMemoryMovieRepository(), null, new InMemoryRentalRepository()));
        }

        [TestMethod]
        public void Constructor_NullRentalRepo_Throws()
        {
            Assert.ThrowsException<System.ArgumentNullException>(() =>
                new SearchController(new InMemoryMovieRepository(), new InMemoryCustomerRepository(), null));
        }
    }
}
