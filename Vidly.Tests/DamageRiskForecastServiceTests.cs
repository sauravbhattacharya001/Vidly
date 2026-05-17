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
    public class DamageRiskForecastServiceTests
    {
        // ── Test doubles ──────────────────────────────────────────
        //
        // We use hand-rolled fakes (not the seeded InMemory* repos) so the
        // forecast portfolio under test contains *only* the rentals each
        // test inserts. That keeps every assertion deterministic.

        private FakeRentalRepo _rentalRepo;
        private FakeDamageRepo _damageRepo;
        private FakeCustomerRepo _customerRepo;
        private FakeMovieRepo _movieRepo;
        private FixedClock _clock;
        private DamageRiskForecastService _service;
        private DateTime _today;
        private int _nextId;

        [TestInitialize]
        public void Setup()
        {
            _rentalRepo = new FakeRentalRepo();
            _damageRepo = new FakeDamageRepo();
            _customerRepo = new FakeCustomerRepo();
            _movieRepo = new FakeMovieRepo();
            _today = new DateTime(2026, 5, 17);
            _clock = new FixedClock(_today);
            _nextId = 1;
            _service = new DamageRiskForecastService(
                _rentalRepo, _damageRepo, _customerRepo, _movieRepo, _clock);
        }

        private class FixedClock : IClock
        {
            public FixedClock(DateTime now) { Now = now; Today = now.Date; }
            public DateTime Now { get; }
            public DateTime Today { get; }
        }

        // Minimal repo fakes — implement only the methods the service uses.
        // Anything else throws to flag accidental new dependencies.
        private class FakeRentalRepo : IRentalRepository
        {
            public readonly List<Rental> Rentals = new List<Rental>();
            public IReadOnlyList<Rental> GetAll() => Rentals;
            public Rental GetById(int id) => Rentals.FirstOrDefault(r => r.Id == id);
            public void Add(Rental entity) => Rentals.Add(entity);
            public void Update(Rental entity) { /* no-op for tests */ }
            public void Remove(int id) => Rentals.RemoveAll(r => r.Id == id);
            public IReadOnlyList<Rental> GetByCustomer(int customerId) =>
                Rentals.Where(r => r.CustomerId == customerId).ToList();
            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) =>
                Rentals.Where(r => r.CustomerId == customerId
                                   && r.Status != RentalStatus.Returned).ToList();
            public IReadOnlyList<Rental> GetByMovie(int movieId) =>
                Rentals.Where(r => r.MovieId == movieId).ToList();
            public IReadOnlyList<Rental> GetOverdue() =>
                Rentals.Where(r => r.Status == RentalStatus.Overdue).ToList();
            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) =>
                throw new NotImplementedException();
            public Rental ReturnRental(int rentalId) => throw new NotImplementedException();
            public bool IsMovieRentedOut(int movieId) =>
                Rentals.Any(r => r.MovieId == movieId && r.Status != RentalStatus.Returned);
            public Rental Checkout(Rental rental) => throw new NotImplementedException();
            public Rental Checkout(Rental rental, int maxConcurrentRentals) =>
                throw new NotImplementedException();
            public Rental ExtendRental(int rentalId, int days) => throw new NotImplementedException();
            public bool IsExtended(int rentalId) => false;
            public RentalStats GetStats() => throw new NotImplementedException();
        }

        private class FakeDamageRepo : IDamageRepository
        {
            public readonly List<DamageReport> Reports = new List<DamageReport>();
            public IEnumerable<DamageReport> GetAll() => Reports;
            public DamageReport GetById(int id) => Reports.FirstOrDefault(d => d.Id == id);
            public IEnumerable<DamageReport> GetByCustomer(int customerId) =>
                Reports.Where(d => d.CustomerId == customerId);
            public IEnumerable<DamageReport> GetByMovie(int movieId) =>
                Reports.Where(d => d.MovieId == movieId);
            public IEnumerable<DamageReport> GetByStatus(DamageStatus status) =>
                Reports.Where(d => d.Status == status);
            public IEnumerable<DamageReport> GetBySeverity(DamageSeverity severity) =>
                Reports.Where(d => d.Severity == severity);
            public DamageSummary GetSummary() => throw new NotImplementedException();
            public void Add(DamageReport report) => Reports.Add(report);
            public void Update(DamageReport report) { /* no-op for tests */ }
        }

        private class FakeCustomerRepo : ICustomerRepository
        {
            public readonly List<Customer> Customers = new List<Customer>();
            public IReadOnlyList<Customer> GetAll() => Customers;
            public Customer GetById(int id) => Customers.FirstOrDefault(c => c.Id == id);
            public void Add(Customer entity) => Customers.Add(entity);
            public void Update(Customer entity) { /* no-op for tests */ }
            public void Remove(int id) => Customers.RemoveAll(c => c.Id == id);
            public IReadOnlyList<Customer> Search(string query, MembershipType? membershipType) =>
                throw new NotImplementedException();
            public IReadOnlyList<Customer> GetByMemberSince(int year, int month) =>
                throw new NotImplementedException();
            public CustomerStats GetStats() => throw new NotImplementedException();
        }

        private class FakeMovieRepo : IMovieRepository
        {
            public readonly List<Movie> Movies = new List<Movie>();
            public IReadOnlyList<Movie> GetAll() => Movies;
            public Movie GetById(int id) => Movies.FirstOrDefault(m => m.Id == id);
            public void Add(Movie entity) => Movies.Add(entity);
            public void Update(Movie entity) { /* no-op for tests */ }
            public void Remove(int id) => Movies.RemoveAll(m => m.Id == id);
            public IReadOnlyList<Movie> GetByReleaseDate(int year, int month) =>
                throw new NotImplementedException();
            public Movie GetRandom() => throw new NotImplementedException();
            public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating) =>
                throw new NotImplementedException();
        }

        // ── Helpers ───────────────────────────────────────────────

        private Customer AddCustomer(string name, MembershipType tier = MembershipType.Basic)
        {
            var c = new Customer
            {
                Id = _nextId++,
                Name = name,
                Email = $"{name}@example.com",
                MembershipType = tier,
            };
            _customerRepo.Add(c);
            return c;
        }

        private Movie AddMovie(string name, Genre genre)
        {
            var m = new Movie
            {
                Id = _nextId++,
                Name = name,
                Genre = genre,
                ReleaseDate = new DateTime(2020, 1, 1),
            };
            _movieRepo.Add(m);
            return m;
        }

        private Rental AddActiveRental(
            Customer c, Movie m,
            int rentedDaysAgo = 2, int dueInDays = 5,
            decimal dailyRate = 2.00m, RentalStatus status = RentalStatus.Active)
        {
            var due = _today.AddDays(dueInDays);
            var actualStatus = status;
            if (actualStatus == RentalStatus.Active && due.Date < _today)
                actualStatus = RentalStatus.Overdue;
            var r = new Rental
            {
                Id = _nextId++,
                CustomerId = c.Id,
                CustomerName = c.Name,
                MovieId = m.Id,
                MovieName = m.Name,
                RentalDate = _today.AddDays(-rentedDaysAgo),
                DueDate = due,
                DailyRate = dailyRate,
                Status = actualStatus,
            };
            _rentalRepo.Add(r);
            return r;
        }

        private void AddDamage(Customer c, Movie m, DamageSeverity sev, int daysAgo,
                               DamageStatus status = DamageStatus.Paid)
        {
            _damageRepo.Add(new DamageReport
            {
                Id = _nextId++,
                CustomerId = c.Id,
                CustomerName = c.Name,
                MovieId = m.Id,
                MovieTitle = m.Name,
                Severity = sev,
                DamageType = DamageType.ScratchedDisc,
                Status = status,
                Description = "test",
                ReportedAt = _today.AddDays(-daysAgo),
            });
        }

        // ── Tests ─────────────────────────────────────────────────

        [TestMethod]
        public void Forecast_NoRentals_ReturnsEmptyCalmReport()
        {
            var report = _service.Forecast();
            Assert.AreEqual(0, report.ActiveRentals);
            Assert.AreEqual(0, report.P0Count);
            Assert.AreEqual("A", report.PortfolioGrade);
            CollectionAssert.Contains(report.Insights, "No active rentals — nothing to forecast.");
        }

        [TestMethod]
        public void Forecast_CleanCustomerLowGenre_ProducesLowRisk()
        {
            var c = AddCustomer("Alice", MembershipType.Gold);
            var m = AddMovie("Quiet Drama", Genre.Drama);
            var r = AddActiveRental(c, m, rentedDaysAgo: 1, dueInDays: 6, dailyRate: 2m);

            var report = _service.Forecast();
            var f = report.Forecasts.Single();
            Assert.AreEqual(r.Id, f.RentalId);
            Assert.IsTrue(f.RiskScore < 40, $"expected low risk, got {f.RiskScore}");
            Assert.AreEqual(DamagePreventionPriority.P2, f.Priority);
        }

        [TestMethod]
        public void Forecast_RepeatOffenderSevereHistory_ProducesP0()
        {
            var c = AddCustomer("Repeat", MembershipType.Basic);
            var m = AddMovie("Action Flick", Genre.Action);
            AddActiveRental(c, m, rentedDaysAgo: 10, dueInDays: -2, dailyRate: 6m);
            AddDamage(c, m, DamageSeverity.Severe, daysAgo: 30);
            AddDamage(c, m, DamageSeverity.Severe, daysAgo: 60);
            AddDamage(c, m, DamageSeverity.Destroyed, daysAgo: 90);

            var report = _service.Forecast();
            var f = report.Forecasts.Single();
            Assert.IsTrue(f.RiskScore >= 75, $"expected P0 score, got {f.RiskScore}");
            Assert.AreEqual(DamagePreventionPriority.P0, f.Priority);
            Assert.IsTrue(f.Band == DamageRiskBand.High || f.Band == DamageRiskBand.Elevated,
                $"expected High or Elevated band, got {f.Band}");
            Assert.IsTrue(f.Signals.Any(s => s.Code == "CUSTOMER_HISTORY"));
            Assert.IsTrue(f.Signals.Any(s => s.Code == "OVERDUE"));
            Assert.IsTrue(f.RecommendedActions.Any(a => a.StartsWith("Call ")));
        }

        [TestMethod]
        public void Forecast_PlatinumMember_GetsLoyaltyDiscount()
        {
            var c = AddCustomer("Loyal", MembershipType.Platinum);
            var m = AddMovie("Romance", Genre.Romance);
            AddActiveRental(c, m, rentedDaysAgo: 1, dueInDays: 5);

            var report = _service.Forecast();
            var f = report.Forecasts.Single();
            var loyalty = f.Signals.SingleOrDefault(s => s.Code == "LOYALTY_DISCOUNT");
            Assert.IsNotNull(loyalty);
            Assert.AreEqual(-10, loyalty.Contribution);
        }

        [TestMethod]
        public void Forecast_OverdueRental_AddsOverdueSignal()
        {
            var c = AddCustomer("Late");
            var m = AddMovie("Adventure", Genre.Adventure);
            AddActiveRental(c, m, rentedDaysAgo: 12, dueInDays: -5);

            var report = _service.Forecast();
            var f = report.Forecasts.Single();
            var overdue = f.Signals.Single(s => s.Code == "OVERDUE");
            Assert.IsTrue(overdue.Contribution > 0);
            Assert.AreEqual(-5, f.DaysUntilDue);
        }

        [TestMethod]
        public void Forecast_HighValueTitle_AddsHighValueSignal()
        {
            var c = AddCustomer("Cust");
            var m = AddMovie("Premium", Genre.SciFi);
            AddActiveRental(c, m, rentedDaysAgo: 0, dueInDays: 3, dailyRate: 8m);

            var report = _service.Forecast();
            var f = report.Forecasts.Single();
            Assert.IsTrue(f.Signals.Any(s => s.Code == "HIGH_VALUE_TITLE"));
        }

        [TestMethod]
        public void Forecast_HighWaiverRate_AddsWaiverSignal()
        {
            var c = AddCustomer("Waived");
            var m = AddMovie("Horror", Genre.Horror);
            AddActiveRental(c, m);
            AddDamage(c, m, DamageSeverity.Minor, 10, DamageStatus.Waived);
            AddDamage(c, m, DamageSeverity.Minor, 40, DamageStatus.Waived);
            AddDamage(c, m, DamageSeverity.Minor, 80, DamageStatus.Paid);

            var report = _service.Forecast();
            var f = report.Forecasts.Single();
            Assert.IsTrue(f.Signals.Any(s => s.Code == "HIGH_WAIVER_RATE"),
                "Expected HIGH_WAIVER_RATE signal");
        }

        [TestMethod]
        public void Forecast_StoreWideDamageSpike_AddsSignalAndInsight()
        {
            var c = AddCustomer("C1");
            var c2 = AddCustomer("C2");
            var m = AddMovie("M", Genre.Action);
            AddActiveRental(c, m);
            AddDamage(c2, m, DamageSeverity.Minor, 5);
            AddDamage(c2, m, DamageSeverity.Minor, 10);
            AddDamage(c2, m, DamageSeverity.Moderate, 15);

            var report = _service.Forecast();
            Assert.IsTrue(report.Forecasts.Single().Signals.Any(s => s.Code == "STORE_WIDE_SPIKE"));
            Assert.IsTrue(report.Insights.Any(i => i.Contains("damage spike")));
        }

        [TestMethod]
        public void Forecast_CautiousAppetite_RaisesScore()
        {
            var c = AddCustomer("X");
            var m = AddMovie("Drama", Genre.Drama);
            AddActiveRental(c, m);

            var baseline = _service.Forecast().Forecasts.Single().RiskScore;
            var cautious = new DamageRiskForecastService(
                _rentalRepo, _damageRepo, _customerRepo, _movieRepo, _clock,
                new DamageRiskForecastConfig { RiskAppetite = DamageRiskAppetite.Cautious });
            var cautiousScore = cautious.Forecast().Forecasts.Single().RiskScore;
            Assert.IsTrue(cautiousScore > baseline,
                $"cautious={cautiousScore} should be > balanced={baseline}");
        }

        [TestMethod]
        public void Forecast_AggressiveAppetite_LowersScore()
        {
            var c = AddCustomer("X");
            var m = AddMovie("Drama", Genre.Drama);
            AddActiveRental(c, m, rentedDaysAgo: 10);

            var baseline = _service.Forecast().Forecasts.Single().RiskScore;
            var aggro = new DamageRiskForecastService(
                _rentalRepo, _damageRepo, _customerRepo, _movieRepo, _clock,
                new DamageRiskForecastConfig { RiskAppetite = DamageRiskAppetite.Aggressive });
            var aggroScore = aggro.Forecast().Forecasts.Single().RiskScore;
            Assert.IsTrue(aggroScore < baseline,
                $"aggressive={aggroScore} should be < balanced={baseline}");
        }

        [TestMethod]
        public void Forecast_OrderedByRiskDescending()
        {
            var c1 = AddCustomer("Low",  MembershipType.Platinum);
            var c2 = AddCustomer("High", MembershipType.Basic);
            var m  = AddMovie("Action", Genre.Action);
            AddActiveRental(c1, m, rentedDaysAgo: 1, dueInDays: 5);
            var r2 = AddActiveRental(c2, m, rentedDaysAgo: 12, dueInDays: -3, dailyRate: 7m);
            AddDamage(c2, m, DamageSeverity.Severe, 20);

            var report = _service.Forecast();
            Assert.AreEqual(r2.Id, report.Forecasts[0].RentalId,
                "highest-risk rental should be first");
        }

        [TestMethod]
        public void Forecast_BuildsCatalogPlaybook_DeduplicatesActions()
        {
            var m = AddMovie("Action", Genre.Action);
            for (int i = 0; i < 2; i++)
            {
                var c = AddCustomer($"Risky{i}");
                AddActiveRental(c, m, rentedDaysAgo: 12, dueInDays: -3, dailyRate: 7m);
                AddDamage(c, m, DamageSeverity.Severe, 20);
                AddDamage(c, m, DamageSeverity.Severe, 50);
                AddDamage(c, m, DamageSeverity.Destroyed, 80);
            }

            var report = _service.Forecast();
            Assert.AreEqual(2, report.P0Count);
            var callAction = report.Playbook.FirstOrDefault(a => a.Action.StartsWith("Call "));
            Assert.IsNotNull(callAction);
            Assert.AreEqual(2, callAction.RentalCount);
            Assert.AreEqual("store_manager", callAction.Owner);
            Assert.AreEqual(DamagePreventionPriority.P0, callAction.Priority);
        }

        [TestMethod]
        public void Forecast_MultipleAtRiskFromSameCustomer_EmitsClusterInsight()
        {
            var c = AddCustomer("Multi");
            var m1 = AddMovie("A1", Genre.Action);
            var m2 = AddMovie("A2", Genre.Adventure);
            AddActiveRental(c, m1, rentedDaysAgo: 10, dueInDays: -2, dailyRate: 6m);
            AddActiveRental(c, m2, rentedDaysAgo: 11, dueInDays: -3, dailyRate: 6m);
            AddDamage(c, m1, DamageSeverity.Severe, 30);
            AddDamage(c, m2, DamageSeverity.Moderate, 60);

            var report = _service.Forecast();
            Assert.IsTrue(report.Insights.Any(i => i.Contains("at-risk active rentals")),
                "expected cluster insight");
        }

        [TestMethod]
        public void FormatText_IncludesHeaderAndPlaybook()
        {
            var c = AddCustomer("X");
            var m = AddMovie("Action", Genre.Action);
            AddActiveRental(c, m, rentedDaysAgo: 12, dueInDays: -3, dailyRate: 7m);
            AddDamage(c, m, DamageSeverity.Severe, 30);
            AddDamage(c, m, DamageSeverity.Severe, 60);

            var report = _service.Forecast();
            var text = _service.FormatText(report);
            StringAssert.Contains(text, "DAMAGE RISK FORECAST");
            StringAssert.Contains(text, "PLAYBOOK");
            StringAssert.Contains(text, "Grade:");
        }

        [TestMethod]
        public void FormatMarkdown_IncludesHeaderAndBullets()
        {
            var c = AddCustomer("X");
            var m = AddMovie("Sci-Fi", Genre.SciFi);
            AddActiveRental(c, m, rentedDaysAgo: 1, dueInDays: 5, dailyRate: 6m);

            var report = _service.Forecast();
            var md = _service.FormatMarkdown(report);
            StringAssert.Contains(md, "# Damage Risk Forecast");
            StringAssert.Contains(md, "## Top At-Risk Rentals");
        }

        [TestMethod]
        public void Forecast_Deterministic_RepeatedCallsProduceIdenticalReport()
        {
            var c = AddCustomer("Steady");
            var m = AddMovie("Drama", Genre.Drama);
            AddActiveRental(c, m, rentedDaysAgo: 5, dueInDays: 2, dailyRate: 4m);
            AddDamage(c, m, DamageSeverity.Minor, 60);

            var a = _service.Forecast();
            var b = _service.Forecast();
            Assert.AreEqual(a.PortfolioRisk, b.PortfolioRisk);
            Assert.AreEqual(a.Forecasts[0].RiskScore, b.Forecasts[0].RiskScore);
            Assert.AreEqual(a.Forecasts[0].Signals.Count, b.Forecasts[0].Signals.Count);
            Assert.AreEqual(_service.FormatText(a), _service.FormatText(b));
        }

        [TestMethod]
        public void Forecast_FailingPortfolio_GradeF()
        {
            var m = AddMovie("Action", Genre.Action);
            for (int i = 0; i < 5; i++)
            {
                var c = AddCustomer($"Bad{i}");
                AddActiveRental(c, m, rentedDaysAgo: 12, dueInDays: -3, dailyRate: 7m);
                AddDamage(c, m, DamageSeverity.Severe, 20);
                AddDamage(c, m, DamageSeverity.Severe, 50);
                AddDamage(c, m, DamageSeverity.Destroyed, 80);
            }

            var report = _service.Forecast();
            Assert.AreEqual("F", report.PortfolioGrade);
        }

        [TestMethod]
        public void Constructor_NullArgs_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new DamageRiskForecastService(null, _damageRepo, _customerRepo, _movieRepo, _clock));
            Assert.ThrowsException<ArgumentNullException>(() =>
                new DamageRiskForecastService(_rentalRepo, null, _customerRepo, _movieRepo, _clock));
            Assert.ThrowsException<ArgumentNullException>(() =>
                new DamageRiskForecastService(_rentalRepo, _damageRepo, null, _movieRepo, _clock));
            Assert.ThrowsException<ArgumentNullException>(() =>
                new DamageRiskForecastService(_rentalRepo, _damageRepo, _customerRepo, null, _clock));
            Assert.ThrowsException<ArgumentNullException>(() =>
                new DamageRiskForecastService(_rentalRepo, _damageRepo, _customerRepo, _movieRepo, null));
        }
    }
}
