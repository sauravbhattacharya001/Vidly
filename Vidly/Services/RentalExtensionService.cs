using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages rental period extensions. Customers can request to extend
    /// their active rentals before or shortly after the due date, subject
    /// to configurable rules around fees, membership perks, reservation
    /// conflicts, and per-rental limits.
    /// </summary>
    public class RentalExtensionService
    {
        private readonly IRentalRepository _rentalRepo;
        private readonly ICustomerRepository _customerRepo;
        private readonly IReservationRepository _reservationRepo;
        private readonly IClock _clock;

        /// <summary>Maximum number of extensions allowed per rental.</summary>
        public const int MaxExtensionsPerRental = 3;

        /// <summary>Default extension length in days.</summary>
        public const int DefaultExtensionDays = 3;

        /// <summary>Maximum single extension length in days.</summary>
        public const int MaxExtensionDays = 7;

        /// <summary>
        /// Percentage of the daily rate charged per extension day.
        /// 1.0 = full daily rate, 0.5 = half rate.
        /// </summary>
        public const decimal ExtensionRateMultiplier = 0.75m;

        /// <summary>
        /// Maximum days past due date a customer can still request an extension.
        /// Beyond this, the rental is too overdue to extend.
        /// </summary>
        public const int MaxDaysPastDueForExtension = 3;

        // Track extension history in-memory (keyed by rental ID)
        private readonly Dictionary<int, List<ExtensionRecord>> _extensionHistory
            = new Dictionary<int, List<ExtensionRecord>>();

        public RentalExtensionService(
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo,
            IReservationRepository reservationRepo,
            IClock clock = null)
        {
            _rentalRepo = rentalRepo
                ?? throw new ArgumentNullException(nameof(rentalRepo));
            _customerRepo = customerRepo
                ?? throw new ArgumentNullException(nameof(customerRepo));
            _reservationRepo = reservationRepo
                ?? throw new ArgumentNullException(nameof(reservationRepo));
            _clock = clock ?? new SystemClock();
        }

        // ── Extend ───────────────────────────────────────────────────

        /// <summary>
        /// Request an extension for an active rental.
        /// </summary>
        /// <param name="rentalId">The rental to extend.</param>
        /// <param name="additionalDays">
        /// Days to add (1–<see cref="MaxExtensionDays"/>).
        /// Defaults to <see cref="DefaultExtensionDays"/> if 0 or negative.
        /// </param>
        /// <returns>Result containing the updated rental and fee details.</returns>
        public ExtensionResult RequestExtension(int rentalId, int additionalDays = 0)
        {
            if (additionalDays <= 0)
                additionalDays = DefaultExtensionDays;

            if (additionalDays > MaxExtensionDays)
                throw new ArgumentOutOfRangeException(nameof(additionalDays),
                    $"Extension cannot exceed {MaxExtensionDays} days.");

            var rental = _rentalRepo.GetById(rentalId);
            if (rental == null)
                throw new ArgumentException(
                    $"Rental {rentalId} not found.", nameof(rentalId));

            if (rental.Status == RentalStatus.Returned)
                throw new InvalidOperationException(
                    "Cannot extend a rental that has already been returned.");

            // Check how far past due
            var today = _clock.Today;
            if (today > rental.DueDate)
            {
                var daysPast = (int)Math.Ceiling((today - rental.DueDate).TotalDays);
                if (daysPast > MaxDaysPastDueForExtension)
                    throw new InvalidOperationException(
                        $"Rental is {daysPast} days overdue — too late to extend " +
                        $"(maximum {MaxDaysPastDueForExtension} days past due).");
            }

            // Check extension count
            var history = GetExtensionHistory(rentalId);
            if (history.Count >= MaxExtensionsPerRental)
                throw new InvalidOperationException(
                    $"Maximum of {MaxExtensionsPerRental} extensions per rental reached.");

            // Check reservation conflict
            var activeReservations = _reservationRepo.GetActiveByMovie(rental.MovieId);
            if (activeReservations.Count > 0)
                throw new InvalidOperationException(
                    $"Cannot extend — {activeReservations.Count} customer(s) " +
                    "waiting for this movie.");

            // Calculate fee
            var customer = _customerRepo.GetById(rental.CustomerId);
            var feePerDay = rental.DailyRate * ExtensionRateMultiplier;
            var discount = GetMembershipDiscount(customer?.MembershipType ?? MembershipType.Basic);
            var discountedFeePerDay = feePerDay * (1m - discount);
            var totalFee = Math.Round(discountedFeePerDay * additionalDays, 2);

            // Apply extension
            var oldDueDate = rental.DueDate;
            rental.DueDate = rental.DueDate.AddDays(additionalDays);

            // If the rental was marked Overdue and the new due date is in the future,
            // reset to Active
            if (rental.Status == RentalStatus.Overdue && rental.DueDate >= today)
                rental.Status = RentalStatus.Active;

            _rentalRepo.Update(rental);

            // Record extension
            var record = new ExtensionRecord
            {
                RentalId = rentalId,
                RequestedDate = today,
                DaysAdded = additionalDays,
                OldDueDate = oldDueDate,
                NewDueDate = rental.DueDate,
                Fee = totalFee,
                DiscountApplied = discount,
                ExtensionNumber = history.Count + 1
            };
            AddExtensionRecord(rentalId, record);

            return new ExtensionResult
            {
                Rental = rental,
                Extension = record,
                CustomerName = customer?.Name ?? "Unknown",
                RemainingExtensions = MaxExtensionsPerRental - record.ExtensionNumber,
                WasFreeExtension = totalFee == 0m
            };
        }

        // ── Eligibility Check ────────────────────────────────────────

        /// <summary>
        /// Check whether a rental is eligible for extension without
        /// actually applying it.
        /// </summary>
        public ExtensionEligibility CheckEligibility(int rentalId)
        {
            var rental = _rentalRepo.GetById(rentalId);
            if (rental == null)
                return new ExtensionEligibility
                {
                    IsEligible = false,
                    Reason = "Rental not found."
                };

            if (rental.Status == RentalStatus.Returned)
                return new ExtensionEligibility
                {
                    IsEligible = false,
                    Reason = "Rental has already been returned."
                };

            var today = _clock.Today;
            if (today > rental.DueDate)
            {
                var daysPast = (int)Math.Ceiling((today - rental.DueDate).TotalDays);
                if (daysPast > MaxDaysPastDueForExtension)
                    return new ExtensionEligibility
                    {
                        IsEligible = false,
                        Reason = $"Rental is {daysPast} days overdue (max {MaxDaysPastDueForExtension})."
                    };
            }

            var history = GetExtensionHistory(rentalId);
            if (history.Count >= MaxExtensionsPerRental)
                return new ExtensionEligibility
                {
                    IsEligible = false,
                    Reason = $"Already extended {MaxExtensionsPerRental} times."
                };

            var reservations = _reservationRepo.GetActiveByMovie(rental.MovieId);
            if (reservations.Count > 0)
                return new ExtensionEligibility
                {
                    IsEligible = false,
                    Reason = $"{reservations.Count} reservation(s) queued for this movie."
                };

            var customer = _customerRepo.GetById(rental.CustomerId);
            var discount = GetMembershipDiscount(customer?.MembershipType ?? MembershipType.Basic);
            var feePerDay = Math.Round(rental.DailyRate * ExtensionRateMultiplier * (1m - discount), 2);

            return new ExtensionEligibility
            {
                IsEligible = true,
                RemainingExtensions = MaxExtensionsPerRental - history.Count,
                EstimatedFeePerDay = feePerDay,
                MembershipDiscount = discount,
                MaxDays = MaxExtensionDays,
                Reason = "Eligible for extension."
            };
        }

        // ── History ──────────────────────────────────────────────────

        /// <summary>
        /// Get the extension history for a rental.
        /// </summary>
        public IReadOnlyList<ExtensionRecord> GetExtensionHistory(int rentalId)
        {
            if (_extensionHistory.TryGetValue(rentalId, out var list))
                return list.AsReadOnly();
            return new List<ExtensionRecord>().AsReadOnly();
        }

        /// <summary>
        /// Get all extensions for a customer across all their rentals.
        /// </summary>
        public IReadOnlyList<ExtensionRecord> GetCustomerExtensions(int customerId)
        {
            var customerRentals = _rentalRepo.GetByCustomer(customerId);
            var customerRentalIds = new HashSet<int>(
                customerRentals.Select(r => r.Id));

            var result = new List<ExtensionRecord>();
            foreach (var kvp in _extensionHistory)
            {
                if (customerRentalIds.Contains(kvp.Key))
                    result.AddRange(kvp.Value);
            }
            return result.OrderByDescending(e => e.RequestedDate).ToList().AsReadOnly();
        }

        // ── Statistics ───────────────────────────────────────────────

        /// <summary>
        /// Get aggregate statistics about rental extensions.
        /// </summary>
        public ExtensionStats GetStats()
        {
            var allRecords = _extensionHistory.Values
                .SelectMany(list => list)
                .ToList();

            if (allRecords.Count == 0)
                return new ExtensionStats();

            var totalFees = allRecords.Sum(r => r.Fee);
            var freeCount = allRecords.Count(r => r.Fee == 0m);

            // Group by rental to get avg extensions per rental
            var byRental = allRecords.GroupBy(r => r.RentalId).ToList();

            return new ExtensionStats
            {
                TotalExtensions = allRecords.Count,
                UniqueRentalsExtended = byRental.Count,
                TotalFeesCollected = totalFees,
                AverageFeePerExtension = Math.Round(totalFees / allRecords.Count, 2),
                FreeExtensionCount = freeCount,
                AverageDaysAdded = Math.Round(allRecords.Average(r => r.DaysAdded), 1),
                MaxExtensionsOnSingleRental = byRental.Max(g => g.Count()),
                ExtensionsByDay = allRecords
                    .GroupBy(r => r.RequestedDate)
                    .OrderBy(g => g.Key)
                    .Select(g => new DailyExtensionCount
                    {
                        Date = g.Key,
                        Count = g.Count(),
                        Revenue = g.Sum(r => r.Fee)
                    })
                    .ToList()
            };
        }

        /// <summary>
        /// Generate a human-readable summary of a rental's extension history.
        /// </summary>
        public string GetExtensionSummary(int rentalId)
        {
            var rental = _rentalRepo.GetById(rentalId);
            if (rental == null) return "Rental not found.";

            var history = GetExtensionHistory(rentalId);
            if (history.Count == 0)
                return $"Rental #{rentalId} (\"{rental.MovieName}\") has no extensions.";

            var lines = new List<string>
            {
                $"Extension history for rental #{rentalId} (\"{rental.MovieName}\"):",
                $"  Original due date: {rental.DueDate.AddDays(-history.Sum(h => h.DaysAdded)).ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}",
                $"  Current due date:  {rental.DueDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}",
                $"  Total days added:  {history.Sum(h => h.DaysAdded)}",
                $"  Total fees:        ${history.Sum(h => h.Fee):F2}",
                ""
            };

            foreach (var ext in history)
            {
                var feeStr = ext.Fee == 0m ? "FREE" : $"${ext.Fee:F2}";
                var discountStr = ext.DiscountApplied > 0
                    ? $" ({ext.DiscountApplied:P0} membership discount)"
                    : "";
                lines.Add($"  #{ext.ExtensionNumber}: +{ext.DaysAdded} days " +
                    $"({ext.OldDueDate.ToString("MMM d", CultureInfo.InvariantCulture)} -> " +
                    $"{ext.NewDueDate.ToString("MMM d", CultureInfo.InvariantCulture)}) " +
                    $"— {feeStr}{discountStr}");
            }

            lines.Add($"\n  Remaining extensions: {MaxExtensionsPerRental - history.Count}");

            return string.Join(Environment.NewLine, lines);
        }

        // ── Private Helpers ──────────────────────────────────────────

        /// <summary>
        /// Get membership-tier discount on extension fees.
        /// Basic = 0%, Silver = 25%, Gold = 50%, Platinum = 100% (free).
        /// Delegates to <see cref="RentalPolicyConstants.TierExtensionDiscount"/>
        /// for a single source of truth.
        /// </summary>
        internal static decimal GetMembershipDiscount(MembershipType tier)
        {
            return RentalPolicyConstants.GetTierValue(
                RentalPolicyConstants.TierExtensionDiscount, tier, 0.00m);
        }

        private void AddExtensionRecord(int rentalId, ExtensionRecord record)
        {
            if (!_extensionHistory.ContainsKey(rentalId))
                _extensionHistory[rentalId] = new List<ExtensionRecord>();
            _extensionHistory[rentalId].Add(record);
        }
    }

    // ── Models ───────────────────────────────────────────────────────

    /// <summary>Record of a single extension applied to a rental.</summary>
    public class ExtensionRecord
    {
        public int RentalId { get; set; }
        public DateTime RequestedDate { get; set; }
        public int DaysAdded { get; set; }
        public DateTime OldDueDate { get; set; }
        public DateTime NewDueDate { get; set; }
        public decimal Fee { get; set; }
        public decimal DiscountApplied { get; set; }
        public int ExtensionNumber { get; set; }
    }

    /// <summary>Result of a successful extension request.</summary>
    public class ExtensionResult
    {
        public Rental Rental { get; set; }
        public ExtensionRecord Extension { get; set; }
        public string CustomerName { get; set; }
        public int RemainingExtensions { get; set; }
        public bool WasFreeExtension { get; set; }
    }

    /// <summary>Eligibility check result.</summary>
    public class ExtensionEligibility
    {
        public bool IsEligible { get; set; }
        public string Reason { get; set; }
        public int RemainingExtensions { get; set; }
        public decimal EstimatedFeePerDay { get; set; }
        public decimal MembershipDiscount { get; set; }
        public int MaxDays { get; set; }
    }

    /// <summary>Aggregate extension statistics.</summary>
    public class ExtensionStats
    {
        public int TotalExtensions { get; set; }
        public int UniqueRentalsExtended { get; set; }
        public decimal TotalFeesCollected { get; set; }
        public decimal AverageFeePerExtension { get; set; }
        public int FreeExtensionCount { get; set; }
        public double AverageDaysAdded { get; set; }
        public int MaxExtensionsOnSingleRental { get; set; }
        public List<DailyExtensionCount> ExtensionsByDay { get; set; }
            = new List<DailyExtensionCount>();
    }

    /// <summary>Extension count and revenue for a single day.</summary>
    public class DailyExtensionCount
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
        public decimal Revenue { get; set; }
    }
}
