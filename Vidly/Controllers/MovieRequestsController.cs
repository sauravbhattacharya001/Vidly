using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Request Board — customers submit, browse, and vote on
    /// movies they want the store to stock. Staff can review, fulfill,
    /// or decline requests.
    /// </summary>
    public class MovieRequestsController : Controller
    {
        private readonly MovieRequestService _service;
        private readonly IMovieRequestRepository _requestRepo;

        public MovieRequestsController()
            : this(
                new InMemoryMovieRequestRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository())
        { }

        public MovieRequestsController(
            IMovieRequestRepository requestRepo,
            IMovieRepository movieRepo,
            ICustomerRepository customerRepo)
        {
            _requestRepo = requestRepo
                ?? throw new ArgumentNullException(nameof(requestRepo));
            _service = new MovieRequestService(requestRepo, movieRepo, customerRepo);
        }

        // GET: MovieRequests
        public ActionResult Index(string status, string genre, string q)
        {
            var stats = _service.GetStats();
            var trending = _service.GetTrending(10);
            var genreBreakdown = _service.GetGenreBreakdown();

            // Parse filters
            MovieRequestStatus? statusFilter = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                MovieRequestStatus parsed;
                if (Enum.TryParse(status, true, out parsed))
                    statusFilter = parsed;
            }

            Genre? genreFilter = null;
            if (!string.IsNullOrWhiteSpace(genre))
            {
                Genre parsedGenre;
                if (Enum.TryParse(genre, true, out parsedGenre))
                    genreFilter = parsedGenre;
            }

            // Get requests
            var requests = !string.IsNullOrWhiteSpace(q) || statusFilter.HasValue || genreFilter.HasValue
                ? _service.Search(q ?? "", statusFilter, genreFilter)
                : _requestRepo.GetAll();

            var vm = new MovieRequestViewModel
            {
                Stats = stats,
                Trending = trending,
                Requests = requests,
                GenreBreakdown = genreBreakdown,
                StatusFilter = status,
                GenreFilter = genre,
                SearchQuery = q
            };

            return View(vm);
        }

        // POST: MovieRequests/Submit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Submit(int customerId, string title, int? year, string genre, string reason)
        {
            try
            {
                Genre? genreParsed = null;
                if (!string.IsNullOrWhiteSpace(genre))
                {
                    Genre g;
                    if (Enum.TryParse(genre, true, out g))
                        genreParsed = g;
                }

                _service.SubmitRequest(customerId, title, year, genreParsed, reason);
                TempData["SuccessMessage"] = $"Request for '{title}' submitted successfully!";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("Index");
        }

        // POST: MovieRequests/Upvote
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Upvote(int requestId, int customerId)
        {
            try
            {
                var result = _service.Upvote(requestId, customerId);
                if (!result)
                    TempData["ErrorMessage"] = "Could not upvote — you may have already voted or this is your own request.";
                else
                    TempData["SuccessMessage"] = "Vote recorded!";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("Index");
        }

        // POST: MovieRequests/Review
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Review(int requestId, string staffNote)
        {
            try
            {
                _service.MarkUnderReview(requestId, staffNote);
                TempData["SuccessMessage"] = "Request marked as under review.";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("Index");
        }

        // POST: MovieRequests/Fulfill
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Fulfill(int requestId, string staffNote)
        {
            try
            {
                _service.Fulfill(requestId, staffNote);
                TempData["SuccessMessage"] = "Request fulfilled — movie added to catalog!";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("Index");
        }

        // POST: MovieRequests/Decline
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Decline(int requestId, string staffNote)
        {
            try
            {
                _service.Decline(requestId, staffNote);
                TempData["SuccessMessage"] = "Request declined.";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("Index");
        }

        // GET: MovieRequests/Stats (JSON endpoint)
        public ActionResult Stats()
        {
            var stats = _service.GetStats();
            return Json(stats, JsonRequestBehavior.AllowGet);
        }
    }
}
