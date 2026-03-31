using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Showdown — a fun "this or that" voting game.
    /// Two random movies are shown head-to-head; pick your favorite
    /// and watch the leaderboard evolve.
    /// </summary>
    public class ShowdownController : Controller
    {
        private readonly ShowdownService _service;

        public ShowdownController()
            : this(new InMemoryMovieRepository())
        {
        }

        public ShowdownController(IMovieRepository movieRepository)
        {
            _service = new ShowdownService(
                movieRepository ?? throw new ArgumentNullException(nameof(movieRepository)));
        }

        // GET: Showdown
        public ActionResult Index()
        {
            var matchup = _service.GenerateMatchup();
            var viewModel = new ShowdownViewModel
            {
                CurrentMatchup = matchup,
                Leaderboard = _service.GetLeaderboard(),
                TotalRounds = _service.GetTotalRounds(),
                Message = matchup == null
                    ? "Not enough movies in the catalog to start a showdown!"
                    : null
            };
            return View(viewModel);
        }

        // POST: Showdown/Vote
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Vote(int winnerId, int loserId)
        {
            _service.RecordVote(winnerId, loserId);
            TempData["VoteResult"] = "Vote recorded! Here's your next matchup.";
            return RedirectToAction("Index");
        }

        // GET: Showdown/Leaderboard
        public ActionResult Leaderboard()
        {
            var viewModel = new ShowdownViewModel
            {
                Leaderboard = _service.GetLeaderboard(),
                TotalRounds = _service.GetTotalRounds()
            };
            return View(viewModel);
        }
    }
}
