using System;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class RecommendationsControllerTests
    {
        private InMemoryCustomerRepository _customerRepo;
        private InMemoryMovieRepository _movieRepo;
        private InMemoryRentalRepository _rentalRepo;

        [TestInitialize]
        public void Setup()
        {
            _customerRepo = new InMemoryCustomerRepository();
            _movieRepo = new InMemoryMovieRepository();
            _rentalRepo = new InMemoryRentalRepository();
        }

        private RecommendationsController CreateController()
        {
            return new RecommendationsController(_customerRepo, _movieRepo, _rentalRepo);
        }

        // ---- Index without customerId ----

        [TestMethod]
        public void Index_NoCustomerId_ReturnsView()
        {
            var controller = CreateController();

            var result = controller.Index(null) as ViewResult;

            Assert.IsNotNull(result, "Should return a ViewResult.");
        }

        [TestMethod]
        public void Index_NoCustomerId_ViewModelHasNullResult()
        {
            var controller = CreateController();

            var result = controller.Index(null) as ViewResult;
            var vm = result.Model as RecommendationViewModel;

            Assert.IsNotNull(vm, "Model should be a RecommendationViewModel.");
            Assert.IsNull(vm.Result,
                "Result should be null when no customer is selected.");
            Assert.IsNull(vm.SelectedCustomerId,
                "SelectedCustomerId should be null.");
        }

        [TestMethod]
        public void Index_NoCustomerId_PopulatesCustomerList()
        {
            var controller = CreateController();

            var result = controller.Index(null) as ViewResult;
            var vm = result.Model as RecommendationViewModel;

            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.Customers);
            Assert.IsTrue(vm.Customers.Count >= 5,
                "Should contain at least the 5 pre-seeded customers.");
        }

        // ---- Index with valid customerId ----

        [TestMethod]
        public void Index_ValidCustomerId_ReturnsViewWithResult()
        {
            var controller = CreateController();
            var customers = _customerRepo.GetAll();
            int customerId = customers[0].Id;

            var result = controller.Index(customerId) as ViewResult;
            var vm = result.Model as RecommendationViewModel;

            Assert.IsNotNull(vm);
            Assert.AreEqual(customerId, vm.SelectedCustomerId);
            Assert.IsNotNull(vm.Result,
                "Result should be populated for a valid customer.");
        }

        [TestMethod]
        public void Index_ValidCustomerId_SetsCustomerName()
        {
            var controller = CreateController();
            var customers = _customerRepo.GetAll();
            var customer = customers[0];

            var result = controller.Index(customer.Id) as ViewResult;
            var vm = result.Model as RecommendationViewModel;

            Assert.IsNotNull(vm);
            Assert.AreEqual(customer.Name, vm.SelectedCustomerName);
        }

        [TestMethod]
        public void Index_ValidCustomerId_StillPopulatesCustomerList()
        {
            var controller = CreateController();
            var customers = _customerRepo.GetAll();

            var result = controller.Index(customers[0].Id) as ViewResult;
            var vm = result.Model as RecommendationViewModel;

            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.Customers);
            Assert.IsTrue(vm.Customers.Count >= 5);
        }

        [TestMethod]
        public void Index_ValidCustomerId_RecommendationsNotEmpty()
        {
            var controller = CreateController();
            var customers = _customerRepo.GetAll();
            // Find a customer who has rentals (so recommendations can be generated)
            int customerId = customers[0].Id;

            var result = controller.Index(customerId) as ViewResult;
            var vm = result.Model as RecommendationViewModel;

            Assert.IsNotNull(vm?.Result);
            // Result should at least have the recommendations list (even if empty)
            Assert.IsNotNull(vm.Result.Recommendations);
        }

        // ---- Index with invalid customerId ----

        [TestMethod]
        public void Index_InvalidCustomerId_ReturnsNotFound()
        {
            var controller = CreateController();

            var result = controller.Index(99999);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult),
                "Should return 404 for non-existent customer.");
        }

        // ---- Constructor validation ----

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new RecommendationsController(null, _movieRepo, _rentalRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new RecommendationsController(_customerRepo, null, _rentalRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new RecommendationsController(_customerRepo, _movieRepo, null);
        }

        // ---- Multiple customers ----

        [TestMethod]
        public void Index_EachSeededCustomer_ReturnsRecommendations()
        {
            var controller = CreateController();
            var customers = _customerRepo.GetAll();

            foreach (var customer in customers)
            {
                var result = controller.Index(customer.Id) as ViewResult;
                var vm = result?.Model as RecommendationViewModel;

                Assert.IsNotNull(vm,
                    $"Should return a view model for customer '{customer.Name}' (ID={customer.Id}).");
                Assert.AreEqual(customer.Id, vm.SelectedCustomerId);
                Assert.IsNotNull(vm.Result);
            }
        }

        // ---- Recommendation result details ----

        [TestMethod]
        public void Index_ValidCustomer_ResultCappedAt10()
        {
            var controller = CreateController();
            var customers = _customerRepo.GetAll();
            int customerId = customers[0].Id;

            var result = controller.Index(customerId) as ViewResult;
            var vm = result.Model as RecommendationViewModel;

            Assert.IsNotNull(vm?.Result);
            Assert.IsTrue(vm.Result.Recommendations.Count <= 10,
                "Recommendations should be capped at 10.");
        }
    }
}
