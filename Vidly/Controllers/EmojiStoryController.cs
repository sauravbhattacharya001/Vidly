using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Services;
using Vidly.ViewModels;
using Newtonsoft.Json;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Emoji Story — guess the movie from its emoji plot summary!
    /// A fun party game with 30 puzzles across 3 difficulty levels,
    /// multiple-choice answers, hints, streak bonuses, and scoring.
    /// </summary>
    public class EmojiStoryController : Controller
    {
        private readonly EmojiStoryService _service;

        public EmojiStoryController()
        {
            _service = new EmojiStoryService();
        }

        /// <summary>
        /// GET /EmojiStory — landing page with difficulty selection and puzzle browser.
        /// </summary>
        public ActionResult Index()
        {
            var viewModel = new EmojiStoryViewModel
            {
                AllPuzzles = _service.GetAll(),
                DifficultyCounts = _service.GetDifficultyCounts(),
                IsPlaying = false,
                IsFinished = false
            };

            return View(viewModel);
        }

        /// <summary>
        /// POST /EmojiStory/Start — begin a new game session.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Start(string difficulty, int rounds = 10)
        {
            rounds = Math.Max(3, Math.Min(rounds, 15));
            var puzzles = _service.GetGameSet(rounds, difficulty);

            if (!puzzles.Any())
            {
                TempData["Error"] = "No puzzles available for that difficulty.";
                return RedirectToAction("Index");
            }

            var session = new EmojiGameSession
            {
                Score = 0,
                Round = 1,
                TotalRounds = puzzles.Count,
                Streak = 0,
                BestStreak = 0
            };

            var first = puzzles.First();
            var queue = string.Join(",", puzzles.Skip(1).Select(p => p.Id));

            var viewModel = new EmojiStoryViewModel
            {
                CurrentPuzzle = first,
                Choices = _service.GetChoices(first),
                Session = session,
                IsPlaying = true,
                Difficulty = difficulty,
                GameQueue = queue,
                SessionJson = JsonConvert.SerializeObject(session)
            };

            return View("Play", viewModel);
        }

        /// <summary>
        /// POST /EmojiStory/Answer — submit an answer and advance.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Answer(int puzzleId, string guess, string gameQueue, string sessionJson, bool hintUsed = false)
        {
            var session = string.IsNullOrEmpty(sessionJson)
                ? new EmojiGameSession()
                : JsonConvert.DeserializeObject<EmojiGameSession>(sessionJson);

            var puzzle = _service.GetById(puzzleId);
            if (puzzle == null)
                return RedirectToAction("Index");

            bool correct = _service.CheckAnswer(puzzleId, guess);

            if (correct)
            {
                session.Streak++;
                if (session.Streak > session.BestStreak)
                    session.BestStreak = session.Streak;
            }
            else
            {
                session.Streak = 0;
            }

            int points = correct ? _service.CalculatePoints(puzzle.Difficulty, hintUsed, session.Streak) : 0;
            session.Score += points;

            var roundResult = new EmojiRoundResult
            {
                Emojis = puzzle.Emojis,
                MovieName = puzzle.MovieName,
                PlayerGuess = guess,
                Correct = correct,
                HintUsed = hintUsed,
                PointsEarned = points
            };
            session.History.Add(roundResult);

            // Parse remaining queue
            var remaining = string.IsNullOrWhiteSpace(gameQueue)
                ? new List<int>()
                : gameQueue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(int.Parse).ToList();

            // If no more puzzles, show results
            if (!remaining.Any())
            {
                session.Round = session.TotalRounds;
                var finishVm = new EmojiStoryViewModel
                {
                    Session = session,
                    LastResult = roundResult,
                    IsFinished = true,
                    IsPlaying = false,
                    DifficultyCounts = _service.GetDifficultyCounts()
                };
                return View("Results", finishVm);
            }

            // Next round
            session.Round++;
            var nextPuzzle = _service.GetById(remaining.First());
            var nextQueue = string.Join(",", remaining.Skip(1));

            var vm = new EmojiStoryViewModel
            {
                CurrentPuzzle = nextPuzzle,
                Choices = _service.GetChoices(nextPuzzle),
                Session = session,
                LastResult = roundResult,
                IsPlaying = true,
                GameQueue = nextQueue,
                SessionJson = JsonConvert.SerializeObject(session)
            };

            return View("Play", vm);
        }
    }
}
