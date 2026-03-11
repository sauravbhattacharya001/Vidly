using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class AwardsController : Controller
    {
        private readonly IMovieRepository _movieRepository;
        private readonly AwardService _awardService;

        public AwardsController()
            : this(new InMemoryMovieRepository())
        {
        }

        public AwardsController(IMovieRepository movieRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _awardService = new AwardService(_movieRepository);
        }

        // GET: Awards
        public ActionResult Index(int? movieId, AwardBody? body, AwardCategory? category,
            int? year, bool? wonOnly, string message, bool? error)
        {
            var vm = new AwardIndexViewModel
            {
                Nominations = _awardService.GetNominations(movieId, body, category, year, wonOnly),
                Leaderboard = _awardService.GetLeaderboard(),
                Movies = _movieRepository.GetAll().OrderBy(m => m.Name).ToList(),
                FilterMovieId = movieId,
                FilterBody = body,
                FilterCategory = category,
                FilterYear = year,
                FilterWonOnly = wonOnly,
                Message = message,
                IsError = error == true
            };

            return View(vm);
        }

        // POST: Awards/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Add(int movieId, AwardBody awardBody, AwardCategory category,
            int year, string nominee, bool won = false)
        {
            try
            {
                _awardService.AddNomination(movieId, awardBody, category, year, nominee, won);
                return RedirectToAction("Index", new
                {
                    message = "Nomination added successfully!",
                    movieId
                });
            }
            catch (Exception ex)
            {
                return RedirectToAction("Index", new
                {
                    message = ex.Message,
                    error = true,
                    movieId
                });
            }
        }

        // POST: Awards/ToggleWin/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ToggleWin(int id)
        {
            try
            {
                var nomination = _awardService.GetById(id);
                if (nomination == null)
                    return RedirectToAction("Index", new { message = "Nomination not found.", error = true });

                _awardService.SetWon(id, !nomination.Won);
                return RedirectToAction("Index", new
                {
                    message = nomination.Won ? "Win reverted to nomination." : "Marked as winner!",
                    movieId = nomination.MovieId
                });
            }
            catch (Exception ex)
            {
                return RedirectToAction("Index", new { message = ex.Message, error = true });
            }
        }

        // POST: Awards/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            var removed = _awardService.RemoveNomination(id);
            return RedirectToAction("Index", new
            {
                message = removed ? "Nomination removed." : "Nomination not found.",
                error = !removed
            });
        }
    }
}
