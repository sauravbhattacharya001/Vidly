using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages refund requests for rentals.
    ///
    /// Thread safety: All mutations to the shared static request ledger are
    /// serialized via <see cref="_lock"/> to prevent race conditions in
    /// financial operations. Without synchronization, concurrent requests
    /// can exploit TOCTOU windows to:
    ///   - Submit duplicate refunds for the same rental (CWE-367)
    ///   - Approve the same request twice, doubling the payout (CWE-362)
    ///   - Generate duplicate IDs from non-atomic _nextId++ (CWE-362)
    /// </summary>
    public class RefundService
    {
        private static readonly List<RefundRequest> _requests = new List<RefundRequest>();
        private static int _nextId = 1;
        private static readonly object _lock = new object();
        private readonly IRentalRepository _rentalRepository;

        public RefundService(IRentalRepository rentalRepository)
        {
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
        }

        public RefundRequest Submit(int rentalId, RefundReason reason, string details, RefundType type)
        {
            var rental = _rentalRepository.GetById(rentalId);
            if (rental == null)
                throw new KeyNotFoundException($"Rental {rentalId} not found.");

            var refundAmount = type == RefundType.Full ? rental.TotalCost : Math.Round(rental.TotalCost * 0.5m, 2);

            lock (_lock)
            {
                // Duplicate check inside lock to prevent TOCTOU race where two
                // concurrent Submit calls both pass the check before either adds
                // their request, allowing double-refund submissions (CWE-367).
                if (_requests.Any(r => r.RentalId == rentalId && r.Status == RefundStatus.Pending))
                    throw new InvalidOperationException("A pending refund request already exists for this rental.");

                var request = new RefundRequest
                {
                    Id = _nextId++,
                    RentalId = rentalId,
                    CustomerId = rental.CustomerId,
                    CustomerName = rental.CustomerName,
                    MovieName = rental.MovieName,
                    Reason = reason,
                    Details = details,
                    RequestedDate = DateTime.Now,
                    Status = RefundStatus.Pending,
                    OriginalAmount = rental.TotalCost,
                    RefundAmount = refundAmount,
                    Type = type
                };

                _requests.Add(request);
                return request;
            }
        }

        public RefundRequest GetById(int id)
        {
            lock (_lock)
            {
                return _requests.FirstOrDefault(r => r.Id == id);
            }
        }

        public List<RefundRequest> GetAll(RefundStatus? statusFilter = null)
        {
            lock (_lock)
            {
                var query = _requests.AsEnumerable();
                if (statusFilter.HasValue)
                    query = query.Where(r => r.Status == statusFilter.Value);
                return query.OrderByDescending(r => r.RequestedDate).ToList();
            }
        }

        public List<RefundRequest> GetByCustomer(int customerId)
        {
            lock (_lock)
            {
                return _requests.Where(r => r.CustomerId == customerId)
                    .OrderByDescending(r => r.RequestedDate).ToList();
            }
        }

        /// <summary>
        /// Approve a pending refund request. Serialized to prevent concurrent
        /// approval of the same request (CWE-367) — without locking, two staff
        /// members clicking "Approve" simultaneously could both see Status==Pending
        /// and each set it to Approved, potentially triggering duplicate payouts
        /// in downstream payment processing.
        /// </summary>
        public RefundRequest Approve(int requestId, string staffNotes, decimal? adjustedAmount = null)
        {
            lock (_lock)
            {
                var request = _requests.FirstOrDefault(r => r.Id == requestId);
                if (request == null)
                    throw new KeyNotFoundException($"Refund request {requestId} not found.");
                if (request.Status != RefundStatus.Pending)
                    throw new InvalidOperationException("Only pending requests can be approved.");

                // CWE-20: Validate adjusted refund amount bounds.
                // Without this check, a compromised staff session or forged request
                // could set adjustedAmount to an arbitrarily large value (e.g. $999,999)
                // far exceeding the original rental charges, enabling fraudulent payouts.
                if (adjustedAmount.HasValue)
                {
                    if (adjustedAmount.Value <= 0)
                        throw new ArgumentOutOfRangeException(nameof(adjustedAmount),
                            "Adjusted refund amount must be greater than zero.");
                    if (adjustedAmount.Value > request.OriginalAmount)
                        throw new ArgumentOutOfRangeException(nameof(adjustedAmount),
                            $"Adjusted refund amount (${adjustedAmount.Value:F2}) cannot exceed " +
                            $"the original rental charges (${request.OriginalAmount:F2}).");
                }

                request.Status = RefundStatus.Approved;
                request.ResolvedDate = DateTime.Now;
                request.StaffNotes = staffNotes;
                if (adjustedAmount.HasValue)
                    request.RefundAmount = adjustedAmount.Value;

                return request;
            }
        }

        /// <summary>
        /// Deny a pending refund request. Serialized to prevent race with
        /// concurrent Approve on the same request (CWE-367).
        /// </summary>
        public RefundRequest Deny(int requestId, string staffNotes)
        {
            lock (_lock)
            {
                var request = _requests.FirstOrDefault(r => r.Id == requestId);
                if (request == null)
                    throw new KeyNotFoundException($"Refund request {requestId} not found.");
                if (request.Status != RefundStatus.Pending)
                    throw new InvalidOperationException("Only pending requests can be denied.");

                request.Status = RefundStatus.Denied;
                request.ResolvedDate = DateTime.Now;
                request.StaffNotes = staffNotes;
                request.RefundAmount = 0;

                return request;
            }
        }

        /// <summary>
        /// Mark an approved request as processed. Serialized to prevent double
        /// processing which could trigger duplicate payment transfers (CWE-367).
        /// </summary>
        public RefundRequest MarkProcessed(int requestId)
        {
            lock (_lock)
            {
                var request = _requests.FirstOrDefault(r => r.Id == requestId);
                if (request == null)
                    throw new KeyNotFoundException($"Refund request {requestId} not found.");
                if (request.Status != RefundStatus.Approved)
                    throw new InvalidOperationException("Only approved requests can be processed.");

                request.Status = RefundStatus.Processed;
                return request;
            }
        }

        public (int Total, int Pending, int Approved, int Denied, decimal TotalRefunded) GetStats()
        {
            lock (_lock)
            {
                return (
                    _requests.Count,
                    _requests.Count(r => r.Status == RefundStatus.Pending),
                    _requests.Count(r => r.Status == RefundStatus.Approved || r.Status == RefundStatus.Processed),
                    _requests.Count(r => r.Status == RefundStatus.Denied),
                    _requests.Where(r => r.Status == RefundStatus.Approved || r.Status == RefundStatus.Processed)
                        .Sum(r => r.RefundAmount)
                );
            }
        }
    }
}
