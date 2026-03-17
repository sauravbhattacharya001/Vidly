using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Orchestrates the movie return process — the service-layer counterpart
    /// to <see cref="IRentalRepository.ReturnRental"/>.  While the repository
    /// handles persistence (mark returned, store late fee), this service
    /// coordinates the full business workflow: late-fee calculation with
    /// tier-based grace periods, condition assessment and damage charges,
    /// loyalty-point awards, inventory reconciliation, and overdue batch
    /// processing.
    ///
    /// <para>Designed to be the single entry point for returns so that
    /// controllers, API endpoints, and batch jobs all get consistent
    /// business rules.</para>
    /// </summary>
    public class RentalReturnService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;

        // ── Late-fee policy constants ────────────────────────────────
        // Delegated to RentalPolicyConstants for single source of truth.

        /// <summary>Per-day late fee (before tier discount).</summary>
        public const decimal BaseLateFeePerDay = RentalPolicyConstants.LateFeePerDay;

        /// <summary>Maximum late fee on any single rental.</summary>
        public const decimal MaxLateFeeCap = RentalPolicyConstants.MaxLateFeeCap;

        /// <summary>Grace period in days before late fees kick in.</summary>
        public const int BaseGracePeriodDays = 1;

        // ── Damage / condition charges ───────────────────────────────

        /// <summary>Charge for minor cosmetic damage (scratches, etc.).</summary>
        public const decimal MinorDamageCharge = 5.00m;

        /// <summary>Charge for moderate damage (skipping, case broken).</summary>
        public const decimal ModerateDamageCharge = 15.00m;

        /// <summary>Charge for severe damage or lost media — full replacement.</summary>
        public const decimal SevereDamageCharge = 29.99m;

        /// <summary>Number of recent damage incidents that trigger a warning.</summary>
        public const int DamageWarningThreshold = 3;

        // ── Loyalty constants ────────────────────────────────────────
        // OnTimeReturnBonus delegated to RentalPolicyConstants.

        /// <summary>Bonus loyalty points for an on-time or early return.</summary>
        public const int OnTimeReturnBonus = RentalPolicyConstants.OnTimeReturnBonus;

        /// <summary>Bonus points for returning in perfect condition.</summary>
        public const int PerfectConditionBonus = 10;

        /// <summary>Point penalty for a very late return (> 14 days overdue).</summary>
        public const int VeryLateReturnPenalty = 50;

        /// <summary>Days overdue before the penalty kicks in.</summary>
        public const int VeryLateDaysThreshold = 14;

        private readonly IClock _clock;

        public RentalReturnService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IClock clock = null)
        {
            _clock = clock ?? new SystemClock();
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        // ── Core return processing ───────────────────────────────────

        /// <summary>
        /// Process the return of a single rental.  Calculates late fees
        /// (with tier-based grace periods), assesses condition charges,
        /// awards or docks loyalty points, and marks the rental returned.
        /// </summary>
        /// <param name="rentalId">The rental to return.</param>
        /// <param name="condition">Physical condition on return.</param>
        /// <param name="returnDate">
        /// Date of return.  Defaults to today when null.
        /// </param>
        /// <returns>A detailed receipt of the return transaction.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the rental does not exist, is already returned, or
        /// the return date precedes the rental date.
        /// </exception>
        public ReturnReceipt ProcessReturn(
            int rentalId,
            ReturnCondition condition = ReturnCondition.Good,
            DateTime? returnDate = null)
        {
            var rental = _rentalRepository.GetById(rentalId);
            if (rental == null)
                throw new InvalidOperationException(
                    $"Rental {rentalId} not found.");
            if (rental.Status == RentalStatus.Returned)
                throw new InvalidOperationException(
                    $"Rental {rentalId} is already returned.");

            var actualReturnDate = returnDate ?? _clock.Today;
            if (actualReturnDate < rental.RentalDate)
                throw new InvalidOperationException(
                    "Return date cannot precede the rental date.");

            var customer = _customerRepository.GetById(rental.CustomerId);
            var membershipType = customer?.MembershipType ?? MembershipType.Basic;

            // 1. Late-fee calculation
            var ReturnLateFeeBreakdown = CalculateLateFee(
                rental.DueDate, actualReturnDate, rental.DailyRate, membershipType);

            // 2. Condition / damage charges
            var damageCharge = GetDamageCharge(condition);

            // 3. Total charges
            var rentalDays = Math.Max(1,
                (int)Math.Ceiling((actualReturnDate - rental.RentalDate).TotalDays));
            var baseCost = rentalDays * rental.DailyRate;
            var totalCost = baseCost + ReturnLateFeeBreakdown.LateFee + damageCharge;

            // 4. Loyalty points
            var loyaltyPoints = CalculateLoyaltyPoints(
                ReturnLateFeeBreakdown, condition, membershipType);

            // 5. Mark returned in repository
            rental.ReturnDate = actualReturnDate;
            rental.LateFee = ReturnLateFeeBreakdown.LateFee + damageCharge;
            rental.Status = RentalStatus.Returned;
            _rentalRepository.Update(rental);

            // 6. Build receipt
            return new ReturnReceipt
            {
                RentalId = rentalId,
                CustomerId = rental.CustomerId,
                CustomerName = rental.CustomerName
                    ?? customer?.Name ?? "Unknown",
                MovieId = rental.MovieId,
                MovieName = rental.MovieName ?? "Unknown",
                RentalDate = rental.RentalDate,
                DueDate = rental.DueDate,
                ReturnDate = actualReturnDate,
                DaysRented = rentalDays,
                DaysOverdue = ReturnLateFeeBreakdown.DaysOverdue,
                DailyRate = rental.DailyRate,
                BaseCost = baseCost,
                LateFee = ReturnLateFeeBreakdown.LateFee,
                GracePeriodDays = ReturnLateFeeBreakdown.GracePeriodDays,
                DamageCharge = damageCharge,
                Condition = condition,
                TotalCost = totalCost,
                LoyaltyPointsEarned = loyaltyPoints,
                ReturnedOnTime = ReturnLateFeeBreakdown.DaysOverdue <= 0,
                MembershipTier = membershipType,
                WaivedDays = ReturnLateFeeBreakdown.WaivedDays
            };
        }

        /// <summary>
        /// Process returns for multiple rentals at once (e.g. customer
        /// returning a stack of movies).
        /// </summary>
        /// <returns>Batch result with individual receipts and totals.</returns>
        public BatchReturnResult ProcessBatchReturn(
            IEnumerable<int> rentalIds,
            ReturnCondition condition = ReturnCondition.Good,
            DateTime? returnDate = null)
        {
            if (rentalIds == null)
                throw new ArgumentNullException(nameof(rentalIds));

            var ids = rentalIds.ToList();
            if (ids.Count == 0)
                throw new ArgumentException(
                    "At least one rental ID is required.", nameof(rentalIds));

            var receipts = new List<ReturnReceipt>();
            var errors = new Dictionary<int, string>();

            foreach (var id in ids)
            {
                try
                {
                    receipts.Add(ProcessReturn(id, condition, returnDate));
                }
                catch (InvalidOperationException ex)
                {
                    errors[id] = ex.Message;
                }
            }

            return new BatchReturnResult
            {
                Receipts = receipts,
                Errors = errors,
                TotalReturned = receipts.Count,
                TotalFailed = errors.Count,
                TotalBaseCost = receipts.Sum(r => r.BaseCost),
                TotalLateFees = receipts.Sum(r => r.LateFee),
                TotalDamageCharges = receipts.Sum(r => r.DamageCharge),
                GrandTotal = receipts.Sum(r => r.TotalCost),
                TotalLoyaltyPoints = receipts.Sum(r => r.LoyaltyPointsEarned)
            };
        }

        // ── Late-fee calculation ─────────────────────────────────────

        /// <summary>
        /// Calculate the late fee for a return, factoring in tier-based
        /// grace periods and fee caps.
        /// </summary>
        public ReturnLateFeeBreakdown CalculateLateFee(
            DateTime dueDate, DateTime returnDate,
            decimal dailyRate, MembershipType tier)
        {
            var gracePeriod = GetGracePeriod(tier);
            var daysOverdue = (int)Math.Ceiling(
                Math.Max(0, (returnDate - dueDate).TotalDays));

            if (daysOverdue <= 0)
            {
                return new ReturnLateFeeBreakdown
                {
                    DaysOverdue = 0,
                    GracePeriodDays = gracePeriod,
                    WaivedDays = 0,
                    LateFee = 0m,
                    FeeCapped = false
                };
            }

            var waivedDays = Math.Min(daysOverdue, gracePeriod);
            var chargeableDays = Math.Max(0, daysOverdue - gracePeriod);
            var fee = chargeableDays * BaseLateFeePerDay;

            // Apply tier discount
            var discount = GetTierLateDiscount(tier);
            fee *= (1m - discount);

            // Apply cap
            var capped = false;
            if (fee > MaxLateFeeCap)
            {
                fee = MaxLateFeeCap;
                capped = true;
            }

            return new ReturnLateFeeBreakdown
            {
                DaysOverdue = daysOverdue,
                GracePeriodDays = gracePeriod,
                WaivedDays = waivedDays,
                LateFee = Math.Round(fee, 2),
                FeeCapped = capped
            };
        }

        /// <summary>
        /// Grace period in days based on membership tier.
        /// Higher tiers get more lenient return windows.
        /// Delegates to <see cref="RentalPolicyConstants.TierExtraGraceDays"/>
        /// for a single source of truth.
        /// </summary>
        public static int GetGracePeriod(MembershipType tier)
        {
            return BaseGracePeriodDays
                + RentalPolicyConstants.GetTierValue(
                    RentalPolicyConstants.TierExtraGraceDays, tier, 0);
        }

        /// <summary>
        /// Late-fee discount percentage based on membership tier.
        /// Delegates to <see cref="RentalPolicyConstants.TierLateFeeDiscount"/>
        /// for a single source of truth.
        /// </summary>
        public static decimal GetTierLateDiscount(MembershipType tier)
        {
            return RentalPolicyConstants.GetTierValue(
                RentalPolicyConstants.TierLateFeeDiscount, tier, 0m);
        }

        // ── Condition / damage ───────────────────────────────────────

        /// <summary>
        /// Get the damage surcharge for the return condition.
        /// </summary>
        public static decimal GetDamageCharge(ReturnCondition condition)
        {
            switch (condition)
            {
                case ReturnCondition.Good:
                case ReturnCondition.Fair:
                    return 0m;
                case ReturnCondition.MinorDamage:
                    return MinorDamageCharge;
                case ReturnCondition.ModerateDamage:
                    return ModerateDamageCharge;
                case ReturnCondition.SevereDamage:
                    return SevereDamageCharge;
                default:
                    return 0m;
            }
        }

        // ── Loyalty points ───────────────────────────────────────────

        /// <summary>
        /// Calculate loyalty points earned (or docked) for this return.
        /// </summary>
        public static int CalculateLoyaltyPoints(
            ReturnLateFeeBreakdown lateFee, ReturnCondition condition,
            MembershipType tier)
        {
            var points = 0;

            // On-time bonus
            if (lateFee.DaysOverdue <= 0)
                points += OnTimeReturnBonus;

            // Perfect condition bonus
            if (condition == ReturnCondition.Good)
                points += PerfectConditionBonus;

            // Very late penalty
            if (lateFee.DaysOverdue > VeryLateDaysThreshold)
                points -= VeryLateReturnPenalty;

            // Tier multiplier on positive points
            if (points > 0)
            {
                var multiplier = GetTierPointMultiplier(tier);
                points = (int)Math.Round(points * multiplier);
            }

            return points;
        }

        /// <summary>
        /// Points multiplier based on membership tier.
        /// Delegates to <see cref="LoyaltyPointsService.GetTierMultiplier"/>
        /// for a single source of truth.
        /// </summary>
        public static decimal GetTierPointMultiplier(MembershipType tier)
        {
            return LoyaltyPointsService.GetTierMultiplier(tier);
        }

        // ── Overdue management ───────────────────────────────────────

        /// <summary>
        /// Get all currently overdue rentals with projected late fees
        /// and recommended actions.
        /// </summary>
        public List<OverdueRentalInfo> GetOverdueRentals()
        {
            var overdueRentals = _rentalRepository.GetOverdue();
            var result = new List<OverdueRentalInfo>();

            foreach (var rental in overdueRentals)
            {
                var customer = _customerRepository.GetById(rental.CustomerId);
                var tier = customer?.MembershipType ?? MembershipType.Basic;
                var projected = CalculateLateFee(
                    rental.DueDate, _clock.Today, rental.DailyRate, tier);

                var daysOverdue = projected.DaysOverdue;
                OverdueAction action;
                if (daysOverdue <= 3)
                    action = OverdueAction.ReminderNotice;
                else if (daysOverdue <= 7)
                    action = OverdueAction.LateFeeWarning;
                else if (daysOverdue <= 14)
                    action = OverdueAction.FinalNotice;
                else
                    action = OverdueAction.AccountHold;

                result.Add(new OverdueRentalInfo
                {
                    Rental = rental,
                    CustomerName = customer?.Name ?? "Unknown",
                    MembershipTier = tier,
                    DaysOverdue = daysOverdue,
                    ProjectedLateFee = projected.LateFee,
                    GracePeriodDays = projected.GracePeriodDays,
                    WaivedDays = projected.WaivedDays,
                    FeeCapped = projected.FeeCapped,
                    RecommendedAction = action
                });
            }

            return result.OrderByDescending(r => r.DaysOverdue).ToList();
        }

        /// <summary>
        /// Get overdue summary statistics for the dashboard.
        /// </summary>
        public OverdueSummary GetOverdueSummary()
        {
            var overdueInfo = GetOverdueRentals();

            if (overdueInfo.Count == 0)
            {
                return new OverdueSummary
                {
                    TotalOverdue = 0,
                    TotalProjectedFees = 0m,
                    AverageDaysOverdue = 0,
                    MaxDaysOverdue = 0,
                    ByAction = new Dictionary<OverdueAction, int>(),
                    ByTier = new Dictionary<MembershipType, int>()
                };
            }

            return new OverdueSummary
            {
                TotalOverdue = overdueInfo.Count,
                TotalProjectedFees = overdueInfo.Sum(r => r.ProjectedLateFee),
                AverageDaysOverdue = (int)Math.Round(
                    overdueInfo.Average(r => r.DaysOverdue)),
                MaxDaysOverdue = overdueInfo.Max(r => r.DaysOverdue),
                ByAction = overdueInfo
                    .GroupBy(r => r.RecommendedAction)
                    .ToDictionary(g => g.Key, g => g.Count()),
                ByTier = overdueInfo
                    .GroupBy(r => r.MembershipTier)
                    .ToDictionary(g => g.Key, g => g.Count())
            };
        }

        /// <summary>
        /// Check a customer's return history for damage patterns.
        /// </summary>
        public CustomerReturnProfile GetCustomerReturnProfile(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new InvalidOperationException(
                    $"Customer {customerId} not found.");

            var allRentals = _rentalRepository.GetByCustomer(customerId);
            var returned = allRentals.Where(r => r.Status == RentalStatus.Returned).ToList();
            var active = allRentals.Where(r => r.Status == RentalStatus.Active).ToList();
            var overdue = allRentals.Where(r => r.Status == RentalStatus.Overdue).ToList();

            var totalReturned = returned.Count;
            var onTimeReturns = returned.Count(r =>
                r.ReturnDate.HasValue && r.ReturnDate.Value <= r.DueDate);
            var lateReturns = totalReturned - onTimeReturns;
            var totalLateFees = returned.Sum(r => r.LateFee);

            var onTimeRate = totalReturned > 0
                ? (decimal)onTimeReturns / totalReturned * 100m : 100m;

            return new CustomerReturnProfile
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                MembershipTier = customer.MembershipType,
                TotalReturned = totalReturned,
                ActiveRentals = active.Count,
                OverdueRentals = overdue.Count,
                OnTimeReturns = onTimeReturns,
                LateReturns = lateReturns,
                OnTimeRate = Math.Round(onTimeRate, 1),
                TotalLateFeesPaid = totalLateFees,
                Reliability = onTimeRate >= 90 ? CustomerReliability.Excellent
                    : onTimeRate >= 75 ? CustomerReliability.Good
                    : onTimeRate >= 50 ? CustomerReliability.Average
                    : CustomerReliability.Poor
            };
        }

        /// <summary>
        /// Estimate the late fee a customer would pay if they returned today.
        /// Useful for self-service "what would I owe?" lookups.
        /// </summary>
        public LateFeeEstimate EstimateCurrentLateFee(int rentalId)
        {
            var rental = _rentalRepository.GetById(rentalId);
            if (rental == null)
                throw new InvalidOperationException(
                    $"Rental {rentalId} not found.");
            if (rental.Status == RentalStatus.Returned)
                throw new InvalidOperationException(
                    $"Rental {rentalId} is already returned.");

            var customer = _customerRepository.GetById(rental.CustomerId);
            var tier = customer?.MembershipType ?? MembershipType.Basic;

            var result = CalculateLateFee(
                rental.DueDate, _clock.Today, rental.DailyRate, tier);

            return new LateFeeEstimate
            {
                RentalId = rentalId,
                MovieName = rental.MovieName ?? "Unknown",
                DueDate = rental.DueDate,
                AsOfDate = _clock.Today,
                DaysOverdue = result.DaysOverdue,
                GracePeriodDays = result.GracePeriodDays,
                EstimatedFee = result.LateFee,
                FeeCapped = result.FeeCapped,
                MembershipDiscount = GetTierLateDiscount(tier) * 100m,
                Message = result.DaysOverdue <= 0
                    ? "No late fee — you're within the due date!"
                    : result.DaysOverdue <= result.GracePeriodDays
                        ? $"Within grace period ({result.GracePeriodDays} days). No fee yet!"
                        : $"Late fee: ${result.LateFee:F2}"
                            + (result.FeeCapped ? " (capped)" : "")
            };
        }
    }

    // ── Supporting types ─────────────────────────────────────────

    /// <summary>Condition of the returned media.</summary>
    public enum ReturnCondition
    {
        Good = 1,
        Fair = 2,
        MinorDamage = 3,
        ModerateDamage = 4,
        SevereDamage = 5
    }

    /// <summary>Recommended action for an overdue rental.</summary>
    public enum OverdueAction
    {
        ReminderNotice = 1,
        LateFeeWarning = 2,
        FinalNotice = 3,
        AccountHold = 4
    }

    /// <summary>Customer reliability rating based on return history.</summary>
    public enum CustomerReliability
    {
        Excellent,
        Good,
        Average,
        Poor
    }

    /// <summary>Detailed breakdown of a late-fee calculation.</summary>
    public class ReturnLateFeeBreakdown
    {
        public int DaysOverdue { get; set; }
        public int GracePeriodDays { get; set; }
        public int WaivedDays { get; set; }
        public decimal LateFee { get; set; }
        public bool FeeCapped { get; set; }
    }

    /// <summary>Receipt produced by <see cref="RentalReturnService.ProcessReturn"/>.</summary>
    public class ReturnReceipt
    {
        public int RentalId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public DateTime RentalDate { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime ReturnDate { get; set; }
        public int DaysRented { get; set; }
        public int DaysOverdue { get; set; }
        public decimal DailyRate { get; set; }
        public decimal BaseCost { get; set; }
        public decimal LateFee { get; set; }
        public int GracePeriodDays { get; set; }
        public decimal DamageCharge { get; set; }
        public ReturnCondition Condition { get; set; }
        public decimal TotalCost { get; set; }
        public int LoyaltyPointsEarned { get; set; }
        public bool ReturnedOnTime { get; set; }
        public MembershipType MembershipTier { get; set; }
        public int WaivedDays { get; set; }
    }

    /// <summary>Result of a batch return operation.</summary>
    public class BatchReturnResult
    {
        public List<ReturnReceipt> Receipts { get; set; } = new List<ReturnReceipt>();
        public Dictionary<int, string> Errors { get; set; } = new Dictionary<int, string>();
        public int TotalReturned { get; set; }
        public int TotalFailed { get; set; }
        public decimal TotalBaseCost { get; set; }
        public decimal TotalLateFees { get; set; }
        public decimal TotalDamageCharges { get; set; }
        public decimal GrandTotal { get; set; }
        public int TotalLoyaltyPoints { get; set; }
    }

    /// <summary>Information about an overdue rental with recommended action.</summary>
    public class OverdueRentalInfo
    {
        public Rental Rental { get; set; }
        public string CustomerName { get; set; }
        public MembershipType MembershipTier { get; set; }
        public int DaysOverdue { get; set; }
        public decimal ProjectedLateFee { get; set; }
        public int GracePeriodDays { get; set; }
        public int WaivedDays { get; set; }
        public bool FeeCapped { get; set; }
        public OverdueAction RecommendedAction { get; set; }
    }

    /// <summary>Dashboard summary of overdue rentals.</summary>
    public class OverdueSummary
    {
        public int TotalOverdue { get; set; }
        public decimal TotalProjectedFees { get; set; }
        public int AverageDaysOverdue { get; set; }
        public int MaxDaysOverdue { get; set; }
        public Dictionary<OverdueAction, int> ByAction { get; set; }
            = new Dictionary<OverdueAction, int>();
        public Dictionary<MembershipType, int> ByTier { get; set; }
            = new Dictionary<MembershipType, int>();
    }

    /// <summary>Customer's return track record.</summary>
    public class CustomerReturnProfile
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public MembershipType MembershipTier { get; set; }
        public int TotalReturned { get; set; }
        public int ActiveRentals { get; set; }
        public int OverdueRentals { get; set; }
        public int OnTimeReturns { get; set; }
        public int LateReturns { get; set; }
        public decimal OnTimeRate { get; set; }
        public decimal TotalLateFeesPaid { get; set; }
        public CustomerReliability Reliability { get; set; }
    }

    /// <summary>Self-service late-fee estimate for an active rental.</summary>
    public class LateFeeEstimate
    {
        public int RentalId { get; set; }
        public string MovieName { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime AsOfDate { get; set; }
        public int DaysOverdue { get; set; }
        public int GracePeriodDays { get; set; }
        public decimal EstimatedFee { get; set; }
        public bool FeeCapped { get; set; }
        public decimal MembershipDiscount { get; set; }
        public string Message { get; set; }
    }
}

