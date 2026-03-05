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
    public class TaggingServiceTests
    {
        #region Helpers

        private InMemoryTagRepository _tagRepo;
        private InMemoryMovieRepository _movieRepo;
        private TaggingService _service;

        [TestInitialize]
        public void SetUp()
        {
            _tagRepo = new InMemoryTagRepository();
            _movieRepo = new InMemoryMovieRepository();
            _service = new TaggingService(_tagRepo, _movieRepo);

            // Seed movies
            _movieRepo.Add(new Movie { Name = "Inception", Genre = Genre.SciFi, Rating = 5, ReleaseDate = new DateTime(2010, 7, 16) });
            _movieRepo.Add(new Movie { Name = "The Hangover", Genre = Genre.Comedy, Rating = 4, ReleaseDate = new DateTime(2009, 6, 5) });
            _movieRepo.Add(new Movie { Name = "Interstellar", Genre = Genre.SciFi, Rating = 5, ReleaseDate = new DateTime(2014, 11, 7) });
            _movieRepo.Add(new Movie { Name = "Titanic", Genre = Genre.Romance, Rating = 4, ReleaseDate = new DateTime(1997, 12, 19) });
            _movieRepo.Add(new Movie { Name = "Fresh Release", Genre = Genre.Action, Rating = 3, ReleaseDate = DateTime.Today.AddDays(-10) });
        }

        private MovieTag CreateTag(string name, bool staffPick = false)
        {
            return _service.CreateTag(name, "Test tag", "#FF0000", staffPick, "admin");
        }

        private int MovieId(string name)
        {
            return _movieRepo.GetAll().First(m => m.Name == name).Id;
        }

        #endregion

        // ── Constructor ───────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullTagRepo_Throws()
        {
            new TaggingService(null, _movieRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new TaggingService(_tagRepo, null);
        }

        // ── CreateTag ─────────────────────────────────────────

        [TestMethod]
        public void CreateTag_ValidName_Succeeds()
        {
            var tag = CreateTag("Staff Pick");
            Assert.IsNotNull(tag);
            Assert.AreEqual("Staff Pick", tag.Name);
            Assert.IsTrue(tag.Id > 0);
            Assert.IsTrue(tag.IsActive);
        }

        [TestMethod]
        public void CreateTag_SetsCreatedDate()
        {
            var before = DateTime.Now;
            var tag = CreateTag("New Tag");
            Assert.IsTrue(tag.CreatedDate >= before);
        }

        [TestMethod]
        public void CreateTag_SetsCreatedBy()
        {
            var tag = CreateTag("Test");
            Assert.AreEqual("admin", tag.CreatedBy);
        }

        [TestMethod]
        public void CreateTag_DefaultCreatedBy_IsSystem()
        {
            var tag = _service.CreateTag("NoAuthor");
            Assert.AreEqual("system", tag.CreatedBy);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateTag_NullName_Throws()
        {
            _service.CreateTag(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateTag_EmptyName_Throws()
        {
            _service.CreateTag("  ");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateTag_TooShort_Throws()
        {
            _service.CreateTag("A");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateTag_TooLong_Throws()
        {
            _service.CreateTag(new string('X', 51));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CreateTag_DuplicateName_Throws()
        {
            CreateTag("Blockbuster");
            CreateTag("Blockbuster");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CreateTag_DuplicateName_CaseInsensitive()
        {
            CreateTag("blockbuster");
            _service.CreateTag("BLOCKBUSTER");
        }

        [TestMethod]
        public void CreateTag_TrimsWhitespace()
        {
            var tag = _service.CreateTag("  Trimmed  ");
            Assert.AreEqual("Trimmed", tag.Name);
        }

        [TestMethod]
        public void CreateTag_StaffPick_Flag()
        {
            var tag = CreateTag("Picks", true);
            Assert.IsTrue(tag.IsStaffPick);
        }

        // ── UpdateTag ─────────────────────────────────────────

        [TestMethod]
        public void UpdateTag_Name_Succeeds()
        {
            var tag = CreateTag("Old Name");
            var updated = _service.UpdateTag(tag.Id, name: "New Name");
            Assert.AreEqual("New Name", updated.Name);
        }

        [TestMethod]
        public void UpdateTag_Description_Succeeds()
        {
            var tag = CreateTag("Test");
            _service.UpdateTag(tag.Id, description: "Updated desc");
            var fetched = _service.GetTag(tag.Id);
            Assert.AreEqual("Updated desc", fetched.Description);
        }

        [TestMethod]
        public void UpdateTag_Color_Succeeds()
        {
            var tag = CreateTag("Colored");
            _service.UpdateTag(tag.Id, color: "#00FF00");
            Assert.AreEqual("#00FF00", _service.GetTag(tag.Id).Color);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void UpdateTag_NotFound_Throws()
        {
            _service.UpdateTag(999, name: "Nope");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void UpdateTag_DuplicateName_Throws()
        {
            CreateTag("Alpha");
            var beta = CreateTag("Beta");
            _service.UpdateTag(beta.Id, name: "Alpha");
        }

        [TestMethod]
        public void UpdateTag_SameNameSameTag_OK()
        {
            var tag = CreateTag("Same");
            var updated = _service.UpdateTag(tag.Id, name: "Same");
            Assert.AreEqual("Same", updated.Name);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void UpdateTag_NameTooShort_Throws()
        {
            var tag = CreateTag("Valid");
            _service.UpdateTag(tag.Id, name: "X");
        }

        // ── Deactivate / Reactivate ───────────────────────────

        [TestMethod]
        public void DeactivateTag_SetsInactive()
        {
            var tag = CreateTag("Active Tag");
            _service.DeactivateTag(tag.Id);
            Assert.IsFalse(_service.GetTag(tag.Id).IsActive);
        }

        [TestMethod]
        public void ReactivateTag_SetsActive()
        {
            var tag = CreateTag("Temp");
            _service.DeactivateTag(tag.Id);
            _service.ReactivateTag(tag.Id);
            Assert.IsTrue(_service.GetTag(tag.Id).IsActive);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void DeactivateTag_NotFound_Throws()
        {
            _service.DeactivateTag(999);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ReactivateTag_NotFound_Throws()
        {
            _service.ReactivateTag(999);
        }

        [TestMethod]
        public void DeactivatedTag_ExcludedFromActiveLists()
        {
            var tag = CreateTag("Hidden");
            _service.DeactivateTag(tag.Id);
            var active = _service.GetAllTags(false);
            Assert.IsFalse(active.Any(t => t.Id == tag.Id));
        }

        [TestMethod]
        public void DeactivatedTag_IncludedWhenRequested()
        {
            var tag = CreateTag("Hidden");
            _service.DeactivateTag(tag.Id);
            var all = _service.GetAllTags(true);
            Assert.IsTrue(all.Any(t => t.Id == tag.Id));
        }

        // ── DeleteTag ─────────────────────────────────────────

        [TestMethod]
        public void DeleteTag_RemovesTagAndAssignments()
        {
            var tag = CreateTag("ToDelete");
            _service.TagMovie(tag.Id, MovieId("Inception"));
            _service.TagMovie(tag.Id, MovieId("Interstellar"));

            int removed = _service.DeleteTag(tag.Id);

            Assert.AreEqual(2, removed);
            Assert.IsNull(_service.GetTag(tag.Id));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void DeleteTag_NotFound_Throws()
        {
            _service.DeleteTag(999);
        }

        // ── SearchTags ────────────────────────────────────────

        [TestMethod]
        public void SearchTags_FindsPartialMatch()
        {
            CreateTag("Blockbuster");
            CreateTag("Block Party");
            CreateTag("Romance");

            var results = _service.SearchTags("block");
            Assert.AreEqual(2, results.Count);
        }

        [TestMethod]
        public void SearchTags_CaseInsensitive()
        {
            CreateTag("Action Hero");
            var results = _service.SearchTags("ACTION");
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void SearchTags_EmptyQuery_ReturnsEmpty()
        {
            CreateTag("Something");
            Assert.AreEqual(0, _service.SearchTags("").Count);
            Assert.AreEqual(0, _service.SearchTags(null).Count);
        }

        // ── TagMovie ──────────────────────────────────────────

        [TestMethod]
        public void TagMovie_CreatesAssignment()
        {
            var tag = CreateTag("Mind Bending");
            var assignment = _service.TagMovie(tag.Id, MovieId("Inception"), "staff");

            Assert.IsNotNull(assignment);
            Assert.AreEqual("Mind Bending", assignment.TagName);
            Assert.AreEqual("Inception", assignment.MovieName);
            Assert.AreEqual("staff", assignment.AppliedBy);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TagMovie_TagNotFound_Throws()
        {
            _service.TagMovie(999, MovieId("Inception"));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TagMovie_MovieNotFound_Throws()
        {
            var tag = CreateTag("Test");
            _service.TagMovie(tag.Id, 999);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void TagMovie_InactiveTag_Throws()
        {
            var tag = CreateTag("Deactivated");
            _service.DeactivateTag(tag.Id);
            _service.TagMovie(tag.Id, MovieId("Inception"));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void TagMovie_Duplicate_Throws()
        {
            var tag = CreateTag("Sci-Fi Classic");
            _service.TagMovie(tag.Id, MovieId("Inception"));
            _service.TagMovie(tag.Id, MovieId("Inception"));
        }

        // ── UntagMovie ────────────────────────────────────────

        [TestMethod]
        public void UntagMovie_RemovesAssignment()
        {
            var tag = CreateTag("Temp Tag");
            _service.TagMovie(tag.Id, MovieId("Inception"));

            bool removed = _service.UntagMovie(tag.Id, MovieId("Inception"));
            Assert.IsTrue(removed);
            Assert.AreEqual(0, _service.GetMovieTags(MovieId("Inception")).Count);
        }

        [TestMethod]
        public void UntagMovie_NotAssigned_ReturnsFalse()
        {
            var tag = CreateTag("Random");
            Assert.IsFalse(_service.UntagMovie(tag.Id, MovieId("Inception")));
        }

        // ── GetMovieTags ──────────────────────────────────────

        [TestMethod]
        public void GetMovieTags_ReturnsAllTags()
        {
            var t1 = CreateTag("Tag One");
            var t2 = CreateTag("Tag Two");
            _service.TagMovie(t1.Id, MovieId("Inception"));
            _service.TagMovie(t2.Id, MovieId("Inception"));

            var tags = _service.GetMovieTags(MovieId("Inception"));
            Assert.AreEqual(2, tags.Count);
        }

        [TestMethod]
        public void GetMovieTags_Empty_WhenNoTags()
        {
            Assert.AreEqual(0, _service.GetMovieTags(MovieId("Inception")).Count);
        }

        // ── GetMoviesByTag ────────────────────────────────────

        [TestMethod]
        public void GetMoviesByTag_ReturnsTaggedMovies()
        {
            var tag = CreateTag("Nolan");
            _service.TagMovie(tag.Id, MovieId("Inception"));
            _service.TagMovie(tag.Id, MovieId("Interstellar"));

            var movies = _service.GetMoviesByTag(tag.Id);
            Assert.AreEqual(2, movies.Count);
        }

        // ── GetMoviesByAllTags ────────────────────────────────

        [TestMethod]
        public void GetMoviesByAllTags_IntersectsCorrectly()
        {
            var scifi = CreateTag("Sci-Fi Classic");
            var nolan = CreateTag("Nolan");

            _service.TagMovie(scifi.Id, MovieId("Inception"));
            _service.TagMovie(scifi.Id, MovieId("Interstellar"));
            _service.TagMovie(nolan.Id, MovieId("Inception"));
            _service.TagMovie(nolan.Id, MovieId("Interstellar"));
            _service.TagMovie(scifi.Id, MovieId("The Hangover")); // only scifi

            var both = _service.GetMoviesByAllTags(new[] { scifi.Id, nolan.Id });
            Assert.AreEqual(2, both.Count);
        }

        [TestMethod]
        public void GetMoviesByAllTags_EmptyList_ReturnsEmpty()
        {
            var result = _service.GetMoviesByAllTags(new List<int>());
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void GetMoviesByAllTags_Null_Throws()
        {
            _service.GetMoviesByAllTags(null);
        }

        // ── GetMoviesByAnyTag ─────────────────────────────────

        [TestMethod]
        public void GetMoviesByAnyTag_UnionsCorrectly()
        {
            var t1 = CreateTag("Tag A");
            var t2 = CreateTag("Tag B");

            _service.TagMovie(t1.Id, MovieId("Inception"));
            _service.TagMovie(t2.Id, MovieId("Titanic"));

            var any = _service.GetMoviesByAnyTag(new[] { t1.Id, t2.Id });
            Assert.AreEqual(2, any.Count);
        }

        [TestMethod]
        public void GetMoviesByAnyTag_NoDuplicates()
        {
            var t1 = CreateTag("Tag A");
            var t2 = CreateTag("Tag B");

            _service.TagMovie(t1.Id, MovieId("Inception"));
            _service.TagMovie(t2.Id, MovieId("Inception"));

            var any = _service.GetMoviesByAnyTag(new[] { t1.Id, t2.Id });
            Assert.AreEqual(1, any.Count);
        }

        // ── BulkTagMovies ─────────────────────────────────────

        [TestMethod]
        public void BulkTagMovies_TagsMultiple()
        {
            var tag = CreateTag("Bulk");
            int count = _service.BulkTagMovies(tag.Id,
                new[] { MovieId("Inception"), MovieId("Interstellar"), MovieId("Titanic") });
            Assert.AreEqual(3, count);
        }

        [TestMethod]
        public void BulkTagMovies_SkipsDuplicates()
        {
            var tag = CreateTag("Bulk2");
            _service.TagMovie(tag.Id, MovieId("Inception"));

            int count = _service.BulkTagMovies(tag.Id,
                new[] { MovieId("Inception"), MovieId("Interstellar") });
            Assert.AreEqual(1, count); // only Interstellar is new
        }

        [TestMethod]
        public void BulkTagMovies_SkipsInvalidMovieIds()
        {
            var tag = CreateTag("Bulk3");
            int count = _service.BulkTagMovies(tag.Id,
                new[] { MovieId("Inception"), 9999 });
            Assert.AreEqual(1, count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void BulkTagMovies_InactiveTag_Throws()
        {
            var tag = CreateTag("Inactive");
            _service.DeactivateTag(tag.Id);
            _service.BulkTagMovies(tag.Id, new[] { MovieId("Inception") });
        }

        // ── BulkUntagMovies ───────────────────────────────────

        [TestMethod]
        public void BulkUntagMovies_RemovesMultiple()
        {
            var tag = CreateTag("BulkRemove");
            _service.TagMovie(tag.Id, MovieId("Inception"));
            _service.TagMovie(tag.Id, MovieId("Interstellar"));

            int removed = _service.BulkUntagMovies(tag.Id,
                new[] { MovieId("Inception"), MovieId("Interstellar") });
            Assert.AreEqual(2, removed);
        }

        // ── BulkTagSingleMovie ────────────────────────────────

        [TestMethod]
        public void BulkTagSingleMovie_AppliesMultipleTags()
        {
            var t1 = CreateTag("Multi1");
            var t2 = CreateTag("Multi2");
            var t3 = CreateTag("Multi3");

            int count = _service.BulkTagSingleMovie(
                MovieId("Inception"), new[] { t1.Id, t2.Id, t3.Id });
            Assert.AreEqual(3, count);
            Assert.AreEqual(3, _service.GetMovieTags(MovieId("Inception")).Count);
        }

        [TestMethod]
        public void BulkTagSingleMovie_SkipsInactiveTags()
        {
            var active = CreateTag("Active");
            var inactive = CreateTag("Deactivated");
            _service.DeactivateTag(inactive.Id);

            int count = _service.BulkTagSingleMovie(
                MovieId("Inception"), new[] { active.Id, inactive.Id });
            Assert.AreEqual(1, count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void BulkTagSingleMovie_MovieNotFound_Throws()
        {
            var tag = CreateTag("Orphan");
            _service.BulkTagSingleMovie(9999, new[] { tag.Id });
        }

        // ── Staff Picks ───────────────────────────────────────

        [TestMethod]
        public void GetStaffPicks_ReturnsMoviesWithStaffPickTags()
        {
            var pick = CreateTag("Staff Favorites", true);
            _service.TagMovie(pick.Id, MovieId("Inception"));
            _service.TagMovie(pick.Id, MovieId("Interstellar"));

            var picks = _service.GetStaffPicks();
            Assert.AreEqual(2, picks.Count);
        }

        [TestMethod]
        public void GetStaffPicks_ExcludesNonStaffPickTags()
        {
            var regular = CreateTag("Regular", false);
            _service.TagMovie(regular.Id, MovieId("Inception"));

            var picks = _service.GetStaffPicks();
            Assert.AreEqual(0, picks.Count);
        }

        [TestMethod]
        public void GetStaffPicks_NoDuplicateMovies()
        {
            var pick1 = CreateTag("Pick 1", true);
            var pick2 = CreateTag("Pick 2", true);
            _service.TagMovie(pick1.Id, MovieId("Inception"));
            _service.TagMovie(pick2.Id, MovieId("Inception"));

            var picks = _service.GetStaffPicks();
            Assert.AreEqual(1, picks.Count);
        }

        [TestMethod]
        public void PromoteToStaffPick_SetsFlag()
        {
            var tag = CreateTag("Promoted");
            Assert.IsFalse(tag.IsStaffPick);

            _service.PromoteToStaffPick(tag.Id);
            Assert.IsTrue(_service.GetTag(tag.Id).IsStaffPick);
        }

        [TestMethod]
        public void DemoteFromStaffPick_ClearsFlag()
        {
            var tag = CreateTag("Demoted", true);
            _service.DemoteFromStaffPick(tag.Id);
            Assert.IsFalse(_service.GetTag(tag.Id).IsStaffPick);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void PromoteToStaffPick_NotFound_Throws()
        {
            _service.PromoteToStaffPick(999);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void DemoteFromStaffPick_NotFound_Throws()
        {
            _service.DemoteFromStaffPick(999);
        }

        // ── Tag Cloud ─────────────────────────────────────────

        [TestMethod]
        public void GetTagCloud_ReturnsUsedTags()
        {
            var t1 = CreateTag("Popular");
            var t2 = CreateTag("Rare");
            CreateTag("Unused"); // should not appear

            _service.TagMovie(t1.Id, MovieId("Inception"));
            _service.TagMovie(t1.Id, MovieId("Interstellar"));
            _service.TagMovie(t1.Id, MovieId("Titanic"));
            _service.TagMovie(t2.Id, MovieId("Inception"));

            var cloud = _service.GetTagCloud();
            Assert.AreEqual(2, cloud.Count); // Unused excluded
            Assert.AreEqual("Popular", cloud[0].TagName);
            Assert.AreEqual(3, cloud[0].MovieCount);
            Assert.AreEqual(1.0, cloud[0].Weight, 0.001);
            Assert.AreEqual(1, cloud[1].MovieCount);
        }

        [TestMethod]
        public void GetTagCloud_NormalizedWeights()
        {
            var t1 = CreateTag("Big");
            var t2 = CreateTag("Small");

            _service.TagMovie(t1.Id, MovieId("Inception"));
            _service.TagMovie(t1.Id, MovieId("Interstellar"));
            _service.TagMovie(t2.Id, MovieId("Titanic"));

            var cloud = _service.GetTagCloud();
            Assert.AreEqual(1.0, cloud[0].Weight, 0.001);
            Assert.AreEqual(0.5, cloud[1].Weight, 0.001);
        }

        [TestMethod]
        public void GetTagCloud_Empty_WhenNoAssignments()
        {
            CreateTag("Lonely");
            Assert.AreEqual(0, _service.GetTagCloud().Count);
        }

        // ── GetPopularTags ────────────────────────────────────

        [TestMethod]
        public void GetPopularTags_RespectsLimit()
        {
            for (int i = 1; i <= 5; i++)
            {
                var tag = CreateTag("Tag " + i);
                _service.TagMovie(tag.Id, MovieId("Inception"));
            }

            var popular = _service.GetPopularTags(3);
            Assert.AreEqual(3, popular.Count);
        }

        // ── GetSummary ────────────────────────────────────────

        [TestMethod]
        public void GetSummary_CorrectCounts()
        {
            var t1 = CreateTag("Active1");
            var t2 = CreateTag("Active2", true);
            var t3 = CreateTag("WillDeactivate");
            _service.DeactivateTag(t3.Id);

            _service.TagMovie(t1.Id, MovieId("Inception"));
            _service.TagMovie(t1.Id, MovieId("Interstellar"));
            _service.TagMovie(t2.Id, MovieId("Inception"));

            var summary = _service.GetSummary();

            Assert.AreEqual(3, summary.TotalTags);
            Assert.AreEqual(2, summary.ActiveTags);
            Assert.AreEqual(3, summary.TotalAssignments);
            Assert.AreEqual(1, summary.StaffPickCount);
            Assert.AreEqual(3, summary.UntaggedMovies); // 5 movies - 2 tagged = 3
            Assert.AreEqual(0.6, summary.AverageTagsPerMovie, 0.01); // 3/5
        }

        // ── SuggestTagsForMovie ───────────────────────────────

        [TestMethod]
        public void SuggestTags_ReturnGenreBased()
        {
            var suggestions = _service.SuggestTagsForMovie(MovieId("Inception"));
            // SciFi genre should suggest Mind Bending, Space, Dystopian
            Assert.IsTrue(suggestions.Contains("Mind Bending"));
        }

        [TestMethod]
        public void SuggestTags_IncludesHighlyRated()
        {
            var suggestions = _service.SuggestTagsForMovie(MovieId("Inception"));
            Assert.IsTrue(suggestions.Contains("Highly Rated"));
        }

        [TestMethod]
        public void SuggestTags_IncludesClassic()
        {
            var suggestions = _service.SuggestTagsForMovie(MovieId("Titanic"));
            Assert.IsTrue(suggestions.Contains("Classic")); // 1997 = >20 years ago
        }

        [TestMethod]
        public void SuggestTags_IncludesNewRelease()
        {
            var suggestions = _service.SuggestTagsForMovie(MovieId("Fresh Release"));
            Assert.IsTrue(suggestions.Contains("New Release"));
        }

        [TestMethod]
        public void SuggestTags_ExcludesAlreadyApplied()
        {
            var tag = CreateTag("Mind Bending");
            _service.TagMovie(tag.Id, MovieId("Inception"));

            var suggestions = _service.SuggestTagsForMovie(MovieId("Inception"));
            Assert.IsFalse(suggestions.Contains("Mind Bending"));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SuggestTags_MovieNotFound_Throws()
        {
            _service.SuggestTagsForMovie(9999);
        }

        [TestMethod]
        public void SuggestTags_NoDuplicates()
        {
            var suggestions = _service.SuggestTagsForMovie(MovieId("The Hangover"));
            Assert.AreEqual(suggestions.Count, suggestions.Distinct().Count());
        }

        // ── FindRelatedMovies ─────────────────────────────────

        [TestMethod]
        public void FindRelatedMovies_BySharedTags()
        {
            var t1 = CreateTag("Space");
            var t2 = CreateTag("Mind Bending");

            _service.TagMovie(t1.Id, MovieId("Inception"));
            _service.TagMovie(t1.Id, MovieId("Interstellar"));
            _service.TagMovie(t2.Id, MovieId("Inception"));
            _service.TagMovie(t2.Id, MovieId("Interstellar"));

            var related = _service.FindRelatedMovies(MovieId("Inception"));
            Assert.AreEqual(1, related.Count);
            Assert.AreEqual("Interstellar", related[0].Name);
        }

        [TestMethod]
        public void FindRelatedMovies_SortedBySharedCount()
        {
            var t1 = CreateTag("A");
            var t2 = CreateTag("B");
            var t3 = CreateTag("C");

            // Inception has all 3 tags
            _service.TagMovie(t1.Id, MovieId("Inception"));
            _service.TagMovie(t2.Id, MovieId("Inception"));
            _service.TagMovie(t3.Id, MovieId("Inception"));

            // Interstellar shares 2 tags
            _service.TagMovie(t1.Id, MovieId("Interstellar"));
            _service.TagMovie(t2.Id, MovieId("Interstellar"));

            // Titanic shares 1 tag
            _service.TagMovie(t1.Id, MovieId("Titanic"));

            var related = _service.FindRelatedMovies(MovieId("Inception"));
            Assert.AreEqual(2, related.Count);
            Assert.AreEqual("Interstellar", related[0].Name);
            Assert.AreEqual("Titanic", related[1].Name);
        }

        [TestMethod]
        public void FindRelatedMovies_RespectsLimit()
        {
            var tag = CreateTag("Common");
            _service.TagMovie(tag.Id, MovieId("Inception"));
            _service.TagMovie(tag.Id, MovieId("Interstellar"));
            _service.TagMovie(tag.Id, MovieId("Titanic"));
            _service.TagMovie(tag.Id, MovieId("The Hangover"));

            var related = _service.FindRelatedMovies(MovieId("Inception"), 2);
            Assert.AreEqual(2, related.Count);
        }

        [TestMethod]
        public void FindRelatedMovies_NoTags_ReturnsEmpty()
        {
            var related = _service.FindRelatedMovies(MovieId("Inception"));
            Assert.AreEqual(0, related.Count);
        }

        [TestMethod]
        public void FindRelatedMovies_ExcludesSelf()
        {
            var tag = CreateTag("Self");
            _service.TagMovie(tag.Id, MovieId("Inception"));

            var related = _service.FindRelatedMovies(MovieId("Inception"));
            Assert.AreEqual(0, related.Count);
        }

        // ── MergeTags ─────────────────────────────────────────

        [TestMethod]
        public void MergeTags_MovesAssignments()
        {
            var source = CreateTag("Source");
            var target = CreateTag("Target");

            _service.TagMovie(source.Id, MovieId("Inception"));
            _service.TagMovie(source.Id, MovieId("Interstellar"));

            int moved = _service.MergeTags(source.Id, target.Id);

            Assert.AreEqual(2, moved);
            Assert.IsNull(_service.GetTag(source.Id)); // source deleted
            Assert.AreEqual(2, _service.GetMoviesByTag(target.Id).Count);
        }

        [TestMethod]
        public void MergeTags_SkipsDuplicates()
        {
            var source = CreateTag("Dup Source");
            var target = CreateTag("Dup Target");

            // Both tagged on Inception
            _service.TagMovie(source.Id, MovieId("Inception"));
            _service.TagMovie(target.Id, MovieId("Inception"));
            // Only source on Interstellar
            _service.TagMovie(source.Id, MovieId("Interstellar"));

            int moved = _service.MergeTags(source.Id, target.Id);

            Assert.AreEqual(1, moved); // only Interstellar moved
            Assert.AreEqual(2, _service.GetMoviesByTag(target.Id).Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void MergeTags_SameTag_Throws()
        {
            var tag = CreateTag("Self Merge");
            _service.MergeTags(tag.Id, tag.Id);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void MergeTags_SourceNotFound_Throws()
        {
            var target = CreateTag("Target");
            _service.MergeTags(999, target.Id);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void MergeTags_TargetNotFound_Throws()
        {
            var source = CreateTag("Source");
            _service.MergeTags(source.Id, 999);
        }
    }
}
