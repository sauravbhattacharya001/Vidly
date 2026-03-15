using System;
using System.Collections.Generic;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class CustomerInsightsControllerTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryCustomerRepository.Reset();
            InMemoryRentalRepository.Reset();
            InMemoryMovieRepository.Reset();
        }

        private CustomerInsightsController CreateController() =>
            new CustomerInsightsController(new InMemoryCustomerRepository(), new InMemoryRentalRepository(), new InMemoryMovieRepository());

        [TestMethod]
        public void Index_NoCustomerId_ReturnsViewWithAllCustomers()
        {
            var result = CreateController().Index(null) as ViewResult;
            var model = result.Model as CustomerInsightsViewModel;
            Assert.IsNull(model.Customer);
            Assert.IsNotNull(model.AllCustomers);
            Assert.IsTrue(model.AllCustomers.Count > 0);
            Assert.AreEqual("Select a customer to view their insights.", model.StatusMessage);
        }

        [TestMethod]
        public void Index_InvalidCustomerId_ReturnsNotFoundMessage()
        {
            var model = (CreateController().Index(99999) as ViewResult).Model as CustomerInsightsViewModel;
            Assert.IsNull(model.Customer);
            Assert.AreEqual("Customer not found.", model.StatusMessage);
        }

        [TestMethod]
        public void Index_ValidCustomerId_ReturnsFullInsights()
        {
            var customers = new InMemoryCustomerRepository().GetAll();
            var model = (CreateController().Index(customers[0].Id) as ViewResult).Model as CustomerInsightsViewModel;
            Assert.IsNotNull(model.Customer);
            Assert.IsNotNull(model.Loyalty);
            Assert.IsNotNull(model.GenreStats);
            Assert.IsNotNull(model.Spending);
            Assert.IsNotNull(model.Patterns);
            Assert.IsNotNull(model.Timeline);
            Assert.IsNotNull(model.RentalHistory);
        }

        [TestMethod]
        public void Index_ValidCustomer_LoyaltyHasTierAndScore()
        {
            var customers = new InMemoryCustomerRepository().GetAll();
            var model = (CreateController().Index(customers[0].Id) as ViewResult).Model as CustomerInsightsViewModel;
            Assert.IsNotNull(model.Loyalty.Tier);
            Assert.IsTrue(model.Loyalty.Score >= 0 && model.Loyalty.Score <= 100);
        }

        [TestMethod]
        public void BuildGenreBreakdown_EmptyHistory_ReturnsZeroCounts()
        {
            var r = CustomerInsightsController.BuildGenreBreakdown(new List<RentalHistoryEntry>());
            Assert.AreEqual(0, r.TotalRentals);
            Assert.IsNull(r.FavoriteGenre);
        }

        [TestMethod]
        public void BuildGenreBreakdown_SingleGenre_IdentifiesFavorite()
        {
            var h = new List<RentalHistoryEntry> { new RentalHistoryEntry { MovieGenre = Genre.Action }, new RentalHistoryEntry { MovieGenre = Genre.Action } };
            var r = CustomerInsightsController.BuildGenreBreakdown(h);
            Assert.AreEqual("Action", r.FavoriteGenre);
            Assert.AreEqual(1, r.UniqueGenres);
        }

        [TestMethod]
        public void BuildGenreBreakdown_MultipleGenres_PicksMostFrequent()
        {
            var h = new List<RentalHistoryEntry> { new RentalHistoryEntry { MovieGenre = Genre.Action },
                new RentalHistoryEntry { MovieGenre = Genre.Comedy }, new RentalHistoryEntry { MovieGenre = Genre.Comedy } };
            Assert.AreEqual("Comedy", CustomerInsightsController.BuildGenreBreakdown(h).FavoriteGenre);
        }

        [TestMethod]
        public void BuildGenreBreakdown_NullGenre_CountsAsUnknown()
        {
            Assert.AreEqual("Unknown", CustomerInsightsController.BuildGenreBreakdown(new List<RentalHistoryEntry> { new RentalHistoryEntry { MovieGenre = null } }).FavoriteGenre);
        }

        [TestMethod]
        public void BuildSpendingSummary_EmptyHistory_ReturnsZeros()
        {
            var r = CustomerInsightsController.BuildSpendingSummary(new List<RentalHistoryEntry>());
            Assert.AreEqual(0m, r.TotalSpent);
            Assert.AreEqual(0m, r.AveragePerRental);
        }

        [TestMethod]
        public void BuildSpendingSummary_ComputesCorrectly()
        {
            var h = new List<RentalHistoryEntry> { new RentalHistoryEntry { TotalCost = 10m, LateFee = 0m }, new RentalHistoryEntry { TotalCost = 15m, LateFee = 3m } };
            var r = CustomerInsightsController.BuildSpendingSummary(h);
            Assert.AreEqual(25m, r.TotalSpent);
            Assert.AreEqual(12.50m, r.AveragePerRental);
            Assert.AreEqual(3m, r.TotalLateFees);
            Assert.AreEqual(12.0m, r.LateFeePct);
        }

        [TestMethod]
        public void BuildRentalPatterns_EmptyHistory_ReturnsDefaults()
        {
            var r = CustomerInsightsController.BuildRentalPatterns(new List<RentalHistoryEntry>());
            Assert.AreEqual(100, r.OnTimeReturnRate);
            Assert.AreEqual("N/A", r.MostActiveDay);
        }

        [TestMethod]
        public void BuildRentalPatterns_AllOnTime_Returns100Percent()
        {
            var h = new List<RentalHistoryEntry> {
                new RentalHistoryEntry { RentalDurationDays = 5, WasLate = false, RentalDate = new DateTime(2025,1,6) },
                new RentalHistoryEntry { RentalDurationDays = 3, WasLate = false, RentalDate = new DateTime(2025,1,7) } };
            var r = CustomerInsightsController.BuildRentalPatterns(h);
            Assert.AreEqual(100.0, r.OnTimeReturnRate);
            Assert.AreEqual(2, r.CurrentStreak);
        }

        [TestMethod]
        public void BuildRentalPatterns_LateReturn_BreaksStreak()
        {
            var h = new List<RentalHistoryEntry> {
                new RentalHistoryEntry { RentalDurationDays = 7, WasLate = true, RentalDate = new DateTime(2025,1,10) },
                new RentalHistoryEntry { RentalDurationDays = 5, WasLate = false, RentalDate = new DateTime(2025,1,6) } };
            var r = CustomerInsightsController.BuildRentalPatterns(h);
            Assert.AreEqual(50.0, r.OnTimeReturnRate);
            Assert.AreEqual(0, r.CurrentStreak);
        }

        [TestMethod]
        public void BuildRentalPatterns_IdentifiesMostActiveDay()
        {
            var h = new List<RentalHistoryEntry> {
                new RentalHistoryEntry { RentalDurationDays = 3, WasLate = false, RentalDate = new DateTime(2025,1,6) },
                new RentalHistoryEntry { RentalDurationDays = 3, WasLate = false, RentalDate = new DateTime(2025,1,8) },
                new RentalHistoryEntry { RentalDurationDays = 3, WasLate = false, RentalDate = new DateTime(2025,1,13) } };
            Assert.AreEqual("Monday", CustomerInsightsController.BuildRentalPatterns(h).MostActiveDay);
        }

        [TestMethod]
        public void Constructor_NullArguments_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new CustomerInsightsController(null, new InMemoryRentalRepository(), new InMemoryMovieRepository()));
            Assert.ThrowsException<ArgumentNullException>(() => new CustomerInsightsController(new InMemoryCustomerRepository(), null, new InMemoryMovieRepository()));
            Assert.ThrowsException<ArgumentNullException>(() => new CustomerInsightsController(new InMemoryCustomerRepository(), new InMemoryRentalRepository(), null));
        }
    }
}
