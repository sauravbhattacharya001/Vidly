using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie rental challenges: browse, join, and track progress.
    /// </summary>
    public class ChallengesController : Controller
    {
        private readonly ChallengeService _challengeService;

        public ChallengesController()
            : this(new InMemoryCustomerRepository())
        {
        }

        public ChallengesController(ICustomerRepository customerRepository)
        {
            _challengeService = new ChallengeService(customerRepository);
        }

        // GET: Challenges
        public ActionResult Index(string difficulty = null)
        {
            if (!string.IsNullOrEmpty(difficulty) &&
                Enum.TryParse<ChallengeDifficulty>(difficulty, true, out var diff))
            {
                ViewBag.Challenges = _challengeService.GetChallengesByDifficulty(diff);
                ViewBag.FilteredDifficulty = difficulty;
            }
            else
            {
                ViewBag.Challenges = _challengeService.GetAllChallenges();
            }

            ViewBag.Leaderboard = _challengeService.GetLeaderboard(10);
            return View();
        }

        // GET: Challenges/Details/5
        public ActionResult Details(int id)
        {
            var challenge = _challengeService.GetChallenge(id);
            if (challenge == null)
                return HttpNotFound("Challenge not found.");

            return View(challenge);
        }

        // POST: Challenges/Join/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Join(int id, int customerId = 1)
        {
            var participant = _challengeService.JoinChallenge(id, customerId, "You");
            if (participant == null)
            {
                TempData["Error"] = "Challenge not found or no longer active.";
                return RedirectToAction("Index");
            }

            TempData["Success"] = "You've joined the challenge! Good luck!";
            return RedirectToAction("Details", new { id });
        }

        // GET: Challenges/Leaderboard
        public ActionResult Leaderboard()
        {
            var leaderboard = _challengeService.GetLeaderboard(20);
            return View(leaderboard);
        }
    }
}
