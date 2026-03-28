using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class SeasonalPromotionServiceTests
    {
        #region Test Repositories

        private class StubMovieRepository : IMovieRepository
        {
            private readonly Dictionary<int, Movie> _movies = new Dictionary<int, Movie>();
            private int _nextId = 1;

            public Movie AddMovie(Movie movie)
            {
                movie.Id = _nextId++;
                _movies[movie.Id] = movie;
                return movie;
            }

            public void Add(Movie movie) => AddMovie(movie);
            public Movie Get(int id) => _movies.TryGetValue(id, out var m) ? m : null;
            public Movie GetById(int id) => Get(id);
            public IReadOnlyList<Movie> GetAll() => _movies.Values.ToList();
            public void Update(Movie movie) { _movies[movie.Id] = movie; }
            public void Remove(int id) { _movies.Remove(id); }
            public int Count => _movies.Count;

            public IReadOnlyList<Movie> GetByGenre(Genre genre) =>
                _movies.Values.Where(m => m.Genre == genre).ToList();

            public IReadOnlyList<Movie> Search(string query) =>
                _movies.Values.Where(m => m.Name.Contains(query)).ToList();

            public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating) =>
                _movies.Values.Where(m =>
                    (query == null || m.Name.Contains(query)) &&
                    (!genre.HasValue || m.Genre == genre) &&
                    (!minRating.HasValue || (m.Rating ?? 0) >= minRating.Value)).ToList();

            public IReadOnlyList<Movie> GetByReleaseDate(int year, int month) =>
                _movies.Values.Where(m => m.ReleaseDate.HasValue &&
                    m.ReleaseDate.Value.Year == year && m.ReleaseDate.Value.Month == month).ToList();

            public Movie GetRandom() =>
                _movies.Values.FirstOrDefault();
        }

        private class StubRentalRepository : IRentalRepository
        {
            private readonly List<Rental> _rentals = new List<Rental>();
            private int _nextId = 1;

            public void Add(Rental rental) { rental.Id = _nextId++; _rentals.Add(rental); }
            public Rental GetById(int id) => _rentals.FirstOrDefault(r => r.Id == id);
            public IReadOnlyList<Rental> GetAll() => _rentals;
            public void Update(Rental rental) { }
            public void Remove(int id) { _rentals.RemoveAll(r => r.Id == id); }
            public int Count => _rentals.Count;
            public Rental Get(int id) => GetById(id);

            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) =>
                _rentals.Where(r => r.CustomerId == customerId && r.Status == RentalStatus.Active).ToList();
            public IReadOnlyList<Rental> GetByCustomer(int customerId) =>
                _rentals.Where(r => r.CustomerId == customerId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetByMovie(int movieId) =>
                _rentals.Where(r => r.MovieId == movieId).ToList();
            public IReadOnlyList<Rental> GetOverdue() =>
                _rentals.Where(r => r.Status == RentalStatus.Overdue).ToList();
            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) => _rentals;
            public Rental ReturnRental(int rentalId) => GetById(rentalId);
            public bool IsMovieRentedOut(int movieId) => false;
            public Rental Checkout(Rental rental) { Add(rental); return rental; }

            public Rental Checkout(Rental rental, int maxConcurrentRentals)
            {
                return Checkout(rental);
            }
            public RentalStats GetStats() => new RentalStats();
        }

        #endregion

        private StubMovieRepository _movieRepo;
        private StubRentalRepository _rentalRepo;
        private SeasonalPromotionService _service;
        private Movie _actionMovie;
        private Movie _comedyMovie;
        private Movie _horrorMovie;
        private Movie _dramaMovie;
        private Movie _noGenreMovie;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new StubMovieRepository();
            _rentalRepo = new StubRentalRepository();
            _service = new SeasonalPromotionService(_rentalRepo, _movieRepo);

            _actionMovie = _movieRepo.AddMovie(new Movie { Name = "Die Hard", Genre = Genre.Action, Rating = 5 });
            _comedyMovie = _movieRepo.AddMovie(new Movie { Name = "Airplane!", Genre = Genre.Comedy, Rating = 4 });
            _horrorMovie = _movieRepo.AddMovie(new Movie { Name = "The Ring", Genre = Genre.Horror, Rating = 4 });
            _dramaMovie = _movieRepo.AddMovie(new Movie { Name = "Shawshank", Genre = Genre.Drama, Rating = 5 });
            _noGenreMovie = _movieRepo.AddMovie(new Movie { Name = "Unknown", Genre = null, Rating = 3 });
        }

        // ── CreatePromotion ─────────────────────────────────────────

        [TestMethod]
        public void CreatePromotion_ValidInput_ReturnsPromotion()
        {
            var promo = _service.CreatePromotion(
                "Test Sale", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.Percentage, 10);

            Assert.IsNotNull(promo);
            Assert.AreEqual("Test Sale", promo.Name);
            Assert.AreEqual(PromotionDiscountType.Percentage, promo.DiscountType);
            Assert.AreEqual(10m, promo.DiscountValue);
            Assert.IsTrue(promo.IsEnabled);
            Assert.AreEqual(0, promo.RedemptionCount);
        }

        [TestMethod]
        public void CreatePromotion_AssignsIncrementingIds()
        {
            var p1 = _service.CreatePromotion("A", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.Percentage, 10);
            var p2 = _service.CreatePromotion("B", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.Percentage, 10);

            Assert.AreEqual(1, p1.Id);
            Assert.AreEqual(2, p2.Id);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreatePromotion_EmptyName_Throws()
        {
            _service.CreatePromotion("", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.Percentage, 10);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreatePromotion_NameTooLong_Throws()
        {
            _service.CreatePromotion(new string('A', 101), DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.Percentage, 10);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreatePromotion_EndBeforeStart_Throws()
        {
            _service.CreatePromotion("Bad", DateTime.Now, DateTime.Now.AddDays(-1),
                PromotionDiscountType.Percentage, 10);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreatePromotion_DurationExceedsMax_Throws()
        {
            _service.CreatePromotion("Long", DateTime.Now,
                DateTime.Now.AddDays(SeasonalPromotionService.MaxDurationDays + 1),
                PromotionDiscountType.Percentage, 10);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreatePromotion_PercentageExceedsMax_Throws()
        {
            _service.CreatePromotion("Too Much", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.Percentage, SeasonalPromotionService.MaxDiscountPercent + 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreatePromotion_FlatExceedsMax_Throws()
        {
            _service.CreatePromotion("Too Much", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.FlatAmount, SeasonalPromotionService.MaxFlatDiscount + 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreatePromotion_ZeroDiscount_Throws()
        {
            _service.CreatePromotion("Zero", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.Percentage, 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreatePromotion_NegativeDiscount_Throws()
        {
            _service.CreatePromotion("Neg", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.FlatAmount, -5);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CreatePromotion_DuplicateName_Throws()
        {
            _service.CreatePromotion("Sale", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.Percentage, 10);
            _service.CreatePromotion("Sale", DateTime.Now, DateTime.Now.AddDays(14),
                PromotionDiscountType.FlatAmount, 2);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreatePromotion_ZeroMaxRedemptions_Throws()
        {
            _service.CreatePromotion("Bad", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.Percentage, 10, maxRedemptions: 0);
        }

        [TestMethod]
        public void CreatePromotion_WithGenresAndMovieIds_SetsCorrectly()
        {
            var promo = _service.CreatePromotion("Genre Sale", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.Percentage, 15,
                eligibleGenres: new List<Genre> { Genre.Action, Genre.Comedy },
                eligibleMovieIds: new List<int> { 1, 2 });

            Assert.AreEqual(2, promo.EligibleGenres.Count);
            Assert.AreEqual(2, promo.EligibleMovieIds.Count);
        }

        [TestMethod]
        public void CreatePromotion_BOGO_AcceptsAnyPositiveValue()
        {
            var promo = _service.CreatePromotion("BOGO", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.BuyOneGetOneFree, 1);
            Assert.AreEqual(PromotionDiscountType.BuyOneGetOneFree, promo.DiscountType);
        }

        // ── GetAllPromotions ────────────────────────────────────────

        [TestMethod]
        public void GetAllPromotions_ReturnsAllCreated()
        {
            _service.CreatePromotion("A", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.Percentage, 10);
            _service.CreatePromotion("B", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.FlatAmount, 2);

            var all = _service.GetAllPromotions();
            Assert.AreEqual(2, all.Count);
        }

        // ── GetActivePromotions ─────────────────────────────────────

        [TestMethod]
        public void GetActivePromotions_FiltersCorrectly()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("Active", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.Percentage, 10);
            _service.CreatePromotion("Future", now.AddDays(10), now.AddDays(20),
                PromotionDiscountType.Percentage, 10);
            _service.CreatePromotion("Past", now.AddDays(-30), now.AddDays(-10),
                PromotionDiscountType.Percentage, 10);

            var active = _service.GetActivePromotions(now);
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("Active", active[0].Name);
        }

        [TestMethod]
        public void GetActivePromotions_ExcludesDisabled()
        {
            var now = DateTime.Now;
            var promo = _service.CreatePromotion("Disabled", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.Percentage, 10);
            _service.UpdatePromotion(promo.Id, isEnabled: false);

            var active = _service.GetActivePromotions(now);
            Assert.AreEqual(0, active.Count);
        }

        [TestMethod]
        public void GetActivePromotions_ExcludesSoldOut()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("Limited", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.Percentage, 10, maxRedemptions: 1);
            _service.RecordRedemption(1);

            var active = _service.GetActivePromotions(now);
            Assert.AreEqual(0, active.Count);
        }

        // ── GetPromotionsForMovie ───────────────────────────────────

        [TestMethod]
        public void GetPromotionsForMovie_AllGenres_AppliesToAnyMovie()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("All Genres", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.Percentage, 10);

            var promos = _service.GetPromotionsForMovie(_actionMovie.Id, now);
            Assert.AreEqual(1, promos.Count);

            promos = _service.GetPromotionsForMovie(_comedyMovie.Id, now);
            Assert.AreEqual(1, promos.Count);
        }

        [TestMethod]
        public void GetPromotionsForMovie_SpecificGenre_FiltersCorrectly()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("Action Only", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.Percentage, 20,
                eligibleGenres: new List<Genre> { Genre.Action });

            Assert.AreEqual(1, _service.GetPromotionsForMovie(_actionMovie.Id, now).Count);
            Assert.AreEqual(0, _service.GetPromotionsForMovie(_comedyMovie.Id, now).Count);
        }

        [TestMethod]
        public void GetPromotionsForMovie_SpecificMovieIds_FiltersCorrectly()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("Movie Specific", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.FlatAmount, 1,
                eligibleMovieIds: new List<int> { _actionMovie.Id });

            Assert.AreEqual(1, _service.GetPromotionsForMovie(_actionMovie.Id, now).Count);
            Assert.AreEqual(0, _service.GetPromotionsForMovie(_comedyMovie.Id, now).Count);
        }

        [TestMethod]
        public void GetPromotionsForMovie_NoGenreMovie_ExcludedFromGenrePromo()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("Genre Sale", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.Percentage, 10,
                eligibleGenres: new List<Genre> { Genre.Action });

            Assert.AreEqual(0, _service.GetPromotionsForMovie(_noGenreMovie.Id, now).Count);
        }

        [TestMethod]
        public void GetPromotionsForMovie_NonexistentMovie_ReturnsEmpty()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("Sale", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.Percentage, 10);

            Assert.AreEqual(0, _service.GetPromotionsForMovie(999, now).Count);
        }

        // ── CalculateBestDiscount ───────────────────────────────────

        [TestMethod]
        public void CalculateBestDiscount_NoPromotions_ReturnsOriginalPrice()
        {
            var result = _service.CalculateBestDiscount(_actionMovie.Id, 10.00m);

            Assert.AreEqual(10.00m, result.OriginalPrice);
            Assert.AreEqual(10.00m, result.FinalPrice);
            Assert.AreEqual(0m, result.TotalDiscountAmount);
            Assert.IsFalse(result.HasDiscount);
            Assert.AreEqual(0, result.AppliedPromotions.Count);
        }

        [TestMethod]
        public void CalculateBestDiscount_PercentageDiscount_CalculatesCorrectly()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("20% Off", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.Percentage, 20);

            var result = _service.CalculateBestDiscount(_actionMovie.Id, 10.00m, now);

            Assert.AreEqual(10.00m, result.OriginalPrice);
            Assert.AreEqual(8.00m, result.FinalPrice);
            Assert.AreEqual(2.00m, result.TotalDiscountAmount);
            Assert.IsTrue(result.HasDiscount);
            Assert.AreEqual(20.0m, result.DiscountPercentage);
        }

        [TestMethod]
        public void CalculateBestDiscount_FlatDiscount_CalculatesCorrectly()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("$2 Off", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.FlatAmount, 2.00m);

            var result = _service.CalculateBestDiscount(_actionMovie.Id, 10.00m, now);

            Assert.AreEqual(8.00m, result.FinalPrice);
            Assert.AreEqual(2.00m, result.TotalDiscountAmount);
        }

        [TestMethod]
        public void CalculateBestDiscount_FlatExceedsPrice_CapsAtPrice()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("$20 Off", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.FlatAmount, 20.00m);

            var result = _service.CalculateBestDiscount(_actionMovie.Id, 5.00m, now);

            Assert.AreEqual(0.00m, result.FinalPrice);
            Assert.AreEqual(5.00m, result.TotalDiscountAmount);
        }

        [TestMethod]
        public void CalculateBestDiscount_BOGO_Gives50PercentOff()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("BOGO", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.BuyOneGetOneFree, 1);

            var result = _service.CalculateBestDiscount(_actionMovie.Id, 10.00m, now);

            Assert.AreEqual(5.00m, result.FinalPrice);
            Assert.AreEqual(5.00m, result.TotalDiscountAmount);
        }

        [TestMethod]
        public void CalculateBestDiscount_MultipleNonStackable_PicksBest()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("10% Off", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.Percentage, 10);
            _service.CreatePromotion("30% Off", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.Percentage, 30);

            var result = _service.CalculateBestDiscount(_actionMovie.Id, 10.00m, now);

            Assert.AreEqual(7.00m, result.FinalPrice);
            Assert.AreEqual(3.00m, result.TotalDiscountAmount);
            Assert.AreEqual(1, result.AppliedPromotions.Count);
            Assert.AreEqual("30% Off", result.AppliedPromotions[0].PromotionName);
        }

        [TestMethod]
        public void CalculateBestDiscount_StackablePromotions_CombineAdditively()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("10% Stackable", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.Percentage, 10, isStackable: true);
            _service.CreatePromotion("$1 Stackable", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.FlatAmount, 1.00m, isStackable: true);

            var result = _service.CalculateBestDiscount(_actionMovie.Id, 10.00m, now);

            // 10% of 10 = $1 + $1 flat = $2 total
            Assert.AreEqual(8.00m, result.FinalPrice);
            Assert.AreEqual(2.00m, result.TotalDiscountAmount);
            Assert.AreEqual(2, result.AppliedPromotions.Count);
        }

        [TestMethod]
        public void CalculateBestDiscount_MixedStackableAndNon_CombinesCorrectly()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("20% Non-Stack", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.Percentage, 20);
            _service.CreatePromotion("$0.50 Stackable", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.FlatAmount, 0.50m, isStackable: true);

            var result = _service.CalculateBestDiscount(_actionMovie.Id, 10.00m, now);

            // 20% = $2 + $0.50 = $2.50
            Assert.AreEqual(7.50m, result.FinalPrice);
            Assert.AreEqual(2.50m, result.TotalDiscountAmount);
            Assert.AreEqual(2, result.AppliedPromotions.Count);
        }

        [TestMethod]
        public void CalculateBestDiscount_TotalDiscountCappedAtPrice()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("70% Stackable", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.Percentage, 70, isStackable: true);
            _service.CreatePromotion("$5 Stackable", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.FlatAmount, 5.00m, isStackable: true);

            var result = _service.CalculateBestDiscount(_actionMovie.Id, 4.00m, now);

            Assert.AreEqual(0.00m, result.FinalPrice);
            Assert.AreEqual(4.00m, result.TotalDiscountAmount);
        }

        // ── RecordRedemption ────────────────────────────────────────

        [TestMethod]
        public void RecordRedemption_IncrementsCount()
        {
            _service.CreatePromotion("Sale", DateTime.Now, DateTime.Now.AddDays(5),
                PromotionDiscountType.Percentage, 10);

            Assert.IsTrue(_service.RecordRedemption(1));

            var promo = _service.GetPromotionById(1);
            Assert.AreEqual(1, promo.RedemptionCount);
        }

        [TestMethod]
        public void RecordRedemption_AtMaxRedemptions_ReturnsFalse()
        {
            _service.CreatePromotion("Limited", DateTime.Now, DateTime.Now.AddDays(5),
                PromotionDiscountType.Percentage, 10, maxRedemptions: 2);

            Assert.IsTrue(_service.RecordRedemption(1));
            Assert.IsTrue(_service.RecordRedemption(1));
            Assert.IsFalse(_service.RecordRedemption(1));
        }

        [TestMethod]
        public void RecordRedemption_DisabledPromotion_ReturnsFalse()
        {
            var promo = _service.CreatePromotion("Disabled", DateTime.Now, DateTime.Now.AddDays(5),
                PromotionDiscountType.Percentage, 10);
            _service.UpdatePromotion(promo.Id, isEnabled: false);

            Assert.IsFalse(_service.RecordRedemption(promo.Id));
        }

        // ── UpdatePromotion ─────────────────────────────────────────

        [TestMethod]
        public void UpdatePromotion_UpdatesFields()
        {
            _service.CreatePromotion("Old Name", DateTime.Now, DateTime.Now.AddDays(30),
                PromotionDiscountType.Percentage, 10);

            var updated = _service.UpdatePromotion(1, name: "New Name",
                discountValue: 25, description: "Updated");

            Assert.AreEqual("New Name", updated.Name);
            Assert.AreEqual(25m, updated.DiscountValue);
            Assert.AreEqual("Updated", updated.Description);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void UpdatePromotion_ExpiredPromotion_Throws()
        {
            _service.CreatePromotion("Old", DateTime.Now.AddDays(-30), DateTime.Now.AddDays(-1),
                PromotionDiscountType.Percentage, 10);

            _service.UpdatePromotion(1, name: "New");
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void UpdatePromotion_NonexistentId_Throws()
        {
            _service.UpdatePromotion(999, name: "Ghost");
        }

        // ── DeletePromotion ─────────────────────────────────────────

        [TestMethod]
        public void DeletePromotion_RemovesPromotion()
        {
            _service.CreatePromotion("Delete Me", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.Percentage, 10);

            Assert.IsTrue(_service.DeletePromotion(1));
            Assert.AreEqual(0, _service.GetAllPromotions().Count);
        }

        [TestMethod]
        public void DeletePromotion_NonexistentId_ReturnsFalse()
        {
            Assert.IsFalse(_service.DeletePromotion(999));
        }

        // ── Seasonal Templates ──────────────────────────────────────

        [TestMethod]
        public void CreateSummerBlockbuster_CreatesCorrectly()
        {
            var promo = _service.CreateSummerBlockbuster(2026);

            Assert.AreEqual("Summer Blockbuster 2026", promo.Name);
            Assert.AreEqual(new DateTime(2026, 6, 1), promo.StartDate);
            Assert.AreEqual(new DateTime(2026, 8, 31), promo.EndDate);
            Assert.AreEqual(PromotionDiscountType.Percentage, promo.DiscountType);
            Assert.AreEqual(20m, promo.DiscountValue);
            Assert.IsTrue(promo.EligibleGenres.Contains(Genre.Action));
            Assert.IsTrue(promo.EligibleGenres.Contains(Genre.Adventure));
            Assert.IsTrue(promo.EligibleGenres.Contains(Genre.SciFi));
        }

        [TestMethod]
        public void CreateHolidaySpecial_CreatesCorrectly()
        {
            var promo = _service.CreateHolidaySpecial(2026);

            Assert.AreEqual("Holiday Special 2026", promo.Name);
            Assert.AreEqual(25m, promo.DiscountValue);
            Assert.IsTrue(promo.EligibleGenres.Contains(Genre.Animation));
            Assert.IsTrue(promo.EligibleGenres.Contains(Genre.Comedy));
        }

        [TestMethod]
        public void CreateSpookySeason_CreatesCorrectly()
        {
            var promo = _service.CreateSpookySeason(2026);

            Assert.AreEqual("Spooky Season 2026", promo.Name);
            Assert.AreEqual(PromotionDiscountType.FlatAmount, promo.DiscountType);
            Assert.AreEqual(1.00m, promo.DiscountValue);
            Assert.IsTrue(promo.EligibleGenres.Contains(Genre.Horror));
            Assert.IsTrue(promo.EligibleGenres.Contains(Genre.Thriller));
        }

        [TestMethod]
        public void CreateOscarSeason_CreatesCorrectly()
        {
            var promo = _service.CreateOscarSeason(2026);

            Assert.AreEqual("Oscar Season 2026", promo.Name);
            Assert.AreEqual(15m, promo.DiscountValue);
            Assert.IsTrue(promo.EligibleGenres.Contains(Genre.Drama));
            Assert.IsTrue(promo.EligibleGenres.Contains(Genre.Documentary));
        }

        [TestMethod]
        public void CreateOscarSeason_LeapYear_EndDateIsFeb29()
        {
            var promo = _service.CreateOscarSeason(2028); // 2028 is a leap year

            Assert.AreEqual(new DateTime(2028, 2, 29), promo.EndDate);
        }

        [TestMethod]
        public void CreateOscarSeason_NonLeapYear_EndDateIsFeb28()
        {
            var promo = _service.CreateOscarSeason(2027); // 2027 is not a leap year

            Assert.AreEqual(new DateTime(2027, 2, 28), promo.EndDate);
        }

        [TestMethod]
        public void CreateValentinesSpecial_CreatesCorrectly()
        {
            var promo = _service.CreateValentinesSpecial(2026);

            Assert.AreEqual("Valentine's Special 2026", promo.Name);
            Assert.AreEqual(PromotionDiscountType.FlatAmount, promo.DiscountType);
            Assert.AreEqual(1.50m, promo.DiscountValue);
            Assert.IsTrue(promo.EligibleGenres.Contains(Genre.Romance));
        }

        [TestMethod]
        public void CreateAllTemplates_NoDuplicateNames()
        {
            _service.CreateSummerBlockbuster(2026);
            _service.CreateHolidaySpecial(2026);
            _service.CreateSpookySeason(2026);
            _service.CreateOscarSeason(2026);
            _service.CreateValentinesSpecial(2026);

            Assert.AreEqual(5, _service.GetAllPromotions().Count);
        }

        // ── Analytics ───────────────────────────────────────────────

        [TestMethod]
        public void GetPromotionAnalytics_ActivePromotion_ReturnsCorrectStatus()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("Active", now.AddDays(-5), now.AddDays(5),
                PromotionDiscountType.Percentage, 10);

            var analytics = _service.GetPromotionAnalytics(1);

            Assert.AreEqual("Active", analytics.Status);
            Assert.AreEqual(10, analytics.DaysTotal);
            Assert.IsTrue(analytics.DaysRemaining > 0);
        }

        [TestMethod]
        public void GetPromotionAnalytics_WithRedemptions_CalculatesRate()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("Popular", now.AddDays(-10), now.AddDays(10),
                PromotionDiscountType.Percentage, 10, maxRedemptions: 100);

            _service.RecordRedemption(1);
            _service.RecordRedemption(1);
            _service.RecordRedemption(1);

            var analytics = _service.GetPromotionAnalytics(1);

            Assert.AreEqual(3, analytics.TotalRedemptions);
            Assert.AreEqual(3.0, analytics.UtilizationRate, 0.1);
            Assert.IsTrue(analytics.RedemptionsPerDay > 0);
        }

        [TestMethod]
        public void GetSummary_ReturnsCorrectCounts()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("Active", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.Percentage, 10);
            _service.CreatePromotion("Future", now.AddDays(10), now.AddDays(20),
                PromotionDiscountType.Percentage, 10);
            _service.CreatePromotion("Past", now.AddDays(-30), now.AddDays(-10),
                PromotionDiscountType.Percentage, 10);

            var summary = _service.GetSummary();

            Assert.AreEqual(3, summary.TotalPromotions);
            Assert.AreEqual(1, summary.ActivePromotions);
            Assert.AreEqual(1, summary.UpcomingPromotions);
            Assert.AreEqual(1, summary.ExpiredPromotions);
        }

        // ── PromotionDiscount Model ─────────────────────────────────

        [TestMethod]
        public void PromotionDiscount_DiscountPercentage_CalculatesCorrectly()
        {
            var discount = new PromotionDiscount
            {
                OriginalPrice = 20.00m,
                FinalPrice = 15.00m,
                TotalDiscountAmount = 5.00m,
                AppliedPromotions = new List<AppliedPromotion>()
            };

            Assert.AreEqual(25.0m, discount.DiscountPercentage);
        }

        [TestMethod]
        public void PromotionDiscount_ZeroOriginalPrice_NoException()
        {
            var discount = new PromotionDiscount
            {
                OriginalPrice = 0,
                FinalPrice = 0,
                TotalDiscountAmount = 0,
                AppliedPromotions = new List<AppliedPromotion>()
            };

            Assert.AreEqual(0m, discount.DiscountPercentage);
            Assert.IsFalse(discount.HasDiscount);
        }

        // ── Edge Cases ──────────────────────────────────────────────

        [TestMethod]
        public void CalculateBestDiscount_GenreRestricted_DoesNotApplyToOtherGenres()
        {
            var now = DateTime.Now;
            _service.CreatePromotion("Horror Only", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.Percentage, 50,
                eligibleGenres: new List<Genre> { Genre.Horror });

            var actionResult = _service.CalculateBestDiscount(_actionMovie.Id, 10.00m, now);
            var horrorResult = _service.CalculateBestDiscount(_horrorMovie.Id, 10.00m, now);

            Assert.AreEqual(10.00m, actionResult.FinalPrice);
            Assert.AreEqual(5.00m, horrorResult.FinalPrice);
        }

        [TestMethod]
        public void CalculateBestDiscount_BothGenreAndMovieRestriction_RequiresBoth()
        {
            var now = DateTime.Now;
            // Must be Action genre AND specific movie ID
            _service.CreatePromotion("Double Filter", now.AddDays(-1), now.AddDays(5),
                PromotionDiscountType.FlatAmount, 2.00m,
                eligibleGenres: new List<Genre> { Genre.Action },
                eligibleMovieIds: new List<int> { _actionMovie.Id });

            // Action movie not in movie list — excluded by movie ID filter
            var comedyResult = _service.CalculateBestDiscount(_comedyMovie.Id, 10.00m, now);
            Assert.AreEqual(10.00m, comedyResult.FinalPrice);

            // Action movie in both genre and movie list — included
            var actionResult = _service.CalculateBestDiscount(_actionMovie.Id, 10.00m, now);
            Assert.AreEqual(8.00m, actionResult.FinalPrice);
        }

        [TestMethod]
        public void DuplicateNameCheck_IsCaseInsensitive()
        {
            _service.CreatePromotion("Summer Sale", DateTime.Now, DateTime.Now.AddDays(7),
                PromotionDiscountType.Percentage, 10);

            Assert.ThrowsException<InvalidOperationException>(() =>
                _service.CreatePromotion("summer sale", DateTime.Now, DateTime.Now.AddDays(7),
                    PromotionDiscountType.Percentage, 10));
        }
    }
}
