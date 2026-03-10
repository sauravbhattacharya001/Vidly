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
    public class AvailabilityServiceTests
    {
        // ── Test Doubles ─────────────────────────────────────────

        private class TestMovieRepository : IMovieRepository
        {
            private readonly Dictionary<int, Movie> _movies = new Dictionary<int, Movie>();
            public void Add(Movie m) => _movies[m.Id] = m;
            public Movie GetById(int id) => _movies.TryGetValue(id, out var m) ? m : null;
            public IReadOnlyList<Movie> GetAll() => _movies.Values.ToList().AsReadOnly();
            public void Update(Movie m) { if (_movies.ContainsKey(m.Id)) _movies[m.Id] = m; }
            public void Remove(int id) => _movies.Remove(id);
            public IReadOnlyList<Movie> GetByReleaseDate(int y, int m) => new List<Movie>().AsReadOnly();
            public Movie GetRandom() => _movies.Values.FirstOrDefault();
            public IReadOnlyList<Movie> Search(string q, Genre? g, int? r) => GetAll();
        }

        private class TestRentalRepository : IRentalRepository
        {
            private readonly List<Rental> _rentals = new List<Rental>();
            private int _nextId = 1;
            public void Add(Rental r) { r.Id = _nextId++; _rentals.Add(r); }
            public Rental GetById(int id) => _rentals.FirstOrDefault(r => r.Id == id);
            public IReadOnlyList<Rental> GetAll() => _rentals.AsReadOnly();
            public void Update(Rental r) { var i = _rentals.FindIndex(x => x.Id == r.Id); if (i >= 0) _rentals[i] = r; }
            public void Remove(int id) => _rentals.RemoveAll(r => r.Id == id);
            public IReadOnlyList<Rental> GetActiveByCustomer(int cid) =>
                _rentals.Where(r => r.CustomerId == cid && r.Status != RentalStatus.Returned).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetByMovie(int mid) =>
                _rentals.Where(r => r.MovieId == mid).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetOverdue() =>
                _rentals.Where(r => r.IsOverdue).ToList().AsReadOnly();
            public IReadOnlyList<Rental> Search(string q, RentalStatus? s) => GetAll();
            public Rental ReturnRental(int id) { var r = GetById(id); if (r != null) { r.Status = RentalStatus.Returned; r.ReturnDate = DateTime.Today; } return r; }
            public bool IsMovieRentedOut(int mid) => _rentals.Any(r => r.MovieId == mid && r.Status != RentalStatus.Returned);
            public Rental Checkout(Rental r) { Add(r); return r; }
            public Rental Checkout(Rental r, int max) { Add(r); return r; }
            public RentalStats GetStats() => new RentalStats { TotalRentals = _rentals.Count };
        }

        // ── Helpers ──────────────────────────────────────────────

        private TestMovieRepository _movies;
        private TestRentalRepository _rentals;
        private AvailabilityService _service;

        [TestInitialize]
        public void Setup()
        {
            _movies = new TestMovieRepository();
            _rentals = new TestRentalRepository();
            _service = new AvailabilityService(_movies, _rentals);
        }

        private void SeedMovies(params Movie[] movies)
        {
            foreach (var m in movies) _movies.Add(m);
        }

        private void SeedRental(int movieId, int customerId, string customerName,
            string movieName, DateTime rentalDate, DateTime dueDate,
            RentalStatus status = RentalStatus.Active)
        {
            _rentals.Add(new Rental
            {
                MovieId = movieId,
                CustomerId = customerId,
                CustomerName = customerName,
                MovieName = movieName,
                RentalDate = rentalDate,
                DueDate = dueDate,
                Status = status,
                DailyRate = 3.99m
            });
        }

        // ── GetAllAvailability ───────────────────────────────────

        [TestMethod]
        public void GetAllAvailability_NoMovies_ReturnsEmpty()
        {
            var result = _service.GetAllAvailability();
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetAllAvailability_AllAvailable_AllMarkedAvailable()
        {
            SeedMovies(
                new Movie { Id = 1, Name = "A", Genre = Genre.Action },
                new Movie { Id = 2, Name = "B", Genre = Genre.Comedy });

            var result = _service.GetAllAvailability();
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.All(a => a.IsAvailable));
        }

        [TestMethod]
        public void GetAllAvailability_RentedMovie_MarkedUnavailable()
        {
            SeedMovies(new Movie { Id = 1, Name = "A", Genre = Genre.Action });
            SeedRental(1, 10, "Alice", "A", DateTime.Today.AddDays(-3),
                DateTime.Today.AddDays(4));

            var result = _service.GetAllAvailability();
            Assert.AreEqual(1, result.Count);
            Assert.IsFalse(result[0].IsAvailable);
            Assert.AreEqual("Alice", result[0].RentedByCustomer);
            Assert.AreEqual(DateTime.Today.AddDays(4), result[0].ExpectedReturnDate);
        }

        [TestMethod]
        public void GetAllAvailability_OverdueRental_MarkedOverdue()
        {
            SeedMovies(new Movie { Id = 1, Name = "A" });
            SeedRental(1, 10, "Bob", "A", DateTime.Today.AddDays(-10),
                DateTime.Today.AddDays(-2));

            var result = _service.GetAllAvailability();
            Assert.IsTrue(result[0].IsOverdue);
            Assert.AreEqual(AvailabilityCategory.Overdue, result[0].Category);
        }

        [TestMethod]
        public void GetAllAvailability_GenreFilter_FiltersCorrectly()
        {
            SeedMovies(
                new Movie { Id = 1, Name = "A", Genre = Genre.Action },
                new Movie { Id = 2, Name = "B", Genre = Genre.Comedy });

            var result = _service.GetAllAvailability(genre: Genre.Action);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("A", result[0].MovieName);
        }

        [TestMethod]
        public void GetAllAvailability_AvailableOnly_FiltersRented()
        {
            SeedMovies(
                new Movie { Id = 1, Name = "A" },
                new Movie { Id = 2, Name = "B" });
            SeedRental(1, 10, "Alice", "A", DateTime.Today, DateTime.Today.AddDays(7));

            var result = _service.GetAllAvailability(availableOnly: true);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("B", result[0].MovieName);
        }

        [TestMethod]
        public void GetAllAvailability_QueryFilter_SearchesByName()
        {
            SeedMovies(
                new Movie { Id = 1, Name = "The Matrix" },
                new Movie { Id = 2, Name = "Inception" });

            var result = _service.GetAllAvailability(query: "matrix");
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("The Matrix", result[0].MovieName);
        }

        [TestMethod]
        public void GetAllAvailability_ReturnedRental_MovieShownAsAvailable()
        {
            SeedMovies(new Movie { Id = 1, Name = "A" });
            SeedRental(1, 10, "Alice", "A", DateTime.Today.AddDays(-7),
                DateTime.Today.AddDays(-1), RentalStatus.Returned);

            var result = _service.GetAllAvailability();
            Assert.IsTrue(result[0].IsAvailable);
        }

        [TestMethod]
        public void GetAllAvailability_ComingSoon_CorrectCategory()
        {
            SeedMovies(new Movie { Id = 1, Name = "A" });
            SeedRental(1, 10, "Alice", "A", DateTime.Today.AddDays(-5),
                DateTime.Today.AddDays(1));

            var result = _service.GetAllAvailability();
            Assert.AreEqual(AvailabilityCategory.ComingSoon, result[0].Category);
        }

        [TestMethod]
        public void GetAllAvailability_SortOrder_AvailableFirst()
        {
            SeedMovies(
                new Movie { Id = 1, Name = "Rented" },
                new Movie { Id = 2, Name = "Available" });
            SeedRental(1, 10, "Alice", "Rented", DateTime.Today,
                DateTime.Today.AddDays(7));

            var result = _service.GetAllAvailability();
            Assert.AreEqual("Available", result[0].MovieName);
        }

        // ── GetMovieAvailability ─────────────────────────────────

        [TestMethod]
        public void GetMovieAvailability_ExistingMovie_ReturnsInfo()
        {
            SeedMovies(new Movie { Id = 1, Name = "Test", Genre = Genre.Drama, Rating = 4 });

            var result = _service.GetMovieAvailability(1);
            Assert.IsNotNull(result);
            Assert.AreEqual("Test", result.MovieName);
            Assert.IsTrue(result.IsAvailable);
        }

        [TestMethod]
        public void GetMovieAvailability_NonExistent_ReturnsNull()
        {
            var result = _service.GetMovieAvailability(999);
            Assert.IsNull(result);
        }

        // ── GetAvailabilityCalendar ──────────────────────────────

        [TestMethod]
        public void GetAvailabilityCalendar_DefaultDays_Returns14Days()
        {
            var result = _service.GetAvailabilityCalendar();
            Assert.AreEqual(14, result.Count);
            Assert.AreEqual(DateTime.Today, result[0].Date);
        }

        [TestMethod]
        public void GetAvailabilityCalendar_CustomDays_ReturnsCorrectCount()
        {
            var result = _service.GetAvailabilityCalendar(7);
            Assert.AreEqual(7, result.Count);
        }

        [TestMethod]
        public void GetAvailabilityCalendar_ClampsToMax90()
        {
            var result = _service.GetAvailabilityCalendar(200);
            Assert.AreEqual(90, result.Count);
        }

        [TestMethod]
        public void GetAvailabilityCalendar_ClampsToMin1()
        {
            var result = _service.GetAvailabilityCalendar(0);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void GetAvailabilityCalendar_RentalDueInRange_ShowsEvent()
        {
            SeedMovies(new Movie { Id = 1, Name = "A" });
            SeedRental(1, 10, "Alice", "A", DateTime.Today.AddDays(-3),
                DateTime.Today.AddDays(2));

            var result = _service.GetAvailabilityCalendar(7);
            var dayWithEvent = result.FirstOrDefault(d => d.Date == DateTime.Today.AddDays(2));
            Assert.IsNotNull(dayWithEvent);
            Assert.AreEqual(1, dayWithEvent.ReturningMovies.Count);
            Assert.AreEqual("A", dayWithEvent.ReturningMovies[0].MovieName);
        }

        [TestMethod]
        public void GetAvailabilityCalendar_RentalDueOutOfRange_NotShown()
        {
            SeedMovies(new Movie { Id = 1, Name = "A" });
            SeedRental(1, 10, "Alice", "A", DateTime.Today,
                DateTime.Today.AddDays(30));

            var result = _service.GetAvailabilityCalendar(7);
            Assert.IsTrue(result.All(d => d.EventCount == 0));
        }

        [TestMethod]
        public void GetAvailabilityCalendar_TodayIsMarked()
        {
            var result = _service.GetAvailabilityCalendar(3);
            Assert.IsTrue(result[0].IsToday);
            Assert.IsFalse(result[1].IsToday);
        }

        [TestMethod]
        public void GetAvailabilityCalendar_MultipleReturnsSameDay()
        {
            SeedMovies(
                new Movie { Id = 1, Name = "A" },
                new Movie { Id = 2, Name = "B" });
            var dueDate = DateTime.Today.AddDays(3);
            SeedRental(1, 10, "Alice", "A", DateTime.Today, dueDate);
            SeedRental(2, 11, "Bob", "B", DateTime.Today, dueDate);

            var result = _service.GetAvailabilityCalendar(7);
            var day = result.First(d => d.Date == dueDate);
            Assert.AreEqual(2, day.ReturningMovies.Count);
        }

        // ── GetSummary ───────────────────────────────────────────

        [TestMethod]
        public void GetSummary_EmptyCatalog_ZeroCounts()
        {
            var summary = _service.GetSummary();
            Assert.AreEqual(0, summary.TotalMovies);
            Assert.AreEqual(0, summary.AvailableNow);
        }

        [TestMethod]
        public void GetSummary_MixedAvailability_CorrectCounts()
        {
            SeedMovies(
                new Movie { Id = 1, Name = "A", Genre = Genre.Action },
                new Movie { Id = 2, Name = "B", Genre = Genre.Action },
                new Movie { Id = 3, Name = "C", Genre = Genre.Comedy });
            SeedRental(1, 10, "Alice", "A", DateTime.Today,
                DateTime.Today.AddDays(7));
            SeedRental(3, 11, "Bob", "C", DateTime.Today.AddDays(-10),
                DateTime.Today.AddDays(-2));

            var summary = _service.GetSummary();
            Assert.AreEqual(3, summary.TotalMovies);
            Assert.AreEqual(1, summary.AvailableNow);
            Assert.AreEqual(1, summary.RentedOut);
            Assert.AreEqual(1, summary.Overdue);
        }

        [TestMethod]
        public void GetSummary_AvailabilityRate_CalculatedCorrectly()
        {
            SeedMovies(
                new Movie { Id = 1, Name = "A" },
                new Movie { Id = 2, Name = "B" },
                new Movie { Id = 3, Name = "C" },
                new Movie { Id = 4, Name = "D" });
            SeedRental(1, 10, "Alice", "A", DateTime.Today, DateTime.Today.AddDays(7));

            var summary = _service.GetSummary();
            Assert.AreEqual(75.0, summary.AvailabilityRate);
        }

        [TestMethod]
        public void GetSummary_GenreStats_BestAndWorstIdentified()
        {
            SeedMovies(
                new Movie { Id = 1, Name = "A1", Genre = Genre.Action },
                new Movie { Id = 2, Name = "A2", Genre = Genre.Action },
                new Movie { Id = 3, Name = "C1", Genre = Genre.Comedy },
                new Movie { Id = 4, Name = "C2", Genre = Genre.Comedy });
            // Both Action available, both Comedy rented
            SeedRental(3, 10, "Alice", "C1", DateTime.Today, DateTime.Today.AddDays(7));
            SeedRental(4, 11, "Bob", "C2", DateTime.Today, DateTime.Today.AddDays(7));

            var summary = _service.GetSummary();
            Assert.AreEqual("Action", summary.BestAvailabilityGenre);
            Assert.AreEqual("Comedy", summary.WorstAvailabilityGenre);
        }

        // ── GetNextAvailableDate ─────────────────────────────────

        [TestMethod]
        public void GetNextAvailableDate_AvailableMovie_ReturnsToday()
        {
            SeedMovies(new Movie { Id = 1, Name = "A" });
            Assert.AreEqual(DateTime.Today, _service.GetNextAvailableDate(1));
        }

        [TestMethod]
        public void GetNextAvailableDate_RentedMovie_ReturnsDueDate()
        {
            SeedMovies(new Movie { Id = 1, Name = "A" });
            var dueDate = DateTime.Today.AddDays(5);
            SeedRental(1, 10, "Alice", "A", DateTime.Today, dueDate);

            Assert.AreEqual(dueDate, _service.GetNextAvailableDate(1));
        }

        // ── GetComingSoon ────────────────────────────────────────

        [TestMethod]
        public void GetComingSoon_NoUpcoming_ReturnsEmpty()
        {
            SeedMovies(new Movie { Id = 1, Name = "A" });
            var result = _service.GetComingSoon();
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetComingSoon_MovieDueWithinWindow_Returned()
        {
            SeedMovies(new Movie { Id = 1, Name = "A" });
            SeedRental(1, 10, "Alice", "A", DateTime.Today.AddDays(-5),
                DateTime.Today.AddDays(2));

            var result = _service.GetComingSoon(3);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("A", result[0].MovieName);
        }

        [TestMethod]
        public void GetComingSoon_MovieDueBeyondWindow_NotReturned()
        {
            SeedMovies(new Movie { Id = 1, Name = "A" });
            SeedRental(1, 10, "Alice", "A", DateTime.Today,
                DateTime.Today.AddDays(10));

            var result = _service.GetComingSoon(3);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetComingSoon_OverdueMovie_NotIncluded()
        {
            SeedMovies(new Movie { Id = 1, Name = "A" });
            SeedRental(1, 10, "Alice", "A", DateTime.Today.AddDays(-10),
                DateTime.Today.AddDays(-2));

            var result = _service.GetComingSoon(3);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetComingSoon_SortedByDaysUntilAvailable()
        {
            SeedMovies(
                new Movie { Id = 1, Name = "Far" },
                new Movie { Id = 2, Name = "Near" });
            SeedRental(1, 10, "Alice", "Far", DateTime.Today.AddDays(-3),
                DateTime.Today.AddDays(3));
            SeedRental(2, 11, "Bob", "Near", DateTime.Today.AddDays(-5),
                DateTime.Today.AddDays(1));

            var result = _service.GetComingSoon(5);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Near", result[0].MovieName);
            Assert.AreEqual("Far", result[1].MovieName);
        }

        // ── Constructor validation ───────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new AvailabilityService(null, _rentals);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new AvailabilityService(_movies, null);
        }

        [TestMethod]
        public void Constructor_NullReservationRepo_Allowed()
        {
            var service = new AvailabilityService(_movies, _rentals, null);
            Assert.IsNotNull(service);
        }

        // ── DaysUntilAvailable ───────────────────────────────────

        [TestMethod]
        public void DaysUntilAvailable_AvailableMovie_IsZero()
        {
            SeedMovies(new Movie { Id = 1, Name = "A" });
            var result = _service.GetAllAvailability();
            Assert.AreEqual(0, result[0].DaysUntilAvailable);
        }

        [TestMethod]
        public void DaysUntilAvailable_RentedMovie_ReflectsDueDate()
        {
            SeedMovies(new Movie { Id = 1, Name = "A" });
            SeedRental(1, 10, "Alice", "A", DateTime.Today.AddDays(-2),
                DateTime.Today.AddDays(5));

            var result = _service.GetAllAvailability();
            Assert.AreEqual(5, result[0].DaysUntilAvailable);
        }

        // ── CalendarEvent properties ─────────────────────────────

        [TestMethod]
        public void CalendarEvent_HasCorrectEventType()
        {
            SeedMovies(new Movie { Id = 1, Name = "A" });
            SeedRental(1, 10, "Alice", "A", DateTime.Today,
                DateTime.Today.AddDays(1));

            var calendar = _service.GetAvailabilityCalendar(3);
            var day = calendar.First(d => d.ReturningMovies.Count > 0);
            Assert.AreEqual(CalendarEventType.MovieReturning,
                day.ReturningMovies[0].EventType);
        }

        [TestMethod]
        public void CalendarDay_Weekend_DetectedCorrectly()
        {
            var calendar = _service.GetAvailabilityCalendar(14);
            var weekends = calendar.Where(d => d.IsWeekend).ToList();
            Assert.IsTrue(weekends.All(d =>
                d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday));
        }
    }
}
