using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Movie trivia quiz engine that generates questions from the store's movie
    /// catalog. Supports difficulty levels, timed sessions, daily challenges,
    /// streak tracking, loyalty point rewards, and leaderboards.
    /// </summary>
    public class MovieQuizService
    {
        private readonly List<Movie> _movies;
        private readonly List<QuizSession> _sessions = new List<QuizSession>();
        private readonly List<DailyChallenge> _dailyChallenges = new List<DailyChallenge>();
        private readonly Dictionary<int, List<int>> _streaks = new Dictionary<int, List<int>>();
        private readonly IClock _clock;
        private int _nextSessionId = 1;
        private int _nextQuestionId = 1;
        private readonly Random _rng;

        private readonly IClock _clock;
        // ── Point values by difficulty ──────────────────────────
        private static readonly Dictionary<QuizDifficulty, int> BasePoints =
            new Dictionary<QuizDifficulty, int>
            {
                { QuizDifficulty.Easy, 10 },
                { QuizDifficulty.Medium, 25 },
                { QuizDifficulty.Hard, 50 }
            };

        // ── Loyalty point conversion: quiz points → loyalty points ──
        private const int QuizPointsPerLoyaltyPoint = 10;

        // ── Speed bonus thresholds (seconds) ────────────────────
        private const double SpeedBonusThreshold = 5.0;
        private const double SpeedBonusMultiplier = 1.5;

        public MovieQuizService(IEnumerable<Movie> movies, int? seed = null, IClock clock = null)
        {
            _movies = movies?.ToList() ?? throw new ArgumentNullException(nameof(movies));
            if (_movies.Count < 4)
                throw new ArgumentException("Need at least 4 movies to generate quizzes.");
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
            _clock = clock ?? new SystemClock();
        }

        // ════════════════════════════════════════════════════════
        // SESSION MANAGEMENT
        // ════════════════════════════════════════════════════════

        /// <summary>Start a new quiz session.</summary>
        public QuizSession StartQuiz(int customerId, QuizDifficulty difficulty,
            QuizCategory category = QuizCategory.MixedBag,
            int questionCount = 10, int timeLimitMinutes = 0)
        {
            if (customerId <= 0) throw new ArgumentException("Invalid customer ID.");
            if (questionCount < 1 || questionCount > 50)
                throw new ArgumentException("Question count must be 1-50.");
            if (timeLimitMinutes < 0)
                throw new ArgumentException("Time limit cannot be negative.");

            var questions = GenerateQuestions(difficulty, category, questionCount);

            var session = new QuizSession
            {
                Id = _nextSessionId++,
                CustomerId = customerId,
                Difficulty = difficulty,
                Category = category,
                StartedAt = _clock.Now,
                Status = QuizStatus.InProgress,
                Questions = questions,
                TimeLimitMinutes = timeLimitMinutes,
                MaxPossiblePoints = questions.Sum(q => q.PointValue)
            };

            _sessions.Add(session);
            return session;
        }

        /// <summary>Submit an answer for a question in an active session.</summary>
        public QuizAnswer SubmitAnswer(int sessionId, int questionId,
            int selectedOptionIndex, double responseTimeSeconds = 0)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null) throw new ArgumentException("Session not found.");
            if (session.Status != QuizStatus.InProgress)
                throw new InvalidOperationException("Quiz is not in progress.");

            // Check time limit
            if (session.TimeLimitMinutes > 0)
            {
                var elapsed = (_clock.Now - session.StartedAt).TotalMinutes;
                if (elapsed > session.TimeLimitMinutes)
                {
                    session.Status = QuizStatus.TimedOut;
                    session.CompletedAt = _clock.Now;
                    throw new InvalidOperationException("Quiz has timed out.");
                }
            }

            var question = session.Questions.FirstOrDefault(q => q.Id == questionId);
            if (question == null) throw new ArgumentException("Question not found in session.");

            if (session.Answers.Any(a => a.QuestionId == questionId))
                throw new InvalidOperationException("Question already answered.");

            if (selectedOptionIndex < 0 || selectedOptionIndex >= question.Options.Count)
                throw new ArgumentException("Invalid option index.");

            bool correct = selectedOptionIndex == question.CorrectOptionIndex;
            int points = 0;

            if (correct)
            {
                points = question.PointValue;
                // Speed bonus
                if (responseTimeSeconds > 0 && responseTimeSeconds <= SpeedBonusThreshold)
                    points = (int)(points * SpeedBonusMultiplier);
                session.CorrectCount++;
            }

            session.TotalPoints += points;

            var answer = new QuizAnswer
            {
                QuestionId = questionId,
                SelectedOptionIndex = selectedOptionIndex,
                IsCorrect = correct,
                PointsEarned = points,
                AnsweredAt = _clock.Now,
                ResponseTimeSeconds = responseTimeSeconds
            };

            session.Answers.Add(answer);

            // Auto-complete if all questions answered
            if (session.Answers.Count == session.Questions.Count)
                CompleteQuiz(sessionId);

            return answer;
        }

        /// <summary>Complete a quiz session and award loyalty points.</summary>
        public QuizSession CompleteQuiz(int sessionId)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null) throw new ArgumentException("Session not found.");
            if (session.Status == QuizStatus.Completed)
                return session;

            session.Status = QuizStatus.Completed;
            session.CompletedAt = _clock.Now;

            // Calculate loyalty points
            session.LoyaltyPointsAwarded = session.TotalPoints / QuizPointsPerLoyaltyPoint;

            // Bonus for perfect score
            if (session.CorrectCount == session.Questions.Count && session.Questions.Count > 0)
                session.LoyaltyPointsAwarded = (int)(session.LoyaltyPointsAwarded * 1.5);

            // Update streak
            UpdateStreak(session.CustomerId, session.CorrectCount > 0);

            return session;
        }

        /// <summary>Abandon a quiz session.</summary>
        public void AbandonQuiz(int sessionId)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null) throw new ArgumentException("Session not found.");
            session.Status = QuizStatus.Abandoned;
            session.CompletedAt = _clock.Now;
            UpdateStreak(session.CustomerId, false);
        }

        public QuizSession GetSession(int sessionId)
        {
            return _sessions.FirstOrDefault(s => s.Id == sessionId);
        }

        public IReadOnlyList<QuizSession> GetCustomerSessions(int customerId)
        {
            return _sessions.Where(s => s.CustomerId == customerId)
                           .OrderByDescending(s => s.StartedAt)
                           .ToList();
        }

        // ════════════════════════════════════════════════════════
        // QUESTION GENERATION
        // ════════════════════════════════════════════════════════

        private List<QuizQuestion> GenerateQuestions(QuizDifficulty difficulty,
            QuizCategory category, int count)
        {
            var questions = new List<QuizQuestion>();
            var categories = category == QuizCategory.MixedBag
                ? new[] { QuizCategory.Genre, QuizCategory.ReleaseYear,
                          QuizCategory.Rating, QuizCategory.PriceRange,
                          QuizCategory.Availability }
                : new[] { category };

            for (int i = 0; i < count; i++)
            {
                var cat = categories[_rng.Next(categories.Length)];
                var q = GenerateQuestion(cat, difficulty, questions);
                if (q != null) questions.Add(q);
            }

            return questions;
        }

        private QuizQuestion GenerateQuestion(QuizCategory category,
            QuizDifficulty difficulty, List<QuizQuestion> existing)
        {
            switch (category)
            {
                case QuizCategory.Genre:
                    return GenerateGenreQuestion(difficulty);
                case QuizCategory.ReleaseYear:
                    return GenerateYearQuestion(difficulty);
                case QuizCategory.Rating:
                    return GenerateRatingQuestion(difficulty);
                case QuizCategory.PriceRange:
                    return GeneratePriceQuestion(difficulty);
                case QuizCategory.Availability:
                    return GenerateAvailabilityQuestion(difficulty);
                default:
                    return GenerateGenreQuestion(difficulty);
            }
        }

        private QuizQuestion GenerateGenreQuestion(QuizDifficulty difficulty)
        {
            var movie = PickRandom(_movies);
            var correctGenre = movie.Genre?.Name ?? "Unknown";
            var allGenres = _movies.Where(m => m.Genre != null)
                                   .Select(m => m.Genre.Name)
                                   .Distinct().ToList();

            if (allGenres.Count < 2) allGenres.AddRange(new[] { "Action", "Comedy", "Drama", "Horror" });

            var options = new List<string> { correctGenre };
            var wrongGenres = allGenres.Where(g => g != correctGenre).ToList();
            Shuffle(wrongGenres);

            int optionCount = difficulty == QuizDifficulty.Easy ? 3 : 4;
            foreach (var g in wrongGenres.Take(optionCount - 1))
                options.Add(g);

            while (options.Count < optionCount)
                options.Add("Other");

            Shuffle(options);

            return new QuizQuestion
            {
                Id = _nextQuestionId++,
                Text = $"What genre is \"{movie.Name}\"?",
                Options = options,
                CorrectOptionIndex = options.IndexOf(correctGenre),
                Category = QuizCategory.Genre,
                Difficulty = difficulty,
                PointValue = BasePoints[difficulty],
                Hint = difficulty != QuizDifficulty.Hard
                    ? $"It starts with '{correctGenre[0]}'"
                    : null
            };
        }

        private QuizQuestion GenerateYearQuestion(QuizDifficulty difficulty)
        {
            var movie = PickRandom(_movies.Where(m => m.ReleaseDate != default).ToList());
            if (movie == null) return GenerateGenreQuestion(difficulty);

            int correctYear = movie.ReleaseDate.Year;
            var options = new List<string> { correctYear.ToString() };

            // Generate plausible wrong years based on difficulty
            int range = difficulty == QuizDifficulty.Easy ? 10
                      : difficulty == QuizDifficulty.Medium ? 5
                      : 2;

            while (options.Count < 4)
            {
                int wrongYear = correctYear + _rng.Next(-range, range + 1);
                if (wrongYear != correctYear && wrongYear > 1900 &&
                    !options.Contains(wrongYear.ToString()))
                    options.Add(wrongYear.ToString());
            }

            Shuffle(options);

            return new QuizQuestion
            {
                Id = _nextQuestionId++,
                Text = $"In what year was \"{movie.Name}\" released?",
                Options = options,
                CorrectOptionIndex = options.IndexOf(correctYear.ToString()),
                Category = QuizCategory.ReleaseYear,
                Difficulty = difficulty,
                PointValue = BasePoints[difficulty]
            };
        }

        private QuizQuestion GenerateRatingQuestion(QuizDifficulty difficulty)
        {
            var moviesWithRating = _movies.Where(m =>
                m.NumberInStock >= 0).ToList(); // proxy for having data
            var movie = PickRandom(moviesWithRating);

            var availableText = movie.NumberInStock > 0 ? "Available" : "Out of stock";
            var options = new List<string>
            {
                $"{movie.NumberInStock} copies",
                $"{Math.Max(0, movie.NumberInStock - 2)} copies",
                $"{movie.NumberInStock + 3} copies",
                $"{movie.NumberInStock + 7} copies"
            };
            options = options.Distinct().ToList();
            while (options.Count < 4) options.Add($"{_rng.Next(1, 20)} copies");

            Shuffle(options);
            var correct = $"{movie.NumberInStock} copies";

            return new QuizQuestion
            {
                Id = _nextQuestionId++,
                Text = $"How many copies of \"{movie.Name}\" are in stock?",
                Options = options,
                CorrectOptionIndex = options.IndexOf(correct),
                Category = QuizCategory.Rating,
                Difficulty = difficulty,
                PointValue = BasePoints[difficulty]
            };
        }

        private QuizQuestion GeneratePriceQuestion(QuizDifficulty difficulty)
        {
            var subset = _movies.OrderBy(_ => _rng.Next()).Take(4).ToList();
            if (subset.Count < 4) subset = _movies.Take(4).ToList();

            // Find most expensive (by NumberInStock as proxy for popularity)
            var mostPopular = subset.OrderByDescending(m => m.NumberInStock).First();

            var options = subset.Select(m => m.Name).ToList();
            Shuffle(options);

            return new QuizQuestion
            {
                Id = _nextQuestionId++,
                Text = "Which of these movies has the most copies in stock?",
                Options = options,
                CorrectOptionIndex = options.IndexOf(mostPopular.Name),
                Category = QuizCategory.PriceRange,
                Difficulty = difficulty,
                PointValue = BasePoints[difficulty]
            };
        }

        private QuizQuestion GenerateAvailabilityQuestion(QuizDifficulty difficulty)
        {
            var available = _movies.Where(m => m.NumberInStock > 0).ToList();
            var unavailable = _movies.Where(m => m.NumberInStock == 0).ToList();

            if (available.Count == 0 || unavailable.Count == 0)
                return GenerateGenreQuestion(difficulty);

            var correctMovie = PickRandom(available);
            var options = new List<string> { correctMovie.Name };

            var wrongMovies = unavailable.OrderBy(_ => _rng.Next()).Take(3).ToList();
            foreach (var m in wrongMovies) options.Add(m.Name);

            while (options.Count < 4)
                options.Add(PickRandom(_movies).Name);

            options = options.Distinct().ToList();
            while (options.Count < 4) options.Add("Unknown Movie");
            Shuffle(options);

            return new QuizQuestion
            {
                Id = _nextQuestionId++,
                Text = "Which of these movies is currently available to rent?",
                Options = options,
                CorrectOptionIndex = options.IndexOf(correctMovie.Name),
                Category = QuizCategory.Availability,
                Difficulty = difficulty,
                PointValue = BasePoints[difficulty]
            };
        }

        // ════════════════════════════════════════════════════════
        // DAILY CHALLENGE
        // ════════════════════════════════════════════════════════

        /// <summary>Get today's daily challenge question (bonus points).</summary>
        public DailyChallenge GetDailyChallenge(DateTime? date = null)
        {
            var today = (date ?? DateTime.Today).Date;
            var existing = _dailyChallenges.FirstOrDefault(d => d.Date == today);
            if (existing != null) return existing;

            var question = GenerateGenreQuestion(QuizDifficulty.Hard);
            question.PointValue *= 2; // Double points for daily

            var challenge = new DailyChallenge
            {
                Date = today,
                Question = question,
                BonusMultiplier = 2
            };
            _dailyChallenges.Add(challenge);
            return challenge;
        }

        /// <summary>Submit answer for daily challenge.</summary>
        public QuizAnswer SubmitDailyAnswer(int customerId, int selectedOptionIndex,
            double responseTimeSeconds = 0)
        {
            var challenge = GetDailyChallenge();
            if (challenge.CompletedByCustomerIds.Contains(customerId))
                throw new InvalidOperationException("Already completed today's challenge.");

            var question = challenge.Question;
            if (selectedOptionIndex < 0 || selectedOptionIndex >= question.Options.Count)
                throw new ArgumentException("Invalid option index.");

            bool correct = selectedOptionIndex == question.CorrectOptionIndex;
            int points = correct ? question.PointValue : 0;

            if (correct && responseTimeSeconds > 0 && responseTimeSeconds <= SpeedBonusThreshold)
                points = (int)(points * SpeedBonusMultiplier);

            challenge.CompletedByCustomerIds.Add(customerId);

            return new QuizAnswer
            {
                QuestionId = question.Id,
                SelectedOptionIndex = selectedOptionIndex,
                IsCorrect = correct,
                PointsEarned = points,
                AnsweredAt = _clock.Now,
                ResponseTimeSeconds = responseTimeSeconds
            };
        }

        // ════════════════════════════════════════════════════════
        // STATS & LEADERBOARD
        // ════════════════════════════════════════════════════════

        /// <summary>Get detailed stats for a customer.</summary>
        public QuizStats GetCustomerStats(int customerId)
        {
            var sessions = _sessions.Where(s =>
                s.CustomerId == customerId && s.Status == QuizStatus.Completed).ToList();

            if (sessions.Count == 0)
                return new QuizStats { CustomerId = customerId };

            var allAnswers = sessions.SelectMany(s => s.Answers).ToList();
            var allQuestions = sessions.SelectMany(s => s.Questions).ToList();

            // Category accuracy
            var categoryAccuracy = new Dictionary<QuizCategory, double>();
            foreach (QuizCategory cat in Enum.GetValues(typeof(QuizCategory)))
            {
                var catQuestions = allQuestions.Where(q => q.Category == cat).ToList();
                var catAnswers = allAnswers.Where(a =>
                    catQuestions.Any(q => q.Id == a.QuestionId)).ToList();
                if (catAnswers.Count > 0)
                    categoryAccuracy[cat] = (double)catAnswers.Count(a => a.IsCorrect) / catAnswers.Count;
            }

            var bestCat = categoryAccuracy.Count > 0
                ? categoryAccuracy.OrderByDescending(kv => kv.Value).First().Key
                : QuizCategory.MixedBag;
            var worstCat = categoryAccuracy.Count > 0
                ? categoryAccuracy.OrderBy(kv => kv.Value).First().Key
                : QuizCategory.MixedBag;

            // Streaks
            var streakData = GetStreakData(customerId);

            // Quizzes by difficulty
            var byDifficulty = new Dictionary<QuizDifficulty, int>();
            foreach (QuizDifficulty diff in Enum.GetValues(typeof(QuizDifficulty)))
                byDifficulty[diff] = sessions.Count(s => s.Difficulty == diff);

            return new QuizStats
            {
                CustomerId = customerId,
                TotalQuizzes = sessions.Count,
                TotalQuestions = allAnswers.Count,
                TotalCorrect = allAnswers.Count(a => a.IsCorrect),
                TotalPointsEarned = sessions.Sum(s => s.TotalPoints),
                TotalLoyaltyPointsAwarded = sessions.Sum(s => s.LoyaltyPointsAwarded),
                OverallAccuracy = allAnswers.Count > 0
                    ? (double)allAnswers.Count(a => a.IsCorrect) / allAnswers.Count : 0,
                AverageResponseTime = allAnswers.Count > 0
                    ? allAnswers.Average(a => a.ResponseTimeSeconds) : 0,
                CurrentStreak = streakData.current,
                BestStreak = streakData.best,
                StrongestCategory = bestCat,
                WeakestCategory = worstCat,
                CategoryAccuracy = categoryAccuracy,
                QuizzesByDifficulty = byDifficulty
            };
        }

        /// <summary>Get the global leaderboard.</summary>
        public List<LeaderboardEntry> GetLeaderboard(int top = 10)
        {
            var customerIds = _sessions.Where(s => s.Status == QuizStatus.Completed)
                                       .Select(s => s.CustomerId)
                                       .Distinct().ToList();

            var entries = new List<LeaderboardEntry>();
            foreach (var cid in customerIds)
            {
                var stats = GetCustomerStats(cid);
                entries.Add(new LeaderboardEntry
                {
                    CustomerId = cid,
                    TotalPoints = stats.TotalPointsEarned,
                    QuizzesCompleted = stats.TotalQuizzes,
                    AverageAccuracy = stats.OverallAccuracy,
                    CurrentStreak = stats.CurrentStreak,
                    BestStreak = stats.BestStreak
                });
            }

            entries = entries.OrderByDescending(e => e.TotalPoints).ToList();
            for (int i = 0; i < entries.Count; i++)
                entries[i].Rank = i + 1;

            return entries.Take(top).ToList();
        }

        /// <summary>Get hint for a question (costs half the point value).</summary>
        public string GetHint(int sessionId, int questionId)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null) throw new ArgumentException("Session not found.");

            var question = session.Questions.FirstOrDefault(q => q.Id == questionId);
            if (question == null) throw new ArgumentException("Question not found.");

            // Reduce point value for using hint
            question.PointValue = Math.Max(1, question.PointValue / 2);
            session.MaxPossiblePoints = session.Questions.Sum(q => q.PointValue);

            return question.Hint ?? "No hint available for this question.";
        }

        // ════════════════════════════════════════════════════════
        // HELPERS
        // ════════════════════════════════════════════════════════

        private void UpdateStreak(int customerId, bool won)
        {
            if (!_streaks.ContainsKey(customerId))
                _streaks[customerId] = new List<int> { 0, 0 }; // [current, best]

            if (won)
            {
                _streaks[customerId][0]++;
                if (_streaks[customerId][0] > _streaks[customerId][1])
                    _streaks[customerId][1] = _streaks[customerId][0];
            }
            else
            {
                _streaks[customerId][0] = 0;
            }
        }

        private (int current, int best) GetStreakData(int customerId)
        {
            if (!_streaks.ContainsKey(customerId)) return (0, 0);
            return (_streaks[customerId][0], _streaks[customerId][1]);
        }

        private T PickRandom<T>(List<T> list)
        {
            if (list == null || list.Count == 0) return default;
            return list[_rng.Next(list.Count)];
        }

        private void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                var temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }
    }
}
