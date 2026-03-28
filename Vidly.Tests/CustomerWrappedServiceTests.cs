using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests.Services
{
    [TestClass]
    public class CustomerWrappedServiceTests
    {
        private InMemoryRentalRepository _rentalRepo;
        private InMemoryMovieRepository _movieRepo;
        private InMemoryCustomerRepository _customerRepo;
        private CustomerWrappedService _service;

        [TestInitialize]
        public void Setup()
        {
            _rentalRepo = new InMemoryRentalRepository();
            _movieRepo = new InMemoryMovieRepository();
            _customerRepo = new InMemoryCustomerRepository();

            InMemoryRentalRepository.Reset();
            InMemoryMovieRepository.ResetEmpty();
            InMemoryCustomerRepository.Reset();

            _service = new CustomerWrappedService(_rentalRepo, _movieRepo, _customerRepo);
        }

        // ── Constructor ──

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new CustomerWrappedService(null, _movieRepo, _customerRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new CustomerWrappedService(_rentalRepo, null, _customerRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new CustomerWrappedService(_rentalRepo, _movieRepo, null);
        }

        // ── GetAllTime ──

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetAllTime_NonExistentCustomer_Throws()
        {
            _service.GetAllTime(999);
        }

        [TestMethod]
        public void GetAllTime_NoRentals_ReturnsEmptyWrapped()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Alice" });

            var result = _service.GetAllTime(1);

            Assert.AreEqual(1, result.CustomerId);
            Assert.AreEqual("Alice", result.CustomerName);
            Assert.AreEqual(0, result.TotalRentals);
            Assert.IsTrue(result.IsAllTime);
            Assert.AreEqual("The Ghost", result.RentalPersonality);
        }

        [TestMethod]
        public void GetAllTime_SingleRental_BasicStats()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Bob" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Matrix", Genre = Genre.SciFi, Rating = 5 });
            _rentalRepo.Add(new Rental
            {
                Id = 1, CustomerId = 1, MovieId = 10,
                RentalDate = new DateTime(2025, 6, 1),
                DueDate = new DateTime(2025, 6, 8),
                ReturnDate = new DateTime(2025, 6, 5),
                DailyRate = 3.99m, LateFee = 0m,
                Status = RentalStatus.Returned
            });

            var result = _service.GetAllTime(1);

            Assert.AreEqual(1, result.TotalRentals);
            Assert.AreEqual(1, result.UniqueMovies);
            Assert.AreEqual(0, result.RepeatRentals);
            Assert.AreEqual(Genre.SciFi, result.FavoriteGenre);
            Assert.AreEqual("Matrix", result.TopRatedMovieRented);
        }

        [TestMethod]
        public void GetAllTime_MultipleRentals_CorrectSpending()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Carol" });
            _movieRepo.Add(new Movie { Id = 10, Name = "M1", Genre = Genre.Action });
            _movieRepo.Add(new Movie { Id = 11, Name = "M2", Genre = Genre.Comedy });

            _rentalRepo.Add(new Rental
            {
                Id = 1, CustomerId = 1, MovieId = 10,
                RentalDate = new DateTime(2025, 3, 1),
                DueDate = new DateTime(2025, 3, 4),
                ReturnDate = new DateTime(2025, 3, 3),
                DailyRate = 2.00m, LateFee = 0m,
                Status = RentalStatus.Returned
            });
            _rentalRepo.Add(new Rental
            {
                Id = 2, CustomerId = 1, MovieId = 11,
                RentalDate = new DateTime(2025, 3, 10),
                DueDate = new DateTime(2025, 3, 13),
                ReturnDate = new DateTime(2025, 3, 15),
                DailyRate = 2.00m, LateFee = 4.00m,
                Status = RentalStatus.Returned
            });

            var result = _service.GetAllTime(1);

            Assert.AreEqual(2, result.TotalRentals);
            Assert.AreEqual(4.00m, result.TotalLateFees);
            Assert.IsTrue(result.TotalSpent > 0);
        }

        [TestMethod]
        public void GetAllTime_RepeatMovies_Counted()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Dave" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Film", Genre = Genre.Drama });

            for (int i = 0; i < 3; i++)
            {
                _rentalRepo.Add(new Rental
                {
                    Id = i + 1, CustomerId = 1, MovieId = 10,
                    RentalDate = new DateTime(2025, 1 + i, 1),
                    DueDate = new DateTime(2025, 1 + i, 5),
                    ReturnDate = new DateTime(2025, 1 + i, 4),
                    DailyRate = 1.00m, LateFee = 0m,
                    Status = RentalStatus.Returned
                });
            }

            var result = _service.GetAllTime(1);

            Assert.AreEqual(3, result.TotalRentals);
            Assert.AreEqual(1, result.UniqueMovies);
            Assert.AreEqual(2, result.RepeatRentals);
        }

        [TestMethod]
        public void GetAllTime_GenreDiversity_SingleGenre_Zero()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Eve" });
            _movieRepo.Add(new Movie { Id = 10, Name = "A", Genre = Genre.Horror });
            _movieRepo.Add(new Movie { Id = 11, Name = "B", Genre = Genre.Horror });

            AddSimpleRental(1, 1, 10, new DateTime(2025, 1, 1));
            AddSimpleRental(2, 1, 11, new DateTime(2025, 1, 5));

            var result = _service.GetAllTime(1);

            Assert.AreEqual(0.0, result.GenreDiversity, 0.001);
        }

        [TestMethod]
        public void GetAllTime_GenreDiversity_EvenSpread_High()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Frank" });
            var genres = new[] { Genre.Action, Genre.Comedy, Genre.Drama, Genre.Horror };
            for (int i = 0; i < 4; i++)
            {
                _movieRepo.Add(new Movie { Id = 10 + i, Name = $"M{i}", Genre = genres[i] });
                AddSimpleRental(i + 1, 1, 10 + i, new DateTime(2025, 1, 1 + i));
            }

            var result = _service.GetAllTime(1);

            Assert.IsTrue(result.GenreDiversity > 0.9, $"Expected high diversity, got {result.GenreDiversity}");
        }

        [TestMethod]
        public void GetAllTime_ConsecutiveDayStreak_Detected()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Grace" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Film", Genre = Genre.Action });

            for (int i = 0; i < 5; i++)
            {
                AddSimpleRental(i + 1, 1, 10, new DateTime(2025, 3, 10 + i));
            }

            var result = _service.GetAllTime(1);

            Assert.AreEqual(5, result.LongestRentalStreak);
            Assert.AreEqual(new DateTime(2025, 3, 10), result.StreakStartDate);
        }

        [TestMethod]
        public void GetAllTime_NoStreak_SingleDay()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Hank" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Film", Genre = Genre.Action });

            AddSimpleRental(1, 1, 10, new DateTime(2025, 5, 1));

            var result = _service.GetAllTime(1);

            Assert.AreEqual(1, result.LongestRentalStreak);
        }

        [TestMethod]
        public void GetAllTime_FavoriteRentalDay_Correct()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Ivy" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Film", Genre = Genre.Comedy });

            // Add 3 Friday rentals and 1 Monday rental
            // 2025-01-03 is Friday, 2025-01-10 is Friday, 2025-01-17 is Friday
            AddSimpleRental(1, 1, 10, new DateTime(2025, 1, 3));
            AddSimpleRental(2, 1, 10, new DateTime(2025, 1, 10));
            AddSimpleRental(3, 1, 10, new DateTime(2025, 1, 17));
            AddSimpleRental(4, 1, 10, new DateTime(2025, 1, 6)); // Monday

            var result = _service.GetAllTime(1);

            Assert.AreEqual(DayOfWeek.Friday, result.FavoriteRentalDay);
        }

        [TestMethod]
        public void GetAllTime_GenreBreakdown_OrderedDescending()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Jack" });
            _movieRepo.Add(new Movie { Id = 10, Name = "A", Genre = Genre.Action });
            _movieRepo.Add(new Movie { Id = 11, Name = "B", Genre = Genre.Comedy });

            AddSimpleRental(1, 1, 10, new DateTime(2025, 1, 1));
            AddSimpleRental(2, 1, 10, new DateTime(2025, 1, 5));
            AddSimpleRental(3, 1, 10, new DateTime(2025, 1, 10));
            AddSimpleRental(4, 1, 11, new DateTime(2025, 1, 15));

            var result = _service.GetAllTime(1);

            Assert.AreEqual(2, result.GenreBreakdown.Count);
            Assert.AreEqual(Genre.Action, result.GenreBreakdown[0].Genre);
            Assert.AreEqual(3, result.GenreBreakdown[0].Count);
            Assert.AreEqual(75.0, result.GenreBreakdown[0].Percentage, 0.1);
        }

        // ── GetForYear ──

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetForYear_NonExistentCustomer_Throws()
        {
            _service.GetForYear(999, 2025);
        }

        [TestMethod]
        public void GetForYear_FiltersToYear()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Kate" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Film", Genre = Genre.Drama });

            AddSimpleRental(1, 1, 10, new DateTime(2024, 6, 1));
            AddSimpleRental(2, 1, 10, new DateTime(2025, 3, 1));
            AddSimpleRental(3, 1, 10, new DateTime(2025, 9, 1));
            AddSimpleRental(4, 1, 10, new DateTime(2026, 1, 1));

            var result = _service.GetForYear(1, 2025);

            Assert.AreEqual(2, result.TotalRentals);
            Assert.IsFalse(result.IsAllTime);
            Assert.AreEqual(new DateTime(2025, 1, 1), result.PeriodStart);
        }

        [TestMethod]
        public void GetForYear_EmptyYear_ReturnsGhost()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Leo" });

            var result = _service.GetForYear(1, 2020);

            Assert.AreEqual(0, result.TotalRentals);
            Assert.AreEqual("The Ghost", result.RentalPersonality);
        }

        // ── Personality ──

        [TestMethod]
        public void Personality_HighLateFees_Procrastinator()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Mia" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Film", Genre = Genre.Drama });

            // High late fee relative to cost
            _rentalRepo.Add(new Rental
            {
                Id = 1, CustomerId = 1, MovieId = 10,
                RentalDate = new DateTime(2025, 1, 1),
                DueDate = new DateTime(2025, 1, 3),
                ReturnDate = new DateTime(2025, 1, 2),
                DailyRate = 1.00m, LateFee = 5.00m,
                Status = RentalStatus.Returned
            });

            var result = _service.GetAllTime(1);

            Assert.AreEqual("The Procrastinator", result.RentalPersonality);
        }

        [TestMethod]
        public void Personality_SciFiFan_Futurist()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Nina" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Blade Runner", Genre = Genre.SciFi });

            AddSimpleRental(1, 1, 10, new DateTime(2025, 1, 1));

            var result = _service.GetAllTime(1);

            Assert.AreEqual("The Futurist", result.RentalPersonality);
        }

        // ── GetLeaderboard ──

        [TestMethod]
        public void GetLeaderboard_SortedByTotalRentals()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "P1" });
            _customerRepo.Add(new Customer { Id = 2, Name = "P2" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Film", Genre = Genre.Action });

            AddSimpleRental(1, 1, 10, new DateTime(2025, 1, 1));
            AddSimpleRental(2, 2, 10, new DateTime(2025, 1, 1));
            AddSimpleRental(3, 2, 10, new DateTime(2025, 1, 5));
            AddSimpleRental(4, 2, 10, new DateTime(2025, 1, 10));

            var board = _service.GetLeaderboard();

            Assert.AreEqual(2, board.Count);
            Assert.AreEqual("P2", board[0].CustomerName);
            Assert.AreEqual(3, board[0].TotalRentals);
        }

        [TestMethod]
        public void GetLeaderboard_WithYear_FiltersCorrectly()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Q1" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Film", Genre = Genre.Action });

            AddSimpleRental(1, 1, 10, new DateTime(2024, 6, 1));
            AddSimpleRental(2, 1, 10, new DateTime(2025, 6, 1));

            var board2024 = _service.GetLeaderboard(2024);
            var board2025 = _service.GetLeaderboard(2025);

            Assert.AreEqual(1, board2024.Count);
            Assert.AreEqual(1, board2024[0].TotalRentals);
            Assert.AreEqual(1, board2025.Count);
            Assert.AreEqual(1, board2025[0].TotalRentals);
        }

        [TestMethod]
        public void GetLeaderboard_SkipsCustomersWithNoRentals()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "R1" });
            _customerRepo.Add(new Customer { Id = 2, Name = "R2" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Film", Genre = Genre.Action });

            AddSimpleRental(1, 1, 10, new DateTime(2025, 1, 1));

            var board = _service.GetLeaderboard();

            Assert.AreEqual(1, board.Count);
            Assert.AreEqual("R1", board[0].CustomerName);
        }

        // ── GenerateReport ──

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void GenerateReport_Null_Throws()
        {
            _service.GenerateReport(null);
        }

        [TestMethod]
        public void GenerateReport_NoRentals_ShowsMessage()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Sam" });
            var wrapped = _service.GetAllTime(1);
            var report = _service.GenerateReport(wrapped);

            Assert.IsTrue(report.Contains("No rentals found"));
        }

        [TestMethod]
        public void GenerateReport_WithRentals_ContainsAllSections()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Tina" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Star Wars", Genre = Genre.SciFi, Rating = 5 });

            AddSimpleRental(1, 1, 10, new DateTime(2025, 3, 1));
            AddSimpleRental(2, 1, 10, new DateTime(2025, 3, 2));

            var wrapped = _service.GetAllTime(1);
            var report = _service.GenerateReport(wrapped);

            Assert.IsTrue(report.Contains("Tina"));
            Assert.IsTrue(report.Contains("BY THE NUMBERS"));
            Assert.IsTrue(report.Contains("TIMING"));
            Assert.IsTrue(report.Contains("GENRE PROFILE"));
            Assert.IsTrue(report.Contains("PERSONALITY"));
        }

        [TestMethod]
        public void GenerateReport_YearScope_ShowsYear()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Uma" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Film", Genre = Genre.Action });

            AddSimpleRental(1, 1, 10, new DateTime(2025, 6, 1));

            var wrapped = _service.GetForYear(1, 2025);
            var report = _service.GenerateReport(wrapped);

            Assert.IsTrue(report.Contains("2025"));
        }

        // ── Duration ──

        [TestMethod]
        public void Duration_MinOneDayForSameDayReturn()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Vera" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Film", Genre = Genre.Action });

            _rentalRepo.Add(new Rental
            {
                Id = 1, CustomerId = 1, MovieId = 10,
                RentalDate = new DateTime(2025, 5, 1),
                DueDate = new DateTime(2025, 5, 3),
                ReturnDate = new DateTime(2025, 5, 1),
                DailyRate = 2.00m, LateFee = 0m,
                Status = RentalStatus.Returned
            });

            var result = _service.GetAllTime(1);

            Assert.IsTrue(result.ShortestRentalDays >= 1);
        }

        // ── TopRatedMovie ──

        [TestMethod]
        public void TopRatedMovie_PicksHighestRating()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Wes" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Okay Film", Genre = Genre.Drama, Rating = 3 });
            _movieRepo.Add(new Movie { Id = 11, Name = "Great Film", Genre = Genre.Action, Rating = 5 });

            AddSimpleRental(1, 1, 10, new DateTime(2025, 1, 1));
            AddSimpleRental(2, 1, 11, new DateTime(2025, 1, 5));

            var result = _service.GetAllTime(1);

            Assert.AreEqual("Great Film", result.TopRatedMovieRented);
        }

        [TestMethod]
        public void TopRatedMovie_NoRatings_Null()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Xena" });
            _movieRepo.Add(new Movie { Id = 10, Name = "Unrated", Genre = Genre.Action });

            AddSimpleRental(1, 1, 10, new DateTime(2025, 1, 1));

            var result = _service.GetAllTime(1);

            Assert.IsNull(result.TopRatedMovieRented);
        }

        // ── Helpers ──

        private void AddSimpleRental(int id, int customerId, int movieId, DateTime rentalDate)
        {
            _rentalRepo.Add(new Rental
            {
                Id = id,
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(7),
                ReturnDate = rentalDate.AddDays(3),
                DailyRate = 2.99m,
                LateFee = 0m,
                Status = RentalStatus.Returned
            });
        }
    }
}
