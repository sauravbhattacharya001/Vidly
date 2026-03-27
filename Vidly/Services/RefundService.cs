using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages refund requests for rentals.
    /// </summary>
    public class RefundService
    {
        private static readonly List<RefundRequest> _requests = new List<RefundRequest>();
        private static int _nextId = 1;
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

            // Check for duplicate pending request
            if (_requests.Any(r => r.RentalId == rentalId && r.Status == RefundStatus.Pending))
                throw new InvalidOperationException("A pending refund request already exists for this rental.");

            var refundAmount = type == RefundType.Full ? rental.TotalCost : Math.Round(rental.TotalCost * 0.5m, 2);

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

        public RefundRequest GetById(int id)
        {
            return _requests.FirstOrDefault(r => r.Id == id);
        }

        public List<RefundRequest> GetAll(RefundStatus? statusFilter = null)
        {
            var query = _requests.AsEnumerable();
            if (statusFilter.HasValue)
                query = query.Where(r => r.Status == statusFilter.Value);
            return query.OrderByDescending(r => r.RequestedDate).ToList();
        }

        public List<RefundRequest> GetByCustomer(int customerId)
        {
            return _requests.Where(r => r.CustomerId == customerId)
                .OrderByDescending(r => r.RequestedDate).ToList();
        }

        public RefundRequest Approve(int requestId, string staffNotes, decimal? adjustedAmount = null)
        {
            var request = GetById(requestId);
            if (request == null)
                throw new KeyNotFoundException($"Refund request {requestId} not found.");
            if (request.Status != RefundStatus.Pending)
                throw new InvalidOperationException("Only pending requests can be approved.");

            request.Status = RefundStatus.Approved;
            request.ResolvedDate = DateTime.Now;
            request.StaffNotes = staffNotes;
            if (adjustedAmount.HasValue)
                request.RefundAmount = adjustedAmount.Value;

            return request;
        }

        public RefundRequest Deny(int requestId, string staffNotes)
        {
            var request = GetById(requestId);
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

        public RefundRequest MarkProcessed(int requestId)
        {
            var request = GetById(requestId);
            if (request == null)
                throw new KeyNotFoundException($"Refund request {requestId} not found.");
            if (request.Status != RefundStatus.Approved)
                throw new InvalidOperationException("Only approved requests can be processed.");

            request.Status = RefundStatus.Processed;
            return request;
        }

        public (int Total, int Pending, int Approved, int Denied, decimal TotalRefunded) GetStats()
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
