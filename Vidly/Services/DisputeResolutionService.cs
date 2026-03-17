using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages the full lifecycle of customer disputes against rental charges
    /// (late fees, damage charges, overcharges). Handles submission, validation,
    /// auto-resolution of simple cases, manual review workflow, refund calculation,
    /// and dispute analytics.
    /// </summary>
    public class DisputeResolutionService
    {
        private readonly IDisputeRepository _disputeRepo;
        private readonly IRentalRepository _rentalRepo;
        private readonly ICustomerRepository _customerRepo;

        // ── Policy constants ────────────────────────────────────────

        /// <summary>Max days after a rental's return to file a dispute.</summary>
        public const int DisputeWindowDays = 30;

        /// <summary>Max open disputes per customer at once.</summary>
        public const int MaxOpenDisputesPerCustomer = 3;

        /// <summary>Maximum length for dispute reason text.</summary>
        public const int MaxReasonLength = 2000;

        /// <summary>Maximum length for reviewer name.</summary>
        public const int MaxReviewerNameLength = 100;

        /// <summary>Maximum length for resolution notes.</summary>
        public const int MaxNotesLength = 5000;

        /// <summary>Disputes older than this many days are auto-expired.</summary>
        public const int AutoExpireDays = 60;

        /// <summary>Late fee threshold for auto-approval (first-time, small amount).</summary>
        public const decimal AutoApproveThreshold = 5.00m;

        /// <summary>How much of the disputed amount is refunded on partial approval.</summary>
        public const decimal PartialRefundPercentage = 0.50m;

        private readonly IClock _clock;

        public DisputeResolutionService(
            IDisputeRepository disputeRepo,
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo,
            IClock clock = null)
        {
            _clock = clock ?? new SystemClock();
            _disputeRepo = disputeRepo
                ?? throw new ArgumentNullException(nameof(disputeRepo));
            _rentalRepo = rentalRepo
                ?? throw new ArgumentNullException(nameof(rentalRepo));
            _customerRepo = customerRepo
                ?? throw new ArgumentNullException(nameof(customerRepo));
        }

        // ── Submission ──────────────────────────────────────────────

        /// <summary>
        /// Submit a new dispute. Validates eligibility, calculates priority,
        /// and attempts auto-resolution for simple cases.
        /// </summary>
        public DisputeResult SubmitDispute(int customerId, int rentalId,
            DisputeType type, string reason, decimal disputedAmount)
        {
            // Validate customer exists
            var customer = _customerRepo.GetById(customerId);
            if (customer == null)
                return DisputeResult.Fail("Customer not found.");

            // Validate rental exists and belongs to this customer
            var rental = _rentalRepo.GetById(rentalId);
            if (rental == null)
                return DisputeResult.Fail("Rental not found.");
            if (rental.CustomerId != customerId)
                return DisputeResult.Fail("Rental does not belong to this customer.");

            // Validate rental is returned (can't dispute an active rental)
            if (rental.Status != RentalStatus.Returned)
                return DisputeResult.Fail("Cannot dispute an active rental. Return the movie first.");

            // Validate dispute window
            if (rental.ReturnDate.HasValue)
            {
                var daysSinceReturn = (_clock.Today - rental.ReturnDate.Value).Days;
                if (daysSinceReturn > DisputeWindowDays)
                    return DisputeResult.Fail(
                        $"Dispute window expired. Disputes must be filed within {DisputeWindowDays} days of return.");
            }

            // Validate disputed amount
            if (disputedAmount <= 0)
                return DisputeResult.Fail("Disputed amount must be greater than zero.");
            if (disputedAmount > rental.LateFee + 50m) // late fee + reasonable margin for damage
                return DisputeResult.Fail("Disputed amount exceeds the charges on this rental.");

            // Validate reason — enforce both minimum detail and maximum length.
            // Without a length cap an attacker could submit a multi-megabyte
            // reason string, exhausting memory and bloating storage/logs.
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
                return DisputeResult.Fail("Please provide a detailed reason (at least 10 characters).");
            if (reason.Trim().Length > MaxReasonLength)
                return DisputeResult.Fail(
                    $"Reason cannot exceed {MaxReasonLength} characters.");

            // Check max open disputes
            var openDisputes = _disputeRepo.GetByCustomer(customerId)
                .Count(d => d.IsOpen);
            if (openDisputes >= MaxOpenDisputesPerCustomer)
                return DisputeResult.Fail(
                    $"Maximum of {MaxOpenDisputesPerCustomer} open disputes allowed. " +
                    "Please wait for existing disputes to be resolved.");

            // Check for duplicate dispute on same rental + type
            var existing = _disputeRepo.GetByRental(rentalId)
                .Any(d => d.Type == type && d.IsOpen);
            if (existing)
                return DisputeResult.Fail(
                    "An open dispute of this type already exists for this rental.");

            // Calculate priority
            var priority = CalculatePriority(customer, disputedAmount);

            var dispute = new Dispute
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                RentalId = rentalId,
                MovieName = rental.MovieName,
                Type = type,
                Reason = reason.Trim(),
                DisputedAmount = disputedAmount,
                Status = DisputeStatus.Open,
                SubmittedDate = _clock.Today,
                Priority = priority
            };

            _disputeRepo.Add(dispute);

            // Attempt auto-resolution for simple cases
            var autoResult = TryAutoResolve(dispute, customer, rental);
            if (autoResult != null)
            {
                return DisputeResult.Success(dispute,
                    $"Dispute #{dispute.Id} auto-resolved: {autoResult}");
            }

            return DisputeResult.Success(dispute,
                $"Dispute #{dispute.Id} submitted successfully. " +
                $"Priority: {priority}. A staff member will review it shortly.");
        }

        // ── Review & Resolution ─────────────────────────────────────

        /// <summary>
        /// Move a dispute to Under Review status.
        /// </summary>
        public DisputeResult StartReview(int disputeId, string reviewerName)
        {
            var dispute = _disputeRepo.GetById(disputeId);
            if (dispute == null)
                return DisputeResult.Fail("Dispute not found.");
            if (dispute.Status != DisputeStatus.Open)
                return DisputeResult.Fail("Only open disputes can be moved to review.");
            if (string.IsNullOrWhiteSpace(reviewerName))
                return DisputeResult.Fail("Reviewer name is required.");
            if (reviewerName.Trim().Length > MaxReviewerNameLength)
                return DisputeResult.Fail(
                    $"Reviewer name cannot exceed {MaxReviewerNameLength} characters.");

            dispute.Status = DisputeStatus.UnderReview;
            dispute.ResolvedBy = reviewerName.Trim();
            _disputeRepo.Update(dispute);

            return DisputeResult.Success(dispute, "Dispute is now under review.");
        }

        /// <summary>
        /// Approve a dispute — full refund of disputed amount.
        /// </summary>
        public DisputeResult Approve(int disputeId, string resolvedBy, string notes = null)
        {
            var dispute = _disputeRepo.GetById(disputeId);
            if (dispute == null)
                return DisputeResult.Fail("Dispute not found.");
            if (!dispute.IsOpen)
                return DisputeResult.Fail("Dispute is already resolved.");

            var lengthError = ValidateResolutionInputLengths(resolvedBy, notes);
            if (lengthError != null) return lengthError;

            dispute.Status = DisputeStatus.Approved;
            dispute.RefundAmount = dispute.DisputedAmount;
            dispute.ResolvedDate = _clock.Today;
            dispute.ResolvedBy = resolvedBy?.Trim() ?? "Staff";
            dispute.ResolutionNotes = notes?.Trim();
            _disputeRepo.Update(dispute);

            return DisputeResult.Success(dispute,
                $"Dispute approved. Refund of {dispute.RefundAmount:C} issued.");
        }

        /// <summary>
        /// Partially approve — refund a percentage of the disputed amount.
        /// </summary>
        public DisputeResult PartiallyApprove(int disputeId, decimal refundAmount,
            string resolvedBy, string notes = null)
        {
            var dispute = _disputeRepo.GetById(disputeId);
            if (dispute == null)
                return DisputeResult.Fail("Dispute not found.");
            if (!dispute.IsOpen)
                return DisputeResult.Fail("Dispute is already resolved.");
            if (refundAmount <= 0 || refundAmount > dispute.DisputedAmount)
                return DisputeResult.Fail(
                    "Refund amount must be between $0.01 and the disputed amount.");

            var lengthError = ValidateResolutionInputLengths(resolvedBy, notes);
            if (lengthError != null) return lengthError;

            dispute.Status = DisputeStatus.PartiallyApproved;
            dispute.RefundAmount = refundAmount;
            dispute.ResolvedDate = _clock.Today;
            dispute.ResolvedBy = resolvedBy?.Trim() ?? "Staff";
            dispute.ResolutionNotes = notes?.Trim();
            _disputeRepo.Update(dispute);

            return DisputeResult.Success(dispute,
                $"Dispute partially approved. Refund of {refundAmount:C} issued " +
                $"(of {dispute.DisputedAmount:C} disputed).");
        }

        /// <summary>
        /// Deny a dispute — no refund.
        /// </summary>
        public DisputeResult Deny(int disputeId, string resolvedBy, string notes)
        {
            var dispute = _disputeRepo.GetById(disputeId);
            if (dispute == null)
                return DisputeResult.Fail("Dispute not found.");
            if (!dispute.IsOpen)
                return DisputeResult.Fail("Dispute is already resolved.");
            if (string.IsNullOrWhiteSpace(notes))
                return DisputeResult.Fail("Denial requires an explanation in notes.");

            var lengthError = ValidateResolutionInputLengths(resolvedBy, notes);
            if (lengthError != null) return lengthError;

            dispute.Status = DisputeStatus.Denied;
            dispute.RefundAmount = 0;
            dispute.ResolvedDate = _clock.Today;
            dispute.ResolvedBy = resolvedBy?.Trim() ?? "Staff";
            dispute.ResolutionNotes = notes.Trim();
            _disputeRepo.Update(dispute);

            return DisputeResult.Success(dispute,
                $"Dispute denied. Reason: {notes.Trim()}");
        }

        // ── Auto-Resolution ─────────────────────────────────────────

        /// <summary>
        /// Attempts to auto-resolve simple disputes. Returns resolution
        /// message if resolved, null if manual review is needed.
        ///
        /// Auto-approves when ALL of these are true:
        /// 1. Dispute is for a late fee
        /// 2. Amount is below the auto-approve threshold
        /// 3. Customer has no prior approved disputes (first-time courtesy)
        /// 4. Customer is Silver tier or above
        /// </summary>
        internal string TryAutoResolve(Dispute dispute, Customer customer, Rental rental)
        {
            if (dispute.Type != DisputeType.LateFee)
                return null;

            if (dispute.DisputedAmount > AutoApproveThreshold)
                return null;

            // First-time courtesy: no prior approved/partially-approved disputes
            var priorApproved = _disputeRepo.GetByCustomer(dispute.CustomerId)
                .Count(d => d.Id != dispute.Id &&
                           (d.Status == DisputeStatus.Approved ||
                            d.Status == DisputeStatus.PartiallyApproved));
            if (priorApproved > 0)
                return null;

            // Must be Silver+ tier
            if (customer.MembershipType < MembershipType.Silver)
                return null;

            // Auto-approve
            dispute.Status = DisputeStatus.Approved;
            dispute.RefundAmount = dispute.DisputedAmount;
            dispute.ResolvedDate = _clock.Today;
            dispute.ResolvedBy = "Auto";
            dispute.ResolutionNotes = "First-time courtesy: small late fee auto-waived " +
                                     $"for {customer.MembershipType} member.";
            _disputeRepo.Update(dispute);

            return $"First-time courtesy refund of {dispute.DisputedAmount:C} " +
                   $"(auto-approved for {customer.MembershipType} tier).";
        }

        // ── Batch Operations ────────────────────────────────────────

        /// <summary>
        /// Expire all disputes older than <see cref="AutoExpireDays"/>.
        /// Returns the number of disputes expired.
        /// </summary>
        public int ExpireStaleDisputes()
        {
            var open = _disputeRepo.GetByStatus(DisputeStatus.Open)
                .Concat(_disputeRepo.GetByStatus(DisputeStatus.UnderReview))
                .Where(d => d.AgeDays > AutoExpireDays)
                .ToList();

            foreach (var dispute in open)
            {
                dispute.Status = DisputeStatus.Expired;
                dispute.ResolvedDate = _clock.Today;
                dispute.ResolvedBy = "System";
                dispute.ResolutionNotes =
                    $"Auto-expired after {AutoExpireDays} days without resolution.";
                _disputeRepo.Update(dispute);
            }

            return open.Count;
        }

        // ── Analytics ───────────────────────────────────────────────

        /// <summary>
        /// Get a summary of dispute statistics.
        /// </summary>
        public DisputeSummary GetSummary()
        {
            var all = _disputeRepo.GetAll().ToList();

            var summary = new DisputeSummary
            {
                TotalDisputes = all.Count,
                OpenDisputes = all.Count(d => d.Status == DisputeStatus.Open),
                UnderReview = all.Count(d => d.Status == DisputeStatus.UnderReview),
                Approved = all.Count(d => d.Status == DisputeStatus.Approved),
                PartiallyApproved = all.Count(d => d.Status == DisputeStatus.PartiallyApproved),
                Denied = all.Count(d => d.Status == DisputeStatus.Denied),
                Expired = all.Count(d => d.Status == DisputeStatus.Expired),
                TotalDisputedAmount = all.Sum(d => d.DisputedAmount),
                TotalRefundedAmount = all.Sum(d => d.RefundAmount),
            };

            var resolved = all.Where(d => d.ResolvedDate.HasValue).ToList();
            if (resolved.Count > 0)
            {
                summary.AverageResolutionDays = resolved
                    .Average(d => (d.ResolvedDate.Value - d.SubmittedDate).Days);

                // Bug fix: approval rate should use resolved disputes as the
                // denominator, not total disputes. Including open/pending disputes
                // in the denominator artificially deflates the rate — a store with
                // 8 approved out of 10 resolved but 5 still open would show 53%
                // instead of the correct 80%.
                summary.ApprovalRate = resolved.Count > 0
                    ? (double)(summary.Approved + summary.PartiallyApproved) / resolved.Count * 100
                    : 0;
            }

            // Dispute type breakdown
            summary.ByType = all.GroupBy(d => d.Type)
                .ToDictionary(g => g.Key, g => g.Count());

            // Top disputers
            summary.TopDisputers = all.GroupBy(d => d.CustomerId)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new DisputerInfo
                {
                    CustomerId = g.Key,
                    CustomerName = g.First().CustomerName,
                    DisputeCount = g.Count(),
                    TotalDisputedAmount = g.Sum(d => d.DisputedAmount),
                    ApprovedCount = g.Count(d =>
                        d.Status == DisputeStatus.Approved ||
                        d.Status == DisputeStatus.PartiallyApproved)
                })
                .ToList();

            return summary;
        }

        /// <summary>
        /// Get a customer's dispute history with stats.
        /// </summary>
        public CustomerDisputeHistory GetCustomerHistory(int customerId)
        {
            var customer = _customerRepo.GetById(customerId);
            if (customer == null) return null;

            var disputes = _disputeRepo.GetByCustomer(customerId).ToList();

            return new CustomerDisputeHistory
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                MembershipType = customer.MembershipType,
                Disputes = disputes.OrderByDescending(d => d.SubmittedDate).ToList(),
                TotalDisputes = disputes.Count,
                ApprovedCount = disputes.Count(d =>
                    d.Status == DisputeStatus.Approved ||
                    d.Status == DisputeStatus.PartiallyApproved),
                DeniedCount = disputes.Count(d => d.Status == DisputeStatus.Denied),
                TotalRefunded = disputes.Sum(d => d.RefundAmount),
                TotalDisputed = disputes.Sum(d => d.DisputedAmount),
                HasOpenDispute = disputes.Any(d => d.IsOpen)
            };
        }

        // ── Input Validation ─────────────────────────────────────────

        /// <summary>
        /// Validates length constraints on resolvedBy and notes strings shared
        /// by Approve, PartiallyApprove, and Deny. Without these checks an
        /// attacker (or a misbehaving client) could submit multi-megabyte
        /// strings, exhausting server memory and bloating the database.
        /// </summary>
        private DisputeResult ValidateResolutionInputLengths(string resolvedBy, string notes)
        {
            if (resolvedBy != null && resolvedBy.Trim().Length > MaxReviewerNameLength)
                return DisputeResult.Fail(
                    $"Reviewer name cannot exceed {MaxReviewerNameLength} characters.");
            if (notes != null && notes.Trim().Length > MaxNotesLength)
                return DisputeResult.Fail(
                    $"Notes cannot exceed {MaxNotesLength} characters.");
            return null;
        }

        // ── Priority Calculation ────────────────────────────────────

        /// <summary>
        /// Calculate dispute priority based on customer tier and amount.
        /// Platinum members and high-value disputes get escalated.
        /// </summary>
        internal DisputePriority CalculatePriority(Customer customer, decimal amount)
        {
            // Platinum members always get high priority
            if (customer.MembershipType == MembershipType.Platinum)
                return amount >= 20m ? DisputePriority.Urgent : DisputePriority.High;

            // Gold members get elevated priority
            if (customer.MembershipType == MembershipType.Gold)
                return amount >= 25m ? DisputePriority.High : DisputePriority.Normal;

            // Large amounts get escalated regardless of tier
            if (amount >= 30m)
                return DisputePriority.High;

            // Silver gets normal
            if (customer.MembershipType == MembershipType.Silver)
                return DisputePriority.Normal;

            // Basic tier, small amount
            return amount < 5m ? DisputePriority.Low : DisputePriority.Normal;
        }
    }

    // ── Result & DTO types ──────────────────────────────────────

    /// <summary>
    /// Result of a dispute operation.
    /// </summary>
    public class DisputeResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public Dispute Dispute { get; set; }

        public static DisputeResult Success(Dispute dispute, string message) =>
            new DisputeResult { IsSuccess = true, Dispute = dispute, Message = message };

        public static DisputeResult Fail(string message) =>
            new DisputeResult { IsSuccess = false, Message = message };
    }

    /// <summary>
    /// Overall dispute analytics summary.
    /// </summary>
    public class DisputeSummary
    {
        public int TotalDisputes { get; set; }
        public int OpenDisputes { get; set; }
        public int UnderReview { get; set; }
        public int Approved { get; set; }
        public int PartiallyApproved { get; set; }
        public int Denied { get; set; }
        public int Expired { get; set; }
        public decimal TotalDisputedAmount { get; set; }
        public decimal TotalRefundedAmount { get; set; }
        public double AverageResolutionDays { get; set; }
        public double ApprovalRate { get; set; }
        public Dictionary<DisputeType, int> ByType { get; set; } = new Dictionary<DisputeType, int>();
        public List<DisputerInfo> TopDisputers { get; set; } = new List<DisputerInfo>();
    }

    /// <summary>
    /// Info about a frequent disputer.
    /// </summary>
    public class DisputerInfo
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int DisputeCount { get; set; }
        public decimal TotalDisputedAmount { get; set; }
        public int ApprovedCount { get; set; }
    }

    /// <summary>
    /// A customer's full dispute history.
    /// </summary>
    public class CustomerDisputeHistory
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public MembershipType MembershipType { get; set; }
        public List<Dispute> Disputes { get; set; } = new List<Dispute>();
        public int TotalDisputes { get; set; }
        public int ApprovedCount { get; set; }
        public int DeniedCount { get; set; }
        public decimal TotalRefunded { get; set; }
        public decimal TotalDisputed { get; set; }
        public bool HasOpenDispute { get; set; }
    }
}
