using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class FranchiseTrackerServiceTests
    {
        private FranchiseTrackerService _service;
        private List<Movie> _movies;
        private List<Rental> _rentals;

        [TestInitialize]
        public void Setup()
        {
            _service = new FranchiseTrackerService();
            _movies = new List<Movie>
            {
                new Movie { Id = 1, Name = "Star Wars: A New Hope", Genre = Genre.Action, Rating = 5, ReleaseDate = new DateTime(1977, 5, 25) },
                new Movie { Id = 2, Name = "The Empire Strikes Back", Genre = Genre.Action, Rating = 5, ReleaseDate = new DateTime(1980, 5, 21) },
                new Movie { Id = 3, Name = "Return of the Jedi", Genre = Genre.Action, Rating = 4, ReleaseDate = new DateTime(1983, 5, 25) },
                new Movie { Id = 4, Name = "The Godfather", Genre = Genre.Drama, Rating = 5, ReleaseDate = new DateTime(1972, 3, 24) },
                new Movie { Id = 5, Name = "The Godfather Part II", Genre = Genre.Drama, Rating = 5, ReleaseDate = new DateTime(1974, 12, 20) },
                new Movie { Id = 6, Name = "The Godfather Part III", Genre = Genre.Drama, Rating = 3, ReleaseDate = new DateTime(1990, 12, 25) },
                new Movie { Id = 7, Name = "The Matrix", Genre = Genre.Action, Rating = 5, ReleaseDate = new DateTime(1999, 3, 31) },
                new Movie { Id = 8, Name = "The Matrix Reloaded", Genre = Genre.Action, Rating = 3, ReleaseDate = new DateTime(2003, 5, 15) },
                new Movie { Id = 9, Name = "The Matrix Revolutions", Genre = Genre.Action, Rating = 2, ReleaseDate = new DateTime(2003, 11, 5) },
                new Movie { Id = 10, Name = "Toy Story", Genre = Genre.Animation, Rating = 5, ReleaseDate = new DateTime(1995, 11, 22) },
                new Movie { Id = 11, Name = "Toy Story 2", Genre = Genre.Animation, Rating = 4, ReleaseDate = new DateTime(1999, 11, 24) },
                new Movie { Id = 12, Name = "Inception", Genre = Genre.Action, Rating = 5, ReleaseDate = new DateTime(2010, 7, 16) },
            };
            _rentals = new List<Rental>();
        }

        private Franchise CreateStarWars() =>
            _service.Create("Star Wars Original Trilogy", new List<int> { 1, 2, 3 },
                "The original trilogy", 1977, false, new List<string> { "sci-fi", "space" });

        private Franchise CreateGodfather() =>
            _service.Create("The Godfather Trilogy", new List<int> { 4, 5, 6 },
                "The Corleone saga", 1972, false, new List<string> { "crime", "drama" });

        private Franchise CreateMatrix() =>
            _service.Create("The Matrix Trilogy", new List<int> { 7, 8, 9 },
                tags: new List<string> { "sci-fi", "action" });

        private Rental MakeRental(int customerId, int movieId, DateTime date) =>
            new Rental
            {
                Id = _rentals.Count + 1,
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = date,
                DueDate = date.AddDays(7),
                DailyRate = 3.99m,
                Status = RentalStatus.Returned,
                ReturnDate = date.AddDays(5)
            };

        // --- Create Tests ---

        [TestMethod]
        public void Create_ValidFranchise_ReturnsWithId()
        {
            var f = CreateStarWars();
            Assert.AreEqual(1, f.Id);
            Assert.AreEqual("Star Wars Original Trilogy", f.Name);
            Assert.AreEqual(3, f.MovieIds.Count);
            Assert.AreEqual(1977, f.StartYear);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Create_EmptyName_Throws()
        {
            _service.Create("", new List<int> { 1 });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Create_NullMovieIds_Throws()
        {
            _service.Create("Test", null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Create_EmptyMovieIds_Throws()
        {
            _service.Create("Test", new List<int>());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Create_DuplicateMovieIds_Throws()
        {
            _service.Create("Test", new List<int> { 1, 1, 2 });
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Create_DuplicateName_Throws()
        {
            CreateStarWars();
            _service.Create("Star Wars Original Trilogy", new List<int> { 7, 8 });
        }

        [TestMethod]
        public void Create_AssignsIncrementingIds()
        {
            var f1 = CreateStarWars();
            var f2 = CreateGodfather();
            Assert.AreEqual(1, f1.Id);
            Assert.AreEqual(2, f2.Id);
        }

        // --- Get/Search Tests ---

        [TestMethod]
        public void GetById_Existing_ReturnsFranchise()
        {
            var f = CreateStarWars();
            var result = _service.GetById(f.Id);
            Assert.AreEqual(f.Name, result.Name);
        }

        [TestMethod]
        public void GetById_NonExistent_ReturnsNull()
        {
            Assert.IsNull(_service.GetById(999));
        }

        [TestMethod]
        public void GetAll_ReturnsAllFranchises()
        {
            CreateStarWars();
            CreateGodfather();
            Assert.AreEqual(2, _service.GetAll().Count);
        }

        [TestMethod]
        public void Search_ByName_FindsMatch()
        {
            CreateStarWars();
            CreateGodfather();
            var results = _service.Search("star");
            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].Name.Contains("Star Wars"));
        }

        [TestMethod]
        public void Search_ByTag_FindsMatch()
        {
            CreateStarWars();
            CreateGodfather();
            var results = _service.Search("crime");
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void Search_ByDescription_FindsMatch()
        {
            CreateGodfather();
            var results = _service.Search("corleone");
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void Search_EmptyQuery_ReturnsAll()
        {
            CreateStarWars();
            CreateGodfather();
            Assert.AreEqual(2, _service.Search("").Count);
        }

        // --- AddMovie/RemoveMovie/Reorder Tests ---

        [TestMethod]
        public void AddMovie_AppendsToEnd()
        {
            var f = _service.Create("Test", new List<int> { 1, 2 });
            _service.AddMovie(f.Id, 3);
            CollectionAssert.AreEqual(new List<int> { 1, 2, 3 }, f.MovieIds);
        }

        [TestMethod]
        public void AddMovie_AtPosition_InsertsCorrectly()
        {
            var f = _service.Create("Test", new List<int> { 1, 3 });
            _service.AddMovie(f.Id, 2, 1);
            CollectionAssert.AreEqual(new List<int> { 1, 2, 3 }, f.MovieIds);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void AddMovie_Duplicate_Throws()
        {
            var f = _service.Create("Test", new List<int> { 1, 2 });
            _service.AddMovie(f.Id, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void AddMovie_InvalidFranchise_Throws()
        {
            _service.AddMovie(999, 1);
        }

        [TestMethod]
        public void RemoveMovie_Succeeds()
        {
            var f = _service.Create("Test", new List<int> { 1, 2, 3 });
            _service.RemoveMovie(f.Id, 2);
            CollectionAssert.AreEqual(new List<int> { 1, 3 }, f.MovieIds);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void RemoveMovie_LastMovie_Throws()
        {
            var f = _service.Create("Test", new List<int> { 1 });
            _service.RemoveMovie(f.Id, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void RemoveMovie_NotInFranchise_Throws()
        {
            var f = _service.Create("Test", new List<int> { 1, 2 });
            _service.RemoveMovie(f.Id, 99);
        }

        [TestMethod]
        public void ReorderMovies_ValidOrder_Works()
        {
            var f = _service.Create("Test", new List<int> { 1, 2, 3 });
            _service.ReorderMovies(f.Id, new List<int> { 3, 1, 2 });
            CollectionAssert.AreEqual(new List<int> { 3, 1, 2 }, f.MovieIds);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ReorderMovies_WrongMovies_Throws()
        {
            var f = _service.Create("Test", new List<int> { 1, 2, 3 });
            _service.ReorderMovies(f.Id, new List<int> { 1, 2, 99 });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ReorderMovies_WrongCount_Throws()
        {
            var f = _service.Create("Test", new List<int> { 1, 2, 3 });
            _service.ReorderMovies(f.Id, new List<int> { 1, 2 });
        }

        [TestMethod]
        public void Delete_Existing_ReturnsTrue()
        {
            var f = CreateStarWars();
            Assert.IsTrue(_service.Delete(f.Id));
            Assert.AreEqual(0, _service.GetAll().Count);
        }

        [TestMethod]
        public void Delete_NonExistent_ReturnsFalse()
        {
            Assert.IsFalse(_service.Delete(999));
        }

        // --- Progress Tests ---

        [TestMethod]
        public void GetProgress_NoRentals_ZeroPercent()
        {
            var f = CreateStarWars();
            var progress = _service.GetProgress(1, f, _rentals);
            Assert.AreEqual(0.0, progress.CompletionPercent);
            Assert.AreEqual(0, progress.WatchedMovieIds.Count);
            Assert.AreEqual(1, progress.NextMovieId);
            Assert.IsNull(progress.StartedDate);
        }

        [TestMethod]
        public void GetProgress_PartialWatch_CorrectPercent()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            var progress = _service.GetProgress(1, f, _rentals);
            Assert.AreEqual(33.3, progress.CompletionPercent);
            Assert.AreEqual(1, progress.WatchedMovieIds.Count);
            Assert.AreEqual(2, progress.NextMovieId);
        }

        [TestMethod]
        public void GetProgress_AllWatched_100Percent()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            _rentals.Add(MakeRental(1, 2, new DateTime(2026, 1, 8)));
            _rentals.Add(MakeRental(1, 3, new DateTime(2026, 1, 15)));
            var progress = _service.GetProgress(1, f, _rentals);
            Assert.AreEqual(100.0, progress.CompletionPercent);
            Assert.IsNull(progress.NextMovieId);
            Assert.IsNotNull(progress.CompletedDate);
        }

        [TestMethod]
        public void GetProgress_SkippedMovie_NextIsFirstUnwatched()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            _rentals.Add(MakeRental(1, 3, new DateTime(2026, 1, 8)));
            var progress = _service.GetProgress(1, f, _rentals);
            Assert.AreEqual(66.7, progress.CompletionPercent);
            Assert.AreEqual(2, progress.NextMovieId);
        }

        [TestMethod]
        public void GetProgress_StartedDate_IsEarliestRental()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 2, new DateTime(2026, 2, 1)));
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            var progress = _service.GetProgress(1, f, _rentals);
            Assert.AreEqual(new DateTime(2026, 1, 1), progress.StartedDate);
        }

        [TestMethod]
        public void GetAllProgress_OnlyReturnsFranchisesStarted()
        {
            CreateStarWars();
            CreateGodfather();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            var all = _service.GetAllProgress(1, _rentals);
            Assert.AreEqual(1, all.Count);
        }

        // --- Report Tests ---

        [TestMethod]
        public void GetReport_BasicStats()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            _rentals.Add(MakeRental(1, 2, new DateTime(2026, 1, 8)));
            _rentals.Add(MakeRental(2, 1, new DateTime(2026, 1, 5)));

            var report = _service.GetReport(f, _rentals, _movies);
            Assert.AreEqual(3, report.TotalMovies);
            Assert.AreEqual(3, report.TotalRentals);
            Assert.AreEqual(2, report.CustomersStarted);
        }

        [TestMethod]
        public void GetReport_CompletionRate()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            _rentals.Add(MakeRental(1, 2, new DateTime(2026, 1, 8)));
            _rentals.Add(MakeRental(1, 3, new DateTime(2026, 1, 15)));
            _rentals.Add(MakeRental(2, 1, new DateTime(2026, 1, 5)));

            var report = _service.GetReport(f, _rentals, _movies);
            Assert.AreEqual(1, report.CustomersCompleted);
            Assert.AreEqual(50.0, report.CompletionRate);
        }

        [TestMethod]
        public void GetReport_MostAndLeastPopular()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            _rentals.Add(MakeRental(2, 1, new DateTime(2026, 1, 2)));
            _rentals.Add(MakeRental(3, 1, new DateTime(2026, 1, 3)));
            _rentals.Add(MakeRental(1, 2, new DateTime(2026, 1, 4)));

            var report = _service.GetReport(f, _rentals, _movies);
            Assert.AreEqual(1, report.MostPopularMovieId);
            Assert.AreEqual(3, report.LeastPopularMovieId);
        }

        [TestMethod]
        public void GetReport_DropoffAnalysis()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            _rentals.Add(MakeRental(2, 1, new DateTime(2026, 1, 2)));
            _rentals.Add(MakeRental(3, 1, new DateTime(2026, 1, 3)));
            _rentals.Add(MakeRental(1, 2, new DateTime(2026, 1, 8)));
            _rentals.Add(MakeRental(2, 2, new DateTime(2026, 1, 9)));
            _rentals.Add(MakeRental(1, 3, new DateTime(2026, 1, 15)));

            var report = _service.GetReport(f, _rentals, _movies);
            Assert.AreEqual(3, report.Dropoffs.Count);
            Assert.AreEqual(33.3, report.Dropoffs[0].DropoffRate);
            Assert.AreEqual(50.0, report.Dropoffs[1].DropoffRate);
        }

        [TestMethod]
        public void GetReport_AverageRating()
        {
            var f = CreateStarWars();
            var report = _service.GetReport(f, _rentals, _movies);
            Assert.AreEqual(4.67, report.AverageRating);
        }

        // --- Recommendation Tests ---

        [TestMethod]
        public void GetRecommendations_InProgress_SuggestsContinue()
        {
            var f = CreateStarWars();
            CreateGodfather();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));

            var recs = _service.GetRecommendations(1, _rentals, _movies);
            Assert.AreEqual(1, recs.Count);
            Assert.AreEqual(RecommendationType.ContinueFranchise, recs[0].Type);
            Assert.AreEqual(2, recs[0].NextMovieId);
        }

        [TestMethod]
        public void GetRecommendations_NearComplete_SuggestsComplete()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            _rentals.Add(MakeRental(1, 2, new DateTime(2026, 1, 8)));

            var recs = _service.GetRecommendations(1, _rentals, _movies);
            Assert.AreEqual(1, recs.Count);
            Assert.AreEqual(RecommendationType.CompleteFranchise, recs[0].Type);
        }

        [TestMethod]
        public void GetRecommendations_Completed_ExcludesFranchise()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            _rentals.Add(MakeRental(1, 2, new DateTime(2026, 1, 8)));
            _rentals.Add(MakeRental(1, 3, new DateTime(2026, 1, 15)));

            var recs = _service.GetRecommendations(1, _rentals, _movies);
            Assert.AreEqual(0, recs.Count);
        }

        [TestMethod]
        public void GetRecommendations_GenreMatch_SuggestsNew()
        {
            CreateStarWars();
            CreateGodfather();
            var dramaMovie = new Movie { Id = 99, Name = "Drama Film", Genre = Genre.Drama, Rating = 4 };
            var moviesWithExtra = new List<Movie>(_movies) { dramaMovie };
            _rentals.Add(MakeRental(1, 99, new DateTime(2026, 1, 5)));

            var recs = _service.GetRecommendations(1, _rentals, moviesWithExtra);
            Assert.IsTrue(recs.Count > 0);
        }

        [TestMethod]
        public void GetRecommendations_RespectsMaxResults()
        {
            CreateStarWars();
            CreateGodfather();
            CreateMatrix();
            _rentals.Add(MakeRental(1, 12, new DateTime(2026, 1, 1)));

            var recs = _service.GetRecommendations(1, _rentals, _movies, 1);
            Assert.AreEqual(1, recs.Count);
        }

        // --- Discovery Tests ---

        [TestMethod]
        public void FindByMovie_FindsFranchise()
        {
            CreateStarWars();
            CreateGodfather();
            var results = _service.FindByMovie(2);
            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].Name.Contains("Star Wars"));
        }

        [TestMethod]
        public void FindByMovie_NotInAny_ReturnsEmpty()
        {
            CreateStarWars();
            Assert.AreEqual(0, _service.FindByMovie(99).Count);
        }

        [TestMethod]
        public void FindByTag_FindsMatching()
        {
            CreateStarWars();
            CreateGodfather();
            CreateMatrix();
            var results = _service.FindByTag("sci-fi");
            Assert.AreEqual(2, results.Count);
        }

        [TestMethod]
        public void GetPopularFranchises_OrdersByRentals()
        {
            CreateStarWars();
            CreateGodfather();
            _rentals.Add(MakeRental(1, 4, new DateTime(2026, 1, 1)));
            _rentals.Add(MakeRental(2, 4, new DateTime(2026, 1, 2)));
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 3)));

            var popular = _service.GetPopularFranchises(_rentals);
            Assert.AreEqual("The Godfather Trilogy", popular[0].Name);
        }

        // --- Summary Tests ---

        [TestMethod]
        public void GenerateSummary_ContainsKeyInfo()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            var report = _service.GetReport(f, _rentals, _movies);
            var summary = _service.GenerateSummary(report);

            Assert.IsTrue(summary.Contains("Star Wars"));
            Assert.IsTrue(summary.Contains("Drop-off Analysis"));
            Assert.IsTrue(summary.Contains("Avg Rating"));
        }

        // --- Edge Cases ---

        [TestMethod]
        public void GetReport_NoRentals_ZeroStats()
        {
            var f = CreateStarWars();
            var report = _service.GetReport(f, _rentals, _movies);
            Assert.AreEqual(0, report.TotalRentals);
            Assert.AreEqual(0, report.CustomersStarted);
            Assert.AreEqual(0.0, report.CompletionRate);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void GetProgress_NullFranchise_Throws()
        {
            _service.GetProgress(1, null, _rentals);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void GetProgress_NullRentals_Throws()
        {
            var f = CreateStarWars();
            _service.GetProgress(1, f, null);
        }

        [TestMethod]
        public void Create_TagsAreOptional()
        {
            var f = _service.Create("Test", new List<int> { 1, 2 });
            Assert.AreEqual(0, f.Tags.Count);
        }

        [TestMethod]
        public void Create_CopiesMovieIds_NotReference()
        {
            var ids = new List<int> { 1, 2, 3 };
            var f = _service.Create("Test", ids);
            ids.Add(4);
            Assert.AreEqual(3, f.MovieIds.Count);
        }

        [TestMethod]
        public void MultipleCustomers_IndependentProgress()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            _rentals.Add(MakeRental(1, 2, new DateTime(2026, 1, 8)));
            _rentals.Add(MakeRental(2, 1, new DateTime(2026, 1, 5)));

            var p1 = _service.GetProgress(1, f, _rentals);
            var p2 = _service.GetProgress(2, f, _rentals);

            Assert.AreEqual(66.7, p1.CompletionPercent);
            Assert.AreEqual(33.3, p2.CompletionPercent);
        }

        [TestMethod]
        public void FindByMovie_MovieInMultipleFranchises_ReturnsBoth()
        {
            _service.Create("Franchise A", new List<int> { 1, 2 });
            _service.Create("Franchise B", new List<int> { 1, 3 });
            var results = _service.FindByMovie(1);
            Assert.AreEqual(2, results.Count);
        }

        [TestMethod]
        public void GetReport_Revenue_SumsCorrectly()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            _rentals.Add(MakeRental(1, 2, new DateTime(2026, 1, 8)));
            var report = _service.GetReport(f, _rentals, _movies);
            Assert.IsTrue(report.TotalRevenue > 0);
        }

        [TestMethod]
        public void GetReport_DropoffLastMovie_ZeroDropped()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            _rentals.Add(MakeRental(1, 2, new DateTime(2026, 1, 8)));
            _rentals.Add(MakeRental(1, 3, new DateTime(2026, 1, 15)));

            var report = _service.GetReport(f, _rentals, _movies);
            Assert.AreEqual(0, report.Dropoffs[2].DroppedCount);
        }

        [TestMethod]
        public void Search_CaseInsensitive()
        {
            CreateStarWars();
            var results = _service.Search("STAR WARS");
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void FindByTag_CaseInsensitive()
        {
            CreateStarWars();
            var results = _service.FindByTag("SCI-FI");
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void GetProgress_CompletedDate_IsLastRentalDate()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            _rentals.Add(MakeRental(1, 2, new DateTime(2026, 1, 8)));
            _rentals.Add(MakeRental(1, 3, new DateTime(2026, 1, 15)));
            var progress = _service.GetProgress(1, f, _rentals);
            Assert.AreEqual(new DateTime(2026, 1, 15), progress.CompletedDate);
        }

        [TestMethod]
        public void GetProgress_Incomplete_NoCompletedDate()
        {
            var f = CreateStarWars();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            var progress = _service.GetProgress(1, f, _rentals);
            Assert.IsNull(progress.CompletedDate);
        }

        [TestMethod]
        public void GetRecommendations_NoPriorRentals_NoResults()
        {
            CreateStarWars();
            var recs = _service.GetRecommendations(1, _rentals, _movies);
            Assert.AreEqual(0, recs.Count);
        }

        [TestMethod]
        public void AddMovie_OutOfRangePosition_AppendsToEnd()
        {
            var f = _service.Create("Test", new List<int> { 1, 2 });
            _service.AddMovie(f.Id, 3, 100);
            Assert.AreEqual(3, f.MovieIds[2]);
        }

        [TestMethod]
        public void GetPopularFranchises_RespectsTopParam()
        {
            CreateStarWars();
            CreateGodfather();
            CreateMatrix();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            _rentals.Add(MakeRental(1, 4, new DateTime(2026, 1, 2)));
            _rentals.Add(MakeRental(1, 7, new DateTime(2026, 1, 3)));

            var popular = _service.GetPopularFranchises(_rentals, 2);
            Assert.AreEqual(2, popular.Count);
        }

        [TestMethod]
        public void GetAllProgress_SortedByCompletion()
        {
            CreateStarWars();
            CreateGodfather();
            _rentals.Add(MakeRental(1, 1, new DateTime(2026, 1, 1)));
            _rentals.Add(MakeRental(1, 4, new DateTime(2026, 1, 2)));
            _rentals.Add(MakeRental(1, 5, new DateTime(2026, 1, 3)));

            var all = _service.GetAllProgress(1, _rentals);
            Assert.AreEqual(2, all.Count);
            Assert.IsTrue(all[0].CompletionPercent >= all[1].CompletionPercent);
        }
    }
}
