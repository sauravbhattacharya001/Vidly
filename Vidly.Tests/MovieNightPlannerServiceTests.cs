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
    public class MovieNightPlannerServiceTests
    {
        private class StubMovieRepository : IMovieRepository
        {
            private readonly List<Movie> _movies = new List<Movie>();
            private int _nextId = 1;
            public void Add(Movie m) { m.Id = _nextId++; _movies.Add(m); }
            public Movie AddAndReturn(Movie m) { Add(m); return m; }
            public Movie GetById(int id) => _movies.FirstOrDefault(m => m.Id == id);
            public IReadOnlyList<Movie> GetAll() => _movies.AsReadOnly();
            public void Update(Movie m) { }
            public void Remove(int id) => _movies.RemoveAll(m => m.Id == id);
            public IReadOnlyList<Movie> GetByReleaseDate(int y, int mo) =>
                _movies.Where(m => m.ReleaseDate?.Year == y && m.ReleaseDate?.Month == mo).ToList();
            public Movie GetRandom() => _movies.Count > 0 ? _movies[0] : null;
            public IReadOnlyList<Movie> Search(string q, Genre? g, int? r) =>
                _movies.Where(m => m.Name.Contains(q ?? "")).ToList();
        }

        private class StubRentalRepository : IRentalRepository
        {
            private readonly List<Rental> _rentals = new List<Rental>();
            private int _nextId = 1;
            private readonly HashSet<int> _rented = new HashSet<int>();
            public void SetMovieRentedOut(int id) => _rented.Add(id);
            public void Add(Rental r) { r.Id = _nextId++; _rentals.Add(r); }
            public Rental GetById(int id) => _rentals.FirstOrDefault(r => r.Id == id);
            public IReadOnlyList<Rental> GetAll() => _rentals.AsReadOnly();
            public void Update(Rental r) { }
            public void Remove(int id) => _rentals.RemoveAll(r => r.Id == id);
            public IReadOnlyList<Rental> GetActiveByCustomer(int cid) =>
                _rentals.Where(r => r.CustomerId == cid && r.Status != RentalStatus.Returned).ToList();
            public IReadOnlyList<Rental> GetByMovie(int mid) => _rentals.Where(r => r.MovieId == mid).ToList();
            public IReadOnlyList<Rental> GetOverdue() => _rentals.Where(r => r.Status == RentalStatus.Overdue).ToList();
            public IReadOnlyList<Rental> Search(string q, RentalStatus? s) => _rentals;
            public Rental ReturnRental(int id) => GetById(id);
            public bool IsMovieRentedOut(int mid) => _rented.Contains(mid);
            public Rental Checkout(Rental r) { Add(r); return r; }
            public Rental Checkout(Rental r, int maxConcurrentRentals) { return Checkout(r); }
            public RentalStats GetStats() => new RentalStats();
        }

        private static Movie MakeMovie(string name, Genre genre, int rating, DateTime? release = null) =>
            new Movie { Name = name, Genre = genre, Rating = rating, ReleaseDate = release ?? new DateTime(2024, 6, 15) };

        private (MovieNightPlannerService svc, StubMovieRepository movies, StubRentalRepository rentals)
            Create(int seed = 42)
        {
            var m = new StubMovieRepository();
            var r = new StubRentalRepository();
            return (new MovieNightPlannerService(m, r, seed), m, r);
        }

        private void Seed(StubMovieRepository m)
        {
            m.Add(MakeMovie("Die Hard", Genre.Action, 5, new DateTime(1988, 7, 15)));
            m.Add(MakeMovie("The Matrix", Genre.SciFi, 5, new DateTime(1999, 3, 31)));
            m.Add(MakeMovie("Inception", Genre.SciFi, 5, new DateTime(2010, 7, 16)));
            m.Add(MakeMovie("The Hangover", Genre.Comedy, 4, new DateTime(2009, 6, 5)));
            m.Add(MakeMovie("Schindler's List", Genre.Drama, 5, new DateTime(1993, 12, 15)));
            m.Add(MakeMovie("Get Out", Genre.Horror, 5, new DateTime(2017, 2, 24)));
            m.Add(MakeMovie("Toy Story", Genre.Animation, 5, new DateTime(1995, 11, 22)));
            m.Add(MakeMovie("The Notebook", Genre.Romance, 4, new DateTime(2004, 6, 25)));
            m.Add(MakeMovie("Planet Earth", Genre.Documentary, 5, new DateTime(2006, 3, 5)));
            m.Add(MakeMovie("Mad Max", Genre.Action, 5, new DateTime(2015, 5, 15)));
            m.Add(MakeMovie("Interstellar", Genre.SciFi, 5, new DateTime(2014, 11, 7)));
            m.Add(MakeMovie("Groundhog Day", Genre.Comedy, 4, new DateTime(1993, 2, 12)));
        }

        [TestMethod] [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullMovieRepo_Throws() => new MovieNightPlannerService(null, new StubRentalRepository());

        [TestMethod] [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullRentalRepo_Throws() => new MovieNightPlannerService(new StubMovieRepository(), null);

        [TestMethod] [ExpectedException(typeof(ArgumentNullException))]
        public void GeneratePlan_Null_Throws() { var (s, _, _) = Create(); s.GeneratePlan(null); }

        [TestMethod]
        public void EmptyCatalog_ReturnsEmptyPlan()
        {
            var (s, _, _) = Create();
            var p = s.GeneratePlan(new MovieNightRequest());
            Assert.AreEqual(0, p.MovieCount);
            Assert.AreEqual("No Movies Available", p.Title);
        }

        [TestMethod]
        public void SurpriseMe_ReturnsPlan()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.SurpriseMe, MovieCount = 3 });
            Assert.AreEqual(3, p.MovieCount);
            Assert.IsTrue(p.TotalMinutes > 0);
        }

        [TestMethod]
        public void DefaultCount_Is3()
        {
            var (s, m, _) = Create(); Seed(m);
            Assert.AreEqual(3, s.GeneratePlan(new MovieNightRequest()).MovieCount);
        }

        [TestMethod]
        public void ClampsCount_Min1()
        {
            var (s, m, _) = Create(); Seed(m);
            Assert.AreEqual(1, s.GeneratePlan(new MovieNightRequest { MovieCount = 0 }).MovieCount);
        }

        [TestMethod]
        public void ClampsCount_Max8()
        {
            var (s, m, _) = Create(); Seed(m);
            Assert.AreEqual(8, s.GeneratePlan(new MovieNightRequest { MovieCount = 20 }).MovieCount);
        }

        [TestMethod]
        public void GenreFocus_SingleGenre()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.GenreFocus, Genre = Genre.SciFi, MovieCount = 3 });
            Assert.IsTrue(p.MovieCount > 0);
            foreach (var slot in p.Slots) Assert.AreEqual(Genre.SciFi, slot.Movie.Genre);
        }

        [TestMethod]
        public void GenreMix_DifferentGenres()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.GenreMix, MovieCount = 4 });
            Assert.AreEqual(4, p.MovieCount);
            Assert.IsTrue(p.Slots.Select(x => x.Movie.Genre).Distinct().Count() >= 2);
        }

        [TestMethod]
        public void CriticsChoice_HighRated()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.CriticsChoice, MovieCount = 3 });
            foreach (var slot in p.Slots) Assert.IsTrue(slot.Movie.Rating >= 4);
            Assert.AreEqual("Critics' Choice Marathon", p.Title);
        }

        [TestMethod]
        public void FanFavorites_Works()
        {
            var (s, m, r) = Create(); Seed(m);
            for (int i = 0; i < 10; i++) r.Add(new Rental { CustomerId = i + 1, MovieId = 1, RentalDate = DateTime.Today });
            var p = s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.FanFavorites, MovieCount = 1 });
            Assert.AreEqual(1, p.MovieCount);
        }

        [TestMethod]
        public void HiddenGems_Works()
        {
            var (s, m, r) = Create(); Seed(m);
            for (int i = 0; i < 20; i++) r.Add(new Rental { CustomerId = i + 1, MovieId = 1, RentalDate = DateTime.Today });
            var p = s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.HiddenGems, MovieCount = 3 });
            Assert.IsTrue(p.MovieCount > 0);
        }

        [TestMethod]
        public void NewReleases_OnlyRecent()
        {
            var (s, m, _) = Create();
            m.Add(MakeMovie("New1", Genre.Action, 4, DateTime.Today.AddDays(-10)));
            m.Add(MakeMovie("New2", Genre.Comedy, 3, DateTime.Today.AddDays(-30)));
            m.Add(MakeMovie("Old", Genre.Drama, 5, new DateTime(2020, 1, 1)));
            var p = s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.NewReleases, MovieCount = 2 });
            Assert.AreEqual(2, p.MovieCount);
            foreach (var slot in p.Slots) Assert.IsTrue(slot.Movie.IsNewRelease);
        }

        [TestMethod]
        public void DecadeFocus_FromDecade()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.DecadeFocus, Decade = 1990, MovieCount = 3 });
            Assert.IsTrue(p.MovieCount > 0);
            foreach (var slot in p.Slots)
            {
                var y = slot.Movie.ReleaseDate?.Year ?? 0;
                Assert.IsTrue(y >= 1990 && y < 2000);
            }
        }

        [TestMethod]
        public void Schedule_CorrectTiming()
        {
            var (s, m, _) = Create(); Seed(m);
            var st = new DateTime(2026, 3, 4, 19, 0, 0);
            var p = s.GeneratePlan(new MovieNightRequest { MovieCount = 3, EstimatedRuntimeMinutes = 120, BreakMinutes = 15, StartTime = st });
            Assert.AreEqual(st, p.Slots[0].StartTime);
            Assert.AreEqual(st.AddMinutes(120), p.Slots[0].EndTime);
            Assert.AreEqual(st.AddMinutes(135), p.Slots[1].StartTime);
            Assert.AreEqual(st.AddMinutes(270), p.Slots[2].StartTime);
        }

        [TestMethod]
        public void Schedule_TotalIncludesBreaks()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { MovieCount = 3, EstimatedRuntimeMinutes = 120, BreakMinutes = 15 });
            Assert.AreEqual(390, p.TotalMinutes);
            Assert.AreEqual("6h 30m", p.TotalDuration);
        }

        [TestMethod]
        public void SingleMovie_NoBreak()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { MovieCount = 1, EstimatedRuntimeMinutes = 120, BreakMinutes = 15 });
            Assert.AreEqual(120, p.TotalMinutes);
            Assert.IsNull(p.Slots[0].BreakSuggestion);
        }

        [TestMethod]
        public void HasSlotNotes()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { MovieCount = 3 });
            foreach (var slot in p.Slots) Assert.IsNotNull(slot.SlotNote);
            Assert.IsTrue(p.Slots[0].SlotNote.Contains("Opening"));
        }

        [TestMethod]
        public void BreakSuggestions_ExceptLast()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { MovieCount = 3 });
            Assert.IsNotNull(p.Slots[0].BreakSuggestion);
            Assert.IsNotNull(p.Slots[1].BreakSuggestion);
            Assert.IsNull(p.Slots[2].BreakSuggestion);
        }

        [TestMethod]
        public void AllAvailable()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { MovieCount = 3 });
            Assert.AreEqual(p.MovieCount, p.AvailableCount);
            Assert.IsNull(p.AvailabilityNote);
        }

        [TestMethod]
        public void SomeRentedOut()
        {
            var (s, m, r) = Create(); Seed(m);
            for (int i = 1; i <= 12; i++) r.SetMovieRentedOut(i);
            var p = s.GeneratePlan(new MovieNightRequest { MovieCount = 3 });
            Assert.AreEqual(0, p.AvailableCount);
            Assert.IsTrue(p.AvailabilityNote.Contains("rented out"));
        }

        [TestMethod]
        public void Personalization_ExcludesRented()
        {
            var (s, m, r) = Create(); Seed(m);
            for (int i = 1; i <= 6; i++) r.Add(new Rental { CustomerId = 1, MovieId = i, RentalDate = DateTime.Today });
            var p = s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.SurpriseMe, MovieCount = 3, CustomerId = 1 });
            var rented = new HashSet<int> { 1, 2, 3, 4, 5, 6 };
            foreach (var slot in p.Slots) Assert.IsFalse(rented.Contains(slot.Movie.Id));
        }

        [TestMethod]
        public void SnackSuggestions_GenreSpecific()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.GenreFocus, Genre = Genre.Horror, MovieCount = 1 });
            Assert.IsTrue(p.SnackSuggestions.Count > 0);
            Assert.IsTrue(p.SnackSuggestions.Any(x => x.Contains("velvet") || x.Contains("worms") || x.Contains("Candy corn")));
        }

        [TestMethod]
        public void SnackSuggestions_MaxSix()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.GenreMix, MovieCount = 8 });
            Assert.IsTrue(p.SnackSuggestions.Count <= 6);
        }

        [TestMethod]
        public void SnackSuggestions_DefaultNoGenre()
        {
            var (s, m, _) = Create();
            m.Add(new Movie { Name = "Unknown", Rating = 3 });
            var p = s.GeneratePlan(new MovieNightRequest { MovieCount = 1 });
            Assert.IsTrue(p.SnackSuggestions.Any(x => x.Contains("popcorn") || x.Contains("Popcorn")));
        }

        [TestMethod]
        public void Duration_HoursAndMinutes()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { MovieCount = 2, EstimatedRuntimeMinutes = 90, BreakMinutes = 10 });
            Assert.AreEqual(190, p.TotalMinutes);
            Assert.AreEqual("3h 10m", p.TotalDuration);
        }

        [TestMethod]
        public void Duration_ExactHours()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { MovieCount = 1, EstimatedRuntimeMinutes = 120 });
            Assert.AreEqual("2h", p.TotalDuration);
        }

        [TestMethod]
        public void Alternatives_Returns()
        {
            var (s, m, _) = Create(); Seed(m);
            var plans = s.GenerateAlternatives(new MovieNightRequest { MovieCount = 2 }, 3);
            Assert.IsTrue(plans.Count >= 1 && plans.Count <= 3);
        }

        [TestMethod] [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Alternatives_Zero_Throws() { var (s, _, _) = Create(); s.GenerateAlternatives(new MovieNightRequest(), 0); }

        [TestMethod] [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Alternatives_TooMany_Throws() { var (s, _, _) = Create(); s.GenerateAlternatives(new MovieNightRequest(), 11); }

        [TestMethod]
        public void GetAvailableThemes_All()
        {
            var (s, _, _) = Create();
            var themes = s.GetAvailableThemes();
            Assert.AreEqual(Enum.GetValues(typeof(MovieNightTheme)).Length, themes.Count);
            foreach (var t in themes) { Assert.IsNotNull(t.Name); Assert.IsNotNull(t.Description); }
        }

        [TestMethod]
        public void GenreFocus_NoMatch_Empty()
        {
            var (s, m, _) = Create();
            m.Add(MakeMovie("Action", Genre.Action, 4));
            var p = s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.GenreFocus, Genre = Genre.Horror });
            Assert.AreEqual(0, p.MovieCount);
        }

        [TestMethod]
        public void NewReleases_NoNew_Empty()
        {
            var (s, m, _) = Create();
            m.Add(MakeMovie("Old", Genre.Drama, 5, new DateTime(2020, 1, 1)));
            Assert.AreEqual(0, s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.NewReleases }).MovieCount);
        }

        [TestMethod]
        public void HiddenGems_NoHighRated_Empty()
        {
            var (s, m, _) = Create();
            m.Add(new Movie { Name = "Low", Genre = Genre.Action, Rating = 2 });
            Assert.AreEqual(0, s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.HiddenGems, MovieCount = 1 }).MovieCount);
        }

        [TestMethod]
        public void ClampsBreak_Negative() { var (s, m, _) = Create(); Seed(m); Assert.AreEqual(240, s.GeneratePlan(new MovieNightRequest { MovieCount = 2, EstimatedRuntimeMinutes = 120, BreakMinutes = -5 }).TotalMinutes); }

        [TestMethod]
        public void ClampsBreak_Excessive() { var (s, m, _) = Create(); Seed(m); Assert.AreEqual(300, s.GeneratePlan(new MovieNightRequest { MovieCount = 2, EstimatedRuntimeMinutes = 120, BreakMinutes = 120 }).TotalMinutes); }

        [TestMethod]
        public void ClampsRuntime_Min60() { var (s, m, _) = Create(); Seed(m); Assert.AreEqual(60, s.GeneratePlan(new MovieNightRequest { MovieCount = 1, EstimatedRuntimeMinutes = 10 }).TotalMinutes); }

        [TestMethod]
        public void ClampsRuntime_Max240() { var (s, m, _) = Create(); Seed(m); Assert.AreEqual(240, s.GeneratePlan(new MovieNightRequest { MovieCount = 1, EstimatedRuntimeMinutes = 500 }).TotalMinutes); }

        [TestMethod]
        public void EndTime_Correct()
        {
            var (s, m, _) = Create(); Seed(m);
            var st = new DateTime(2026, 3, 4, 18, 0, 0);
            var p = s.GeneratePlan(new MovieNightRequest { MovieCount = 2, EstimatedRuntimeMinutes = 120, BreakMinutes = 15, StartTime = st });
            Assert.AreEqual(st.AddMinutes(255), p.EstimatedEndTime);
        }

        [TestMethod]
        public void DefaultStart_7PM()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { MovieCount = 1 });
            Assert.AreEqual(19, p.Slots[0].StartTime.Hour);
        }

        [TestMethod]
        public void AvailabilityNote_Singular()
        {
            var (s, m, r) = Create();
            m.Add(MakeMovie("Only", Genre.Action, 4));
            r.SetMovieRentedOut(1);
            Assert.IsTrue(s.GeneratePlan(new MovieNightRequest { MovieCount = 1 }).AvailabilityNote.Contains("1 movie is"));
        }

        [TestMethod]
        public void AvailabilityNote_Plural()
        {
            var (s, m, r) = Create(); Seed(m);
            for (int i = 1; i <= 12; i++) r.SetMovieRentedOut(i);
            Assert.IsTrue(s.GeneratePlan(new MovieNightRequest { MovieCount = 3 }).AvailabilityNote.Contains("movies are"));
        }

        [TestMethod]
        public void GenreFocus_RandomGenre_AllSame()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.GenreFocus, MovieCount = 2 });
            if (p.MovieCount >= 2)
            {
                var g = p.Slots[0].Movie.Genre;
                foreach (var slot in p.Slots) Assert.AreEqual(g, slot.Movie.Genre);
            }
        }

        [TestMethod]
        public void DecadeFocus_RandomDecade()
        {
            var (s, m, _) = Create(); Seed(m);
            var p = s.GeneratePlan(new MovieNightRequest { Theme = MovieNightTheme.DecadeFocus, MovieCount = 3 });
            Assert.IsTrue(p.MovieCount > 0 || p.Title.Contains("Best of"));
        }
    }
}
