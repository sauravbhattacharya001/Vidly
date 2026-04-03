using System;
using System.Web.Mvc;
using Vidly.Filters;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Manages refund requests: submit, approve, deny, process.
    /// Rate-limited to prevent automated refund fraud — an attacker could
    /// submit many refund requests or rapidly approve their own requests
    /// in a compromised staff session (CWE-799).
    /// </summary>
    [RateLimit(MaxRequests = 10, WindowSeconds = 60,
        Message = "Too many refund operations. Please wait before trying again.")]
    public class RefundsController : Controller
    {
        private readonly RefundService _refundService;
        private readonly IRentalRepository _rentalRepository;

        public RefundsController()
            : this(new InMemoryRentalRepository())
        {
        }

        public RefundsController(IRentalRepository rentalRepository)
        {
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _refundService = new RefundService(rentalRepository);
        }

        // GET: Refunds
        public ActionResult Index(string status, string message, bool? error)
        {
            RefundStatus? filter = null;
            if (Enum.TryParse<RefundStatus>(status, true, out var parsed))
                filter = parsed;

            var stats = _refundService.GetStats();
            var viewModel = new RefundViewModel
            {
                Requests = _refundService.GetAll(filter),
                TotalRequests = stats.Total,
                PendingCount = stats.Pending,
                ApprovedCount = stats.Approved,
                DeniedCount = stats.Denied,
                TotalRefunded = stats.TotalRefunded,
                Message = message,
                IsError = error ?? false
            };

            return View(viewModel);
        }

        // GET: Refunds/Request?rentalId=1
        public ActionResult Request(int? rentalId)
        {
            if (!rentalId.HasValue)
                return RedirectToAction("Index", "Rentals");

            var rental = _rentalRepository.GetById(rentalId.Value);
            if (rental == null)
                return HttpNotFound("Rental not found.");

            var viewModel = new RefundViewModel
            {
                RentalId = rentalId.Value,
                CurrentRequest = new RefundRequest
                {
                    RentalId = rentalId.Value,
                    CustomerId = rental.CustomerId,
                    CustomerName = rental.CustomerName,
                    MovieName = rental.MovieName,
                    OriginalAmount = rental.TotalCost
                }
            };

            return View(viewModel);
        }

        // POST: Refunds/Submit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Submit(int rentalId, RefundReason reason, string details, RefundType type)
        {
            try
            {
                var request = _refundService.Submit(rentalId, reason, details, type);
                return RedirectToAction("Index", new
                {
                    message = $"Refund request #{request.Id} submitted for \"{request.MovieName}\" — ${request.RefundAmount:F2} ({request.Type}).",
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                return RedirectToAction("Request", new { rentalId, message = ex.Message, error = true });
            }
        }

        // POST: Refunds/Approve
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Approve(int requestId, string staffNotes, decimal? adjustedAmount)
        {
            try
            {
                var request = _refundService.Approve(requestId, staffNotes, adjustedAmount);
                return RedirectToAction("Index", new
                {
                    message = $"Refund #{request.Id} approved — ${request.RefundAmount:F2} for \"{request.MovieName}\"."
                });
            }
            catch (Exception ex)
            {
                return RedirectToAction("Index", new { message = ex.Message, error = true });
            }
        }

        // POST: Refunds/Deny
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Deny(int requestId, string staffNotes)
        {
            try
            {
                var request = _refundService.Deny(requestId, staffNotes);
                return RedirectToAction("Index", new
                {
                    message = $"Refund #{request.Id} denied for \"{request.MovieName}\"."
                });
            }
            catch (Exception ex)
            {
                return RedirectToAction("Index", new { message = ex.Message, error = true });
            }
        }

        // POST: Refunds/Process
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Process(int requestId)
        {
            try
            {
                var request = _refundService.MarkProcessed(requestId);
                return RedirectToAction("Index", new
                {
                    message = $"Refund #{request.Id} processed — ${request.RefundAmount:F2} returned to customer."
                });
            }
            catch (Exception ex)
            {
                return RedirectToAction("Index", new { message = ex.Message, error = true });
            }
        }

        // GET: Refunds/Details/1
        public ActionResult Details(int id)
        {
            var request = _refundService.GetById(id);
            if (request == null)
                return HttpNotFound("Refund request not found.");

            var viewModel = new RefundViewModel
            {
                CurrentRequest = request
            };

            return View(viewModel);
        }
    }
}
