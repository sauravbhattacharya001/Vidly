using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Manages movie series/franchises — browse series, view entries,
    /// track per-customer progress, and get "next up" recommendations.
    /// </summary>
    public class SeriesController : Controller
    {
        private static readonly MovieSeriesService _service = new MovieSeriesService();

        // GET: Series
        public ActionResult Index(Genre? genre = null)
        {
            var series = _service.ListSeries(genre);
            ViewBag.Genre = genre;
            return View(series);
        }

        // GET: Series/Details/5
        public ActionResult Details(int id, int? customerId = null)
        {
            var series = _service.GetSeries(id);
            if (series == null)
                return HttpNotFound();

            var entries = _service.GetSeriesEntries(id);
            ViewBag.Series = series;
            ViewBag.Entries = entries;

            if (customerId.HasValue)
            {
                var progress = _service.GetProgress(customerId.Value, id, null);
                ViewBag.Progress = progress;
            }

            return View(series);
        }

        // GET: Series/Search?q=star
        public ActionResult Search(string q)
        {
            var results = _service.SearchSeries(q);
            ViewBag.Query = q;
            return View("Index", results);
        }

        // POST: Series/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(string name, string description, Genre? genre, bool isOngoing = false)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["Error"] = "Series name is required.";
                return RedirectToAction("Index");
            }

            try
            {
                _service.CreateSeries(name, description, genre, isOngoing);
                TempData["Success"] = $"Series '{name}' created.";
            }
            catch (System.InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index");
        }

        // POST: Series/AddMovie
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddMovie(int seriesId, int movieId, int orderIndex, string label)
        {
            try
            {
                _service.AddMovie(seriesId, movieId, orderIndex, label);
                TempData["Success"] = "Movie added to series.";
            }
            catch (System.Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Details", new { id = seriesId });
        }

        // POST: Series/RemoveMovie
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RemoveMovie(int seriesId, int movieId)
        {
            _service.RemoveMovie(seriesId, movieId);
            TempData["Success"] = "Movie removed from series.";
            return RedirectToAction("Details", new { id = seriesId });
        }

        // POST: Series/MarkWatched
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult MarkWatched(int customerId, int seriesEntryId, int seriesId)
        {
            try
            {
                _service.MarkWatched(customerId, seriesEntryId);
                TempData["Success"] = "Marked as watched!";
            }
            catch (System.InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Details", new { id = seriesId, customerId });
        }

        // GET: Series/Progress?customerId=1
        public ActionResult Progress(int customerId)
        {
            var progress = _service.GetAllProgress(customerId, null);
            ViewBag.CustomerId = customerId;
            return View(progress);
        }

        // GET: Series/NextUp?customerId=1
        public ActionResult NextUp(int customerId)
        {
            var recommendations = _service.GetNextUpRecommendations(customerId, null);
            ViewBag.CustomerId = customerId;
            return View(recommendations);
        }

        // GET: Series/Stats
        public ActionResult Stats()
        {
            var stats = _service.GetStats();
            return View(stats);
        }

        // POST: Series/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            _service.DeleteSeries(id);
            TempData["Success"] = "Series deleted.";
            return RedirectToAction("Index");
        }
    }
}
