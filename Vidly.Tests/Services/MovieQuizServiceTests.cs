using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.Tests.Services
{
    [TestClass]
    public class MovieQuizServiceTests
    {
        private List<Movie> _movies;
        private MovieQuizService _service;

        [TestInitialize]
        public void Setup()
        {
            _movies = new List<Movie>
            {
                new Movie { Id = 1, Name = "The Matrix", Genre = new Genre { Id = 1, Name = "Sci-Fi" }, NumberInStock = 5, ReleaseDate = new DateTime(1999, 3, 31) },
                new Movie { Id = 2, Name = "Titanic", Genre = new Genre { Id = 2, Name = "Romance" }, NumberInStock = 3, ReleaseDate = new DateTime(1997, 12, 19) },
                new Movie { Id = 3, Name = "The Godfather", Genre = new Genre { Id = 3, Name = "Crime" }, NumberInStock = 0, ReleaseDate = new DateTime(1972, 3, 24) },
                new Movie { Id = 4, Name = "Toy Story", Genre = new Genre { Id = 4, Name = "Animation" }, NumberInStock = 8, ReleaseDate = new DateTime(1995, 11, 22) },
                new Movie { Id = 5, Name = "Inception", Genre = new Genre { Id = 1, Name = "Sci-Fi" }, NumberInStock = 4, ReleaseDate = new DateTime(2010, 7, 16) },
                new Movie { Id = 6, Name = "Frozen", Genre = new Genre { Id = 4, Name = "Animation" }, NumberInStock = 6, ReleaseDate = new DateTime(2013, 11, 27) },
                new Movie { Id = 7, Name = "Jaws", Genre = new Genre { Id = 5, Name = "Thriller" }, NumberInStock = 2, ReleaseDate = new DateTime(1975, 6, 20) },
                new Movie { Id = 8, Name = "Alien", Genre = new Genre { Id = 5, Name = "Thriller" }, NumberInStock = 0, ReleaseDate = new DateTime(1979, 5, 25) }
            };
            _service = new MovieQuizService(_movies, seed: 42);
        }

        // ── Constructor ─────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovies_Throws()
        {
            new MovieQuizService(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_TooFewMovies_Throws()
        {
            new MovieQuizService(new List<Movie> { _movies[0] });
        }

        // ── StartQuiz ───────────────────────────────────────────

        [TestMethod]
        public void StartQuiz_ReturnsSession()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy);
            Assert.AreEqual(1, session.CustomerId);
            Assert.AreEqual(QuizDifficulty.Easy, session.Difficulty);
            Assert.AreEqual(QuizStatus.InProgress, session.Status);
            Assert.AreEqual(10, session.Questions.Count);
        }

        [TestMethod]
        public void StartQuiz_CustomCount()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Medium, questionCount: 5);
            Assert.AreEqual(5, session.Questions.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void StartQuiz_InvalidCustomerId_Throws()
        {
            _service.StartQuiz(0, QuizDifficulty.Easy);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void StartQuiz_TooManyQuestions_Throws()
        {
            _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 51);
        }

        [TestMethod]
        public void StartQuiz_WithTimeLimit()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy, timeLimitMinutes: 5);
            Assert.AreEqual(5, session.TimeLimitMinutes);
        }

        [TestMethod]
        public void StartQuiz_SpecificCategory()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy,
                category: QuizCategory.Genre, questionCount: 3);
            Assert.AreEqual(3, session.Questions.Count);
            Assert.IsTrue(session.Questions.All(q => q.Category == QuizCategory.Genre));
        }

        // ── SubmitAnswer ────────────────────────────────────────

        [TestMethod]
        public void SubmitAnswer_CorrectAnswer_AwardsPoints()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 3);
            var q = session.Questions[0];
            var answer = _service.SubmitAnswer(session.Id, q.Id, q.CorrectOptionIndex);
            Assert.IsTrue(answer.IsCorrect);
            Assert.IsTrue(answer.PointsEarned > 0);
        }

        [TestMethod]
        public void SubmitAnswer_WrongAnswer_ZeroPoints()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 3);
            var q = session.Questions[0];
            int wrong = (q.CorrectOptionIndex + 1) % q.Options.Count;
            var answer = _service.SubmitAnswer(session.Id, q.Id, wrong);
            Assert.IsFalse(answer.IsCorrect);
            Assert.AreEqual(0, answer.PointsEarned);
        }

        [TestMethod]
        public void SubmitAnswer_SpeedBonus()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 3);
            var q = session.Questions[0];
            var answer = _service.SubmitAnswer(session.Id, q.Id, q.CorrectOptionIndex, 3.0);
            Assert.IsTrue(answer.IsCorrect);
            Assert.AreEqual((int)(10 * 1.5), answer.PointsEarned); // Easy base=10 * 1.5
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SubmitAnswer_InvalidSession_Throws()
        {
            _service.SubmitAnswer(999, 1, 0);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SubmitAnswer_DuplicateAnswer_Throws()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 3);
            var q = session.Questions[0];
            _service.SubmitAnswer(session.Id, q.Id, 0);
            _service.SubmitAnswer(session.Id, q.Id, 1); // duplicate
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SubmitAnswer_InvalidOption_Throws()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 3);
            var q = session.Questions[0];
            _service.SubmitAnswer(session.Id, q.Id, 99);
        }

        [TestMethod]
        public void SubmitAnswer_AllAnswered_AutoCompletes()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 2);
            foreach (var q in session.Questions)
                _service.SubmitAnswer(session.Id, q.Id, q.CorrectOptionIndex);

            var updated = _service.GetSession(session.Id);
            Assert.AreEqual(QuizStatus.Completed, updated.Status);
        }

        // ── CompleteQuiz ────────────────────────────────────────

        [TestMethod]
        public void CompleteQuiz_AwardsLoyaltyPoints()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Medium, questionCount: 5);
            foreach (var q in session.Questions)
                _service.SubmitAnswer(session.Id, q.Id, q.CorrectOptionIndex);

            var completed = _service.GetSession(session.Id);
            Assert.AreEqual(QuizStatus.Completed, completed.Status);
            Assert.IsTrue(completed.LoyaltyPointsAwarded > 0);
        }

        [TestMethod]
        public void CompleteQuiz_PerfectScore_BonusLoyalty()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 3);
            foreach (var q in session.Questions)
                _service.SubmitAnswer(session.Id, q.Id, q.CorrectOptionIndex);

            var completed = _service.GetSession(session.Id);
            // Perfect score gets 1.5x loyalty bonus
            int baseLP = completed.TotalPoints / 10;
            Assert.AreEqual((int)(baseLP * 1.5), completed.LoyaltyPointsAwarded);
        }

        // ── AbandonQuiz ─────────────────────────────────────────

        [TestMethod]
        public void AbandonQuiz_SetsAbandoned()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 3);
            _service.AbandonQuiz(session.Id);
            var updated = _service.GetSession(session.Id);
            Assert.AreEqual(QuizStatus.Abandoned, updated.Status);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void AbandonQuiz_InvalidSession_Throws()
        {
            _service.AbandonQuiz(999);
        }

        // ── GetCustomerSessions ─────────────────────────────────

        [TestMethod]
        public void GetCustomerSessions_ReturnsSorted()
        {
            _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 2);
            _service.StartQuiz(1, QuizDifficulty.Hard, questionCount: 2);
            _service.StartQuiz(2, QuizDifficulty.Easy, questionCount: 2);

            var sessions = _service.GetCustomerSessions(1);
            Assert.AreEqual(2, sessions.Count);
        }

        // ── Daily Challenge ─────────────────────────────────────

        [TestMethod]
        public void DailyChallenge_ReturnsSameForSameDay()
        {
            var c1 = _service.GetDailyChallenge(new DateTime(2026, 3, 8));
            var c2 = _service.GetDailyChallenge(new DateTime(2026, 3, 8));
            Assert.AreSame(c1, c2);
        }

        [TestMethod]
        public void DailyChallenge_DifferentDays_DifferentChallenges()
        {
            var c1 = _service.GetDailyChallenge(new DateTime(2026, 3, 8));
            var c2 = _service.GetDailyChallenge(new DateTime(2026, 3, 9));
            Assert.AreNotSame(c1, c2);
        }

        [TestMethod]
        public void SubmitDailyAnswer_Correct()
        {
            var challenge = _service.GetDailyChallenge(new DateTime(2026, 3, 8));
            var answer = _service.SubmitDailyAnswer(1,
                challenge.Question.CorrectOptionIndex);
            Assert.IsTrue(answer.IsCorrect);
            Assert.IsTrue(answer.PointsEarned > 0);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SubmitDailyAnswer_AlreadyCompleted_Throws()
        {
            var challenge = _service.GetDailyChallenge(new DateTime(2026, 3, 8));
            _service.SubmitDailyAnswer(1, challenge.Question.CorrectOptionIndex);
            _service.SubmitDailyAnswer(1, challenge.Question.CorrectOptionIndex);
        }

        // ── GetHint ─────────────────────────────────────────────

        [TestMethod]
        public void GetHint_ReducesPointValue()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy,
                category: QuizCategory.Genre, questionCount: 3);
            var q = session.Questions[0];
            int originalPoints = q.PointValue;
            var hint = _service.GetHint(session.Id, q.Id);
            Assert.IsNotNull(hint);
            Assert.IsTrue(q.PointValue < originalPoints);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetHint_InvalidSession_Throws()
        {
            _service.GetHint(999, 1);
        }

        // ── Stats ───────────────────────────────────────────────

        [TestMethod]
        public void GetStats_EmptyForNewCustomer()
        {
            var stats = _service.GetCustomerStats(99);
            Assert.AreEqual(0, stats.TotalQuizzes);
        }

        [TestMethod]
        public void GetStats_TracksAccuracy()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 4);
            // Answer 2 correct, 2 wrong
            _service.SubmitAnswer(session.Id, session.Questions[0].Id,
                session.Questions[0].CorrectOptionIndex);
            _service.SubmitAnswer(session.Id, session.Questions[1].Id,
                session.Questions[1].CorrectOptionIndex);
            _service.SubmitAnswer(session.Id, session.Questions[2].Id,
                (session.Questions[2].CorrectOptionIndex + 1) % session.Questions[2].Options.Count);
            _service.SubmitAnswer(session.Id, session.Questions[3].Id,
                (session.Questions[3].CorrectOptionIndex + 1) % session.Questions[3].Options.Count);

            var stats = _service.GetCustomerStats(1);
            Assert.AreEqual(1, stats.TotalQuizzes);
            Assert.AreEqual(4, stats.TotalQuestions);
            Assert.AreEqual(2, stats.TotalCorrect);
            Assert.AreEqual(0.5, stats.OverallAccuracy, 0.01);
        }

        // ── Leaderboard ─────────────────────────────────────────

        [TestMethod]
        public void Leaderboard_RanksCorrectly()
        {
            // Customer 1: answer all correctly
            var s1 = _service.StartQuiz(1, QuizDifficulty.Hard, questionCount: 3);
            foreach (var q in s1.Questions)
                _service.SubmitAnswer(s1.Id, q.Id, q.CorrectOptionIndex);

            // Customer 2: answer all wrong
            var s2 = _service.StartQuiz(2, QuizDifficulty.Easy, questionCount: 3);
            foreach (var q in s2.Questions)
                _service.SubmitAnswer(s2.Id, q.Id, (q.CorrectOptionIndex + 1) % q.Options.Count);

            var board = _service.GetLeaderboard();
            Assert.AreEqual(2, board.Count);
            Assert.AreEqual(1, board[0].CustomerId); // Customer 1 should be first
            Assert.AreEqual(1, board[0].Rank);
            Assert.AreEqual(2, board[1].Rank);
        }

        [TestMethod]
        public void Leaderboard_LimitsResults()
        {
            for (int i = 1; i <= 15; i++)
            {
                var s = _service.StartQuiz(i, QuizDifficulty.Easy, questionCount: 1);
                _service.SubmitAnswer(s.Id, s.Questions[0].Id,
                    s.Questions[0].CorrectOptionIndex);
            }

            var board = _service.GetLeaderboard(top: 5);
            Assert.AreEqual(5, board.Count);
        }

        // ── Streaks ─────────────────────────────────────────────

        [TestMethod]
        public void Streak_IncreasesOnWin()
        {
            var s1 = _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            _service.SubmitAnswer(s1.Id, s1.Questions[0].Id,
                s1.Questions[0].CorrectOptionIndex);

            var s2 = _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            _service.SubmitAnswer(s2.Id, s2.Questions[0].Id,
                s2.Questions[0].CorrectOptionIndex);

            var stats = _service.GetCustomerStats(1);
            Assert.AreEqual(2, stats.CurrentStreak);
        }

        [TestMethod]
        public void Streak_ResetsOnAbandon()
        {
            var s1 = _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            _service.SubmitAnswer(s1.Id, s1.Questions[0].Id,
                s1.Questions[0].CorrectOptionIndex);

            var s2 = _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            _service.AbandonQuiz(s2.Id);

            var stats = _service.GetCustomerStats(1);
            Assert.AreEqual(0, stats.CurrentStreak);
            Assert.AreEqual(1, stats.BestStreak);
        }

        // ── Question Generation ─────────────────────────────────

        [TestMethod]
        public void Questions_HaveValidOptions()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Medium, questionCount: 10);
            foreach (var q in session.Questions)
            {
                Assert.IsNotNull(q.Text);
                Assert.IsTrue(q.Options.Count >= 3);
                Assert.IsTrue(q.CorrectOptionIndex >= 0 && q.CorrectOptionIndex < q.Options.Count);
                Assert.IsTrue(q.PointValue > 0);
            }
        }

        [TestMethod]
        public void Questions_DifficultyAffectsPoints()
        {
            var easy = _service.StartQuiz(1, QuizDifficulty.Easy,
                category: QuizCategory.Genre, questionCount: 1);
            var hard = _service.StartQuiz(2, QuizDifficulty.Hard,
                category: QuizCategory.Genre, questionCount: 1);

            Assert.IsTrue(hard.Questions[0].PointValue > easy.Questions[0].PointValue);
        }

        [TestMethod]
        public void MixedBag_GeneratesMultipleCategories()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy,
                category: QuizCategory.MixedBag, questionCount: 20);
            var categories = session.Questions.Select(q => q.Category).Distinct().ToList();
            Assert.IsTrue(categories.Count > 1, "Mixed bag should generate multiple categories");
        }

        // ── Completed quiz cannot accept answers ────────────────

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SubmitAnswer_CompletedQuiz_Throws()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            _service.SubmitAnswer(session.Id, session.Questions[0].Id,
                session.Questions[0].CorrectOptionIndex);
            // Quiz auto-completed after last answer
            // Try to submit again with a phantom question id
            _service.SubmitAnswer(session.Id, 9999, 0);
        }

        // ── Stats category breakdown ────────────────────────────

        [TestMethod]
        public void Stats_TracksCategoryAccuracy()
        {
            var session = _service.StartQuiz(1, QuizDifficulty.Easy,
                category: QuizCategory.Genre, questionCount: 3);
            foreach (var q in session.Questions)
                _service.SubmitAnswer(session.Id, q.Id, q.CorrectOptionIndex);

            var stats = _service.GetCustomerStats(1);
            Assert.IsTrue(stats.CategoryAccuracy.ContainsKey(QuizCategory.Genre));
            Assert.AreEqual(1.0, stats.CategoryAccuracy[QuizCategory.Genre], 0.01);
        }

        [TestMethod]
        public void Stats_TracksDifficultyBreakdown()
        {
            var s1 = _service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            foreach (var q in s1.Questions)
                _service.SubmitAnswer(s1.Id, q.Id, q.CorrectOptionIndex);

            var s2 = _service.StartQuiz(1, QuizDifficulty.Hard, questionCount: 1);
            foreach (var q in s2.Questions)
                _service.SubmitAnswer(s2.Id, q.Id, q.CorrectOptionIndex);

            var stats = _service.GetCustomerStats(1);
            Assert.AreEqual(1, stats.QuizzesByDifficulty[QuizDifficulty.Easy]);
            Assert.AreEqual(1, stats.QuizzesByDifficulty[QuizDifficulty.Hard]);
        }
    }
}
