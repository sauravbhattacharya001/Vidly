using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Manages customer charge disputes — submit disputes against rental charges
    /// (late fees, damage charges, overcharges), review/escalate, approve/deny with
    /// refund amounts, auto-expire stale disputes, and view resolution statistics.
    /// </summary>
    public class DisputeController : Controller
    {
        private readonly IDisputeRepository _repository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IRentalRepository _rentalRepository;

        public DisputeController()
            : this(new InMemoryDisputeRepository(), new InMemoryCustomerRepository(), new InMemoryRentalRepository())
        {
        }

        public DisputeController(
            IDisputeRepository repository,
            ICustomerRepository customerRepository,
            IRentalRepository rentalRepository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
        }

        // GET: Dispute
        public ActionResult Index(DisputeStatus? status, DisputeType? type,
            DisputePriority? priority, string q, string message, bool? error)
        {
            // Auto-expire disputes older than 30 days that are still open
            AutoExpireStaleDisputes();

            var disputes = _repository.GetAll();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var query = q.Trim().ToLowerInvariant();
                disputes = disputes.Where(d =>
                    (d.CustomerName != null && d.CustomerName.ToLowerInvariant().Contains(query)) ||
                    (d.MovieName != null && d.MovieName.ToLowerInvariant().Contains(query)) ||
                    (d.Reason != null && d.Reason.ToLowerInvariant().Contains(query)));
            }
            else
            {
                if (status.HasValue) disputes = disputes.Where(d => d.Status == status.Value);
                if (type.HasValue) disputes = disputes.Where(d => d.Type == type.Value);
                if (priority.HasValue) disputes = disputes.Where(d => d.Priority == priority.Value);
            }

            var allDisputes = _repository.GetAll().ToList();
            var resolved = allDisputes.Where(d =>
                d.Status == DisputeStatus.Approved ||
                d.Status == DisputeStatus.PartiallyApproved ||
                d.Status == DisputeStatus.Denied).ToList();

            var stats = new DisputeStats
            {
                Total = allDisputes.Count,
                Open = allDisputes.Count(d => d.Status == DisputeStatus.Open),
                UnderReview = allDisputes.Count(d => d.Status == DisputeStatus.UnderReview),
                Approved = allDisputes.Count(d => d.Status == DisputeStatus.Approved),
                PartiallyApproved = allDisputes.Count(d => d.Status == DisputeStatus.PartiallyApproved),
                Denied = allDisputes.Count(d => d.Status == DisputeStatus.Denied),
                Expired = allDisputes.Count(d => d.Status == DisputeStatus.Expired),
                TotalDisputed = allDisputes.Sum(d => d.DisputedAmount),
                TotalRefunded = allDisputes.Sum(d => d.RefundAmount),
                ApprovalRate = resolved.Count > 0
                    ? Math.Round(100.0 * resolved.Count(d =>
                        d.Status == DisputeStatus.Approved ||
                        d.Status == DisputeStatus.PartiallyApproved) / resolved.Count, 1)
                    : 0,
                AverageResolutionDays = resolved.Count > 0
                    ? Math.Round(resolved
                        .Where(d => d.ResolvedDate.HasValue)
                        .Select(d => (d.ResolvedDate.Value - d.SubmittedDate).TotalDays)
                        .DefaultIfEmpty(0)
                        .Average(), 1)
                    : 0,
            };

            var viewModel = new DisputeViewModel
            {
                Disputes = disputes.OrderByDescending(d => d.Priority).ThenByDescending(d => d.SubmittedDate),
                Stats = stats,
                FilterStatus = status,
                FilterType = type,
                FilterPriority = priority,
                SearchQuery = q,
                StatusMessage = message,
                IsError = error ?? false,
            };

            return View(viewModel);
        }

        // POST: Dispute/Submit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Submit(int customerId, int rentalId, DisputeType type,
            string reason, decimal disputedAmount)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                return RedirectToAction("Index", new { message = "Customer not found.", error = true });

            var rental = _rentalRepository.GetById(rentalId);
            if (rental == null)
                return RedirectToAction("Index", new { message = "Rental not found.", error = true });

            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
                return RedirectToAction("Index", new { message = "Reason must be at least 10 characters.", error = true });

            if (disputedAmount <= 0)
                return RedirectToAction("Index", new { message = "Disputed amount must be greater than zero.", error = true });

            // Check for duplicate open disputes on same rental
            var existing = _repository.GetByRental(rentalId)
                .Any(d => d.CustomerId == customerId && d.IsOpen);
            if (existing)
                return RedirectToAction("Index", new { message = "An open dispute already exists for this rental.", error = true });

            // Auto-assign priority based on amount
            var priority = DisputePriority.Normal;
            if (disputedAmount >= 50) priority = DisputePriority.High;
            if (disputedAmount >= 100) priority = DisputePriority.Urgent;

            var dispute = new Dispute
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                RentalId = rentalId,
                MovieName = rental.MovieName,
                Type = type,
                Reason = reason.Trim(),
                DisputedAmount = disputedAmount,
                SubmittedDate = DateTime.Today,
                Status = DisputeStatus.Open,
                Priority = priority,
            };

            _repository.Add(dispute);

            return RedirectToAction("Index", new
            {
                message = $"Dispute #{dispute.Id} submitted for ${disputedAmount:F2} ({type}). Priority: {priority}.",
            });
        }

        // POST: Dispute/Escalate
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Escalate(int id, string staffId)
        {
            var dispute = _repository.GetById(id);
            if (dispute == null)
                return RedirectToAction("Index", new { message = "Dispute not found.", error = true });

            if (!dispute.IsOpen)
                return RedirectToAction("Index", new { message = "Only open disputes can be escalated.", error = true });

            dispute.Status = DisputeStatus.UnderReview;
            if (dispute.Priority < DisputePriority.Urgent)
                dispute.Priority = dispute.Priority + 1;
            dispute.ResolutionNotes = AppendNote(dispute.ResolutionNotes,
                $"Escalated by {(string.IsNullOrWhiteSpace(staffId) ? "STAFF" : staffId.Trim())} on {DateTime.Today:yyyy-MM-dd}.");
            _repository.Update(dispute);

            return RedirectToAction("Index", new
            {
                message = $"Dispute #{id} escalated to {dispute.Status} with {dispute.Priority} priority.",
            });
        }

        // POST: Dispute/Resolve
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Resolve(int id, string resolution, decimal refundAmount, string notes, string staffId)
        {
            var dispute = _repository.GetById(id);
            if (dispute == null)
                return RedirectToAction("Index", new { message = "Dispute not found.", error = true });

            if (!dispute.IsOpen)
                return RedirectToAction("Index", new { message = "This dispute has already been resolved.", error = true });

            DisputeStatus newStatus;
            switch (resolution?.ToLowerInvariant())
            {
                case "approve":
                    newStatus = DisputeStatus.Approved;
                    refundAmount = dispute.DisputedAmount; // full refund
                    break;
                case "partial":
                    newStatus = DisputeStatus.PartiallyApproved;
                    if (refundAmount <= 0 || refundAmount >= dispute.DisputedAmount)
                        return RedirectToAction("Index", new
                        {
                            message = "Partial approval requires a refund between $0.01 and the disputed amount.",
                            error = true,
                        });
                    break;
                case "deny":
                    newStatus = DisputeStatus.Denied;
                    refundAmount = 0;
                    break;
                default:
                    return RedirectToAction("Index", new { message = "Invalid resolution type.", error = true });
            }

            dispute.Status = newStatus;
            dispute.RefundAmount = refundAmount;
            dispute.ResolvedDate = DateTime.Today;
            dispute.ResolvedBy = string.IsNullOrWhiteSpace(staffId) ? "STAFF" : staffId.Trim();
            dispute.ResolutionNotes = AppendNote(dispute.ResolutionNotes,
                $"[{newStatus}] {(string.IsNullOrWhiteSpace(notes) ? "No additional notes." : notes.Trim())}");
            _repository.Update(dispute);

            var refundText = refundAmount > 0 ? $" Refund: ${refundAmount:F2}." : "";
            return RedirectToAction("Index", new
            {
                message = $"Dispute #{id} resolved as {newStatus}.{refundText}",
            });
        }

        // GET: Dispute/Stats
        public ActionResult Stats()
        {
            var all = _repository.GetAll().ToList();
            var byType = all.GroupBy(d => d.Type)
                .Select(g => new { Type = g.Key.ToString(), Count = g.Count(), Total = g.Sum(d => d.DisputedAmount) });
            var byMonth = all.GroupBy(d => d.SubmittedDate.ToString("yyyy-MM"))
                .OrderByDescending(g => g.Key)
                .Select(g => new { Month = g.Key, Count = g.Count(), Refunded = g.Sum(d => d.RefundAmount) });

            return Json(new { byType, byMonth, total = all.Count }, JsonRequestBehavior.AllowGet);
        }

        // GET: Dispute/Customer/5
        public ActionResult Customer(int id)
        {
            var disputes = _repository.GetByCustomer(id).OrderByDescending(d => d.SubmittedDate);
            return Json(disputes, JsonRequestBehavior.AllowGet);
        }

        private void AutoExpireStaleDisputes()
        {
            var stale = _repository.GetByStatus(DisputeStatus.Open)
                .Where(d => d.AgeDays > 30).ToList();

            foreach (var dispute in stale)
            {
                dispute.Status = DisputeStatus.Expired;
                dispute.ResolvedDate = DateTime.Today;
                dispute.ResolvedBy = "Auto";
                dispute.ResolutionNotes = AppendNote(dispute.ResolutionNotes,
                    "Auto-expired after 30 days without resolution.");
                _repository.Update(dispute);
            }
        }

        private static string AppendNote(string existing, string addition)
        {
            if (string.IsNullOrWhiteSpace(existing))
                return addition;
            return existing + " | " + addition;
        }
    }
}
