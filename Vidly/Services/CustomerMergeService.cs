using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Detects duplicate customers and merges their accounts,
    /// transferring rentals from the secondary to the primary customer.
    /// </summary>
    public class CustomerMergeService
    {
        private readonly ICustomerRepository _customers;
        private readonly IRentalRepository _rentals;
        private readonly IClock _clock;
        private readonly List<MergeAuditEntry> _auditLog = new List<MergeAuditEntry>();
        private readonly object _lock = new object();
        private int _nextAuditId = 1;

        /// <summary>
        /// Maximum number of audit entries retained in memory.
        /// Prevents unbounded growth in long-running server processes.
        /// </summary>
        private const int MaxAuditEntries = 10_000;

        public CustomerMergeService(
            ICustomerRepository customers,
            IRentalRepository rentals,
            IClock clock = null)
        {
            _customers = customers ?? throw new ArgumentNullException(nameof(customers));
            _rentals = rentals ?? throw new ArgumentNullException(nameof(rentals));
            _clock = clock ?? new SystemClock();
        }

        /// <summary>
        /// Finds potential duplicate customer pairs using name/email/phone similarity.
        /// </summary>
        public IReadOnlyList<DuplicateCandidate> FindDuplicates()
        {
            var all = _customers.GetAll();
            var candidates = new List<DuplicateCandidate>();

            for (int i = 0; i < all.Count; i++)
            {
                for (int j = i + 1; j < all.Count; j++)
                {
                    var a = all[i];
                    var b = all[j];
                    double score = 0;
                    var reasons = new List<string>();

                    // Exact email match (strongest signal)
                    if (!string.IsNullOrWhiteSpace(a.Email) &&
                        !string.IsNullOrWhiteSpace(b.Email) &&
                        string.Equals(a.Email, b.Email, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 0.6;
                        reasons.Add("Same email");
                    }

                    // Exact phone match
                    if (!string.IsNullOrWhiteSpace(a.Phone) &&
                        !string.IsNullOrWhiteSpace(b.Phone) &&
                        NormalizePhone(a.Phone) == NormalizePhone(b.Phone))
                    {
                        score += 0.4;
                        reasons.Add("Same phone");
                    }

                    // Similar name (case-insensitive)
                    if (!string.IsNullOrWhiteSpace(a.Name) &&
                        !string.IsNullOrWhiteSpace(b.Name))
                    {
                        if (string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            score += 0.3;
                            reasons.Add("Same name");
                        }
                        else if (NamesAreSimilar(a.Name, b.Name))
                        {
                            score += 0.15;
                            reasons.Add("Similar name");
                        }
                    }

                    if (score >= 0.3)
                    {
                        candidates.Add(new DuplicateCandidate
                        {
                            CustomerA = a,
                            CustomerB = b,
                            Confidence = Math.Min(score, 1.0),
                            Reason = string.Join(", ", reasons)
                        });
                    }
                }
            }

            return candidates
                .OrderByDescending(c => c.Confidence)
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// Merges two customer accounts. The secondary customer's rentals
        /// are transferred to the primary, then the secondary is removed.
        /// </summary>
        public MergeResult Merge(MergeRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (request.PrimaryId == request.SecondaryId)
                return new MergeResult
                {
                    Success = false,
                    Message = "Cannot merge a customer with themselves."
                };

            var primary = _customers.GetById(request.PrimaryId);
            var secondary = _customers.GetById(request.SecondaryId);

            if (primary == null)
                return new MergeResult { Success = false, Message = $"Primary customer #{request.PrimaryId} not found." };
            if (secondary == null)
                return new MergeResult { Success = false, Message = $"Secondary customer #{request.SecondaryId} not found." };

            // Apply field selections
            if (request.KeepName == "secondary")
                primary.Name = secondary.Name;

            if (request.KeepEmail == "secondary")
                primary.Email = secondary.Email;

            if (request.KeepPhone == "secondary")
                primary.Phone = secondary.Phone;

            // Membership: keep the higher tier
            if (request.KeepMembership == "higher")
            {
                if (secondary.MembershipType > primary.MembershipType)
                    primary.MembershipType = secondary.MembershipType;
            }
            else if (request.KeepMembership == "secondary")
            {
                primary.MembershipType = secondary.MembershipType;
            }

            // Keep the earlier join date
            if (secondary.MemberSince.HasValue &&
                (!primary.MemberSince.HasValue || secondary.MemberSince < primary.MemberSince))
            {
                primary.MemberSince = secondary.MemberSince;
            }

            // Transfer rentals from secondary → primary
            var secondaryRentals = _rentals.GetByCustomer(request.SecondaryId);
            int transferred = 0;
            foreach (var rental in secondaryRentals)
            {
                rental.CustomerId = request.PrimaryId;
                rental.CustomerName = primary.Name;
                _rentals.Update(rental);
                transferred++;
            }

            // Update primary, remove secondary
            _customers.Update(primary);
            _customers.Remove(request.SecondaryId);

            var now = _clock.Now;

            // Record audit
            lock (_lock)
            {
                _auditLog.Insert(0, new MergeAuditEntry
                {
                    Id = _nextAuditId++,
                    PrimaryId = request.PrimaryId,
                    PrimaryName = primary.Name,
                    SecondaryId = request.SecondaryId,
                    SecondaryName = secondary.Name,
                    RentalsTransferred = transferred,
                    MergedAt = now
                });

                // Evict oldest entries to prevent unbounded memory growth
                while (_auditLog.Count > MaxAuditEntries)
                    _auditLog.RemoveAt(_auditLog.Count - 1);
            }

            return new MergeResult
            {
                Success = true,
                Message = $"Merged \"{secondary.Name}\" (#{request.SecondaryId}) into \"{primary.Name}\" (#{request.PrimaryId}). {transferred} rental(s) transferred.",
                MergedCustomer = primary,
                RentalsTransferred = transferred,
                MergedAt = now
            };
        }

        /// <summary>
        /// Returns the merge audit log (most recent first).
        /// </summary>
        public IReadOnlyList<MergeAuditEntry> GetAuditLog()
        {
            lock (_lock)
            {
                return _auditLog.ToList().AsReadOnly();
            }
        }

        private static string NormalizePhone(string phone)
        {
            return new string(phone.Where(char.IsDigit).ToArray());
        }

        private static bool NamesAreSimilar(string a, string b)
        {
            // Check if one name contains the other, or if they share
            // a significant token (first or last name match).
            var aNorm = a.Trim().ToLowerInvariant();
            var bNorm = b.Trim().ToLowerInvariant();

            if (aNorm.Contains(bNorm) || bNorm.Contains(aNorm))
                return true;

            var aTokens = aNorm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var bTokens = bNorm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // Share at least one non-trivial token (length >= 3)
            return aTokens.Any(t => t.Length >= 3 && bTokens.Contains(t));
        }
    }
}
