using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class MovieQuizServiceTests
    {
        private static readonly Genre Action = new Genre { Id = 1, Name = "Action" };
        private static readonly Genre Comedy = new Genre { Id = 2, Name = "Comedy" };
        private static readonly Genre Drama = new Genre { Id = 3, Name = "Drama" };
        private static readonly Genre Horror = new Genre { Id = 4, Name = "Horror" };

        private static List<Movie> CreateTestMovies()
        {
            return new List<Movie>
            {
                new Movie { Id = 1, Name = "Die Hard", Genre = Action, ReleaseDate = new DateTime(1988, 7, 20), NumberInStock = 5 },
                new Movie { Id = 2, Name = "Airplane!", Genre = Comedy, ReleaseDate = new DateTime(1980, 7, 2), NumberInStock = 3 },
                new Movie { Id = 3, Name = "The Godfather", Genre = Drama, ReleaseDate = new DateTime(1972, 3, 24), NumberInStock = 0 },
                new Movie { Id = 4, Name = "Psycho", Genre = Horror, ReleaseDate = new DateTime(1960, 9, 8), NumberInStock = 2 },
                new Movie { Id = 5, Name = "Lethal Weapon", Genre = Action, ReleaseDate = new DateTime(1987, 3, 6), NumberInStock = 4 },
            };
        }

        private MovieQuizService CreateService(int seed = 42)
        {
            return new MovieQuizService(CreateTestMovies(), seed);
        }

        // ── Constructor ────────────────────────────────────────

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
            new MovieQuizService(new List<Movie>
            {
                new Movie { Id = 1, Name = "A", Genre = Action },
                new Movie { Id = 2, Name = "B", Genre = Comedy },
                new Movie { Id = 3, Name = "C", Genre = Drama },
            });
        }

        [TestMethod]
        public void Constructor_FourOrMoreMovies_Succeeds()
        {
            var service = CreateService();
            Assert.IsNotNull(service);
        }

        // ── StartQuiz ──────────────────────────────────────────

        [TestMethod]
        public void StartQuiz_ReturnsSessionInProgress()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy);

            Assert.IsNotNull(session);
            Assert.AreEqual(QuizStatus.InProgress, session.Status);
            Assert.AreEqual(1, session.CustomerId);
            Assert.AreEqual(QuizDifficulty.Easy, session.Difficulty);
            Assert.IsTrue(session.Questions.Count > 0);
        }

        [TestMethod]
        public void StartQuiz_DefaultQuestionCount_Is10()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Medium);

            Assert.AreEqual(10, session.Questions.Count);
        }

        [TestMethod]
        public void StartQuiz_CustomQuestionCount()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 5);

            Assert.AreEqual(5, session.Questions.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void StartQuiz_InvalidCustomerId_Throws()
        {
            var service = CreateService();
            service.StartQuiz(0, QuizDifficulty.Easy);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void StartQuiz_QuestionCountTooLarge_Throws()
        {
            var service = CreateService();
            service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 51);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void StartQuiz_QuestionCountZero_Throws()
        {
            var service = CreateService();
            service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void StartQuiz_NegativeTimeLimit_Throws()
        {
            var service = CreateService();
            service.StartQuiz(1, QuizDifficulty.Easy, timeLimitMinutes: -1);
        }

        [TestMethod]
        public void StartQuiz_SequentialSessionIds()
        {
            var service = CreateService();
            var s1 = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            var s2 = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);

            Assert.AreEqual(s1.Id + 1, s2.Id);
        }

        [TestMethod]
        public void StartQuiz_MaxPossiblePoints_IsSum()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 3);

            int expected = session.Questions.Sum(q => q.PointValue);
            Assert.AreEqual(expected, session.MaxPossiblePoints);
        }

        // ── SubmitAnswer ───────────────────────────────────────

        [TestMethod]
        public void SubmitAnswer_CorrectAnswer_AwardsPoints()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            var q = session.Questions[0];

            var answer = service.SubmitAnswer(session.Id, q.Id, q.CorrectOptionIndex);

            Assert.IsTrue(answer.IsCorrect);
            Assert.IsTrue(answer.PointsEarned > 0);
        }

        [TestMethod]
        public void SubmitAnswer_WrongAnswer_ZeroPoints()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            var q = session.Questions[0];
            int wrongIdx = (q.CorrectOptionIndex + 1) % q.Options.Count;

            var answer = service.SubmitAnswer(session.Id, q.Id, wrongIdx);

            Assert.IsFalse(answer.IsCorrect);
            Assert.AreEqual(0, answer.PointsEarned);
        }

        [TestMethod]
        public void SubmitAnswer_SpeedBonus_WhenFastEnough()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Medium, questionCount: 2);
            var q = session.Questions[0];

            // Fast answer within 5 seconds
            var fast = service.SubmitAnswer(session.Id, q.Id, q.CorrectOptionIndex, 3.0);
            Assert.IsTrue(fast.IsCorrect);
            int fastPoints = fast.PointsEarned;

            // Normal speed answer
            var q2 = session.Questions[1];
            var normal = service.SubmitAnswer(session.Id, q2.Id, q2.CorrectOptionIndex, 10.0);
            int normalPoints = normal.PointsEarned;

            // Fast answer should earn more due to speed bonus
            Assert.IsTrue(fastPoints > normalPoints,
                $"Fast points ({fastPoints}) should exceed normal points ({normalPoints}).");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SubmitAnswer_InvalidSession_Throws()
        {
            var service = CreateService();
            service.SubmitAnswer(999, 1, 0);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SubmitAnswer_AlreadyAnswered_Throws()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 2);
            var q = session.Questions[0];

            service.SubmitAnswer(session.Id, q.Id, 0);
            service.SubmitAnswer(session.Id, q.Id, 0); // duplicate
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SubmitAnswer_InvalidOptionIndex_Throws()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            var q = session.Questions[0];

            service.SubmitAnswer(session.Id, q.Id, -1);
        }

        [TestMethod]
        public void SubmitAnswer_AllAnswered_AutoCompletes()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 2);

            foreach (var q in session.Questions)
                service.SubmitAnswer(session.Id, q.Id, q.CorrectOptionIndex);

            var updated = service.GetSession(session.Id);
            Assert.AreEqual(QuizStatus.Completed, updated.Status);
            Assert.IsNotNull(updated.CompletedAt);
        }

        // ── CompleteQuiz ───────────────────────────────────────

        [TestMethod]
        public void CompleteQuiz_AwardsLoyaltyPoints()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 2);

            foreach (var q in session.Questions)
                service.SubmitAnswer(session.Id, q.Id, q.CorrectOptionIndex);

            var completed = service.GetSession(session.Id);
            Assert.IsTrue(completed.LoyaltyPointsAwarded > 0);
        }

        [TestMethod]
        public void CompleteQuiz_PerfectScore_BonusLoyaltyPoints()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 2);

            foreach (var q in session.Questions)
                service.SubmitAnswer(session.Id, q.Id, q.CorrectOptionIndex);

            var completed = service.GetSession(session.Id);
            // Perfect score gives 1.5x loyalty points
            int baseLP = completed.TotalPoints / 10;
            int expected = (int)(baseLP * 1.5);
            Assert.AreEqual(expected, completed.LoyaltyPointsAwarded);
        }

        [TestMethod]
        public void CompleteQuiz_AlreadyCompleted_ReturnsSession()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            var q = session.Questions[0];
            service.SubmitAnswer(session.Id, q.Id, q.CorrectOptionIndex);

            // Already completed via auto-complete, calling again returns same
            var result = service.CompleteQuiz(session.Id);
            Assert.AreEqual(QuizStatus.Completed, result.Status);
        }

        // ── AbandonQuiz ────────────────────────────────────────

        [TestMethod]
        public void AbandonQuiz_SetsStatusAbandoned()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 5);

            service.AbandonQuiz(session.Id);

            var updated = service.GetSession(session.Id);
            Assert.AreEqual(QuizStatus.Abandoned, updated.Status);
            Assert.IsNotNull(updated.CompletedAt);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void AbandonQuiz_InvalidSession_Throws()
        {
            var service = CreateService();
            service.AbandonQuiz(999);
        }

        // ── GetSession / GetCustomerSessions ───────────────────

        [TestMethod]
        public void GetSession_ReturnsCorrectSession()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);

            var result = service.GetSession(session.Id);
            Assert.AreEqual(session.Id, result.Id);
        }

        [TestMethod]
        public void GetSession_NotFound_ReturnsNull()
        {
            var service = CreateService();
            Assert.IsNull(service.GetSession(999));
        }

        [TestMethod]
        public void GetCustomerSessions_ReturnsAllForCustomer()
        {
            var service = CreateService();
            service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            service.StartQuiz(1, QuizDifficulty.Hard, questionCount: 1);
            service.StartQuiz(2, QuizDifficulty.Easy, questionCount: 1);

            var sessions = service.GetCustomerSessions(1);
            Assert.AreEqual(2, sessions.Count);
            Assert.IsTrue(sessions.All(s => s.CustomerId == 1));
        }

        // ── Daily Challenge ────────────────────────────────────

        [TestMethod]
        public void GetDailyChallenge_ReturnsSameForSameDay()
        {
            var service = CreateService();
            var date = new DateTime(2026, 3, 9);

            var c1 = service.GetDailyChallenge(date);
            var c2 = service.GetDailyChallenge(date);

            Assert.AreEqual(c1.Question.Id, c2.Question.Id);
        }

        [TestMethod]
        public void GetDailyChallenge_DoublePoints()
        {
            var service = CreateService();
            var challenge = service.GetDailyChallenge(new DateTime(2026, 3, 9));

            // Hard base is 50, doubled to 100
            Assert.AreEqual(100, challenge.Question.PointValue);
            Assert.AreEqual(2, challenge.BonusMultiplier);
        }

        [TestMethod]
        public void SubmitDailyAnswer_CorrectAnswer_AwardsPoints()
        {
            var service = CreateService();
            var challenge = service.GetDailyChallenge();
            var q = challenge.Question;

            var answer = service.SubmitDailyAnswer(1, q.CorrectOptionIndex);

            Assert.IsTrue(answer.IsCorrect);
            Assert.IsTrue(answer.PointsEarned > 0);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SubmitDailyAnswer_AlreadyCompleted_Throws()
        {
            var service = CreateService();
            var challenge = service.GetDailyChallenge();
            var q = challenge.Question;

            service.SubmitDailyAnswer(1, q.CorrectOptionIndex);
            service.SubmitDailyAnswer(1, q.CorrectOptionIndex); // duplicate
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SubmitDailyAnswer_InvalidOption_Throws()
        {
            var service = CreateService();
            service.GetDailyChallenge();

            service.SubmitDailyAnswer(1, -1);
        }

        // ── GetHint ────────────────────────────────────────────

        [TestMethod]
        public void GetHint_ReducesPointValue()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            var q = session.Questions[0];
            int originalPoints = q.PointValue;

            service.GetHint(session.Id, q.Id);

            Assert.AreEqual(Math.Max(1, originalPoints / 2), q.PointValue);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetHint_InvalidSession_Throws()
        {
            var service = CreateService();
            service.GetHint(999, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetHint_InvalidQuestion_Throws()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            service.GetHint(session.Id, 999);
        }

        // ── Stats ──────────────────────────────────────────────

        [TestMethod]
        public void GetCustomerStats_NoQuizzes_ReturnsEmpty()
        {
            var service = CreateService();
            var stats = service.GetCustomerStats(1);

            Assert.AreEqual(0, stats.TotalQuizzes);
            Assert.AreEqual(0, stats.TotalPointsEarned);
        }

        [TestMethod]
        public void GetCustomerStats_AfterCompletedQuiz_ReturnsCorrectStats()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 2);

            foreach (var q in session.Questions)
                service.SubmitAnswer(session.Id, q.Id, q.CorrectOptionIndex);

            var stats = service.GetCustomerStats(1);
            Assert.AreEqual(1, stats.TotalQuizzes);
            Assert.AreEqual(2, stats.TotalCorrect);
            Assert.AreEqual(1.0, stats.OverallAccuracy, 0.001);
        }

        // ── Leaderboard ────────────────────────────────────────

        [TestMethod]
        public void GetLeaderboard_RanksCustomersByPoints()
        {
            var service = CreateService();

            // Customer 1 completes a quiz with all correct
            var s1 = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 2);
            foreach (var q in s1.Questions)
                service.SubmitAnswer(s1.Id, q.Id, q.CorrectOptionIndex);

            // Customer 2 completes a quiz with all wrong
            var s2 = service.StartQuiz(2, QuizDifficulty.Easy, questionCount: 2);
            foreach (var q in s2.Questions)
            {
                int wrongIdx = (q.CorrectOptionIndex + 1) % q.Options.Count;
                service.SubmitAnswer(s2.Id, q.Id, wrongIdx);
            }

            var leaderboard = service.GetLeaderboard();
            Assert.IsTrue(leaderboard.Count >= 2);
            Assert.AreEqual(1, leaderboard[0].Rank);
            Assert.IsTrue(leaderboard[0].TotalPoints >= leaderboard[1].TotalPoints);
        }

        // ── Streak tracking ───────────────────────────────────

        [TestMethod]
        public void Streak_IncreasesOnWin_ResetsOnLoss()
        {
            var service = CreateService();

            // Win two quizzes
            for (int i = 0; i < 2; i++)
            {
                var s = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
                service.SubmitAnswer(s.Id, s.Questions[0].Id, s.Questions[0].CorrectOptionIndex);
            }

            var stats = service.GetCustomerStats(1);
            Assert.AreEqual(2, stats.CurrentStreak);
            Assert.AreEqual(2, stats.BestStreak);

            // Abandon a quiz (breaks streak)
            var s3 = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            service.AbandonQuiz(s3.Id);

            stats = service.GetCustomerStats(1);
            Assert.AreEqual(0, stats.CurrentStreak);
            Assert.AreEqual(2, stats.BestStreak); // best preserved
        }

        // ── Question generation ────────────────────────────────

        [TestMethod]
        public void GeneratedQuestions_HaveValidStructure()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 5);

            foreach (var q in session.Questions)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(q.Text), "Question text should not be empty.");
                Assert.IsTrue(q.Options.Count >= 3, "Should have at least 3 options.");
                Assert.IsTrue(q.CorrectOptionIndex >= 0 && q.CorrectOptionIndex < q.Options.Count,
                    "Correct option index should be valid.");
                Assert.IsTrue(q.PointValue > 0, "Point value should be positive.");
            }
        }

        [TestMethod]
        public void GenreCategory_QuestionsAskAboutGenre()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy,
                category: QuizCategory.Genre, questionCount: 3);

            foreach (var q in session.Questions)
            {
                Assert.AreEqual(QuizCategory.Genre, q.Category);
                Assert.IsTrue(q.Text.Contains("genre"), "Genre question should mention genre.");
            }
        }

        [TestMethod]
        public void DifferentDifficulties_HaveDifferentPointValues()
        {
            var service = CreateService();

            var easy = service.StartQuiz(1, QuizDifficulty.Easy,
                category: QuizCategory.Genre, questionCount: 1);
            var hard = service.StartQuiz(2, QuizDifficulty.Hard,
                category: QuizCategory.Genre, questionCount: 1);

            Assert.IsTrue(hard.Questions[0].PointValue > easy.Questions[0].PointValue,
                "Hard questions should be worth more points than easy.");
        }

        [TestMethod]
        public void SubmitAnswer_ToCompletedQuiz_Throws()
        {
            var service = CreateService();
            var session = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            var q = session.Questions[0];
            service.SubmitAnswer(session.Id, q.Id, q.CorrectOptionIndex);

            // Quiz auto-completed, now try to submit to completed quiz with new session
            var s2 = service.StartQuiz(1, QuizDifficulty.Easy, questionCount: 1);
            service.AbandonQuiz(s2.Id);

            // Can't submit to abandoned quiz
            try
            {
                service.SubmitAnswer(s2.Id, s2.Questions[0].Id, 0);
                Assert.Fail("Should throw for non-in-progress quiz.");
            }
            catch (InvalidOperationException)
            {
                // expected
            }
        }
    }
}
