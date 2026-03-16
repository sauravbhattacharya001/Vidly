using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class MovieTournamentServiceTests
    {
        private MovieTournamentService _service;
        private List<Movie> _movies;

        [TestInitialize]
        public void Setup()
        {
            _movies = new List<Movie>();
            for (int i = 1; i <= 20; i++)
            {
                _movies.Add(new Movie
                {
                    Id = i,
                    Name = $"Movie {i}",
                    Genre = (Genre)((i % 10) + 1),
                    Rating = (i % 5) + 1,
                    ReleaseDate = new DateTime(2020, 1, 1)
                });
            }
            _service = new MovieTournamentService(_movies, seed: 42);
        }

        private Tournament CreateDefault(int size = 8)
        {
            return _service.CreateTournament("Test Bracket", 1, "Alice", size);
        }

        // ── Creation ──

        [TestMethod]
        public void CreateTournament_ValidSize_CreatesWithCorrectSeeds()
        {
            var t = CreateDefault(8);
            Assert.AreEqual(8, t.Seeds.Count);
            Assert.AreEqual(3, t.TotalRounds); // log2(8)
            Assert.AreEqual(1, t.CurrentRound);
            Assert.AreEqual(TournamentStatus.InProgress, t.Status);
        }

        [TestMethod]
        public void CreateTournament_Size4_Creates2Rounds()
        {
            var t = CreateDefault(4);
            Assert.AreEqual(4, t.Seeds.Count);
            Assert.AreEqual(2, t.TotalRounds);
        }

        [TestMethod]
        public void CreateTournament_Size16_Creates4Rounds()
        {
            var t = CreateDefault(16);
            Assert.AreEqual(16, t.Seeds.Count);
            Assert.AreEqual(4, t.TotalRounds);
            Assert.AreEqual(8, t.Matches.Count(m => m.Round == 1));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateTournament_InvalidSize_Throws()
        {
            _service.CreateTournament("Bad", 1, "Alice", 6);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateTournament_EmptyName_Throws()
        {
            _service.CreateTournament("", 1, "Alice", 8);
        }

        [TestMethod]
        public void CreateTournament_WithSpecificMovieIds_UsesThoseMovies()
        {
            var ids = new List<int> { 1, 2, 3, 4 };
            var t = _service.CreateTournament("Pick4", 1, "Bob", 4, movieIds: ids);
            Assert.IsTrue(t.Seeds.All(s => ids.Contains(s.MovieId)));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateTournament_DuplicateMovieIds_Throws()
        {
            _service.CreateTournament("Dup", 1, "X", 4, movieIds: new List<int> { 1, 1, 2, 3 });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateTournament_WrongMovieIdCount_Throws()
        {
            _service.CreateTournament("Wrong", 1, "X", 8, movieIds: new List<int> { 1, 2, 3 });
        }

        [TestMethod]
        public void CreateTournament_GenreFilter_FiltersMovies()
        {
            var t = _service.CreateTournament("Action Only", 1, "Alice", 4, genreFilter: Genre.Action);
            Assert.IsTrue(t.Seeds.All(s => s.Genre == Genre.Action));
        }

        [TestMethod]
        public void CreateTournament_GeneratesFirstRoundMatches()
        {
            var t = CreateDefault(8);
            var round1 = t.Matches.Where(m => m.Round == 1).ToList();
            Assert.AreEqual(4, round1.Count);
        }

        // ── Voting ──

        [TestMethod]
        public void Vote_ValidVote_RecordsWinner()
        {
            var t = CreateDefault(4);
            var match = t.Matches.First(m => m.Round == 1);
            var result = _service.Vote(t.Id, match.Id, match.Movie1Id, "Better film");
            Assert.AreEqual(match.Movie1Id, result.WinnerMovieId);
            Assert.AreEqual("Better film", result.VoteReason);
            Assert.IsTrue(result.IsComplete);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Vote_InvalidWinner_Throws()
        {
            var t = CreateDefault(4);
            var match = t.Matches.First();
            _service.Vote(t.Id, match.Id, 999);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Vote_AlreadyVoted_Throws()
        {
            var t = CreateDefault(4);
            var match = t.Matches.First();
            _service.Vote(t.Id, match.Id, match.Movie1Id);
            _service.Vote(t.Id, match.Id, match.Movie2Id);
        }

        [TestMethod]
        public void Vote_CompletesRound_AdvancesToNext()
        {
            var t = CreateDefault(4);
            // Vote all round 1 matches
            foreach (var m in t.Matches.Where(m => m.Round == 1).ToList())
                _service.Vote(t.Id, m.Id, m.Movie1Id);

            Assert.AreEqual(2, t.CurrentRound);
            Assert.IsTrue(t.Matches.Any(m => m.Round == 2));
        }

        [TestMethod]
        public void Vote_Finals_CompletesTournament()
        {
            var t = CreateDefault(4);
            // Round 1
            foreach (var m in t.Matches.Where(m => m.Round == 1).ToList())
                _service.Vote(t.Id, m.Id, m.Movie1Id);
            // Finals
            var final = t.Matches.First(m => m.Round == 2);
            _service.Vote(t.Id, final.Id, final.Movie1Id);

            Assert.AreEqual(TournamentStatus.Completed, t.Status);
            Assert.IsNotNull(t.ChampionMovieId);
            Assert.IsNotNull(t.ChampionMovieName);
        }

        [TestMethod]
        public void Vote_EliminatesLoser()
        {
            var t = CreateDefault(4);
            var match = t.Matches.First();
            _service.Vote(t.Id, match.Id, match.Movie1Id);

            var loserSeed = t.Seeds.First(s => s.MovieId == match.Movie2Id);
            Assert.IsTrue(loserSeed.Eliminated);
        }

        [TestMethod]
        public void Vote_IncrementsWinnerWins()
        {
            var t = CreateDefault(4);
            var match = t.Matches.First();
            var winnerSeed = t.Seeds.First(s => s.MovieId == match.Movie1Id);
            Assert.AreEqual(0, winnerSeed.Wins);
            _service.Vote(t.Id, match.Id, match.Movie1Id);
            Assert.AreEqual(1, winnerSeed.Wins);
        }

        // ── Queries ──

        [TestMethod]
        public void ListTournaments_FiltersByStatus()
        {
            CreateDefault(4);
            Assert.AreEqual(1, _service.ListTournaments(TournamentStatus.InProgress).Count);
            Assert.AreEqual(0, _service.ListTournaments(TournamentStatus.Completed).Count);
        }

        [TestMethod]
        public void GetPendingMatches_ReturnsCurrentRoundUnvoted()
        {
            var t = CreateDefault(4);
            var pending = _service.GetPendingMatches(t.Id);
            Assert.AreEqual(2, pending.Count);

            _service.Vote(t.Id, pending[0].Id, pending[0].Movie1Id);
            Assert.AreEqual(1, _service.GetPendingMatches(t.Id).Count);
        }

        [TestMethod]
        public void GetTournament_NotFound_ReturnsNull()
        {
            Assert.IsNull(_service.GetTournament(999));
        }

        // ── Hall of Fame ──

        [TestMethod]
        public void GetHallOfFame_ReturnsCompletedTournaments()
        {
            var t = RunFullTournament(4);
            var hof = _service.GetHallOfFame();
            Assert.AreEqual(1, hof.Count);
            Assert.AreEqual(t.ChampionMovieName, hof[0].ChampionName);
        }

        [TestMethod]
        public void GetMovieRecords_TracksWinsAndLosses()
        {
            RunFullTournament(4);
            var records = _service.GetMovieRecords();
            Assert.IsTrue(records.Count > 0);
            var champion = records.First(r => r.TournamentsWon > 0);
            Assert.IsTrue(champion.MatchesWon > 0);
            Assert.IsTrue(champion.WinRate > 0);
        }

        // ── Cancel ──

        [TestMethod]
        public void CancelTournament_SetsStatusCancelled()
        {
            var t = CreateDefault(4);
            Assert.IsTrue(_service.CancelTournament(t.Id));
            Assert.AreEqual(TournamentStatus.Cancelled, t.Status);
        }

        [TestMethod]
        public void CancelTournament_Completed_ReturnsFalse()
        {
            var t = RunFullTournament(4);
            Assert.IsFalse(_service.CancelTournament(t.Id));
        }

        [TestMethod]
        public void CancelTournament_NotFound_ReturnsFalse()
        {
            Assert.IsFalse(_service.CancelTournament(999));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Vote_CancelledTournament_Throws()
        {
            var t = CreateDefault(4);
            _service.CancelTournament(t.Id);
            var match = t.Matches.First();
            _service.Vote(t.Id, match.Id, match.Movie1Id);
        }

        // ── Bracket Seeding ──

        [TestMethod]
        public void BracketSeeding_TopSeedDoesNotFaceSecondSeedInRound1()
        {
            var t = CreateDefault(8);
            var round1 = t.Matches.Where(m => m.Round == 1).ToList();
            // Seed 1 and seed 2 should not be in the same match
            foreach (var m in round1)
            {
                var seeds = new[] { m.Movie1Seed, m.Movie2Seed };
                Assert.IsFalse(seeds.Contains(1) && seeds.Contains(2),
                    "Top two seeds should not meet in round 1");
            }
        }

        [TestMethod]
        public void GetRoundMatches_ReturnsMatchesForSpecificRound()
        {
            var t = CreateDefault(8);
            var r1 = _service.GetRoundMatches(t.Id, 1);
            Assert.AreEqual(4, r1.Count);
            Assert.IsTrue(r1.All(m => m.Round == 1));
        }

        [TestMethod]
        public void RoundLabels_AreCorrect()
        {
            var t = CreateDefault(8);
            var r1 = t.Matches.Where(m => m.Round == 1).ToList();
            Assert.AreEqual("Quarterfinals", r1[0].RoundLabel);

            // Complete round 1
            foreach (var m in r1) _service.Vote(t.Id, m.Id, m.Movie1Id);
            var r2 = t.Matches.Where(m => m.Round == 2).ToList();
            Assert.AreEqual("Semifinals", r2[0].RoundLabel);

            // Complete round 2
            foreach (var m in r2) _service.Vote(t.Id, m.Id, m.Movie1Id);
            var r3 = t.Matches.Where(m => m.Round == 3).ToList();
            Assert.AreEqual("Finals", r3[0].RoundLabel);
        }

        // ── Helper ──

        private Tournament RunFullTournament(int size)
        {
            var t = CreateDefault(size);
            while (t.Status == TournamentStatus.InProgress)
            {
                var pending = _service.GetPendingMatches(t.Id);
                foreach (var m in pending)
                    _service.Vote(t.Id, m.Id, m.Movie1Id);
            }
            return t;
        }
    }
}
