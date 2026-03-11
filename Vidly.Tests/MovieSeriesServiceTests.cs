using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class MovieSeriesServiceTests
    {
        private MovieSeriesService _service;
        private List<Movie> _movies;

        [TestInitialize]
        public void Setup()
        {
            _service = new MovieSeriesService();
            _movies = new List<Movie>
            {
                new Movie { Id = 1, Name = "Fellowship of the Ring", ReleaseDate = new DateTime(2001, 12, 19), Genre = Genre.Action },
                new Movie { Id = 2, Name = "The Two Towers", ReleaseDate = new DateTime(2002, 12, 18), Genre = Genre.Action },
                new Movie { Id = 3, Name = "Return of the King", ReleaseDate = new DateTime(2003, 12, 17), Genre = Genre.Action },
                new Movie { Id = 4, Name = "The Hobbit", ReleaseDate = new DateTime(2012, 12, 14), Genre = Genre.Action },
                new Movie { Id = 5, Name = "Inception", ReleaseDate = new DateTime(2010, 7, 16), Genre = Genre.Thriller },
            };
        }

        // ── Create ──────────────────────────────────────────────────

        [TestMethod]
        public void CreateSeries_ValidName_ReturnsSeries()
        {
            var s = _service.CreateSeries("Lord of the Rings");
            Assert.AreEqual("Lord of the Rings", s.Name);
            Assert.IsTrue(s.Id > 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateSeries_EmptyName_Throws()
        {
            _service.CreateSeries("");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CreateSeries_DuplicateName_Throws()
        {
            _service.CreateSeries("Star Wars");
            _service.CreateSeries("star wars");
        }

        [TestMethod]
        public void CreateSeries_WithGenreAndOngoing_SetsProperties()
        {
            var s = _service.CreateSeries("MCU", genre: Genre.Action, isOngoing: true);
            Assert.AreEqual(Genre.Action, s.Genre);
            Assert.IsTrue(s.IsOngoing);
        }

        // ── List / Search ───────────────────────────────────────────

        [TestMethod]
        public void ListSeries_FiltersByGenre()
        {
            _service.CreateSeries("Action Series", genre: Genre.Action);
            _service.CreateSeries("Comedy Series", genre: Genre.Comedy);

            var action = _service.ListSeries(Genre.Action);
            Assert.AreEqual(1, action.Count);
            Assert.AreEqual("Action Series", action[0].Name);
        }

        [TestMethod]
        public void ListSeries_NoFilter_ReturnsAll()
        {
            _service.CreateSeries("A");
            _service.CreateSeries("B");
            Assert.AreEqual(2, _service.ListSeries().Count);
        }

        [TestMethod]
        public void SearchSeries_FindsPartialMatch()
        {
            _service.CreateSeries("Lord of the Rings");
            _service.CreateSeries("Star Wars");

            var results = _service.SearchSeries("lord");
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void SearchSeries_EmptyQuery_ReturnsEmpty()
        {
            _service.CreateSeries("Test");
            Assert.AreEqual(0, _service.SearchSeries("").Count);
        }

        // ── Delete ──────────────────────────────────────────────────

        [TestMethod]
        public void DeleteSeries_RemovesEntriesAndProgress()
        {
            var s = _service.CreateSeries("LOTR");
            var e = _service.AddMovie(s.Id, 1, 1);
            _service.MarkWatched(100, e.Id);

            Assert.IsTrue(_service.DeleteSeries(s.Id));
            Assert.IsNull(_service.GetSeries(s.Id));
            Assert.AreEqual(0, _service.GetSeriesEntries(s.Id).Count);
        }

        [TestMethod]
        public void DeleteSeries_NotFound_ReturnsFalse()
        {
            Assert.IsFalse(_service.DeleteSeries(999));
        }

        // ── Add Movie ───────────────────────────────────────────────

        [TestMethod]
        public void AddMovie_ValidInput_ReturnsEntry()
        {
            var s = _service.CreateSeries("LOTR");
            var e = _service.AddMovie(s.Id, 1, 1, "Part 1");
            Assert.AreEqual(1, e.OrderIndex);
            Assert.AreEqual("Part 1", e.Label);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void AddMovie_InvalidSeries_Throws()
        {
            _service.AddMovie(999, 1, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void AddMovie_ZeroOrder_Throws()
        {
            var s = _service.CreateSeries("Test");
            _service.AddMovie(s.Id, 1, 0);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void AddMovie_DuplicateMovie_Throws()
        {
            var s = _service.CreateSeries("Test");
            _service.AddMovie(s.Id, 1, 1);
            _service.AddMovie(s.Id, 1, 2);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void AddMovie_DuplicateOrder_Throws()
        {
            var s = _service.CreateSeries("Test");
            _service.AddMovie(s.Id, 1, 1);
            _service.AddMovie(s.Id, 2, 1);
        }

        // ── Remove Movie ────────────────────────────────────────────

        [TestMethod]
        public void RemoveMovie_Exists_ReturnsTrue()
        {
            var s = _service.CreateSeries("Test");
            _service.AddMovie(s.Id, 1, 1);
            Assert.IsTrue(_service.RemoveMovie(s.Id, 1));
            Assert.AreEqual(0, _service.GetSeriesEntries(s.Id).Count);
        }

        [TestMethod]
        public void RemoveMovie_NotFound_ReturnsFalse()
        {
            var s = _service.CreateSeries("Test");
            Assert.IsFalse(_service.RemoveMovie(s.Id, 999));
        }

        // ── Get Entries ─────────────────────────────────────────────

        [TestMethod]
        public void GetSeriesEntries_ReturnsSortedByOrder()
        {
            var s = _service.CreateSeries("LOTR");
            _service.AddMovie(s.Id, 3, 3);
            _service.AddMovie(s.Id, 1, 1);
            _service.AddMovie(s.Id, 2, 2);

            var entries = _service.GetSeriesEntries(s.Id);
            Assert.AreEqual(3, entries.Count);
            Assert.AreEqual(1, entries[0].MovieId);
            Assert.AreEqual(2, entries[1].MovieId);
            Assert.AreEqual(3, entries[2].MovieId);
        }

        // ── Reorder ─────────────────────────────────────────────────

        [TestMethod]
        public void ReorderEntry_UpdatesOrderIndex()
        {
            var s = _service.CreateSeries("Test");
            var e = _service.AddMovie(s.Id, 1, 1);
            Assert.IsTrue(_service.ReorderEntry(e.Id, 5));
            Assert.AreEqual(5, _service.GetSeriesEntries(s.Id)[0].OrderIndex);
        }

        [TestMethod]
        public void ReorderEntry_NotFound_ReturnsFalse()
        {
            Assert.IsFalse(_service.ReorderEntry(999, 1));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ReorderEntry_ConflictingOrder_Throws()
        {
            var s = _service.CreateSeries("Test");
            _service.AddMovie(s.Id, 1, 1);
            var e2 = _service.AddMovie(s.Id, 2, 2);
            _service.ReorderEntry(e2.Id, 1);
        }

        // ── Progress Tracking ───────────────────────────────────────

        [TestMethod]
        public void MarkWatched_ValidInput_ReturnsProgress()
        {
            var s = _service.CreateSeries("Test");
            var e = _service.AddMovie(s.Id, 1, 1);

            var p = _service.MarkWatched(1, e.Id);
            Assert.AreEqual(1, p.CustomerId);
            Assert.AreEqual(e.Id, p.SeriesEntryId);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void MarkWatched_AlreadyWatched_Throws()
        {
            var s = _service.CreateSeries("Test");
            var e = _service.AddMovie(s.Id, 1, 1);
            _service.MarkWatched(1, e.Id);
            _service.MarkWatched(1, e.Id);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void MarkWatched_InvalidEntry_Throws()
        {
            _service.MarkWatched(1, 999);
        }

        [TestMethod]
        public void UnmarkWatched_Exists_ReturnsTrue()
        {
            var s = _service.CreateSeries("Test");
            var e = _service.AddMovie(s.Id, 1, 1);
            _service.MarkWatched(1, e.Id);
            Assert.IsTrue(_service.UnmarkWatched(1, e.Id));
        }

        [TestMethod]
        public void UnmarkWatched_NotFound_ReturnsFalse()
        {
            Assert.IsFalse(_service.UnmarkWatched(1, 999));
        }

        // ── Progress Summary ────────────────────────────────────────

        [TestMethod]
        public void GetProgress_PartiallyWatched_ShowsCorrectPercentAndNextUp()
        {
            var s = _service.CreateSeries("LOTR");
            var e1 = _service.AddMovie(s.Id, 1, 1);
            var e2 = _service.AddMovie(s.Id, 2, 2);
            var e3 = _service.AddMovie(s.Id, 3, 3);

            _service.MarkWatched(1, e1.Id);

            var progress = _service.GetProgress(1, s.Id, _movies);
            Assert.AreEqual(3, progress.TotalMovies);
            Assert.AreEqual(1, progress.WatchedCount);
            Assert.AreEqual(33.3, progress.CompletionPercent);
            Assert.IsFalse(progress.IsComplete);
            Assert.IsNotNull(progress.NextUp);
            Assert.AreEqual(2, progress.NextUp.MovieId);
            Assert.AreEqual("The Two Towers", progress.NextUp.MovieName);
        }

        [TestMethod]
        public void GetProgress_AllWatched_IsComplete()
        {
            var s = _service.CreateSeries("LOTR");
            var e1 = _service.AddMovie(s.Id, 1, 1);
            var e2 = _service.AddMovie(s.Id, 2, 2);

            _service.MarkWatched(1, e1.Id);
            _service.MarkWatched(1, e2.Id);

            var progress = _service.GetProgress(1, s.Id, _movies);
            Assert.IsTrue(progress.IsComplete);
            Assert.AreEqual(100.0, progress.CompletionPercent);
            Assert.IsNull(progress.NextUp);
        }

        [TestMethod]
        public void GetProgress_EmptySeries_ZeroPercent()
        {
            var s = _service.CreateSeries("Empty");
            var progress = _service.GetProgress(1, s.Id, _movies);
            Assert.AreEqual(0, progress.TotalMovies);
            Assert.AreEqual(0, progress.CompletionPercent);
        }

        [TestMethod]
        public void GetProgress_NotFoundSeries_ReturnsNull()
        {
            Assert.IsNull(_service.GetProgress(1, 999, _movies));
        }

        // ── All Progress / Next Up ──────────────────────────────────

        [TestMethod]
        public void GetAllProgress_MultipleSeriesOrdered()
        {
            var s1 = _service.CreateSeries("Alpha");
            var s2 = _service.CreateSeries("Beta");
            _service.AddMovie(s1.Id, 1, 1);
            _service.AddMovie(s2.Id, 2, 1);

            var all = _service.GetAllProgress(1, _movies);
            Assert.AreEqual(2, all.Count);
        }

        [TestMethod]
        public void GetNextUpRecommendations_ReturnsOnlyInProgress()
        {
            var s1 = _service.CreateSeries("LOTR");
            var e1 = _service.AddMovie(s1.Id, 1, 1);
            var e2 = _service.AddMovie(s1.Id, 2, 2);
            _service.MarkWatched(1, e1.Id);

            var s2 = _service.CreateSeries("Standalone");
            var e3 = _service.AddMovie(s2.Id, 5, 1);
            _service.MarkWatched(1, e3.Id); // complete

            var recs = _service.GetNextUpRecommendations(1, _movies);
            Assert.AreEqual(1, recs.Count);
            Assert.AreEqual("The Two Towers", recs[0].MovieName);
        }

        // ── Series For Movie ────────────────────────────────────────

        [TestMethod]
        public void GetSeriesForMovie_FindsAllSeries()
        {
            var s1 = _service.CreateSeries("LOTR");
            var s2 = _service.CreateSeries("Middle Earth");
            _service.AddMovie(s1.Id, 1, 1);
            _service.AddMovie(s2.Id, 1, 1);

            var series = _service.GetSeriesForMovie(1);
            Assert.AreEqual(2, series.Count);
        }

        [TestMethod]
        public void GetSeriesForMovie_NotInAnySeries_ReturnsEmpty()
        {
            Assert.AreEqual(0, _service.GetSeriesForMovie(999).Count);
        }

        // ── Stats ───────────────────────────────────────────────────

        [TestMethod]
        public void GetStats_ReturnsCorrectCounts()
        {
            var s = _service.CreateSeries("Test");
            var e = _service.AddMovie(s.Id, 1, 1);
            _service.MarkWatched(1, e.Id);

            var stats = _service.GetStats();
            Assert.AreEqual(1, stats.TotalSeries);
            Assert.AreEqual(1, stats.TotalEntries);
            Assert.AreEqual(1, stats.TotalProgressRecords);
            Assert.AreEqual("Test", stats.MostPopularSeries);
        }

        [TestMethod]
        public void GetStats_Empty_AllZero()
        {
            var stats = _service.GetStats();
            Assert.AreEqual(0, stats.TotalSeries);
            Assert.IsNull(stats.MostPopularSeries);
        }
    }
}
