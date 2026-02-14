using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class RandomMovieViewModelTests
    {
        /// <summary>
        /// Verifies that the Customers list is initialized to an empty list by default.
        /// This prevents NullReferenceExceptions when iterating in views.
        /// </summary>
        [TestMethod]
        public void ViewModel_Customers_DefaultsToEmptyList()
        {
            var vm = new RandomMovieViewModel();

            Assert.IsNotNull(vm.Customers,
                "Customers list should be initialized by default.");
            Assert.AreEqual(0, vm.Customers.Count,
                "Default Customers list should be empty.");
        }

        /// <summary>
        /// Verifies that Movie property is null by default.
        /// </summary>
        [TestMethod]
        public void ViewModel_Movie_DefaultsToNull()
        {
            var vm = new RandomMovieViewModel();

            Assert.IsNull(vm.Movie,
                "Movie should be null by default.");
        }

        /// <summary>
        /// Verifies the view model can be populated correctly.
        /// </summary>
        [TestMethod]
        public void ViewModel_CanBePopulated()
        {
            var movie = new Movie { Id = 1, Name = "Inception", ReleaseDate = new DateTime(2010, 7, 16) };
            var customers = new List<Customer>
            {
                new Customer { Id = 1, Name = "Alice" },
                new Customer { Id = 2, Name = "Bob" }
            };

            var vm = new RandomMovieViewModel
            {
                Movie = movie,
                Customers = customers
            };

            Assert.AreEqual("Inception", vm.Movie.Name);
            Assert.AreEqual(2, vm.Customers.Count);
            Assert.AreEqual("Alice", vm.Customers[0].Name);
            Assert.AreEqual("Bob", vm.Customers[1].Name);
        }
    }
}
