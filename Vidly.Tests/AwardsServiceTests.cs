using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class AwardsServiceTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryRentalRepository.Reset();
            InMemoryMovieRepository.Reset();
            InMemoryCustomerRepository.Reset();
            InMemoryReviewRepository.Reset();
        }

        private AwardsService CreateService() =>
            new AwardsService(
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryReviewRepository());

        private AwardsController CreateController() =>
            new AwardsController(CreateService());

        // --- Service Tests ---

        [TestMethod]
        public void GetAvailableYears_ReturnsDistinctYearsDescending()
        {
            var service = CreateService();
            var years = service.GetAvailableYears();
            Assert.IsTrue(years.Count > 0, "Should have at least one year from seed data");
            for (int i = 1; i < years.Count; i++)
                Assert.IsTrue(years[i - 1] >= years[i], "Years should be descending");
        }

        [TestMethod]
        public void GetCeremony_ReturnsValidCeremony()
        {
            var service = CreateService();
            var years = service.GetAvailableYears();
            if (years.Count == 0) return;

            var ceremony = service.GetCeremony(years.First());
            Assert.IsNotNull(ceremony);
            Assert.AreEqual(years.First(), ceremony.Year);
            Assert.IsNotNull(ceremony.Summary);
            Assert.IsNotNull(ceremony.Categories);
        }

        [TestMethod]
        public void GetCeremony_SummaryHasCorrectYear()
        {
            var service = CreateService();
            var ceremony = service.GetCeremony(2025);
            Assert.AreEqual(2025, ceremony.Summary.Year);
        }

        [TestMethod]
        public void GetCeremony_IncludesAvailableYears()
        {
            var service = CreateService();
            var ceremony = service.GetCeremony(2025);
            Assert.IsNotNull(ceremony.AvailableYears);
            Assert.IsTrue(ceremony.AvailableYears.Count > 0);
        }

        [TestMethod]
        public void GetCeremony_CategoriesHaveWinners()
        {
            var service = CreateService();
            var years = service.GetAvailableYears();
            if (years.Count == 0) return;

            var ceremony = service.GetCeremony(years.First());
            foreach (var cat in ceremony.Categories)
            {
                Assert.IsNotNull(cat.Winner, $"Category '{cat.Name}' should have a winner");
                Assert.IsFalse(string.IsNullOrEmpty(cat.Winner.Name), $"Winner name in '{cat.Name}' should not be empty");
            }
        }

        [TestMethod]
        public void GetCeremony_CategoriesHaveIcons()
        {
            var service = CreateService();
            var years = service.GetAvailableYears();
            if (years.Count == 0) return;

            var ceremony = service.GetCeremony(years.First());
            foreach (var cat in ceremony.Categories)
            {
                Assert.IsFalse(string.IsNullOrEmpty(cat.Icon), $"Category '{cat.Name}' should have an icon");
            }
        }

        [TestMethod]
        public void GetCeremony_NomineesHaveRanks()
        {
            var service = CreateService();
            var years = service.GetAvailableYears();
            if (years.Count == 0) return;

            var ceremony = service.GetCeremony(years.First());
            foreach (var cat in ceremony.Categories)
            {
                foreach (var nom in cat.Nominees)
                {
                    Assert.IsTrue(nom.Rank >= 2, $"Nominee rank should be >= 2 in '{cat.Name}'");
                }
            }
        }

        [TestMethod]
        public void GetCeremony_EmptyYear_ReturnsEmptyCategories()
        {
            var service = CreateService();
            var ceremony = service.GetCeremony(1900);
            Assert.AreEqual(0, ceremony.Categories.Count);
            Assert.AreEqual(0, ceremony.Summary.TotalRentals);
        }

        [TestMethod]
        public void GetCeremony_SummaryRevenueIsNonNegative()
        {
            var service = CreateService();
            var years = service.GetAvailableYears();
            if (years.Count == 0) return;

            var ceremony = service.GetCeremony(years.First());
            Assert.IsTrue(ceremony.Summary.TotalRevenue >= 0);
        }

        [TestMethod]
        public void GetCeremony_CategoryNamesAreUnique()
        {
            var service = CreateService();
            var years = service.GetAvailableYears();
            if (years.Count == 0) return;

            var ceremony = service.GetCeremony(years.First());
            var names = ceremony.Categories.Select(c => c.Name).ToList();
            Assert.AreEqual(names.Count, names.Distinct().Count(), "Category names should be unique");
        }

        [TestMethod]
        public void GetCeremony_WinnersHaveStatLabelsAndValues()
        {
            var service = CreateService();
            var years = service.GetAvailableYears();
            if (years.Count == 0) return;

            var ceremony = service.GetCeremony(years.First());
            foreach (var cat in ceremony.Categories)
            {
                Assert.IsFalse(string.IsNullOrEmpty(cat.Winner.StatLabel), $"StatLabel missing in '{cat.Name}'");
                Assert.IsFalse(string.IsNullOrEmpty(cat.Winner.StatValue), $"StatValue missing in '{cat.Name}'");
            }
        }

        // --- Controller Tests ---

        [TestMethod]
        public void Index_NoYear_ReturnsCurrentYear()
        {
            var controller = CreateController();
            var result = controller.Index(null) as ViewResult;
            Assert.IsNotNull(result);
            var model = result.Model as AwardsCeremony;
            Assert.IsNotNull(model);
            Assert.AreEqual(DateTime.Now.Year, model.Year);
        }

        [TestMethod]
        public void Index_WithYear_ReturnsSpecifiedYear()
        {
            var controller = CreateController();
            var result = controller.Index(2025) as ViewResult;
            Assert.IsNotNull(result);
            var model = result.Model as AwardsCeremony;
            Assert.IsNotNull(model);
            Assert.AreEqual(2025, model.Year);
        }

        [TestMethod]
        public void Index_ReturnsViewResult()
        {
            var controller = CreateController();
            var result = controller.Index(null);
            Assert.IsInstanceOfType(result, typeof(ViewResult));
        }

        [TestMethod]
        public void Constructor_NullService_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new AwardsController(null));
        }

        [TestMethod]
        public void Service_NullRentalRepo_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AwardsService(null, new InMemoryMovieRepository(), new InMemoryCustomerRepository(), new InMemoryReviewRepository()));
        }

        [TestMethod]
        public void Service_NullMovieRepo_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AwardsService(new InMemoryRentalRepository(), null, new InMemoryCustomerRepository(), new InMemoryReviewRepository()));
        }

        [TestMethod]
        public void Service_NullCustomerRepo_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AwardsService(new InMemoryRentalRepository(), new InMemoryMovieRepository(), null, new InMemoryReviewRepository()));
        }

        [TestMethod]
        public void Service_NullReviewRepo_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AwardsService(new InMemoryRentalRepository(), new InMemoryMovieRepository(), new InMemoryCustomerRepository(), null));
        }

        [TestMethod]
        public void GetCeremony_MultipleCalls_ConsistentResults()
        {
            var service = CreateService();
            var years = service.GetAvailableYears();
            if (years.Count == 0) return;

            var c1 = service.GetCeremony(years.First());
            var c2 = service.GetCeremony(years.First());
            Assert.AreEqual(c1.Categories.Count, c2.Categories.Count);
            Assert.AreEqual(c1.Summary.TotalRentals, c2.Summary.TotalRentals);
        }

        [TestMethod]
        public void GetCeremony_NomineesMaxFour()
        {
            var service = CreateService();
            var years = service.GetAvailableYears();
            if (years.Count == 0) return;

            var ceremony = service.GetCeremony(years.First());
            foreach (var cat in ceremony.Categories)
            {
                Assert.IsTrue(cat.Nominees.Count <= 4, $"'{cat.Name}' should have at most 4 nominees (runners-up)");
            }
        }

        [TestMethod]
        public void GetCeremony_CategoriesHaveDescriptions()
        {
            var service = CreateService();
            var years = service.GetAvailableYears();
            if (years.Count == 0) return;

            var ceremony = service.GetCeremony(years.First());
            foreach (var cat in ceremony.Categories)
            {
                Assert.IsFalse(string.IsNullOrEmpty(cat.Description), $"'{cat.Name}' should have a description");
            }
        }
    }
}
