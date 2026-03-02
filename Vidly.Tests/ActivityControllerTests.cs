using System;
using System.Collections.Generic;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class ActivityControllerTests
    {
        private InMemoryCustomerRepository _customerRepo;
        private InMemoryMovieRepository _movieRepo;
        private InMemoryRentalRepository _rentalRepo;
        private CustomerActivityService _service;

        [TestInitialize]
        public void Setup()
        {
            _customerRepo = new InMemoryCustomerRepository();
            _movieRepo = new InMemoryMovieRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _service = new CustomerActivityService(_customerRepo, _movieRepo, _rentalRepo);
        }

        private ActivityController CreateController()
        {
            return new ActivityController(_customerRepo, _service);
        }

        // ---- Index without customerId ----

        [TestMethod]
        public void Index_NoCustomerId_ReturnsView()
        {
            var controller = CreateController();

            var result = controller.Index(null) as ViewResult;

            Assert.IsNotNull(result, "Should return a ViewResult.");
            Assert.AreEqual("Index", result.ViewName);
        }

        [TestMethod]
        public void Index_NoCustomerId_ModelIsNull()
        {
            var controller = CreateController();

            var result = controller.Index(null) as ViewResult;

            Assert.IsNull(result.Model, "Model should be null when no customer is selected.");
        }

        [TestMethod]
        public void Index_NoCustomerId_PopulatesCustomerList()
        {
            var controller = CreateController();

            var result = controller.Index(null) as ViewResult;

            var customers = result.ViewBag.Customers as List<Customer>;
            Assert.IsNotNull(customers, "ViewBag.Customers should be populated.");
            Assert.IsTrue(customers.Count >= 5,
                "Should contain at least the 5 pre-seeded customers.");
        }

        [TestMethod]
        public void Index_NoCustomerId_CustomersSortedByName()
        {
            var controller = CreateController();

            var result = controller.Index(null) as ViewResult;
            var customers = result.ViewBag.Customers as List<Customer>;

            Assert.IsNotNull(customers);
            for (int i = 1; i < customers.Count; i++)
            {
                Assert.IsTrue(
                    string.Compare(customers[i - 1].Name, customers[i].Name,
                        StringComparison.Ordinal) <= 0,
                    $"Customers should be sorted by name: '{customers[i - 1].Name}' before '{customers[i].Name}'.");
            }
        }

        // ---- Index with valid customerId ----

        [TestMethod]
        public void Index_ValidCustomerId_ReturnsViewWithReport()
        {
            var controller = CreateController();
            var customers = _customerRepo.GetAll();
            int customerId = customers[0].Id;

            var result = controller.Index(customerId) as ViewResult;

            Assert.IsNotNull(result, "Should return a ViewResult.");
            Assert.AreEqual("Index", result.ViewName);
            var report = result.Model as CustomerActivityReport;
            Assert.IsNotNull(report, "Model should be a CustomerActivityReport.");
        }

        [TestMethod]
        public void Index_ValidCustomerId_ReportHasCustomerName()
        {
            var controller = CreateController();
            var customers = _customerRepo.GetAll();
            var customer = customers[0];

            var result = controller.Index(customer.Id) as ViewResult;
            var report = result.Model as CustomerActivityReport;

            Assert.IsNotNull(report);
            Assert.AreEqual(customer.Name, report.CustomerName);
        }

        [TestMethod]
        public void Index_ValidCustomerId_StillPopulatesCustomerList()
        {
            var controller = CreateController();
            var customers = _customerRepo.GetAll();

            var result = controller.Index(customers[0].Id) as ViewResult;

            var viewBagCustomers = result.ViewBag.Customers as List<Customer>;
            Assert.IsNotNull(viewBagCustomers,
                "ViewBag.Customers should be populated even when a customer is selected.");
            Assert.IsTrue(viewBagCustomers.Count >= 5);
        }

        // ---- Index with invalid customerId ----

        [TestMethod]
        public void Index_InvalidCustomerId_ReturnsViewWithNullModel()
        {
            var controller = CreateController();

            var result = controller.Index(99999) as ViewResult;

            Assert.IsNotNull(result);
            Assert.IsNull(result.Model,
                "Model should be null when customer is not found.");
        }

        [TestMethod]
        public void Index_InvalidCustomerId_SetsErrorTempData()
        {
            var controller = CreateController();

            var result = controller.Index(99999) as ViewResult;

            Assert.IsNotNull(result);
            // TempData should have error message
            Assert.IsTrue(
                controller.TempData.ContainsKey("Error"),
                "TempData should contain an error message for invalid customer.");
            Assert.AreEqual("Customer not found.", controller.TempData["Error"]);
        }

        // ---- Constructor validation ----

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new ActivityController(null, _service);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullService_Throws()
        {
            new ActivityController(_customerRepo, null);
        }

        // ---- Multiple customers exercise ----

        [TestMethod]
        public void Index_EachSeededCustomer_ReturnsReport()
        {
            var controller = CreateController();
            var customers = _customerRepo.GetAll();

            foreach (var customer in customers)
            {
                var result = controller.Index(customer.Id) as ViewResult;
                var report = result?.Model as CustomerActivityReport;

                Assert.IsNotNull(report,
                    $"Should return a report for customer '{customer.Name}' (ID={customer.Id}).");
                Assert.AreEqual(customer.Id, report.CustomerId,
                    $"Report should match the requested customer ID.");
            }
        }
    }
}
