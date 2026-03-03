using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class BundleServiceTests
    {
        private BundleService _service;

        [TestInitialize]
        public void Setup()
        {
            _service = new BundleService();
        }

        [TestMethod]
        public void GetAll_ReturnsSeededBundles()
        {
            var bundles = _service.GetAll();
            Assert.IsTrue(bundles.Count >= 4, "Should have at least 4 seeded bundles");
        }

        [TestMethod]
        public void GetActive_ReturnsOnlyValidBundles()
        {
            var active = _service.GetActive();
            Assert.IsTrue(active.All(b => b.IsCurrentlyValid));
        }

        [TestMethod]
        public void GetById_ReturnsCorrectBundle()
        {
            var bundle = _service.GetById(1);
            Assert.IsNotNull(bundle);
            Assert.AreEqual("3 for 2", bundle.Name);
        }

        [TestMethod]
        public void GetById_InvalidId_ReturnsNull()
        {
            Assert.IsNull(_service.GetById(999));
        }

        [TestMethod]
        public void Add_ValidBundle_AssignsId()
        {
            var bundle = new BundleDeal
            {
                Name = "Test Bundle",
                MinMovies = 2,
                DiscountType = BundleDiscountType.Percentage,
                DiscountValue = 15
            };
            var result = _service.Add(bundle);
            Assert.IsTrue(result.Id > 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Add_NullBundle_Throws()
        {
            _service.Add(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Add_EmptyName_Throws()
        {
            _service.Add(new BundleDeal { Name = "", MinMovies = 2, DiscountValue = 10 });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Add_MinMoviesLessThan2_Throws()
        {
            _service.Add(new BundleDeal { Name = "Bad", MinMovies = 1, DiscountValue = 10 });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Add_ZeroDiscount_Throws()
        {
            _service.Add(new BundleDeal { Name = "Bad", MinMovies = 2, DiscountValue = 0 });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Add_PercentageOver100_Throws()
        {
            _service.Add(new BundleDeal
            {
                Name = "Bad",
                MinMovies = 2,
                DiscountType = BundleDiscountType.Percentage,
                DiscountValue = 101
            });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Add_FreeMoviesEqualToMin_Throws()
        {
            _service.Add(new BundleDeal
            {
                Name = "Bad",
                MinMovies = 2,
                DiscountType = BundleDiscountType.FreeMovies,
                DiscountValue = 2
            });
        }

        [TestMethod]
        public void Update_ValidBundle_UpdatesFields()
        {
            var bundle = _service.GetById(1);
            bundle.Name = "Updated Name";
            bundle.Description = "Updated Desc";
            var result = _service.Update(bundle);
            Assert.AreEqual("Updated Name", result.Name);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void Update_InvalidId_Throws()
        {
            _service.Update(new BundleDeal { Id = 999, Name = "No" });
        }

        [TestMethod]
        public void Remove_ValidId_RemovesBundle()
        {
            var countBefore = _service.GetAll().Count;
            _service.Remove(1);
            Assert.AreEqual(countBefore - 1, _service.GetAll().Count);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void Remove_InvalidId_Throws()
        {
            _service.Remove(999);
        }

        private List<Movie> CreateMovies(params (int id, string name, decimal rate, Genre? genre)[] specs)
        {
            return specs.Select(s => new Movie
            {
                Id = s.id,
                Name = s.name,
                DailyRate = s.rate,
                Genre = s.genre
            }).ToList();
        }

        private Dictionary<int, decimal> CreateRates(List<Movie> movies)
        {
            return movies.ToDictionary(m => m.Id, m => m.DailyRate ?? 3.99m);
        }

        [TestMethod]
        public void FindBestBundle_EmptyMovies_ReturnsNoDiscount()
        {
            var result = _service.FindBestBundle(new List<Movie>(), new Dictionary<int, decimal>(), 3);
            Assert.IsFalse(result.Applied);
            Assert.AreEqual(0, result.DiscountAmount);
        }

        [TestMethod]
        public void FindBestBundle_SingleMovie_NoBundle()
        {
            var movies = CreateMovies((1, "Movie 1", 3.99m, Genre.Action));
            var rates = CreateRates(movies);
            var result = _service.FindBestBundle(movies, rates, 3);
            Assert.IsFalse(result.Applied);
        }

        [TestMethod]
        public void FindBestBundle_TwoMovies_AppliesDoubleFeature()
        {
            var movies = CreateMovies(
                (1, "Movie A", 3.99m, Genre.Comedy),
                (2, "Movie B", 4.99m, Genre.Drama));
            var rates = CreateRates(movies);
            var result = _service.FindBestBundle(movies, rates, 3);
            Assert.IsTrue(result.Applied);
            Assert.AreEqual("Double Feature", result.Bundle.Name);
            Assert.AreEqual(2.00m, result.DiscountAmount);
        }

        [TestMethod]
        public void FindBestBundle_ThreeMovies_GivesDiscount()
        {
            var movies = CreateMovies(
                (1, "Movie A", 2.00m, Genre.Comedy),
                (2, "Movie B", 3.00m, Genre.Drama),
                (3, "Movie C", 5.00m, Genre.Action));
            var rates = CreateRates(movies);
            var result = _service.FindBestBundle(movies, rates, 3);
            Assert.IsTrue(result.Applied);
            Assert.IsTrue(result.DiscountAmount > 0);
        }

        [TestMethod]
        public void FindBestBundle_FiveMovies_AppliesWeekendBinge()
        {
            var movies = CreateMovies(
                (1, "M1", 4.00m, Genre.Comedy),
                (2, "M2", 4.00m, Genre.Drama),
                (3, "M3", 4.00m, Genre.Action),
                (4, "M4", 4.00m, Genre.Horror),
                (5, "M5", 4.00m, Genre.Romance));
            var rates = CreateRates(movies);
            var result = _service.FindBestBundle(movies, rates, 3);
            Assert.IsTrue(result.Applied);
            Assert.IsTrue(result.DiscountAmount >= 15.00m);
        }

        [TestMethod]
        public void FindBestBundle_FourActionMovies_AppliesActionPack()
        {
            var movies = CreateMovies(
                (1, "Die Hard", 4.00m, Genre.Action),
                (2, "Rambo", 4.00m, Genre.Action),
                (3, "Terminator", 4.00m, Genre.Action),
                (4, "Predator", 4.00m, Genre.Action));
            var rates = CreateRates(movies);
            var result = _service.FindBestBundle(movies, rates, 3);
            Assert.IsTrue(result.Applied);
            Assert.IsTrue(result.DiscountAmount > 0);
        }

        [TestMethod]
        public void FindBestBundle_GenreRestriction_NonMatching_NoApply()
        {
            var service = new BundleService();
            foreach (var b in service.GetAll().ToList())
                service.Remove(b.Id);

            service.Add(new BundleDeal
            {
                Name = "Horror Special",
                MinMovies = 2,
                DiscountType = BundleDiscountType.Percentage,
                DiscountValue = 20,
                RequiredGenre = Genre.Horror,
                IsActive = true
            });

            var movies = CreateMovies(
                (1, "Comedy 1", 4.00m, Genre.Comedy),
                (2, "Comedy 2", 4.00m, Genre.Comedy));
            var rates = CreateRates(movies);
            var result = service.FindBestBundle(movies, rates, 3);
            Assert.IsFalse(result.Applied);
        }

        [TestMethod]
        public void FindBestBundle_FreeMovies_MarksCorrectMoviesFree()
        {
            var service = new BundleService();
            foreach (var b in service.GetAll().ToList())
                service.Remove(b.Id);

            service.Add(new BundleDeal
            {
                Name = "3 for 2",
                MinMovies = 3,
                MaxMovies = 3,
                DiscountType = BundleDiscountType.FreeMovies,
                DiscountValue = 1,
                IsActive = true
            });

            var movies = CreateMovies(
                (1, "Cheap", 2.00m, Genre.Comedy),
                (2, "Mid", 4.00m, Genre.Drama),
                (3, "Expensive", 6.00m, Genre.Action));
            var rates = CreateRates(movies);
            var result = service.FindBestBundle(movies, rates, 3);
            Assert.IsTrue(result.Applied);
            var freeMovie = result.MoviePrices.First(p => p.IsFree);
            Assert.AreEqual(1, freeMovie.MovieId);
            Assert.AreEqual(6.00m, result.DiscountAmount);
        }

        [TestMethod]
        public void RecordUsage_IncrementsCounter()
        {
            var bundle = _service.GetById(1);
            var before = bundle.TimesUsed;
            _service.RecordUsage(1);
            Assert.AreEqual(before + 1, bundle.TimesUsed);
        }

        [TestMethod]
        public void GetStats_ReturnsCorrectCounts()
        {
            var stats = _service.GetStats();
            Assert.IsTrue(stats.TotalBundles >= 4);
            Assert.IsTrue(stats.ActiveBundles > 0);
        }

        [TestMethod]
        public void IsCurrentlyValid_ExpiredBundle_ReturnsFalse()
        {
            var bundle = new BundleDeal
            {
                IsActive = true,
                StartDate = DateTime.Today.AddDays(-30),
                EndDate = DateTime.Today.AddDays(-1)
            };
            Assert.IsFalse(bundle.IsCurrentlyValid);
        }

        [TestMethod]
        public void IsCurrentlyValid_FutureBundle_ReturnsFalse()
        {
            var bundle = new BundleDeal
            {
                IsActive = true,
                StartDate = DateTime.Today.AddDays(1)
            };
            Assert.IsFalse(bundle.IsCurrentlyValid);
        }

        [TestMethod]
        public void IsCurrentlyValid_ActiveNoDate_ReturnsTrue()
        {
            var bundle = new BundleDeal { IsActive = true };
            Assert.IsTrue(bundle.IsCurrentlyValid);
        }

        [TestMethod]
        public void IsCurrentlyValid_InactiveBundle_ReturnsFalse()
        {
            var bundle = new BundleDeal { IsActive = false };
            Assert.IsFalse(bundle.IsCurrentlyValid);
        }

        [TestMethod]
        public void BundleApplyResult_FinalTotal_CannotGoNegative()
        {
            var result = new BundleApplyResult
            {
                OriginalTotal = 10,
                DiscountAmount = 15
            };
            Assert.AreEqual(0, result.FinalTotal);
        }

        [TestMethod]
        public void BundleApplyResult_DiscountPercent_Calculated()
        {
            var result = new BundleApplyResult
            {
                OriginalTotal = 100,
                DiscountAmount = 25
            };
            Assert.AreEqual(25.0m, result.DiscountPercent);
        }
    }
}
