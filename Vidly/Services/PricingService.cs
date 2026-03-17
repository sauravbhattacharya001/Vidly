using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Calculates rental pricing with membership discounts, late fee policies
    /// (grace periods, fee caps), and promotional deals. Each membership tier
    /// unlocks better pricing, and the service enforces transparent, testable
    /// billing logic.
    /// </summary>
    public class PricingService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly IClock _clock;

        /// <summary>
        /// Base daily rental rate when no movie-specific rate is set.
        /// </summary>
        public const decimal DefaultDailyRate = 3.99m;

        /// <summary>
        /// Premium daily rate for new releases (within 90 days of release).
        /// </summary>
        public const decimal NewReleaseDailyRate = 5.99m;

        /// <summary>
        /// Discounted daily rate for catalog titles older than 1 year.
        /// </summary>
        public const decimal CatalogDailyRate = 2.99m;

        /// <summary>
        /// Default rental period in days.
        /// </summary>
        public const int DefaultRentalDays = 7;

        /// <summary>
        /// Maximum late fee that can be charged on a single rental.
        /// Delegates to <see cref="RentalPolicyConstants"/> for single source of truth.
        /// </summary>
        public const decimal MaxLateFeeCap = RentalPolicyConstants.MaxLateFeeCap;

        /// <summary>
        /// Per-day late fee rate applied after the grace period.
        /// Delegates to <see cref="RentalPolicyConstants"/> for single source of truth.
        /// </summary>
        public const decimal LateFeePerDay = RentalPolicyConstants.LateFeePerDay;

        public PricingService(
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IRentalRepository rentalRepository,
            IClock clock)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _clock = clock
                ?? throw new ArgumentNullException(nameof(clock));
        }

        // ── Membership benefits ──────────────────────────────────────

        /// <summary>
        /// Get the full benefits breakdown for a membership tier.
        /// </summary>
        public static MembershipBenefits GetBenefits(MembershipType tier)
        {
            switch (tier)
            {
                case MembershipType.Basic:
                    return new MembershipBenefits
                    {
                        Tier = MembershipType.Basic,
                        DiscountPercent = 0,
                        GracePeriodDays = RentalReturnService.GetGracePeriod(MembershipType.Basic),
                        MaxConcurrentRentals = 2,
                        FreeRentalsPerMonth = 0,
                        LateFeeDiscount = 0,
                        ExtendedRentalDays = 0,
                        Description = "Standard pricing with no perks."
                    };
                case MembershipType.Silver:
                    return new MembershipBenefits
                    {
                        Tier = MembershipType.Silver,
                        DiscountPercent = 10,
                        GracePeriodDays = RentalReturnService.GetGracePeriod(MembershipType.Silver),
                        MaxConcurrentRentals = 3,
                        FreeRentalsPerMonth = 0,
                        LateFeeDiscount = 10,
                        ExtendedRentalDays = 1,
                        Description = "10% off daily rate, 2-day grace period, 8-day rentals."
                    };
                case MembershipType.Gold:
                    return new MembershipBenefits
                    {
                        Tier = MembershipType.Gold,
                        DiscountPercent = 20,
                        GracePeriodDays = RentalReturnService.GetGracePeriod(MembershipType.Gold),
                        MaxConcurrentRentals = 5,
                        FreeRentalsPerMonth = 1,
                        LateFeeDiscount = 25,
                        ExtendedRentalDays = 2,
                        Description = "20% off, 3-day grace, 1 free rental/month, 25% late fee reduction."
                    };
                case MembershipType.Platinum:
                    return new MembershipBenefits
                    {
                        Tier = MembershipType.Platinum,
                        DiscountPercent = 30,
                        GracePeriodDays = RentalReturnService.GetGracePeriod(MembershipType.Platinum),
                        MaxConcurrentRentals = 10,
                        FreeRentalsPerMonth = 3,
                        LateFeeDiscount = 50,
                        ExtendedRentalDays = 3,
                        Description = "30% off, 5-day grace, 3 free rentals/month, 50% late fee reduction."
                    };
                default:
                    return GetBenefits(MembershipType.Basic);
            }
        }

        // ── Rental pricing ───────────────────────────────────────────

        /// <summary>
        /// Calculate the rental price for a movie given a customer's membership.
        /// Returns a detailed price breakdown.
        /// </summary>
        public RentalPriceQuote CalculateRentalPrice(
            int movieId, int customerId, int? rentalDays = null)
        {
            var movie = _movieRepository.GetById(movieId);
            var customer = _customerRepository.GetById(customerId);

            if (movie == null)
                throw new ArgumentException($"Movie {movieId} not found.");
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            var benefits = GetBenefits(customer.MembershipType);
            var baseDailyRate = GetMovieDailyRate(movie);
            var days = rentalDays ?? (DefaultRentalDays + benefits.ExtendedRentalDays);

            // Apply membership discount
            var discountAmount = baseDailyRate * benefits.DiscountPercent / 100m;
            var discountedRate = baseDailyRate - discountAmount;

            // Check for free rental eligibility
            var freeRentalApplied = false;
            if (benefits.FreeRentalsPerMonth > 0)
            {
                var usedThisMonth = CountRentalsThisMonth(customerId);
                if (usedThisMonth < benefits.FreeRentalsPerMonth)
                {
                    freeRentalApplied = true;
                }
            }

            var totalBeforeFree = discountedRate * days;
            var finalTotal = freeRentalApplied ? 0m : totalBeforeFree;

            return new RentalPriceQuote
            {
                MovieName = movie.Name,
                CustomerName = customer.Name,
                MembershipTier = customer.MembershipType,
                BaseDailyRate = baseDailyRate,
                DiscountPercent = benefits.DiscountPercent,
                DiscountedDailyRate = discountedRate,
                RentalDays = days,
                DueDate = _clock.Today.AddDays(days),
                SubTotal = totalBeforeFree,
                FreeRentalApplied = freeRentalApplied,
                FinalTotal = finalTotal,
                GracePeriodDays = benefits.GracePeriodDays,
                Savings = (baseDailyRate * days) - finalTotal
            };
        }

        // ── Late fee calculation ─────────────────────────────────────

        /// <summary>
        /// Calculate the late fee for a rental, applying membership benefits.
        /// </summary>
        public LateFeeResult CalculateLateFee(Rental rental)
        {
            if (rental == null)
                throw new ArgumentNullException(nameof(rental));

            var customer = _customerRepository.GetById(rental.CustomerId);
            var benefits = customer != null
                ? GetBenefits(customer.MembershipType)
                : GetBenefits(MembershipType.Basic);

            var endDate = rental.ReturnDate ?? _clock.Today;
            var rawDaysLate = (int)Math.Ceiling((endDate - rental.DueDate).TotalDays);

            // Apply grace period
            var effectiveDaysLate = Math.Max(0, rawDaysLate - benefits.GracePeriodDays);

            if (effectiveDaysLate == 0)
            {
                return new LateFeeResult
                {
                    RentalId = rental.Id,
                    RawDaysLate = Math.Max(0, rawDaysLate),
                    GracePeriodDays = benefits.GracePeriodDays,
                    EffectiveDaysLate = 0,
                    BaseFee = 0m,
                    LateFeeDiscount = 0m,
                    FinalFee = 0m,
                    WasFeeWaived = rawDaysLate > 0,
                    Explanation = rawDaysLate > 0
                        ? $"Returned {rawDaysLate} day(s) late but within {benefits.GracePeriodDays}-day grace period."
                        : "Returned on time."
                };
            }

            var baseFee = effectiveDaysLate * LateFeePerDay;
            var cappedFee = Math.Min(baseFee, MaxLateFeeCap);
            var discountAmount = cappedFee * benefits.LateFeeDiscount / 100m;
            var finalFee = Math.Round(cappedFee - discountAmount, 2);

            var explanation = $"{rawDaysLate} day(s) late";
            if (benefits.GracePeriodDays > 0)
                explanation += $" minus {benefits.GracePeriodDays}-day grace = {effectiveDaysLate} chargeable day(s)";
            if (baseFee > MaxLateFeeCap)
                explanation += $", capped at ${MaxLateFeeCap:F2}";
            if (benefits.LateFeeDiscount > 0)
                explanation += $", {benefits.LateFeeDiscount}% membership discount applied";
            explanation += ".";

            return new LateFeeResult
            {
                RentalId = rental.Id,
                RawDaysLate = rawDaysLate,
                GracePeriodDays = benefits.GracePeriodDays,
                EffectiveDaysLate = effectiveDaysLate,
                BaseFee = cappedFee,
                LateFeeDiscount = discountAmount,
                FinalFee = finalFee,
                WasFeeWaived = false,
                Explanation = explanation
            };
        }

        // ── Membership comparison ────────────────────────────────────

        /// <summary>
        /// Compare what a customer would pay for a movie across all membership tiers.
        /// Useful for upsell prompts ("Upgrade to Gold and save $X!").
        /// </summary>
        public List<PricingTierComparison> CompareTiers(int movieId, int? rentalDays = null)
        {
            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                throw new ArgumentException($"Movie {movieId} not found.");

            var baseDailyRate = GetMovieDailyRate(movie);
            var comparisons = new List<PricingTierComparison>();

            foreach (MembershipType tier in Enum.GetValues(typeof(MembershipType)))
            {
                var benefits = GetBenefits(tier);
                var days = rentalDays ?? (DefaultRentalDays + benefits.ExtendedRentalDays);
                var discountedRate = baseDailyRate * (1m - benefits.DiscountPercent / 100m);
                var total = discountedRate * days;
                var baseCost = baseDailyRate * DefaultRentalDays;

                comparisons.Add(new PricingTierComparison
                {
                    Tier = tier,
                    DailyRate = discountedRate,
                    RentalDays = days,
                    TotalCost = total,
                    Savings = baseCost - total,
                    GracePeriodDays = benefits.GracePeriodDays,
                    FreeRentalsPerMonth = benefits.FreeRentalsPerMonth,
                    LateFeeReduction = benefits.LateFeeDiscount
                });
            }

            return comparisons;
        }

        // ── Customer billing summary ─────────────────────────────────

        /// <summary>
        /// Get a billing summary for a customer showing all active/overdue rentals
        /// with projected late fees.
        /// </summary>
        public CustomerBillingSummary GetBillingSummary(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            // Filter directly from GetAll() — allRentals was only used to
            // derive customerRentals, so the intermediate variable was unnecessary.
            var customerRentals = _rentalRepository.GetAll()
                .Where(r => r.CustomerId == customerId)
                .ToList();

            var benefits = GetBenefits(customer.MembershipType);
            var activeRentals = customerRentals
                .Where(r => r.Status != RentalStatus.Returned).ToList();
            var overdueRentals = activeRentals.Where(r => r.IsOverdue).ToList();

            var totalProjectedLateFees = 0m;
            var rentalDetails = new List<RentalBillingDetail>();

            foreach (var rental in activeRentals)
            {
                var lateFee = CalculateLateFee(rental);
                totalProjectedLateFees += lateFee.FinalFee;

                rentalDetails.Add(new RentalBillingDetail
                {
                    RentalId = rental.Id,
                    MovieName = rental.MovieName,
                    RentalDate = rental.RentalDate,
                    DueDate = rental.DueDate,
                    DailyRate = rental.DailyRate,
                    IsOverdue = rental.IsOverdue,
                    DaysOverdue = rental.DaysOverdue,
                    ProjectedLateFee = lateFee.FinalFee,
                    LateFeeExplanation = lateFee.Explanation
                });
            }

            // Count total spent (returned rentals)
            var totalSpent = customerRentals
                .Where(r => r.Status == RentalStatus.Returned)
                .Sum(r => r.TotalCost);

            // Cache the count — previously called CountRentalsThisMonth() twice,
            // each triggering a full GetAll() + LINQ scan.
            var freeRentalsUsed = CountRentalsThisMonth(customerId);

            return new CustomerBillingSummary
            {
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                MembershipTier = customer.MembershipType,
                Benefits = benefits,
                ActiveRentalCount = activeRentals.Count,
                OverdueRentalCount = overdueRentals.Count,
                MaxConcurrentRentals = benefits.MaxConcurrentRentals,
                CanRentMore = activeRentals.Count < benefits.MaxConcurrentRentals,
                RemainingSlots = Math.Max(0, benefits.MaxConcurrentRentals - activeRentals.Count),
                TotalProjectedLateFees = totalProjectedLateFees,
                LifetimeSpend = totalSpent,
                Rentals = rentalDetails,
                FreeRentalsUsedThisMonth = freeRentalsUsed,
                FreeRentalsRemaining = Math.Max(0,
                    benefits.FreeRentalsPerMonth - freeRentalsUsed)
            };
        }

        // ── Helpers ──────────────────────────────────────────────────

        private int CountRentalsThisMonth(int customerId)
        {
            var firstOfMonth = new DateTime(_clock.Today.Year, _clock.Today.Month, 1);
            return _rentalRepository.GetAll()
                .Count(r => r.CustomerId == customerId
                    && r.RentalDate >= firstOfMonth);
        }

        /// <summary>
        /// Determine the daily rate for a movie based on its properties.
        /// Priority: explicit DailyRate override > new release premium > catalog discount > default.
        /// </summary>
        internal static decimal GetMovieDailyRate(Movie movie)
        {
            // 1. Explicit per-movie rate override takes highest priority
            if (movie.DailyRate.HasValue)
                return movie.DailyRate.Value;

            // 2. New releases (within 90 days) get premium pricing
            if (movie.IsNewRelease)
                return NewReleaseDailyRate;

            // 3. Catalog titles (older than 1 year) get a discount
            if (movie.ReleaseDate.HasValue &&
                (_clock.Today - movie.ReleaseDate.Value).TotalDays > 365)
                return CatalogDailyRate;

            // 4. Everything else uses the default rate
            return DefaultDailyRate;
        }
    }

    // ── Models ───────────────────────────────────────────────────────

    /// <summary>
    /// Benefits associated with a membership tier.
    /// </summary>
    public class MembershipBenefits
    {
        public MembershipType Tier { get; set; }
        public int DiscountPercent { get; set; }
        public int GracePeriodDays { get; set; }
        public int MaxConcurrentRentals { get; set; }
        public int FreeRentalsPerMonth { get; set; }
        public int LateFeeDiscount { get; set; }
        public int ExtendedRentalDays { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Detailed rental price quote.
    /// </summary>
    public class RentalPriceQuote
    {
        public string MovieName { get; set; }
        public string CustomerName { get; set; }
        public MembershipType MembershipTier { get; set; }
        public decimal BaseDailyRate { get; set; }
        public int DiscountPercent { get; set; }
        public decimal DiscountedDailyRate { get; set; }
        public int RentalDays { get; set; }
        public DateTime DueDate { get; set; }
        public decimal SubTotal { get; set; }
        public bool FreeRentalApplied { get; set; }
        public decimal FinalTotal { get; set; }
        public int GracePeriodDays { get; set; }
        public decimal Savings { get; set; }
    }

    /// <summary>
    /// Detailed late fee calculation result.
    /// </summary>
    public class LateFeeResult
    {
        public int RentalId { get; set; }
        public int RawDaysLate { get; set; }
        public int GracePeriodDays { get; set; }
        public int EffectiveDaysLate { get; set; }
        public decimal BaseFee { get; set; }
        public decimal LateFeeDiscount { get; set; }
        public decimal FinalFee { get; set; }
        public bool WasFeeWaived { get; set; }
        public string Explanation { get; set; }
    }

    /// <summary>
    /// Cost comparison across membership tiers.
    /// </summary>
    public class PricingTierComparison
    {
        public MembershipType Tier { get; set; }
        public decimal DailyRate { get; set; }
        public int RentalDays { get; set; }
        public decimal TotalCost { get; set; }
        public decimal Savings { get; set; }
        public int GracePeriodDays { get; set; }
        public int FreeRentalsPerMonth { get; set; }
        public int LateFeeReduction { get; set; }
    }

    /// <summary>
    /// Customer billing summary.
    /// </summary>
    public class CustomerBillingSummary
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public MembershipType MembershipTier { get; set; }
        public MembershipBenefits Benefits { get; set; }
        public int ActiveRentalCount { get; set; }
        public int OverdueRentalCount { get; set; }
        public int MaxConcurrentRentals { get; set; }
        public bool CanRentMore { get; set; }
        public int RemainingSlots { get; set; }
        public decimal TotalProjectedLateFees { get; set; }
        public decimal LifetimeSpend { get; set; }
        public List<RentalBillingDetail> Rentals { get; set; }
        public int FreeRentalsUsedThisMonth { get; set; }
        public int FreeRentalsRemaining { get; set; }
    }

    /// <summary>
    /// Billing detail for a single active rental.
    /// </summary>
    public class RentalBillingDetail
    {
        public int RentalId { get; set; }
        public string MovieName { get; set; }
        public DateTime RentalDate { get; set; }
        public DateTime DueDate { get; set; }
        public decimal DailyRate { get; set; }
        public bool IsOverdue { get; set; }
        public int DaysOverdue { get; set; }
        public decimal ProjectedLateFee { get; set; }
        public string LateFeeExplanation { get; set; }
    }
}

