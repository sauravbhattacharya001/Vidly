using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.Tests.Services
{
    [TestClass]
    public class HabitCoachServiceTests
    {
        private InMemoryTestMovieRepo _movies;
        private InMemoryTestRentalRepo _rentals;
        private InMemoryTestCustomerRepo _customers;
        private FakeClock _clock;
        private HabitCoachService _service;

        [TestInitialize]
        public void SetUp()
        {
            _movies = new InMemoryTestMovieRepo();
            _rentals = new InMemoryTestRentalRepo();
            _customers = new InMemoryTestCustomerRepo();
            _clock = new FakeClock(new DateTime(2026, 4, 27));
            _service = new HabitCoachService(_rentals, _movies, _customers, _clock);

            _customers.Add(new Customer { Id = 1, Name = "Alice" });
            _customers.Add(new Customer { Id = 2, Name = "Bob" });

            // 10 movies across genres
            _movies.Add(new Movie { Id = 1, Name = "Action Hero", Genre = Genre.Action });
            _movies.Add(new Movie { Id = 2, Name = "Laugh Out Loud", Genre = Genre.Comedy });
            _movies.Add(new Movie { Id = 3, Name = "Tears", Genre = Genre.Drama });
            _movies.Add(new Movie { Id = 4, Name = "Scream", Genre = Genre.Horror });
            _movies.Add(new Movie { Id = 5, Name = "Stars", Genre = Genre.SciFi });
            _movies.Add(new Movie { Id = 6, Name = "Toon", Genre = Genre.Animation });
            _movies.Add(new Movie { Id = 7, Name = "Edge", Genre = Genre.Thriller });
            _movies.Add(new Movie { Id = 8, Name = "Love", Genre = Genre.Romance });
            _movies.Add(new Movie { Id = 9, Name = "Facts", Genre = Genre.Documentary });
            _movies.Add(new Movie { Id = 10, Name = "Quest", Genre = Genre.Adventure });
        }

        // ── Helpers ────────────────────────────────────────────────────

        private Rental MakeRental(int id, int custId, int movieId, DateTime date, int days = 3, bool returned = true, int lateDays = 0)
        {
            var due = date.AddDays(days);
            DateTime? returnDate = returned ? due.AddDays(lateDays) : (DateTime?)null;
            return new Rental
            {
                Id = id,
                CustomerId = custId,
                MovieId = movieId,
                RentalDate = date,
                DueDate = due,
                ReturnDate = returnDate,
                DailyRate = 2.99m,
                Status = returned ? RentalStatus.Returned : RentalStatus.Active
            };
        }

        private void AddDiverseRentals(int custId, int count, DateTime startDate)
        {
            for (int i = 0; i < count; i++)
            {
                var movieId = (i % 10) + 1; // cycle through genres
                _rentals.Add(MakeRental(100 + i, custId, movieId, startDate.AddDays(i * 3)));
            }
        }

        private void AddSameGenreRentals(int custId, int movieId, int count, DateTime startDate)
        {
            for (int i = 0; i < count; i++)
            {
                _rentals.Add(MakeRental(200 + i, custId, movieId, startDate.AddDays(i * 2)));
            }
        }

        // ── Empty History Tests ────────────────────────────────────────

        [TestMethod]
        public void Analyze_EmptyHistory_ReturnsSensibleDefaults()
        {
            var report = _service.Analyze(1);

            Assert.AreEqual("Dormant", report.RhythmState);
            Assert.AreEqual("Stable", report.RhythmTrend);
            Assert.AreEqual(0, report.GenreEntropy);
            Assert.IsFalse(report.InGenreRut);
            Assert.AreEqual(0, report.EngagementScore);
            Assert.AreEqual("F", report.WellnessGrade);
            Assert.AreEqual(0, report.OverallWellnessScore);
            Assert.IsTrue(report.Goals.Count > 0);
            Assert.IsTrue(report.Nudges.Count > 0);
        }

        [TestMethod]
        public void Analyze_EmptyHistory_HasGetStartedGoal()
        {
            var report = _service.Analyze(1);
            Assert.AreEqual("Get Started", report.Goals[0].Title);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void Analyze_NonexistentCustomer_Throws()
        {
            _service.Analyze(999);
        }

        // ── Rhythm Tests ───────────────────────────────────────────────

        [TestMethod]
        public void ClassifyRhythmState_Binge_Above8()
        {
            Assert.AreEqual("Binge", HabitCoachService.ClassifyRhythmState(9));
            Assert.AreEqual("Binge", HabitCoachService.ClassifyRhythmState(15));
        }

        [TestMethod]
        public void ClassifyRhythmState_Active_4To8()
        {
            Assert.AreEqual("Active", HabitCoachService.ClassifyRhythmState(4));
            Assert.AreEqual("Active", HabitCoachService.ClassifyRhythmState(8));
        }

        [TestMethod]
        public void ClassifyRhythmState_Casual_1To3()
        {
            Assert.AreEqual("Casual", HabitCoachService.ClassifyRhythmState(1));
            Assert.AreEqual("Casual", HabitCoachService.ClassifyRhythmState(3));
        }

        [TestMethod]
        public void ClassifyRhythmState_Dormant_Zero()
        {
            Assert.AreEqual("Dormant", HabitCoachService.ClassifyRhythmState(0));
        }

        [TestMethod]
        public void Analyze_RecentBinge_DetectsBingeState()
        {
            // 10 rentals in last 30 days
            var now = _clock.Now;
            for (int i = 0; i < 10; i++)
                _rentals.Add(MakeRental(300 + i, 1, (i % 10) + 1, now.AddDays(-i * 2)));

            var report = _service.Analyze(1);
            Assert.AreEqual("Binge", report.RhythmState);
        }

        [TestMethod]
        public void Analyze_NoRecentRentals_DetectsDormant()
        {
            // Rentals only 200 days ago
            _rentals.Add(MakeRental(1, 1, 1, _clock.Now.AddDays(-200)));

            var report = _service.Analyze(1);
            Assert.AreEqual("Dormant", report.RhythmState);
        }

        // ── Genre Diversity Tests ──────────────────────────────────────

        [TestMethod]
        public void Analyze_AllSameGenre_DetectsGenreRut()
        {
            AddSameGenreRentals(1, 1, 20, _clock.Now.AddDays(-60));

            var report = _service.Analyze(1);
            Assert.IsTrue(report.InGenreRut);
            Assert.AreEqual("Action", report.DominantGenre);
        }

        [TestMethod]
        public void Analyze_DiverseGenres_NoGenreRut()
        {
            AddDiverseRentals(1, 20, _clock.Now.AddDays(-60));

            var report = _service.Analyze(1);
            Assert.IsFalse(report.InGenreRut);
        }

        [TestMethod]
        public void Analyze_DiverseGenres_HighEntropy()
        {
            AddDiverseRentals(1, 20, _clock.Now.AddDays(-60));

            var report = _service.Analyze(1);
            Assert.IsTrue(report.GenreEntropy > 2.0, $"Expected entropy > 2.0, got {report.GenreEntropy}");
        }

        [TestMethod]
        public void Analyze_SingleGenre_ZeroOrLowEntropy()
        {
            AddSameGenreRentals(1, 1, 10, _clock.Now.AddDays(-30));

            var report = _service.Analyze(1);
            Assert.AreEqual(0, report.GenreEntropy);
        }

        [TestMethod]
        public void Analyze_DiverseGenres_HighDiversityScore()
        {
            AddDiverseRentals(1, 20, _clock.Now.AddDays(-60));

            var report = _service.Analyze(1);
            Assert.IsTrue(report.DiversityScore > 80, $"Expected diversity > 80, got {report.DiversityScore}");
        }

        // ── Engagement Tests ───────────────────────────────────────────

        [TestMethod]
        public void Analyze_IncreasingRentals_GrowingEngagement()
        {
            // 1 rental/month increasing to 6
            var baseDate = _clock.Now.AddMonths(-6);
            int id = 400;
            for (int month = 0; month < 6; month++)
            {
                var count = month + 1;
                for (int j = 0; j < count; j++)
                {
                    _rentals.Add(MakeRental(id++, 1, (j % 10) + 1, baseDate.AddMonths(month).AddDays(j)));
                }
            }

            var report = _service.Analyze(1);
            Assert.AreEqual("Growing", report.EngagementTrend);
            Assert.IsTrue(report.TrendSlope > 0);
        }

        [TestMethod]
        public void Analyze_DecreasingRentals_DecliningEngagement()
        {
            var baseDate = _clock.Now.AddMonths(-6);
            int id = 500;
            for (int month = 0; month < 6; month++)
            {
                var count = 6 - month;
                for (int j = 0; j < count; j++)
                {
                    _rentals.Add(MakeRental(id++, 1, (j % 10) + 1, baseDate.AddMonths(month).AddDays(j)));
                }
            }

            var report = _service.Analyze(1);
            Assert.AreEqual("Declining", report.EngagementTrend);
            Assert.IsTrue(report.TrendSlope < 0);
        }

        [TestMethod]
        public void Analyze_SteadyRentals_StableEngagement()
        {
            var baseDate = _clock.Now.AddMonths(-4);
            int id = 600;
            for (int month = 0; month < 4; month++)
            {
                for (int j = 0; j < 3; j++)
                {
                    _rentals.Add(MakeRental(id++, 1, (j % 10) + 1, baseDate.AddMonths(month).AddDays(j * 5)));
                }
            }

            var report = _service.Analyze(1);
            Assert.AreEqual("Stable", report.EngagementTrend);
        }

        // ── Timing Tests ───────────────────────────────────────────────

        [TestMethod]
        public void Analyze_WeekendRentals_DetectsWeekendWarrior()
        {
            int id = 700;
            // Add rentals only on Saturdays
            var sat = _clock.Now;
            while (sat.DayOfWeek != DayOfWeek.Saturday) sat = sat.AddDays(-1);
            for (int i = 0; i < 10; i++)
            {
                _rentals.Add(MakeRental(id++, 1, (i % 10) + 1, sat.AddDays(-7 * i)));
            }

            var report = _service.Analyze(1);
            Assert.AreEqual("Weekend Warrior", report.TimingPersona);
        }

        [TestMethod]
        public void Analyze_WeekdayRentals_DetectsWeekdayRegular()
        {
            int id = 800;
            var tue = _clock.Now;
            while (tue.DayOfWeek != DayOfWeek.Tuesday) tue = tue.AddDays(-1);
            for (int i = 0; i < 10; i++)
            {
                _rentals.Add(MakeRental(id++, 1, (i % 10) + 1, tue.AddDays(-7 * i)));
            }

            var report = _service.Analyze(1);
            Assert.AreEqual("Weekday Regular", report.TimingPersona);
        }

        // ── Punctuality Tests ──────────────────────────────────────────

        [TestMethod]
        public void Analyze_AllOnTimeReturns_HighPunctuality()
        {
            for (int i = 0; i < 10; i++)
                _rentals.Add(MakeRental(900 + i, 1, (i % 10) + 1, _clock.Now.AddDays(-30 + i * 2), returned: true, lateDays: 0));

            var report = _service.Analyze(1);
            Assert.IsTrue(report.PunctualityScore >= 80);
            Assert.AreEqual(0, report.LateCount);
        }

        [TestMethod]
        public void Analyze_AllLateReturns_LowPunctuality()
        {
            for (int i = 0; i < 10; i++)
                _rentals.Add(MakeRental(1000 + i, 1, (i % 10) + 1, _clock.Now.AddDays(-30 + i * 2), returned: true, lateDays: 5));

            var report = _service.Analyze(1);
            Assert.AreEqual("Chronically Late", report.ReturnBehavior);
            Assert.IsTrue(report.PunctualityScore < 50);
        }

        [TestMethod]
        public void Analyze_EarlyReturns_DetectsEarlyBird()
        {
            for (int i = 0; i < 10; i++)
                _rentals.Add(MakeRental(1100 + i, 1, (i % 10) + 1, _clock.Now.AddDays(-30 + i * 2), days: 7, returned: true, lateDays: -3));

            var report = _service.Analyze(1);
            Assert.AreEqual("Early Bird", report.ReturnBehavior);
        }

        // ── Goal Generation Tests ──────────────────────────────────────

        [TestMethod]
        public void Analyze_GenreRut_GeneratesBreakRutGoal()
        {
            AddSameGenreRentals(1, 1, 20, _clock.Now.AddDays(-60));

            var report = _service.Analyze(1);
            Assert.IsTrue(report.Goals.Any(g => g.Title.Contains("Genre Rut")));
        }

        [TestMethod]
        public void Analyze_DormantState_GeneratesRekindleGoal()
        {
            _rentals.Add(MakeRental(1, 1, 1, _clock.Now.AddDays(-200)));

            var report = _service.Analyze(1);
            Assert.IsTrue(report.Goals.Any(g => g.Title.Contains("Rekindle")));
        }

        [TestMethod]
        public void Analyze_LateReturns_GeneratesPunctualityGoal()
        {
            for (int i = 0; i < 10; i++)
                _rentals.Add(MakeRental(1200 + i, 1, (i % 10) + 1, _clock.Now.AddDays(-20 + i), returned: true, lateDays: 5));

            var report = _service.Analyze(1);
            Assert.IsTrue(report.Goals.Any(g => g.Title.Contains("Return on Time")));
        }

        [TestMethod]
        public void Analyze_MaxThreeGoals()
        {
            // Trigger many conditions
            AddSameGenreRentals(1, 1, 20, _clock.Now.AddDays(-200)); // old rentals, dormant, genre rut

            var report = _service.Analyze(1);
            Assert.IsTrue(report.Goals.Count <= 3);
        }

        // ── Nudge Generation Tests ─────────────────────────────────────

        [TestMethod]
        public void Analyze_DormantState_GeneratesEngagementNudge()
        {
            _rentals.Add(MakeRental(1, 1, 1, _clock.Now.AddDays(-200)));

            var report = _service.Analyze(1);
            Assert.IsTrue(report.Nudges.Any(n => n.Category == "Engagement"));
        }

        [TestMethod]
        public void Analyze_GenreRut_GeneratesDiversityNudge()
        {
            AddSameGenreRentals(1, 1, 20, _clock.Now.AddDays(-60));

            var report = _service.Analyze(1);
            Assert.IsTrue(report.Nudges.Any(n => n.Category == "Diversity"));
        }

        // ── Wellness Score Tests ───────────────────────────────────────

        [TestMethod]
        public void Analyze_ActiveDiverseOnTime_HighWellness()
        {
            // Active, diverse, on-time
            int id = 1300;
            for (int i = 0; i < 20; i++)
            {
                _rentals.Add(MakeRental(id++, 1, (i % 10) + 1, _clock.Now.AddDays(-i * 2), returned: true, lateDays: 0));
            }

            var report = _service.Analyze(1);
            Assert.IsTrue(report.OverallWellnessScore >= 50, $"Expected wellness >= 50, got {report.OverallWellnessScore}");
        }

        [TestMethod]
        public void Analyze_WellnessGrade_MatchesScore()
        {
            AddDiverseRentals(1, 10, _clock.Now.AddDays(-30));

            var report = _service.Analyze(1);

            // Verify grade is consistent with score
            if (report.OverallWellnessScore >= 90)
                Assert.IsTrue(report.WellnessGrade.StartsWith("A"));
            else if (report.OverallWellnessScore >= 40)
                Assert.IsFalse(report.WellnessGrade == "F");
        }

        // ── Edge Cases ─────────────────────────────────────────────────

        [TestMethod]
        public void Analyze_SingleRental_Works()
        {
            _rentals.Add(MakeRental(1, 1, 1, _clock.Now.AddDays(-5)));

            var report = _service.Analyze(1);
            Assert.IsNotNull(report);
            Assert.AreEqual("Alice", report.CustomerName);
            Assert.IsTrue(report.MonthlyHistory.Count >= 1);
        }

        [TestMethod]
        public void Analyze_AllSameDay_Works()
        {
            var date = _clock.Now.AddDays(-10);
            for (int i = 0; i < 5; i++)
                _rentals.Add(MakeRental(1400 + i, 1, (i % 10) + 1, date));

            var report = _service.Analyze(1);
            Assert.IsNotNull(report);
            Assert.AreEqual(1, report.MonthlyHistory.Count);
        }

        [TestMethod]
        public void Analyze_CustomerName_Correct()
        {
            _rentals.Add(MakeRental(1, 2, 1, _clock.Now.AddDays(-5)));

            var report = _service.Analyze(2);
            Assert.AreEqual("Bob", report.CustomerName);
        }

        [TestMethod]
        public void Analyze_SpendingTrend_Increasing()
        {
            int id = 1500;
            // Few cheap early, many expensive later
            _rentals.Add(MakeRental(id++, 1, 1, _clock.Now.AddMonths(-4), returned: true));
            for (int i = 0; i < 8; i++)
                _rentals.Add(MakeRental(id++, 1, (i % 10) + 1, _clock.Now.AddDays(-i * 2), returned: true));

            var report = _service.Analyze(1);
            // Just verify spending analysis runs without error
            Assert.IsNotNull(report.SpendingTrend);
        }

        [TestMethod]
        public void Analyze_NoReturns_PunctualityDefaultsHigh()
        {
            _rentals.Add(MakeRental(1, 1, 1, _clock.Now.AddDays(-2), returned: false));

            var report = _service.Analyze(1);
            Assert.AreEqual(100, report.PunctualityScore);
            Assert.AreEqual("No Returns Yet", report.ReturnBehavior);
        }

        // ── Test helpers ───────────────────────────────────────────────

        private class FakeClock : IClock
        {
            public DateTime Now { get; }
            public FakeClock(DateTime now) { Now = now; }
        }

        private class InMemoryTestCustomerRepo : ICustomerRepository
        {
            private readonly List<Customer> _data = new List<Customer>();
            public void Add(Customer entity) => _data.Add(entity);
            public void Delete(int id) => _data.RemoveAll(c => c.Id == id);
            public IReadOnlyList<Customer> GetAll() => _data.AsReadOnly();
            public Customer GetById(int id) => _data.FirstOrDefault(c => c.Id == id);
            public void Update(Customer entity) { }
        }

        private class InMemoryTestMovieRepo : IMovieRepository
        {
            private readonly List<Movie> _data = new List<Movie>();
            public void Add(Movie entity) => _data.Add(entity);
            public void Delete(int id) => _data.RemoveAll(m => m.Id == id);
            public IReadOnlyList<Movie> GetAll() => _data.AsReadOnly();
            public Movie GetById(int id) => _data.FirstOrDefault(m => m.Id == id);
            public void Update(Movie entity) { }
        }

        private class InMemoryTestRentalRepo : IRentalRepository
        {
            private readonly List<Rental> _data = new List<Rental>();
            public void Add(Rental entity) => _data.Add(entity);
            public void Delete(int id) => _data.RemoveAll(r => r.Id == id);
            public IReadOnlyList<Rental> GetAll() => _data.AsReadOnly();
            public Rental GetById(int id) => _data.FirstOrDefault(r => r.Id == id);
            public void Update(Rental entity) { }
            public IReadOnlyList<Rental> GetByCustomer(int customerId) => _data.Where(r => r.CustomerId == customerId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) => _data.Where(r => r.CustomerId == customerId && r.Status == RentalStatus.Active).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetByMovie(int movieId) => _data.Where(r => r.MovieId == movieId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetOverdue() => _data.Where(r => r.Status == RentalStatus.Overdue).ToList().AsReadOnly();
            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) => _data.AsReadOnly();
            public Rental ReturnRental(int rentalId) => GetById(rentalId);
            public bool IsMovieRentedOut(int movieId) => _data.Any(r => r.MovieId == movieId && r.Status == RentalStatus.Active);
            public Rental Checkout(Rental rental) => rental;
            public Rental Checkout(Rental rental, int maxConcurrentRentals) => rental;
            public Rental ExtendRental(int rentalId, int days) => GetById(rentalId);
            public bool IsExtended(int rentalId) => false;
            public RentalStats GetStats() => new RentalStats();
        }
    }
}
