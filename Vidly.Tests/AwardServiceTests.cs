using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class AwardServiceTests
    {
        private InMemoryMovieRepository _movieRepo;
        private AwardService _service;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new InMemoryMovieRepository();
            _service = new AwardService(_movieRepo);
            _service.ClearAll();
        }

        private int AddMovie(string name = "Test Movie")
        {
            var movie = new Movie { Name = name, Genre = Genre.Drama };
            _movieRepo.Add(movie);
            return movie.Id;
        }

        // ── Constructor ─────────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new AwardService(null);
        }

        // ── AddNomination ───────────────────────────────────────────

        [TestMethod]
        public void AddNomination_ValidInput_ReturnsNomination()
        {
            var movieId = AddMovie("The Godfather");
            var nom = _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 1973);

            Assert.IsNotNull(nom);
            Assert.AreEqual(movieId, nom.MovieId);
            Assert.AreEqual("The Godfather", nom.MovieName);
            Assert.AreEqual(AwardBody.Oscar, nom.AwardBody);
            Assert.AreEqual(AwardCategory.BestPicture, nom.Category);
            Assert.AreEqual(1973, nom.Year);
            Assert.IsFalse(nom.Won);
        }

        [TestMethod]
        public void AddNomination_WithNomineeAndWon_SetsFields()
        {
            var movieId = AddMovie();
            var nom = _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestActor,
                2020, "Joaquin Phoenix", true);

            Assert.AreEqual("Joaquin Phoenix", nom.Nominee);
            Assert.IsTrue(nom.Won);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void AddNomination_InvalidMovieId_Throws()
        {
            _service.AddNomination(9999, AwardBody.Oscar, AwardCategory.BestPicture, 2024);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void AddNomination_YearTooLow_Throws()
        {
            var movieId = AddMovie();
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 1900);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void AddNomination_YearTooHigh_Throws()
        {
            var movieId = AddMovie();
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2200);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void AddNomination_Duplicate_Throws()
        {
            var movieId = AddMovie();
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2024);
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2024);
        }

        [TestMethod]
        public void AddNomination_SameMovieDifferentCategory_Succeeds()
        {
            var movieId = AddMovie();
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2024);
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestDirector, 2024);

            var noms = _service.GetNominations(movieId: movieId);
            Assert.AreEqual(2, noms.Count);
        }

        [TestMethod]
        public void AddNomination_SameMovieDifferentBody_Succeeds()
        {
            var movieId = AddMovie();
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2024);
            _service.AddNomination(movieId, AwardBody.GoldenGlobe, AwardCategory.BestPicture, 2024);

            var noms = _service.GetNominations(movieId: movieId);
            Assert.AreEqual(2, noms.Count);
        }

        [TestMethod]
        public void AddNomination_TrimsNominee()
        {
            var movieId = AddMovie();
            var nom = _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestActor,
                2024, "  Tom Hanks  ");
            Assert.AreEqual("Tom Hanks", nom.Nominee);
        }

        [TestMethod]
        public void AddNomination_AssignsIncrementingIds()
        {
            var movieId = AddMovie();
            var n1 = _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2020);
            var n2 = _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestDirector, 2020);
            Assert.IsTrue(n2.Id > n1.Id);
        }

        // ── SetWon ──────────────────────────────────────────────────

        [TestMethod]
        public void SetWon_ValidId_TogglesWon()
        {
            var movieId = AddMovie();
            var nom = _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2024);
            Assert.IsFalse(nom.Won);

            var updated = _service.SetWon(nom.Id, true);
            Assert.IsTrue(updated.Won);

            updated = _service.SetWon(nom.Id, false);
            Assert.IsFalse(updated.Won);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SetWon_InvalidId_Throws()
        {
            _service.SetWon(9999, true);
        }

        // ── RemoveNomination ────────────────────────────────────────

        [TestMethod]
        public void RemoveNomination_ExistingId_ReturnsTrue()
        {
            var movieId = AddMovie();
            var nom = _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2024);
            Assert.IsTrue(_service.RemoveNomination(nom.Id));
            Assert.AreEqual(0, _service.GetNominations().Count);
        }

        [TestMethod]
        public void RemoveNomination_NonExistentId_ReturnsFalse()
        {
            Assert.IsFalse(_service.RemoveNomination(9999));
        }

        // ── GetNominations (filtering) ──────────────────────────────

        [TestMethod]
        public void GetNominations_NoFilter_ReturnsAll()
        {
            var m1 = AddMovie("Film A");
            var m2 = AddMovie("Film B");
            _service.AddNomination(m1, AwardBody.Oscar, AwardCategory.BestPicture, 2024);
            _service.AddNomination(m2, AwardBody.BAFTA, AwardCategory.BestDirector, 2023);

            Assert.AreEqual(2, _service.GetNominations().Count);
        }

        [TestMethod]
        public void GetNominations_FilterByMovieId()
        {
            var m1 = AddMovie("Film A");
            var m2 = AddMovie("Film B");
            _service.AddNomination(m1, AwardBody.Oscar, AwardCategory.BestPicture, 2024);
            _service.AddNomination(m2, AwardBody.Oscar, AwardCategory.BestPicture, 2023);

            var result = _service.GetNominations(movieId: m1);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(m1, result[0].MovieId);
        }

        [TestMethod]
        public void GetNominations_FilterByBody()
        {
            var movieId = AddMovie();
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2024);
            _service.AddNomination(movieId, AwardBody.BAFTA, AwardCategory.BestPicture, 2024);

            var result = _service.GetNominations(body: AwardBody.BAFTA);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(AwardBody.BAFTA, result[0].AwardBody);
        }

        [TestMethod]
        public void GetNominations_FilterByYear()
        {
            var movieId = AddMovie();
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2023);
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestDirector, 2024);

            var result = _service.GetNominations(year: 2024);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(2024, result[0].Year);
        }

        [TestMethod]
        public void GetNominations_FilterWonOnly()
        {
            var movieId = AddMovie();
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2024, won: true);
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestDirector, 2024);

            var result = _service.GetNominations(wonOnly: true);
            Assert.AreEqual(1, result.Count);
            Assert.IsTrue(result[0].Won);
        }

        [TestMethod]
        public void GetNominations_OrderedByYearDescending()
        {
            var movieId = AddMovie();
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2020);
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestDirector, 2024);
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestActor, 2022);

            var result = _service.GetNominations();
            Assert.AreEqual(2024, result[0].Year);
            Assert.AreEqual(2022, result[1].Year);
            Assert.AreEqual(2020, result[2].Year);
        }

        // ── GetById ─────────────────────────────────────────────────

        [TestMethod]
        public void GetById_ExistingId_ReturnsNomination()
        {
            var movieId = AddMovie();
            var nom = _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2024);
            var found = _service.GetById(nom.Id);
            Assert.IsNotNull(found);
            Assert.AreEqual(nom.Id, found.Id);
        }

        [TestMethod]
        public void GetById_NonExistent_ReturnsNull()
        {
            Assert.IsNull(_service.GetById(9999));
        }

        // ── GetLeaderboard ──────────────────────────────────────────

        [TestMethod]
        public void GetLeaderboard_Empty_ReturnsEmptyList()
        {
            Assert.AreEqual(0, _service.GetLeaderboard().Count);
        }

        [TestMethod]
        public void GetLeaderboard_OrderedByWinsDescending()
        {
            var m1 = AddMovie("Underdog");
            var m2 = AddMovie("Champion");

            _service.AddNomination(m1, AwardBody.Oscar, AwardCategory.BestPicture, 2024);
            _service.AddNomination(m2, AwardBody.Oscar, AwardCategory.BestPicture, 2024, won: true);
            _service.AddNomination(m2, AwardBody.Oscar, AwardCategory.BestDirector, 2024, won: true);

            var board = _service.GetLeaderboard();
            Assert.AreEqual(2, board.Count);
            Assert.AreEqual("Champion", board[0].MovieName);
            Assert.AreEqual(2, board[0].TotalWins);
            Assert.AreEqual("Underdog", board[1].MovieName);
        }

        [TestMethod]
        public void GetLeaderboard_CalculatesWinRate()
        {
            var movieId = AddMovie();
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2024, won: true);
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestDirector, 2024);

            var board = _service.GetLeaderboard();
            Assert.AreEqual(1, board.Count);
            Assert.AreEqual(50.0, board[0].WinRate);
        }

        // ── GetMovieSummary ─────────────────────────────────────────

        [TestMethod]
        public void GetMovieSummary_ValidMovie_ReturnsSummary()
        {
            var movieId = AddMovie("Test Film");
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2024, won: true);
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestDirector, 2024);
            _service.AddNomination(movieId, AwardBody.BAFTA, AwardCategory.BestPicture, 2024, won: true);

            var summary = _service.GetMovieSummary(movieId);
            Assert.AreEqual("Test Film", summary.MovieName);
            Assert.AreEqual(3, summary.TotalNominations);
            Assert.AreEqual(2, summary.TotalWins);
        }

        [TestMethod]
        public void GetMovieSummary_NoNominations_ReturnsZeros()
        {
            var movieId = AddMovie();
            var summary = _service.GetMovieSummary(movieId);
            Assert.AreEqual(0, summary.TotalNominations);
            Assert.AreEqual(0, summary.TotalWins);
            Assert.AreEqual(0, summary.WinRate);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetMovieSummary_InvalidMovieId_Throws()
        {
            _service.GetMovieSummary(9999);
        }

        // ── GetAwardYears ───────────────────────────────────────────

        [TestMethod]
        public void GetAwardYears_ReturnsDistinctDescending()
        {
            var movieId = AddMovie();
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2020);
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestDirector, 2024);
            _service.AddNomination(movieId, AwardBody.BAFTA, AwardCategory.BestPicture, 2020);

            var years = _service.GetAwardYears();
            Assert.AreEqual(2, years.Count);
            Assert.AreEqual(2024, years[0]);
            Assert.AreEqual(2020, years[1]);
        }

        // ── GetCountsByBody ─────────────────────────────────────────

        [TestMethod]
        public void GetCountsByBody_GroupsCorrectly()
        {
            var movieId = AddMovie();
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2024);
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestDirector, 2024);
            _service.AddNomination(movieId, AwardBody.BAFTA, AwardCategory.BestPicture, 2024);

            var counts = _service.GetCountsByBody();
            Assert.AreEqual(2, counts[AwardBody.Oscar]);
            Assert.AreEqual(1, counts[AwardBody.BAFTA]);
        }

        // ── AwardSummary.WinRate ────────────────────────────────────

        [TestMethod]
        public void AwardSummary_WinRate_ZeroNominations_ReturnsZero()
        {
            var summary = new AwardSummary { TotalNominations = 0, TotalWins = 0 };
            Assert.AreEqual(0, summary.WinRate);
        }

        [TestMethod]
        public void AwardSummary_WinRate_RoundsToOneDecimal()
        {
            var summary = new AwardSummary { TotalNominations = 3, TotalWins = 1 };
            Assert.AreEqual(33.3, summary.WinRate);
        }

        // ── ClearAll ────────────────────────────────────────────────

        [TestMethod]
        public void ClearAll_RemovesEverything()
        {
            var movieId = AddMovie();
            _service.AddNomination(movieId, AwardBody.Oscar, AwardCategory.BestPicture, 2024);
            _service.ClearAll();
            Assert.AreEqual(0, _service.GetNominations().Count);
        }
    }
}
