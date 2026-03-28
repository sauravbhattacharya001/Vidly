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
    public class RentalTrendsServiceTests
    {
        private class TestRentalRepo : IRentalRepository
        {
            private readonly List<Rental> _r = new List<Rental>();
            private int _id = 1;
            public void Add(Rental r) { r.Id = _id++; _r.Add(r); }
            public Rental GetById(int id) => _r.FirstOrDefault(r => r.Id == id);
            public IReadOnlyList<Rental> GetAll() => _r.AsReadOnly();
            public void Update(Rental r) { var i = _r.FindIndex(x => x.Id == r.Id); if (i >= 0) _r[i] = r; }
            public void Remove(int id) => _r.RemoveAll(r => r.Id == id);
            public IReadOnlyList<Rental> GetActiveByCustomer(int c) => _r.Where(r => r.CustomerId == c && r.Status != RentalStatus.Returned).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetByMovie(int m) => _r.Where(r => r.MovieId == m).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetOverdue() => _r.Where(r => r.Status == RentalStatus.Overdue).ToList().AsReadOnly();
            public IReadOnlyList<Rental> Search(string q, RentalStatus? s) => GetAll();
            public Rental ReturnRental(int id) { var r = GetById(id); if (r != null) { r.Status = RentalStatus.Returned; r.ReturnDate = DateTime.Today; } return r; }
            public bool IsMovieRentedOut(int m) => _r.Any(r => r.MovieId == m && r.Status != RentalStatus.Returned);
            public Rental Checkout(Rental rental) { Add(rental); return rental; }
            public Rental Checkout(Rental rental, int max) => Checkout(rental);
            public RentalStats GetStats() => new RentalStats { TotalRentals = _r.Count };
        }

        private class TestMovieRepo : IMovieRepository
        {
            private readonly List<Movie> _m = new List<Movie>();
            public void Add(Movie m) => _m.Add(m);
            public Movie GetById(int id) => _m.FirstOrDefault(m => m.Id == id);
            public IReadOnlyList<Movie> GetAll() => _m.AsReadOnly();
            public void Update(Movie m) { var i = _m.FindIndex(x => x.Id == m.Id); if (i >= 0) _m[i] = m; }
            public void Remove(int id) => _m.RemoveAll(m => m.Id == id);
            public IReadOnlyList<Movie> GetByReleaseDate(int y, int mo) => new List<Movie>().AsReadOnly();
            public Movie GetRandom() => _m.FirstOrDefault();
            public IReadOnlyList<Movie> Search(string q, Genre? g, int? r) => GetAll();
        }

        private TestRentalRepo _rr;
        private TestMovieRepo _mr;
        private RentalTrendsService _svc;
        private readonly DateTime _d = new DateTime(2026, 3, 1);

        [TestInitialize]
        public void Setup() { _rr = new TestRentalRepo(); _mr = new TestMovieRepo(); _svc = new RentalTrendsService(_rr, _mr); }

        private void M(int id, string name, Genre g) => _mr.Add(new Movie { Id = id, Name = name, Genre = g });
        private void R(int mid, DateTime d) => _rr.Add(new Rental { MovieId = mid, CustomerId = 1, RentalDate = d, DueDate = d.AddDays(7), DailyRate = 3.99m, Status = RentalStatus.Active });

        [TestMethod][ExpectedException(typeof(ArgumentNullException))]
        public void Null_RentalRepo() => new RentalTrendsService(null, _mr);

        [TestMethod][ExpectedException(typeof(ArgumentNullException))]
        public void Null_MovieRepo() => new RentalTrendsService(_rr, null);

        [TestMethod]
        public void Empty_Report()
        {
            var r = _svc.GetTrendsReport(30, 10, _d);
            Assert.AreEqual(0, r.TotalRentals); Assert.AreEqual(0, r.TopMovies.Count);
            Assert.AreEqual(7, r.DayOfWeekBreakdown.Count); Assert.IsNull(r.PeakDay);
        }

        [TestMethod]
        public void Basic_Ranking()
        {
            M(1, "Action Hero", Genre.Action); M(2, "Comedy Gold", Genre.Comedy);
            for (int i = 0; i < 5; i++) R(1, _d.AddDays(-5));
            for (int i = 0; i < 2; i++) R(2, _d.AddDays(-3));
            var r = _svc.GetTrendsReport(30, 10, _d);
            Assert.AreEqual(7, r.TotalRentals);
            Assert.AreEqual("Action Hero", r.TopMovies[0].MovieName);
            Assert.AreEqual(1, r.TopMovies[0].Rank);
        }

        [TestMethod]
        public void Rising_Direction()
        {
            M(1, "X", Genre.Action);
            for (int i = 0; i < 2; i++) R(1, _d.AddDays(-45));
            for (int i = 0; i < 5; i++) R(1, _d.AddDays(-10));
            Assert.AreEqual(TrendDirection.Rising, _svc.GetTrendsReport(30, 10, _d).TopMovies[0].Direction);
        }

        [TestMethod]
        public void Cooling_Direction()
        {
            M(1, "X", Genre.Drama);
            for (int i = 0; i < 10; i++) R(1, _d.AddDays(-45));
            for (int i = 0; i < 3; i++) R(1, _d.AddDays(-5));
            Assert.AreEqual(TrendDirection.Cooling, _svc.GetTrendsReport(30, 10, _d).TopMovies[0].Direction);
        }

        [TestMethod]
        public void NewEntry_Direction()
        {
            M(1, "X", Genre.SciFi); R(1, _d.AddDays(-5));
            var r = _svc.GetTrendsReport(30, 10, _d);
            Assert.AreEqual(TrendDirection.NewEntry, r.TopMovies[0].Direction);
            Assert.AreEqual(1, r.NewEntries.Count);
        }

        [TestMethod]
        public void Stable_Direction()
        {
            M(1, "X", Genre.Comedy);
            for (int i = 0; i < 10; i++) R(1, _d.AddDays(-45));
            for (int i = 0; i < 10; i++) R(1, _d.AddDays(-5));
            Assert.AreEqual(TrendDirection.Stable, _svc.GetTrendsReport(30, 10, _d).TopMovies[0].Direction);
        }

        [TestMethod]
        public void Genre_MarketShare()
        {
            M(1, "A", Genre.Action); M(2, "C", Genre.Comedy);
            for (int i = 0; i < 3; i++) R(1, _d.AddDays(-5));
            R(2, _d.AddDays(-5));
            Assert.AreEqual(75.0, _svc.GetTrendsReport(30, 10, _d).GenreTrends.First(g => g.Genre == Genre.Action).MarketShare);
        }

        [TestMethod]
        public void DayOfWeek_7() { M(1, "T", Genre.Action); R(1, _d.AddDays(-5)); Assert.AreEqual(7, _svc.GetTrendsReport(30, 10, _d).DayOfWeekBreakdown.Count); }

        [TestMethod]
        public void PeakDay()
        {
            M(1, "B", Genre.Action); var p = _d.AddDays(-10);
            for (int i = 0; i < 5; i++) R(1, p); R(1, _d.AddDays(-20));
            var r = _svc.GetTrendsReport(30, 10, _d);
            Assert.AreEqual(p.Date, r.PeakDay); Assert.AreEqual(5, r.PeakDayRentals);
        }

        [TestMethod]
        public void MovieTrend_Valid() { M(1, "T", Genre.Action); R(1, _d.AddDays(-5)); var t = _svc.GetMovieTrend(1, 30, _d); Assert.AreEqual("T", t.MovieName); }

        [TestMethod]
        public void MovieTrend_NotFound() { Assert.IsNull(_svc.GetMovieTrend(999, 30, _d)); }

        [TestMethod]
        public void Trending_OnlyRisingNew()
        {
            M(1, "H", Genre.Action); M(2, "O", Genre.Drama);
            for (int i = 0; i < 5; i++) R(1, _d.AddDays(-5));
            for (int i = 0; i < 10; i++) R(2, _d.AddDays(-45)); R(2, _d.AddDays(-5));
            Assert.IsTrue(_svc.GetTrending(30, 10, _d).All(t => t.Direction == TrendDirection.Rising || t.Direction == TrendDirection.NewEntry));
        }

        [TestMethod] public void Clamp_Min() { Assert.AreEqual(1, _svc.GetTrendsReport(0, 10, _d).PeriodDays); }
        [TestMethod] public void Clamp_Max() { Assert.AreEqual(365, _svc.GetTrendsReport(999, 10, _d).PeriodDays); }

        [TestMethod]
        public void OverallChange()
        {
            M(1, "T", Genre.Action);
            for (int i = 0; i < 4; i++) R(1, _d.AddDays(-45));
            for (int i = 0; i < 8; i++) R(1, _d.AddDays(-5));
            Assert.AreEqual(100.0, _svc.GetTrendsReport(30, 10, _d).OverallChangePercent);
        }

        [TestMethod]
        public void AvgPerDay() { M(1, "T", Genre.Action); for (int i = 0; i < 30; i++) R(1, _d.AddDays(-15)); Assert.AreEqual(1.0, _svc.GetTrendsReport(30, 10, _d).AverageRentalsPerDay); }

        [TestMethod]
        public void RankChange()
        {
            M(1, "A", Genre.Action); M(2, "B", Genre.Comedy);
            for (int i = 0; i < 5; i++) R(2, _d.AddDays(-45));
            for (int i = 0; i < 2; i++) R(1, _d.AddDays(-45));
            for (int i = 0; i < 8; i++) R(1, _d.AddDays(-5)); R(2, _d.AddDays(-5));
            var a = _svc.GetTrendsReport(30, 10, _d).TopMovies.First(t => t.MovieName == "A");
            Assert.AreEqual(1, a.Rank); Assert.IsTrue(a.RankChange > 0);
        }

        [TestMethod]
        public void NewEntry_RankNull() { M(1, "N", Genre.Action); R(1, _d.AddDays(-5)); Assert.IsNull(_svc.GetTrendsReport(30, 10, _d).TopMovies[0].RankChange); }

        [TestMethod]
        public void Velocity_Positive() { M(1, "H", Genre.Action); R(1, _d.AddDays(-45)); for (int i = 0; i < 5; i++) R(1, _d.AddDays(-5)); Assert.IsTrue(_svc.GetMovieTrend(1, 30, _d).VelocityScore > 0); }

        [TestMethod]
        public void BiggestMovers_Ordered()
        {
            M(1, "S", Genre.Action); M(2, "B", Genre.Comedy);
            for (int i = 0; i < 5; i++) R(1, _d.AddDays(-45)); for (int i = 0; i < 7; i++) R(1, _d.AddDays(-5));
            for (int i = 0; i < 2; i++) R(2, _d.AddDays(-45)); for (int i = 0; i < 8; i++) R(2, _d.AddDays(-5));
            var bm = _svc.GetTrendsReport(30, 10, _d).BiggestMovers;
            if (bm.Count >= 2) Assert.IsTrue(bm[0].ChangePercent >= bm[1].ChangePercent);
        }

        [TestMethod]
        public void Falling() { M(1, "T", Genre.Horror); for (int i = 0; i < 10; i++) R(1, _d.AddDays(-45)); R(1, _d.AddDays(-5)); Assert.AreEqual(1, _svc.GetTrendsReport(30, 10, _d).FallingMovies.Count); }

        [TestMethod]
        public void Genre_Ordered()
        {
            M(1, "A", Genre.Action); M(2, "C", Genre.Comedy); M(3, "D", Genre.Drama);
            for (int i = 0; i < 5; i++) R(1, _d.AddDays(-5));
            for (int i = 0; i < 3; i++) R(2, _d.AddDays(-5)); R(3, _d.AddDays(-5));
            Assert.AreEqual(Genre.Action, _svc.GetTrendsReport(30, 10, _d).GenreTrends[0].Genre);
        }

        [TestMethod]
        public void TopCount() { for (int i = 1; i <= 10; i++) { M(i, $"M{i}", Genre.Action); R(i, _d.AddDays(-5)); } Assert.AreEqual(3, _svc.GetTrendsReport(30, 3, _d).TopMovies.Count); }

        [TestMethod]
        public void NoGenre_Excluded() { _mr.Add(new Movie { Id = 1, Name = "X", Genre = null }); R(1, _d.AddDays(-5)); var r = _svc.GetTrendsReport(30, 10, _d); Assert.AreEqual(0, r.GenreTrends.Count); Assert.AreEqual(1, r.TopMovies.Count); }

        [TestMethod]
        public void DiffWindows() { M(1, "T", Genre.Action); R(1, _d.AddDays(-5)); R(1, _d.AddDays(-20)); Assert.AreEqual(1, _svc.GetTrendsReport(7, 10, _d).TotalRentals); Assert.AreEqual(2, _svc.GetTrendsReport(30, 10, _d).TotalRentals); }

        [TestMethod]
        public void Trending_Velocity()
        {
            M(1, "S", Genre.Action); M(2, "F", Genre.Comedy);
            R(1, _d.AddDays(-5)); for (int i = 0; i < 10; i++) R(2, _d.AddDays(-5));
            var t = _svc.GetTrending(30, 10, _d);
            if (t.Count >= 2) Assert.IsTrue(t[0].VelocityScore >= t[1].VelocityScore);
        }

        [TestMethod]
        public void Trending_Count() { for (int i = 1; i <= 10; i++) { M(i, $"N{i}", Genre.Action); for (int j = 0; j < i; j++) R(i, _d.AddDays(-5)); } Assert.IsTrue(_svc.GetTrending(30, 3, _d).Count <= 3); }

        [TestMethod]
        public void Genre_TopMovie()
        {
            M(1, "Best", Genre.Action); M(2, "Ok", Genre.Action);
            for (int i = 0; i < 5; i++) R(1, _d.AddDays(-5)); R(2, _d.AddDays(-5));
            Assert.AreEqual("Best", _svc.GetTrendsReport(30, 10, _d).GenreTrends.First(g => g.Genre == Genre.Action).TopMovie);
        }
    }
}
