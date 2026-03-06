using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class RentalsControllerTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryRentalRepository.Reset();
            InMemoryMovieRepository.Reset();
            InMemoryCustomerRepository.Reset();
        }

        private RentalsController CreateController()
        {
            return new RentalsController(
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository());
        }

        [TestMethod]
        public void Index_ReturnsViewWithRentalSearchViewModel()
        {
            var controller = CreateController();

            var result = controller.Index(null, null, null) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as RentalSearchViewModel;
            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.Rentals.Count >= 3);
            Assert.IsNotNull(vm.Stats);
        }

        [TestMethod]
        public void Index_FilterByStatus_FiltersCorrectly()
        {
            var controller = CreateController();

            var result = controller.Index(null, RentalStatus.Returned, null) as ViewResult;
            var vm = result?.Model as RentalSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.Rentals.All(r => r.Status == RentalStatus.Returned));
        }

        [TestMethod]
        public void Index_SearchByCustomer_ReturnsMatches()
        {
            var controller = CreateController();

            var result = controller.Index("John", null, null) as ViewResult;
            var vm = result?.Model as RentalSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.Rentals.Any(r => r.CustomerName.Contains("John")));
        }

        [TestMethod]
        public void Index_SortByCustomer_SortsCorrectly()
        {
            var controller = CreateController();

            var result = controller.Index(null, null, "Customer") as ViewResult;
            var vm = result?.Model as RentalSearchViewModel;

            Assert.IsNotNull(vm);
            for (int i = 1; i < vm.Rentals.Count; i++)
            {
                Assert.IsTrue(
                    string.Compare(
                        vm.Rentals[i - 1].CustomerName ?? "",
                        vm.Rentals[i].CustomerName ?? "",
                        StringComparison.Ordinal) <= 0,
                    "Rentals should be sorted by customer name.");
            }
        }

        [TestMethod]
        public void Details_ExistingId_ReturnsViewWithRental()
        {
            var controller = CreateController();

            var result = controller.Details(1) as ViewResult;

            Assert.IsNotNull(result);
            var rental = result.Model as Rental;
            Assert.IsNotNull(rental);
            Assert.AreEqual(1, rental.Id);
        }

        [TestMethod]
        public void Details_NonExistentId_ReturnsNotFound()
        {
            var controller = CreateController();

            var result = controller.Details(9999);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        [TestMethod]
        public void Checkout_Get_ReturnsViewWithCheckoutViewModel()
        {
            var controller = CreateController();

            var result = controller.Checkout() as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as RentalCheckoutViewModel;
            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.Rental);
            Assert.IsTrue(vm.Customers.Count > 0);
            Assert.IsNotNull(vm.AvailableMovies);
        }

        [TestMethod]
        public void Checkout_Get_ExcludesRentedOutMovies()
        {
            var controller = CreateController();

            var result = controller.Checkout() as ViewResult;
            var vm = result?.Model as RentalCheckoutViewModel;

            Assert.IsNotNull(vm);
            // Movie 1 (Shrek) is rented out — should not be in available list
            Assert.IsFalse(vm.AvailableMovies.Any(m => m.Id == 1),
                "Rented-out movie should not appear in available list.");
        }

        [TestMethod]
        public void Checkout_Post_NullViewModel_Returns400()
        {
            var controller = CreateController();

            var result = controller.Checkout(null) as HttpStatusCodeResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(400, result.StatusCode);
        }

        [TestMethod]
        public void Return_ExistingActiveRental_RedirectsToIndex()
        {
            var controller = CreateController();
            var repo = new InMemoryRentalRepository();

            // Add a rental we can return
            var rental = new Rental
            {
                CustomerId = 5,
                CustomerName = "Charlie Brown",
                MovieId = 80,
                MovieName = "Return Test",
                DailyRate = 3.99m
            };
            repo.Add(rental);
            var id = rental.Id;

            var result = controller.Return(id) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
        }

        [TestMethod]
        public void Return_NonExistentRental_ReturnsNotFound()
        {
            var controller = CreateController();

            var result = controller.Return(9999);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        [TestMethod]
        public void Delete_ExistingRental_RedirectsToIndex()
        {
            var controller = CreateController();
            var repo = new InMemoryRentalRepository();

            var rental = new Rental
            {
                CustomerId = 1,
                CustomerName = "Test",
                MovieId = 81,
                MovieName = "Delete Test"
            };
            repo.Add(rental);
            var id = rental.Id;

            var result = controller.Delete(id) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
        }

        [TestMethod]
        public void Delete_NonExistentRental_ReturnsNotFound()
        {
            var controller = CreateController();

            var result = controller.Delete(9999);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        [TestMethod]
        public void Overdue_ReturnsViewWithOverdueRentals()
        {
            var controller = CreateController();

            var result = controller.Overdue() as ViewResult;

            Assert.IsNotNull(result);
            var rentals = result.Model as IReadOnlyList<Rental>;
            Assert.IsNotNull(rentals);
            Assert.IsTrue(rentals.All(r => r.Status == RentalStatus.Overdue));
        }

        [TestMethod]
        public void Index_StatsMatchesRentalCounts()
        {
            var controller = CreateController();

            var result = controller.Index(null, null, null) as ViewResult;
            var vm = result?.Model as RentalSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.AreEqual(vm.Stats.TotalRentals,
                vm.Stats.ActiveRentals + vm.Stats.OverdueRentals + vm.Stats.ReturnedRentals);
        }

        [TestMethod]
        public void Checkout_Post_ExceedingMaxConcurrentRentals_ReturnsViewWithError()
        {
            // Bob Wilson (Id=3) is Basic tier — max 2 concurrent rentals.
            // Seed already has some rentals; reset and manually create 2 active rentals
            // then try a third.
            var rentalRepo = new InMemoryRentalRepository();
            var movieRepo = new InMemoryMovieRepository();
            var customerRepo = new InMemoryCustomerRepository();
            var controller = new RentalsController(rentalRepo, movieRepo, customerRepo);

            int customerId = 3; // Basic tier, max 2

            // Rent 2 movies for Bob (movies 1 and 2)
            var rental1 = new Rental
            {
                CustomerId = customerId,
                MovieId = 1,
                CustomerName = "Bob Wilson",
                MovieName = "Movie 1",
                RentalDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(7),
                DailyRate = 3.99m
            };
            rentalRepo.Checkout(rental1);

            var rental2 = new Rental
            {
                CustomerId = customerId,
                MovieId = 2,
                CustomerName = "Bob Wilson",
                MovieName = "Movie 2",
                RentalDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(7),
                DailyRate = 3.99m
            };
            rentalRepo.Checkout(rental2);

            // Now attempt a third rental — should be blocked
            var viewModel = new RentalCheckoutViewModel
            {
                Rental = new Rental
                {
                    CustomerId = customerId,
                    MovieId = 3,
                    RentalDate = DateTime.Today,
                    DueDate = DateTime.Today.AddDays(7),
                    DailyRate = 3.99m
                }
            };

            var result = controller.Checkout(viewModel) as ViewResult;

            Assert.IsNotNull(result, "Expected ViewResult when concurrent rental limit exceeded");
            Assert.IsFalse(controller.ModelState.IsValid);
            Assert.IsTrue(controller.ModelState["Rental.CustomerId"]?.Errors.Count > 0,
                "Expected model error on CustomerId about concurrent rental limit");
        }
    }
}
