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
    public class MovieCurationServiceTests
    {
        private InMemoryMovieRepository _movieRepo;
        private InMemoryRentalRepository _rentalRepo;
        private MovieCurationService _service;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new InMemoryMovieRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _service = new MovieCurationService(_movieRepo, _rentalRepo);
        }

        // -- Constructor --

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new MovieCurationService(null, _rentalRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new MovieCurationService(_movieRepo, null);
        }

        // -- CreateList --

        [TestMethod]
        public void CreateList_ValidInput_ReturnsList()
        {
            var list = _service.CreateList("Staff Faves", "Our picks",
                CurationThemes.StaffPicks, "Alice", 1);

            Assert.IsNotNull(list);
            Assert.AreEqual("Staff Faves", list.Title);
            Assert.AreEqual(CurationThemes.StaffPicks, list.Theme);
            Assert.AreEqual("Alice", list.CuratorName);
            Assert.AreEqual(1, list.CuratorStaffId);
            Assert.IsFalse(list.IsFeatured);
            Assert.AreEqual(0, list.Entries.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateList_EmptyTitle_Throws()
        {
            _service.CreateList("", "desc", CurationThemes.StaffPicks, "Alice", 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateList_EmptyCurator_Throws()
        {
            _service.CreateList("Title", "desc", CurationThemes.StaffPicks, "", 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateList_InvalidTheme_Throws()
        {
            _service.CreateList("Title", "desc", "Nonexistent Theme", "Alice", 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateList_PastExpiry_Throws()
        {
            _service.CreateList("Title", "desc", CurationThemes.StaffPicks,
                "Alice", 1, DateTime.UtcNow.AddDays(-1));
        }

        [TestMethod]
        public void CreateList_WithFutureExpiry_Succeeds()
        {
            var future = DateTime.UtcNow.AddDays(30);
            var list = _service.CreateList("Seasonal", "desc",
                CurationThemes.RainyDay, "Bob", 2, future);
            Assert.IsNotNull(list.ExpiresAt);
        }

        [TestMethod]
        public void CreateList_AssignsIncrementingIds()
        {
            var l1 = _service.CreateList("A", "d", CurationThemes.StaffPicks, "A", 1);
            var l2 = _service.CreateList("B", "d", CurationThemes.HiddenGems, "B", 2);
            Assert.AreEqual(l1.Id + 1, l2.Id);
        }

        // -- GetList --

        [TestMethod]
        public void GetList_ExistingId_Returns()
        {
            var created = _service.CreateList("Test", "d", CurationThemes.DateNight, "X", 1);
            var found = _service.GetList(created.Id);
            Assert.IsNotNull(found);
            Assert.AreEqual(created.Id, found.Id);
        }

        [TestMethod]
        public void GetList_InvalidId_ReturnsNull()
        {
            Assert.IsNull(_service.GetList(999));
        }

        // -- DeleteList --

        [TestMethod]
        public void DeleteList_ExistingId_ReturnsTrue()
        {
            var list = _service.CreateList("Del", "d", CurationThemes.FeelGood, "X", 1);
            Assert.IsTrue(_service.DeleteList(list.Id));
            Assert.IsNull(_service.GetList(list.Id));
        }

        [TestMethod]
        public void DeleteList_InvalidId_ReturnsFalse()
        {
            Assert.IsFalse(_service.DeleteList(999));
        }

        // -- AddMovie --

        [TestMethod]
        public void AddMovie_ValidInput_AddsEntry()
        {
            var list = _service.CreateList("Test", "d", CurationThemes.StaffPicks, "X", 1);
            var entry = _service.AddMovie(list.Id, 1, "Great film!");

            Assert.IsNotNull(entry);
            Assert.AreEqual(1, entry.MovieId);
            Assert.AreEqual("Great film!", entry.CuratorNote);
            Assert.AreEqual(1, entry.Position);
            Assert.AreEqual(1, list.Entries.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void AddMovie_InvalidList_Throws()
        {
            _service.AddMovie(999, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void AddMovie_InvalidMovie_Throws()
        {
            var list = _service.CreateList("T", "d", CurationThemes.StaffPicks, "X", 1);
            _service.AddMovie(list.Id, 9999);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void AddMovie_Duplicate_Throws()
        {
            var list = _service.CreateList("T", "d", CurationThemes.StaffPicks, "X", 1);
            _service.AddMovie(list.Id, 1);
            _service.AddMovie(list.Id, 1);
        }

        [TestMethod]
        public void AddMovie_MultipleMovies_CorrectPositions()
        {
            var list = _service.CreateList("T", "d", CurationThemes.StaffPicks, "X", 1);
            _service.AddMovie(list.Id, 1);
            _service.AddMovie(list.Id, 2);
            _service.AddMovie(list.Id, 3);

            Assert.AreEqual(3, list.Entries.Count);
            Assert.AreEqual(1, list.Entries[0].Position);
            Assert.AreEqual(2, list.Entries[1].Position);
            Assert.AreEqual(3, list.Entries[2].Position);
        }

        // -- RemoveMovie --

        [TestMethod]
        public void RemoveMovie_ExistingEntry_ReturnsTrue()
        {
            var list = _service.CreateList("T", "d", CurationThemes.StaffPicks, "X", 1);
            _service.AddMovie(list.Id, 1);
            _service.AddMovie(list.Id, 2);

            Assert.IsTrue(_service.RemoveMovie(list.Id, 1));
            Assert.AreEqual(1, list.Entries.Count);
            Assert.AreEqual(1, list.Entries[0].Position);
        }

        [TestMethod]
        public void RemoveMovie_InvalidList_ReturnsFalse()
        {
            Assert.IsFalse(_service.RemoveMovie(999, 1));
        }

        [TestMethod]
        public void RemoveMovie_MovieNotInList_ReturnsFalse()
        {
            var list = _service.CreateList("T", "d", CurationThemes.StaffPicks, "X", 1);
            Assert.IsFalse(_service.RemoveMovie(list.Id, 999));
        }

        // -- ReorderMovie --

        [TestMethod]
        public void ReorderMovie_ValidReorder_UpdatesPositions()
        {
            var list = _service.CreateList("T", "d", CurationThemes.StaffPicks, "X", 1);
            _service.AddMovie(list.Id, 1);
            _service.AddMovie(list.Id, 2);
            _service.AddMovie(list.Id, 3);

            Assert.IsTrue(_service.ReorderMovie(list.Id, 3, 1));
            Assert.AreEqual(3, list.Entries[0].MovieId);
            Assert.AreEqual(1, list.Entries[0].Position);
        }

        [TestMethod]
        public void ReorderMovie_InvalidPosition_ReturnsFalse()
        {
            var list = _service.CreateList("T", "d", CurationThemes.StaffPicks, "X", 1);
            _service.AddMovie(list.Id, 1);
            Assert.IsFalse(_service.ReorderMovie(list.Id, 1, 5));
        }

        [TestMethod]
        public void ReorderMovie_InvalidList_ReturnsFalse()
        {
            Assert.IsFalse(_service.ReorderMovie(999, 1, 1));
        }

        // -- FeatureList --

        [TestMethod]
        public void FeatureList_SetsIsFeatured()
        {
            var list = _service.CreateList("T", "d", CurationThemes.StaffPicks, "X", 1);
            Assert.IsTrue(_service.FeatureList(list.Id));
            Assert.IsTrue(list.IsFeatured);
        }

        [TestMethod]
        public void FeatureList_AlreadyFeatured_ReturnsTrue()
        {
            var list = _service.CreateList("T", "d", CurationThemes.StaffPicks, "X", 1);
            _service.FeatureList(list.Id);
            Assert.IsTrue(_service.FeatureList(list.Id));
        }

        [TestMethod]
        public void FeatureList_MaxThree_UnfeaturesOldest()
        {
            var l1 = _service.CreateList("A", "d", CurationThemes.StaffPicks, "X", 1);
            var l2 = _service.CreateList("B", "d", CurationThemes.HiddenGems, "X", 1);
            var l3 = _service.CreateList("C", "d", CurationThemes.DateNight, "X", 1);
            var l4 = _service.CreateList("D", "d", CurationThemes.FeelGood, "X", 1);

            _service.FeatureList(l1.Id);
            _service.FeatureList(l2.Id);
            _service.FeatureList(l3.Id);
            _service.FeatureList(l4.Id);

            Assert.IsFalse(l1.IsFeatured);
            Assert.IsTrue(l4.IsFeatured);
        }

        [TestMethod]
        public void FeatureList_InvalidId_ReturnsFalse()
        {
            Assert.IsFalse(_service.FeatureList(999));
        }

        // -- UnfeatureList --

        [TestMethod]
        public void UnfeatureList_RemovesFeatured()
        {
            var list = _service.CreateList("T", "d", CurationThemes.StaffPicks, "X", 1);
            _service.FeatureList(list.Id);
            Assert.IsTrue(_service.UnfeatureList(list.Id));
            Assert.IsFalse(list.IsFeatured);
        }

        [TestMethod]
        public void UnfeatureList_InvalidId_ReturnsFalse()
        {
            Assert.IsFalse(_service.UnfeatureList(999));
        }

        // -- Voting --

        [TestMethod]
        public void UpVote_IncrementsCount()
        {
            var list = _service.CreateList("T", "d", CurationThemes.StaffPicks, "X", 1);
            _service.UpVote(list.Id);
            _service.UpVote(list.Id);
            Assert.AreEqual(2, list.UpVotes);
        }

        [TestMethod]
        public void DownVote_IncrementsCount()
        {
            var list = _service.CreateList("T", "d", CurationThemes.StaffPicks, "X", 1);
            _service.DownVote(list.Id);
            Assert.AreEqual(1, list.DownVotes);
        }

        [TestMethod]
        public void UpVote_InvalidId_ReturnsFalse()
        {
            Assert.IsFalse(_service.UpVote(999));
        }

        [TestMethod]
        public void DownVote_InvalidId_ReturnsFalse()
        {
            Assert.IsFalse(_service.DownVote(999));
        }

        // -- GetAllLists --

        [TestMethod]
        public void GetAllLists_FilterByTheme()
        {
            _service.CreateList("A", "d", CurationThemes.StaffPicks, "X", 1);
            _service.CreateList("B", "d", CurationThemes.HiddenGems, "X", 1);

            var result = _service.GetAllLists(theme: CurationThemes.StaffPicks);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("A", result[0].Title);
        }

        [TestMethod]
        public void GetAllLists_FilterByCurator()
        {
            _service.CreateList("A", "d", CurationThemes.StaffPicks, "Alice", 1);
            _service.CreateList("B", "d", CurationThemes.HiddenGems, "Bob", 2);

            var result = _service.GetAllLists(curatorName: "alice");
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void GetAllLists_FeaturedOnly()
        {
            var l1 = _service.CreateList("A", "d", CurationThemes.StaffPicks, "X", 1);
            _service.CreateList("B", "d", CurationThemes.HiddenGems, "X", 1);
            _service.FeatureList(l1.Id);

            var result = _service.GetAllLists(featuredOnly: true);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("A", result[0].Title);
        }

        [TestMethod]
        public void GetAllLists_OrderedByFeaturedThenVotes()
        {
            var l1 = _service.CreateList("Low", "d", CurationThemes.StaffPicks, "X", 1);
            var l2 = _service.CreateList("High", "d", CurationThemes.HiddenGems, "X", 1);
            _service.UpVote(l2.Id);
            _service.UpVote(l2.Id);

            var result = _service.GetAllLists();
            Assert.AreEqual("High", result[0].Title);
        }

        // -- GetTopLists --

        [TestMethod]
        public void GetTopLists_ReturnsTopByNetVotes()
        {
            var l1 = _service.CreateList("A", "d", CurationThemes.StaffPicks, "X", 1);
            var l2 = _service.CreateList("B", "d", CurationThemes.HiddenGems, "X", 1);
            _service.UpVote(l1.Id);
            _service.DownVote(l2.Id);

            var top = _service.GetTopLists(1);
            Assert.AreEqual(1, top.Count);
            Assert.AreEqual("A", top[0].Title);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetTopLists_ZeroCount_Throws()
        {
            _service.GetTopLists(0);
        }

        // -- GetListsByCurator --

        [TestMethod]
        public void GetListsByCurator_ReturnsOnlyMatchingStaffId()
        {
            _service.CreateList("A", "d", CurationThemes.StaffPicks, "Alice", 1);
            _service.CreateList("B", "d", CurationThemes.HiddenGems, "Bob", 2);
            _service.CreateList("C", "d", CurationThemes.DateNight, "Alice", 1);

            var result = _service.GetListsByCurator(1);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.All(l => l.CuratorStaffId == 1));
        }

        // -- GetListStats --

        [TestMethod]
        public void GetListStats_ReturnsCorrectCounts()
        {
            var list = _service.CreateList("T", "d", CurationThemes.StaffPicks, "X", 1);
            _service.AddMovie(list.Id, 1);
            _service.AddMovie(list.Id, 2);
            _service.UpVote(list.Id);
            _service.UpVote(list.Id);
            _service.DownVote(list.Id);

            var stats = _service.GetListStats(list.Id);
            Assert.IsNotNull(stats);
            Assert.AreEqual(2, stats.MovieCount);
            Assert.AreEqual(2, stats.UpVotes);
            Assert.AreEqual(1, stats.DownVotes);
            Assert.IsTrue(stats.ApprovalRate > 60 && stats.ApprovalRate < 70);
        }

        [TestMethod]
        public void GetListStats_InvalidId_ReturnsNull()
        {
            Assert.IsNull(_service.GetListStats(999));
        }

        // -- GetFrequentlyCuratedMovies --

        [TestMethod]
        public void GetFrequentlyCuratedMovies_FindsOverlap()
        {
            var l1 = _service.CreateList("A", "d", CurationThemes.StaffPicks, "X", 1);
            var l2 = _service.CreateList("B", "d", CurationThemes.HiddenGems, "X", 1);
            _service.AddMovie(l1.Id, 1);
            _service.AddMovie(l1.Id, 2);
            _service.AddMovie(l2.Id, 1);

            var frequent = _service.GetFrequentlyCuratedMovies(2);
            Assert.AreEqual(1, frequent.Count);
            Assert.IsTrue(frequent.ContainsKey(1));
            Assert.AreEqual(2, frequent[1].Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetFrequentlyCuratedMovies_ZeroMinLists_Throws()
        {
            _service.GetFrequentlyCuratedMovies(0);
        }

        // -- GenerateReport --

        [TestMethod]
        public void GenerateReport_EmptyStore_HasRecommendations()
        {
            var report = _service.GenerateReport();
            Assert.AreEqual(0, report.TotalLists);
            Assert.IsTrue(report.Recommendations.Any(r => r.Contains("Start curating")));
        }

        [TestMethod]
        public void GenerateReport_WithData_PopulatesFields()
        {
            var l1 = _service.CreateList("A", "d", CurationThemes.StaffPicks, "Alice", 1);
            var l2 = _service.CreateList("B", "d", CurationThemes.StaffPicks, "Bob", 2);
            _service.FeatureList(l1.Id);
            _service.AddMovie(l1.Id, 1);
            _service.AddMovie(l2.Id, 2);

            var report = _service.GenerateReport();
            Assert.AreEqual(2, report.TotalLists);
            Assert.AreEqual(1, report.FeaturedLists);
            Assert.AreEqual(2, report.TotalCurators);
            Assert.AreEqual(2, report.TotalMoviesCurated);
            Assert.AreEqual(CurationThemes.StaffPicks, report.MostPopularTheme);
            Assert.IsNotNull(report.MostUpvotedList);
            Assert.IsNotNull(report.MostRecentList);
        }

        [TestMethod]
        public void GenerateReport_ThemeDistribution_Correct()
        {
            _service.CreateList("A", "d", CurationThemes.StaffPicks, "X", 1);
            _service.CreateList("B", "d", CurationThemes.StaffPicks, "X", 1);
            _service.CreateList("C", "d", CurationThemes.DateNight, "X", 1);

            var report = _service.GenerateReport();
            Assert.AreEqual(2, report.ThemeDistribution[CurationThemes.StaffPicks]);
            Assert.AreEqual(1, report.ThemeDistribution[CurationThemes.DateNight]);
        }

        [TestMethod]
        public void GenerateReport_CuratorLeaderboard_Correct()
        {
            _service.CreateList("A", "d", CurationThemes.StaffPicks, "Alice", 1);
            _service.CreateList("B", "d", CurationThemes.HiddenGems, "Alice", 1);
            _service.CreateList("C", "d", CurationThemes.DateNight, "Bob", 2);

            var report = _service.GenerateReport();
            Assert.AreEqual("Alice", report.TopCurator);
            Assert.AreEqual(2, report.TopCuratorListCount);
        }

        // -- SuggestMoviesForTheme --

        [TestMethod]
        public void SuggestMoviesForTheme_ReturnsSuggestions()
        {
            var suggestions = _service.SuggestMoviesForTheme(CurationThemes.FamilyFun, 5);
            Assert.IsTrue(suggestions.Count > 0);
            Assert.IsTrue(suggestions.Count <= 5);
        }

        [TestMethod]
        public void SuggestMoviesForTheme_ExcludesAlreadyCurated()
        {
            var list = _service.CreateList("Fam", "d", CurationThemes.FamilyFun, "X", 1);
            _service.AddMovie(list.Id, 1); // Shrek (Animation)

            var suggestions = _service.SuggestMoviesForTheme(CurationThemes.FamilyFun, 10);
            Assert.IsFalse(suggestions.Any(s => s.MovieId == 1));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SuggestMoviesForTheme_EmptyTheme_Throws()
        {
            _service.SuggestMoviesForTheme("", 5);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void SuggestMoviesForTheme_ZeroMax_Throws()
        {
            _service.SuggestMoviesForTheme(CurationThemes.StaffPicks, 0);
        }

        [TestMethod]
        public void SuggestMoviesForTheme_ScoresGenreMatch()
        {
            var suggestions = _service.SuggestMoviesForTheme(CurationThemes.FamilyFun, 10);
            if (suggestions.Count >= 2)
            {
                Assert.IsTrue(suggestions[0].Score >= suggestions[1].Score);
            }
        }
    }
}
