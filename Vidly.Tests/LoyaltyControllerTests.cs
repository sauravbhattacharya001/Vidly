using System;
using System.Linq;
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
    public class LoyaltyControllerTests
    {
        private InMemoryCustomerRepository _customerRepo;
        private InMemoryRentalRepository _rentalRepo;
        private InMemoryMovieRepository _movieRepo;
        private LoyaltyController _controller;

        [TestInitialize]
        public void Setup()
        {
            InMemoryCustomerRepository.Reset();
            InMemoryRentalRepository.Reset();
            InMemoryMovieRepository.Reset();

            _customerRepo = new InMemoryCustomerRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _movieRepo = new InMemoryMovieRepository();
            _controller = new LoyaltyController(_customerRepo, _rentalRepo, _movieRepo);
        }

        // ── Constructor tests ───────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new LoyaltyController(null, _rentalRepo, _movieRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new LoyaltyController(_customerRepo, null, _movieRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new LoyaltyController(_customerRepo, _rentalRepo, null);
        }

        [TestMethod]
        public void DefaultConstructor_DoesNotThrow()
        {
            var controller = new LoyaltyController();
            Assert.IsNotNull(controller);
        }

        // ── Index (no customer selected) ────────────────────────────

        [TestMethod]
        public void Index_NoCustomerId_ReturnsViewWithAllCustomers()
        {
            var result = _controller.Index(null) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as LoyaltyViewModel;
            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.AllCustomers);
            Assert.IsTrue(vm.AllCustomers.Any(),
                "AllCustomers should contain seeded customers.");
            Assert.IsNull(vm.SelectedCustomer,
                "No customer should be selected.");
            Assert.IsNotNull(vm.StatusMessage,
                "Should display a prompt message.");
        }

        [TestMethod]
        public void Index_NoCustomerId_IncludesLeaderboard()
        {
            var result = _controller.Index(null) as ViewResult;
            var vm = result?.Model as LoyaltyViewModel;

            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.Leaderboard);
        }

        // ── Index (invalid customer) ────────────────────────────────

        [TestMethod]
        public void Index_InvalidCustomerId_ReturnsNotFoundMessage()
        {
            var result = _controller.Index(99999) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as LoyaltyViewModel;
            Assert.IsNotNull(vm);
            Assert.IsNull(vm.SelectedCustomer);
            Assert.AreEqual("Customer not found.", vm.StatusMessage);
            Assert.IsNotNull(vm.AllCustomers);
        }

        // ── Index (valid customer) ──────────────────────────────────

        [TestMethod]
        public void Index_ValidCustomer_ReturnsSummaryAndHistory()
        {
            var customer = _customerRepo.GetAll().First();

            var result = _controller.Index(customer.Id) as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as LoyaltyViewModel;
            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.SelectedCustomer);
            Assert.AreEqual(customer.Id, vm.SelectedCustomer.Id);
            Assert.IsNotNull(vm.Summary);
            Assert.IsNotNull(vm.Transactions);
            Assert.IsNotNull(vm.Leaderboard);
        }

        [TestMethod]
        public void Index_ValidCustomer_CalculatesNextRewardProgress()
        {
            var customer = _customerRepo.GetAll().First();

            var result = _controller.Index(customer.Id) as ViewResult;
            var vm = result?.Model as LoyaltyViewModel;

            Assert.IsNotNull(vm);
            // Progress should be between 0 and 100
            Assert.IsTrue(vm.NextRewardProgress >= 0 && vm.NextRewardProgress <= 100,
                $"Progress should be 0-100 but was {vm.NextRewardProgress}.");
        }

        // ── Redeem ──────────────────────────────────────────────────

        [TestMethod]
        public void Redeem_InsufficientPoints_SetsRedemptionError()
        {
            var customer = _customerRepo.GetAll().First();
            // Attempt to redeem without any points earned
            _controller.TempData = new TempDataDictionary();

            var result = _controller.Redeem(customer.Id, (int)RewardType.FreeRental) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.AreEqual(customer.Id, result.RouteValues["customerId"]);
            Assert.IsNotNull(_controller.TempData["RedemptionError"],
                "Should set a redemption error for insufficient points.");
        }

        [TestMethod]
        public void Redeem_InvalidCustomer_SetsRedemptionError()
        {
            _controller.TempData = new TempDataDictionary();

            var result = _controller.Redeem(99999, (int)RewardType.FreeRental) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.IsNotNull(_controller.TempData["RedemptionError"]);
        }

        [TestMethod]
        public void Redeem_RedirectsToIndexWithCustomerId()
        {
            var customer = _customerRepo.GetAll().First();
            _controller.TempData = new TempDataDictionary();

            var result = _controller.Redeem(customer.Id, (int)RewardType.HalfOffRental) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.AreEqual(customer.Id, result.RouteValues["customerId"]);
        }

        // ── EarnPoints ──────────────────────────────────────────────

        [TestMethod]
        public void EarnPoints_InvalidRental_SetsEarnError()
        {
            var customer = _customerRepo.GetAll().First();
            _controller.TempData = new TempDataDictionary();

            var result = _controller.EarnPoints(customer.Id, 99999) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.IsNotNull(_controller.TempData["EarnError"],
                "Should set an error for non-existent rental.");
        }

        [TestMethod]
        public void EarnPoints_ActiveRental_SetsEarnError()
        {
            // Active rentals should not earn points
            var rental = _rentalRepo.GetAll()
                .FirstOrDefault(r => r.Status == RentalStatus.Active);
            if (rental == null)
                return; // Skip if no active rentals in seed data

            _controller.TempData = new TempDataDictionary();

            var result = _controller.EarnPoints(rental.CustomerId, rental.Id) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.IsNotNull(_controller.TempData["EarnError"],
                "Active rentals should not earn points.");
        }

        [TestMethod]
        public void EarnPoints_ReturnedRental_SetsEarnSuccess()
        {
            var rental = _rentalRepo.GetAll()
                .FirstOrDefault(r => r.Status == RentalStatus.Returned);
            if (rental == null)
                return; // Skip if no returned rentals in seed data

            _controller.TempData = new TempDataDictionary();

            var result = _controller.EarnPoints(rental.CustomerId, rental.Id) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(true, _controller.TempData["EarnSuccess"],
                "Should set success flag for returned rental.");
        }

        [TestMethod]
        public void EarnPoints_DoubleAward_SetsEarnError()
        {
            var rental = _rentalRepo.GetAll()
                .FirstOrDefault(r => r.Status == RentalStatus.Returned);
            if (rental == null)
                return;

            _controller.TempData = new TempDataDictionary();
            _controller.EarnPoints(rental.CustomerId, rental.Id);

            // Second attempt should fail
            _controller.TempData = new TempDataDictionary();
            var result = _controller.EarnPoints(rental.CustomerId, rental.Id) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.IsNotNull(_controller.TempData["EarnError"],
                "Double-awarding points should fail.");
        }

        [TestMethod]
        public void EarnPoints_RedirectsToIndexWithCustomerId()
        {
            var customer = _customerRepo.GetAll().First();
            _controller.TempData = new TempDataDictionary();

            var result = _controller.EarnPoints(customer.Id, 1) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.AreEqual(customer.Id, result.RouteValues["customerId"]);
        }

        // ── Leaderboard ─────────────────────────────────────────────

        [TestMethod]
        public void Leaderboard_ReturnsViewWithLeaderboardData()
        {
            var result = _controller.Leaderboard() as ViewResult;

            Assert.IsNotNull(result);
            var vm = result.Model as LoyaltyViewModel;
            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.Leaderboard);
            Assert.IsNotNull(vm.AllCustomers);
        }

        [TestMethod]
        public void Leaderboard_ReturnsUpTo25Entries()
        {
            var result = _controller.Leaderboard() as ViewResult;
            var vm = result?.Model as LoyaltyViewModel;

            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.Leaderboard.Count <= 25,
                "Leaderboard should return at most 25 entries.");
        }
    }
}
