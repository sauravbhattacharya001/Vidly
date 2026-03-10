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
    public class MovieComparisonServiceTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.ResetEmpty();
            InMemoryReviewRepository.ResetEmpty();
            InMemoryRentalRepository.Reset();
        }

        private MovieComparisonService CreateService()
        {
            return new MovieComparisonService(
                new InMemoryMovieRepository(),
                new InMemoryReviewRepository(),
                new InMemoryRentalRepository());
        }

        private Movie AddMovie(string name, int? rating = null, decimal? dailyRate = null,
            DateTime? releaseDate = null)
        {
            var repo = new InMemoryMovieRepository();
            var movie = new Movie
            {
                Name = name,
                Rating = rating,
                DailyRate = dailyRate,
                ReleaseDate = releaseDate ?? DateTime.Today.AddYears(-1),
                Genre = Genre.Action
            };
            repo.Add(movie);
            return movie;
        }

        private void AddReview(int movieId, int stars)
        {
            var repo = new InMemoryReviewRepository();
            repo.Add(new Review
            {
                MovieId = movieId,
                Stars = stars,
                CustomerId = 1,
                ReviewText = "Test review"
            });
        }

        private void AddRental(int movieId, RentalStatus status = RentalStatus.Returned)
        {
            var repo = new InMemoryRentalRepository();
            repo.Add(new Rental
            {
                MovieId = movieId,
                CustomerId = 1,
                CustomerName = "Test",
                MovieName = "Test",
                RentalDate = DateTime.Today.AddDays(-7),
                DueDate = DateTime.Today.AddDays(-2),
                ReturnDate = status == RentalStatus.Returned ? DateTime.Today.AddDays(-3) : (DateTime?)null,
                Status = status,
                DailyRate = 1.99m
            });
        }

        // ── Constructor tests ────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new MovieComparisonService(null, new InMemoryReviewRepository(), new InMemoryRentalRepository());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullReviewRepo_Throws()
        {
            new MovieComparisonService(new InMemoryMovieRepository(), null, new InMemoryRentalRepository());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new MovieComparisonService(new InMemoryMovieRepository(), new InMemoryReviewRepository(), null);
        }

        // ── Compare: validation ──────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Compare_NullIds_Throws()
        {
            var service = CreateService();
            service.Compare(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Compare_SingleId_Throws()
        {
            var movie = AddMovie("Solo");
            var service = CreateService();
            service.Compare(new[] { movie.Id });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Compare_FiveIds_Throws()
        {
            var movies = Enumerable.Range(0, 5).Select(i => AddMovie($"Movie{i}")).ToList();
            var service = CreateService();
            service.Compare(movies.Select(m => m.Id));
        }

        [TestMethod]
        public void Compare_DuplicateIds_DeduplicatedAndThrowsIfLessThanTwo()
        {
            var movie = AddMovie("Dup");
            var service = CreateService();

            // Two duplicate IDs = 1 unique, should throw
            try
            {
                service.Compare(new[] { movie.Id, movie.Id });
                Assert.Fail("Expected ArgumentException for single unique movie");
            }
            catch (ArgumentException) { }
        }

        // ── Compare: basic functionality ─────────────────────────

        [TestMethod]
        public void Compare_TwoMovies_ReturnsCorrectEntryCount()
        {
            var m1 = AddMovie("Movie A");
            var m2 = AddMovie("Movie B");
            var service = CreateService();

            var result = service.Compare(new[] { m1.Id, m2.Id });

            Assert.AreEqual(2, result.Entries.Count);
        }

        [TestMethod]
        public void Compare_FourMovies_Succeeds()
        {
            var movies = Enumerable.Range(0, 4).Select(i => AddMovie($"M{i}")).ToList();
            var service = CreateService();

            var result = service.Compare(movies.Select(m => m.Id));

            Assert.AreEqual(4, result.Entries.Count);
        }

        [TestMethod]
        public void Compare_SetsComparedAtTimestamp()
        {
            var m1 = AddMovie("A");
            var m2 = AddMovie("B");
            var service = CreateService();

            var before = DateTime.Now;
            var result = service.Compare(new[] { m1.Id, m2.Id });

            Assert.IsTrue(result.ComparedAt >= before && result.ComparedAt <= DateTime.Now);
        }

        // ── Compare: ratings ─────────────────────────────────────

        [TestMethod]
        public void Compare_BestRatedId_IdentifiesHighestRated()
        {
            var m1 = AddMovie("Low Rated");
            var m2 = AddMovie("High Rated");
            AddReview(m1.Id, 2);
            AddReview(m2.Id, 5);

            var service = CreateService();
            var result = service.Compare(new[] { m1.Id, m2.Id });

            Assert.AreEqual(m2.Id, result.BestRatedId);
        }

        [TestMethod]
        public void Compare_NoReviews_AverageRatingIsNull()
        {
            var m1 = AddMovie("A");
            var m2 = AddMovie("B");
            var service = CreateService();

            var result = service.Compare(new[] { m1.Id, m2.Id });

            Assert.IsNull(result.Entries[0].AverageRating);
            Assert.AreEqual(0, result.Entries[0].ReviewCount);
        }

        [TestMethod]
        public void Compare_MultipleReviews_AverageIsCorrect()
        {
            var m1 = AddMovie("A");
            var m2 = AddMovie("B");
            AddReview(m1.Id, 3);
            AddReview(m1.Id, 5);

            var service = CreateService();
            var result = service.Compare(new[] { m1.Id, m2.Id });

            var entry = result.Entries.First(e => e.Movie.Id == m1.Id);
            Assert.AreEqual(4.0, entry.AverageRating);
            Assert.AreEqual(2, entry.ReviewCount);
        }

        // ── Compare: rentals & availability ──────────────────────

        [TestMethod]
        public void Compare_MostPopularId_IdentifiesMostRented()
        {
            var m1 = AddMovie("Popular");
            var m2 = AddMovie("Unpopular");
            AddRental(m1.Id);
            AddRental(m1.Id);
            AddRental(m2.Id);

            var service = CreateService();
            var result = service.Compare(new[] { m1.Id, m2.Id });

            Assert.AreEqual(m1.Id, result.MostPopularId);
        }

        [TestMethod]
        public void Compare_ActiveRental_MarksUnavailable()
        {
            var m1 = AddMovie("Rented Out");
            var m2 = AddMovie("Available");
            AddRental(m1.Id, RentalStatus.Active);

            var service = CreateService();
            var result = service.Compare(new[] { m1.Id, m2.Id });

            var rented = result.Entries.First(e => e.Movie.Id == m1.Id);
            var available = result.Entries.First(e => e.Movie.Id == m2.Id);

            Assert.IsTrue(rented.CurrentlyRented);
            Assert.IsFalse(rented.IsAvailable);
            Assert.IsFalse(available.CurrentlyRented);
            Assert.IsTrue(available.IsAvailable);
        }

        // ── Compare: pricing ─────────────────────────────────────

        [TestMethod]
        public void Compare_CheapestId_IdentifiesLowestRate()
        {
            var m1 = AddMovie("Cheap", dailyRate: 0.99m);
            var m2 = AddMovie("Expensive", dailyRate: 4.99m);

            var service = CreateService();
            var result = service.Compare(new[] { m1.Id, m2.Id });

            Assert.AreEqual(m1.Id, result.CheapestId);
        }

        [TestMethod]
        public void Compare_WeeklyEstimate_Is7xDailyRate()
        {
            var m1 = AddMovie("A", dailyRate: 2.00m);
            var m2 = AddMovie("B", dailyRate: 3.00m);

            var service = CreateService();
            var result = service.Compare(new[] { m1.Id, m2.Id });

            foreach (var entry in result.Entries)
            {
                Assert.AreEqual(entry.DailyRate * 7, entry.WeeklyEstimate);
            }
        }

        // ── Compare: age calculations ────────────────────────────

        [TestMethod]
        public void Compare_MovieWithReleaseDate_CalculatesAgeDays()
        {
            var releaseDate = DateTime.Today.AddDays(-100);
            var m1 = AddMovie("Old", releaseDate: releaseDate);
            var m2 = AddMovie("New");

            var service = CreateService();
            var result = service.Compare(new[] { m1.Id, m2.Id });

            var entry = result.Entries.First(e => e.Movie.Id == m1.Id);
            Assert.AreEqual(100, entry.AgeDays);
        }

        // ── Compare: missing movies ──────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Compare_NonexistentMovies_ThrowsWhenNotEnough()
        {
            var m1 = AddMovie("Real");
            var service = CreateService();

            // One real, one fake — only 1 valid entry, should throw
            service.Compare(new[] { m1.Id, 99999 });
        }

        // ── GetAvailableMovies ───────────────────────────────────

        [TestMethod]
        public void GetAvailableMovies_ReturnsAllMovies()
        {
            AddMovie("A");
            AddMovie("B");
            AddMovie("C");

            var service = CreateService();
            var movies = service.GetAvailableMovies();

            Assert.AreEqual(3, movies.Count);
        }

        // ── Model display tests ──────────────────────────────────

        [TestMethod]
        public void AgeDisplay_LessThan30Days()
        {
            var entry = new MovieComparisonEntry { AgeDays = 15 };
            Assert.AreEqual("15 days", entry.AgeDisplay);
        }

        [TestMethod]
        public void AgeDisplay_OneDay()
        {
            var entry = new MovieComparisonEntry { AgeDays = 1 };
            Assert.AreEqual("1 day", entry.AgeDisplay);
        }

        [TestMethod]
        public void AgeDisplay_Months()
        {
            var entry = new MovieComparisonEntry { AgeDays = 90 };
            Assert.AreEqual("3 months", entry.AgeDisplay);
        }

        [TestMethod]
        public void AgeDisplay_OneMonth()
        {
            var entry = new MovieComparisonEntry { AgeDays = 30 };
            Assert.AreEqual("1 month", entry.AgeDisplay);
        }

        [TestMethod]
        public void AgeDisplay_Years()
        {
            var entry = new MovieComparisonEntry { AgeDays = 730 };
            Assert.AreEqual("2 years", entry.AgeDisplay);
        }

        [TestMethod]
        public void AgeDisplay_Unknown()
        {
            var entry = new MovieComparisonEntry { AgeDays = null };
            Assert.AreEqual("Unknown", entry.AgeDisplay);
        }

        [TestMethod]
        public void AgeDisplay_Upcoming()
        {
            var entry = new MovieComparisonEntry { AgeDays = -5 };
            Assert.AreEqual("Upcoming", entry.AgeDisplay);
        }

        [TestMethod]
        public void StarDisplay_FiveStars()
        {
            var entry = new MovieComparisonEntry { AverageRating = 5.0 };
            Assert.AreEqual("★★★★★", entry.StarDisplay);
        }

        [TestMethod]
        public void StarDisplay_ThreeStars()
        {
            var entry = new MovieComparisonEntry { AverageRating = 3.2 };
            Assert.AreEqual("★★★☆☆", entry.StarDisplay);
        }

        [TestMethod]
        public void StarDisplay_NoRatings()
        {
            var entry = new MovieComparisonEntry { AverageRating = null };
            Assert.AreEqual("No ratings", entry.StarDisplay);
        }

        [TestMethod]
        public void StarDisplay_ZeroStars()
        {
            var entry = new MovieComparisonEntry { AverageRating = 0.0 };
            Assert.AreEqual("☆☆☆☆☆", entry.StarDisplay);
        }
    }
}
