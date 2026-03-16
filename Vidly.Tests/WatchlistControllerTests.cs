using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class WatchlistControllerTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryWatchlistRepository.Reset();
            InMemoryCustomerRepository.Reset();
            InMemoryMovieRepository.Reset();
        }

        [TestMethod]
        public void Index_NoCustomer_ReturnsViewWithCustomerList()
        {
            var controller = new WatchlistController();

            var result = controller.Index(null, null, null) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as WatchlistViewModel;
            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.Customers);
            Assert.IsTrue(vm.Customers.Count > 0);
            Assert.IsNull(vm.SelectedCustomerId);
        }

        [TestMethod]
        public void Index_WithValidCustomer_ReturnsCustomerWatchlist()
        {
            var controller = new WatchlistController();

            var result = controller.Index(1, null, null) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as WatchlistViewModel;
            Assert.IsNotNull(vm);
            Assert.AreEqual(1, vm.SelectedCustomerId);
            Assert.IsNotNull(vm.SelectedCustomerName);
            Assert.IsNotNull(vm.Items);
        }

        [TestMethod]
        public void Index_WithInvalidCustomer_ReturnsNotFound()
        {
            var controller = new WatchlistController();

            var result = controller.Index(9999, null, null);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        [TestMethod]
        public void Index_WithMessage_SetsStatusMessage()
        {
            var controller = new WatchlistController();

            var result = controller.Index(null, "Added!", null) as ViewResult;
            var vm = result?.Model as WatchlistViewModel;

            Assert.IsNotNull(vm);
            Assert.AreEqual("Added!", vm.StatusMessage);
            Assert.IsFalse(vm.IsError);
        }

        [TestMethod]
        public void Add_Get_NoCustomer_ReturnsAllMovies()
        {
            var controller = new WatchlistController();

            var result = controller.Add(null, null) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as WatchlistAddViewModel;
            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.AvailableMovies);
            Assert.IsTrue(vm.AvailableMovies.Count > 0);
        }

        [TestMethod]
        public void Add_Get_WithCustomer_FiltersAlreadyWatchlisted()
        {
            var controller = new WatchlistController();

            // Add a movie to customer 1's watchlist first
            var item = new WatchlistItem
            {
                CustomerId = 1,
                MovieId = 1,
                CustomerName = "Test",
                MovieName = "Test",
                Priority = WatchlistPriority.Normal
            };
            new InMemoryWatchlistRepository().Add(item);

            var result = controller.Add(1, null) as ViewResult;
            var vm = result?.Model as WatchlistAddViewModel;

            Assert.IsNotNull(vm);
            // Movie 1 should be filtered out
            foreach (var movie in vm.AvailableMovies)
            {
                Assert.AreNotEqual(1, movie.Id,
                    "Movie already on watchlist should be filtered out.");
            }
        }

        [TestMethod]
        public void Add_Post_ValidItem_RedirectsToIndex()
        {
            var controller = new WatchlistController();

            var item = new WatchlistItem
            {
                CustomerId = 1,
                MovieId = 1,
                Priority = WatchlistPriority.Normal
            };

            var result = controller.Add(item) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.AreEqual(1, result.RouteValues["customerId"]);
        }

        [TestMethod]
        public void Add_Post_InvalidCustomerId_ReturnsView()
        {
            var controller = new WatchlistController();

            var item = new WatchlistItem
            {
                CustomerId = 0,
                MovieId = 1,
                Priority = WatchlistPriority.Normal
            };

            var result = controller.Add(item) as ViewResult;

            Assert.IsNotNull(result, "Should return view when validation fails.");
        }

        [TestMethod]
        public void Add_Post_InvalidMovieId_ReturnsView()
        {
            var controller = new WatchlistController();

            var item = new WatchlistItem
            {
                CustomerId = 1,
                MovieId = 0,
                Priority = WatchlistPriority.Normal
            };

            var result = controller.Add(item) as ViewResult;

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void Remove_ExistingItem_RedirectsWithSuccess()
        {
            var repo = new InMemoryWatchlistRepository();
            var item = new WatchlistItem
            {
                CustomerId = 1,
                MovieId = 1,
                CustomerName = "Test",
                MovieName = "Test Movie",
                Priority = WatchlistPriority.Normal
            };
            repo.Add(item);
            var addedId = item.Id;

            var controller = new WatchlistController();
            var result = controller.Remove(addedId, 1) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.AreEqual(1, result.RouteValues["customerId"]);
        }

        [TestMethod]
        public void Remove_NonexistentItem_RedirectsWithError()
        {
            var controller = new WatchlistController();

            var result = controller.Remove(9999, 1) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(true, result.RouteValues["error"]);
        }

        [TestMethod]
        public void Clear_WithItems_RedirectsWithCount()
        {
            var repo = new InMemoryWatchlistRepository();
            repo.Add(new WatchlistItem
            {
                CustomerId = 1, MovieId = 1,
                CustomerName = "T", MovieName = "M",
                Priority = WatchlistPriority.Normal
            });
            repo.Add(new WatchlistItem
            {
                CustomerId = 1, MovieId = 2,
                CustomerName = "T", MovieName = "M2",
                Priority = WatchlistPriority.Normal
            });

            var controller = new WatchlistController();
            var result = controller.Clear(1) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.AreEqual(1, result.RouteValues["customerId"]);
            Assert.IsTrue(result.RouteValues["message"].ToString().Contains("2"));
        }

        [TestMethod]
        public void Clear_EmptyWatchlist_RedirectsWithEmptyMessage()
        {
            var controller = new WatchlistController();

            var result = controller.Clear(1) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.IsTrue(result.RouteValues["message"].ToString().Contains("empty"));
        }

        [TestMethod]
        public void UpdatePriority_ValidItem_Updates()
        {
            var repo = new InMemoryWatchlistRepository();
            repo.Add(new WatchlistItem
            {
                CustomerId = 1, MovieId = 1,
                CustomerName = "T", MovieName = "M",
                Priority = WatchlistPriority.Normal
            });
            var addedId = repo.GetByCustomer(1)[0].Id;

            var controller = new WatchlistController();
            var result = controller.UpdatePriority(addedId, WatchlistPriority.MustWatch, 1) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);

            // Verify priority was updated
            var updated = repo.GetById(addedId);
            Assert.AreEqual(WatchlistPriority.MustWatch, updated.Priority);
        }

        [TestMethod]
        public void UpdatePriority_NonexistentItem_ReturnsNotFound()
        {
            var controller = new WatchlistController();

            var result = controller.UpdatePriority(9999, WatchlistPriority.High, 1);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        [TestMethod]
        public void Constructor_NullCustomerRepo_Throws()
        {
            Assert.ThrowsException<System.ArgumentNullException>(() =>
                new WatchlistController(null, new InMemoryMovieRepository(), new InMemoryWatchlistRepository()));
        }

        [TestMethod]
        public void Constructor_NullMovieRepo_Throws()
        {
            Assert.ThrowsException<System.ArgumentNullException>(() =>
                new WatchlistController(new InMemoryCustomerRepository(), null, new InMemoryWatchlistRepository()));
        }

        [TestMethod]
        public void Constructor_NullWatchlistRepo_Throws()
        {
            Assert.ThrowsException<System.ArgumentNullException>(() =>
                new WatchlistController(new InMemoryCustomerRepository(), new InMemoryMovieRepository(), null));
        }
    }
}
