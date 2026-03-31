using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Watch Party planner — pick a theme, get a curated movie night plan
    /// with films, snack pairings, runtime estimates, and a shareable code.
    /// </summary>
    public class WatchPartyController : Controller
    {
        private readonly WatchPartyService _service;

        public WatchPartyController()
            : this(new InMemoryMovieRepository())
        {
        }

        public WatchPartyController(IMovieRepository movieRepository)
        {
            _service = new WatchPartyService(
                movieRepository ?? throw new ArgumentNullException(nameof(movieRepository)));
        }

        // GET: WatchParty
        public ActionResult Index()
        {
            var vm = new WatchPartyViewModel
            {
                Themes = _service.GetThemes(),
                SavedParties = _service.GetSavedParties()
            };
            return View(vm);
        }

        // GET: WatchParty/Plan?theme=movie-marathon&guests=4
        public ActionResult Plan(string theme, int? guests)
        {
            if (string.IsNullOrWhiteSpace(theme))
                return RedirectToAction("Index");

            var plan = _service.GeneratePlan(theme, guests);
            if (plan == null)
            {
                TempData["Error"] = "Unknown theme. Please pick from the list.";
                return RedirectToAction("Index");
            }

            var vm = new WatchPartyViewModel
            {
                Themes = _service.GetThemes(),
                Plan = plan,
                SavedParties = _service.GetSavedParties(),
                Message = "Your watch party is ready! 🎉"
            };
            return View("Index", vm);
        }

        // GET: WatchParty/Share/ABC123
        public ActionResult Share(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return RedirectToAction("Index");

            var plan = _service.GetByShareCode(id);
            if (plan == null)
            {
                TempData["Error"] = "Party not found. The share code may have expired.";
                return RedirectToAction("Index");
            }

            var vm = new WatchPartyViewModel
            {
                Themes = _service.GetThemes(),
                Plan = plan,
                SavedParties = _service.GetSavedParties(),
                Message = "Shared watch party plan:"
            };
            return View("Index", vm);
        }
    }
}
