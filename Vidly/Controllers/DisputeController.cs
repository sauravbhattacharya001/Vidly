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

            // Single fetch — reuse for both filtering and stats to avoid
            // redundant repository calls and repeated full-list iterations.
            var allDisputes = _repository.GetAll().ToList();

            IEnumerable<Dispute> filtered;
            if (!string.IsNullOrWhiteSpace(q))
            {
                var query = q.Trim().ToLowerInvariant();
                filtered = allDisputes.Where(d =>
                    (d.CustomerName != null && d.CustomerName.ToLowerInvariant().Contains(query)) ||
                    (d.MovieName != null && d.MovieName.ToLowerInvariant().Contains(query)) ||
                    (d.Reason != null && d.Reason.ToLowerInvariant().Contains(query)));
            }
            else
            {
                filtered = allDisputes.AsEnumerable();
                if (status.HasValue) filtered = filtered.Where(d => d.Status == status.Value);
                if (type.HasValue) filtered = filtered.Where(d => d.Type == type.Value);
                if (priority.HasValue) filtered = filtered.Where(d => d.Priority == priority.Value);
            }

            // Compute stats in a single pass instead of N separate enumerations.
            var stats = ComputeStats(allDisputes);

            var viewModel = new DisputeViewModel
            {
                Disputes = filtered.OrderByDescending(d => d.Priority).ThenByDescending(d => d.SubmittedDate),
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

        /// <summary>
        /// Computes dispute statistics in a single pass over the list, replacing
        /// the previous approach of 7+ separate Count/Sum enumerations.
        /// </summary>
        private static DisputeStats ComputeStats(List<Dispute> allDisputes)
        {
            int open = 0, underReview = 0, approved = 0, partiallyApproved = 0, denied = 0, expired = 0;
            decimal totalDisputed = 0, totalRefunded = 0;
            int resolvedCount = 0, approvedOrPartialCount = 0;
            double resolutionDaysSum = 0;
            int resolutionDaysCount = 0;

            for (int i = 0; i < allDisputes.Count; i++)
            {
                var d = allDisputes[i];
                totalDisputed += d.DisputedAmount;
                totalRefunded += d.RefundAmount;

                switch (d.Status)
                {
                    case DisputeStatus.Open:
                        open++;
                        break;
                    case DisputeStatus.UnderReview:
                        underReview++;
                        break;
                    case DisputeStatus.Approved:
                        approved++;
                        resolvedCount++;
                        approvedOrPartialCount++;
                        if (d.ResolvedDate.HasValue)
                        {
                            resolutionDaysSum += (d.ResolvedDate.Value - d.SubmittedDate).TotalDays;
                            resolutionDaysCount++;
                        }
                        break;
                    case DisputeStatus.PartiallyApproved:
                        partiallyApproved++;
                        resolvedCount++;
                        approvedOrPartialCount++;
                        if (d.ResolvedDate.HasValue)
                        {
                            resolutionDaysSum += (d.ResolvedDate.Value - d.SubmittedDate).TotalDays;
                            resolutionDaysCount++;
                        }
                        break;
                    case DisputeStatus.Denied:
                        denied++;
                        resolvedCount++;
                        if (d.ResolvedDate.HasValue)
                        {
                            resolutionDaysSum += (d.ResolvedDate.Value - d.SubmittedDate).TotalDays;
                            resolutionDaysCount++;
                        }
                        break;
                    case DisputeStatus.Expired:
                        expired++;
                        break;
                }
            }

            return new DisputeStats
            {
                Total = allDisputes.Count,
                Open = open,
                UnderReview = underReview,
                Approved = approved,
                PartiallyApproved = partiallyApproved,
                Denied = denied,
                Expired = expired,
                TotalDisputed = totalDisputed,
                TotalRefunded = totalRefunded,
                ApprovalRate = resolvedCount > 0
                    ? Math.Round(100.0 * approvedOrPartialCount / resolvedCount, 1)
                    : 0,
                AverageResolutionDays = resolutionDaysCount > 0
                    ? Math.Round(resolutionDaysSum / resolutionDaysCount, 1)
                    : 0,
            };
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

            // Validate disputed amount does not exceed actual rental charges (CWE-20)
            // Without this check, a customer could dispute an arbitrarily large amount
            // (e.g. $10,000 on a $5 rental) and receive a fraudulent refund if approved.
            var maxDisputableAmount = rental.TotalCost + rental.LateFee;
            if (disputedAmount > maxDisputableAmount)
                return RedirectToAction("Index", new { message = $"Disputed amount (${disputedAmount:F2}) exceeds total rental charges (${maxDisputableAmount:F2}).", error = true });

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
