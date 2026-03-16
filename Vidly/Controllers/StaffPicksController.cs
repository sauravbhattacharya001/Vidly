using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    public class StaffPicksController : Controller
    {
        private readonly StaffPicksService _service;
        private readonly IMovieRepository _movieRepo;

        public StaffPicksController()
            : this(new InMemoryMovieRepository())
        {
        }

        public StaffPicksController(IMovieRepository movieRepository)
        {
            _movieRepo = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _service = new StaffPicksService(_movieRepo);
        }

        // GET: StaffPicks
        public ActionResult Index(string staff = null, string theme = null)
        {
            var model = _service.GetPageViewModel(staff, theme);
            ViewBag.FilterStaff = staff;
            ViewBag.FilterTheme = theme;
            return View(model);
        }

        // GET: StaffPicks/Staff/Maria
        public ActionResult Staff(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return RedirectToAction("Index");

            var picks = _service.GetPicksByStaff(id);
            ViewBag.StaffName = id;
            return View(picks);
        }

        // GET: StaffPicks/Theme/Hidden%20Gems
        public ActionResult Theme(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return RedirectToAction("Index");

            var picks = _service.GetPicksByTheme(id);
            ViewBag.ThemeName = id;
            return View(picks);
        }

        // POST: StaffPicks/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Add(int movieId, string staffName, string theme, string note, bool isFeatured = false)
        {
            try
            {
                _service.AddPick(movieId, staffName, theme, note, isFeatured);
                TempData["Success"] = "Staff pick added successfully!";
            }
            catch (ArgumentException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("Index");
        }

        // POST: StaffPicks/Remove/1
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Remove(int id)
        {
            if (_service.RemovePick(id))
                TempData["Success"] = "Pick removed.";
            else
                TempData["Error"] = "Pick not found.";
            return RedirectToAction("Index");
        }
    }
}
