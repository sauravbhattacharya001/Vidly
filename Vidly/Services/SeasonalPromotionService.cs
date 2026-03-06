using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages calendar-driven seasonal promotions that automatically apply
    /// discounts based on date ranges, eligible genres, and configurable
    /// discount types. Unlike one-off coupons, seasonal promotions activate
    /// and expire automatically without customer action.
    /// </summary>
    public class SeasonalPromotionService
    {
        private readonly IRentalRepository _rentalRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly List<SeasonalPromotion> _promotions = new List<SeasonalPromotion>();
        private int _nextId = 1;

        /// <summary>Maximum promotions that can be active simultaneously.</summary>
        public const int MaxConcurrentPromotions = 20;

        /// <summary>Maximum discount percentage allowed.</summary>
        public const int MaxDiscountPercent = 75;

        /// <summary>Maximum flat discount amount.</summary>
        public const decimal MaxFlatDiscount = 50.00m;

        /// <summary>Minimum promotion duration in days.</summary>
        public const int MinDurationDays = 1;

        /// <summary>Maximum promotion duration in days.</summary>
        public const int MaxDurationDays = 365;

        public SeasonalPromotionService(
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo)
        {
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _movieRepo = movieRepo ?? throw new ArgumentNullException(nameof(movieRepo));
        }

        // ── Promotion Management ────────────────────────────────────

        /// <summary>
        /// Creates a new seasonal promotion.
        /// </summary>
        public SeasonalPromotion CreatePromotion(
            string name,
            DateTime startDate,
            DateTime endDate,
            PromotionDiscountType discountType,
            decimal discountValue,
            List<Genre> eligibleGenres = null,
            List<int> eligibleMovieIds = null,
            bool isStackable = false,
            int? maxRedemptions = null,
            string description = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Promotion name is required.", nameof(name));

            if (name.Length > 100)
                throw new ArgumentException("Promotion name cannot exceed 100 characters.", nameof(name));

            if (endDate <= startDate)
                throw new ArgumentException("End date must be after start date.");

            var durationDays = (int)Math.Ceiling((endDate - startDate).TotalDays);
            if (durationDays < MinDurationDays)
                throw new ArgumentException($"Promotion must last at least {MinDurationDays} day(s).");
            if (durationDays > MaxDurationDays)
                throw new ArgumentException($"Promotion cannot exceed {MaxDurationDays} days.");

            ValidateDiscount(discountType, discountValue);

            if (maxRedemptions.HasValue && maxRedemptions.Value <= 0)
                throw new ArgumentException("Max redemptions must be positive.", nameof(maxRedemptions));

            // Check for duplicate names
            if (_promotions.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A promotion named '{name}' already exists.");

            var promo = new SeasonalPromotion
            {
                Id = _nextId++,
                Name = name,
                Description = description ?? "",
                StartDate = startDate,
                EndDate = endDate,
                DiscountType = discountType,
                DiscountValue = discountValue,
                EligibleGenres = eligibleGenres ?? new List<Genre>(),
                EligibleMovieIds = eligibleMovieIds ?? new List<int>(),
                IsStackable = isStackable,
                MaxRedemptions = maxRedemptions,
                RedemptionCount = 0,
                IsEnabled = true,
                CreatedAt = DateTime.Now
            };

            _promotions.Add(promo);
            return promo;
        }

        /// <summary>
        /// Updates an existing promotion. Cannot update once expired.
        /// </summary>
        public SeasonalPromotion UpdatePromotion(
            int promotionId,
            string name = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            PromotionDiscountType? discountType = null,
            decimal? discountValue = null,
            List<Genre> eligibleGenres = null,
            bool? isStackable = null,
            bool? isEnabled = null,
            string description = null)
        {
            var promo = GetPromotionById(promotionId);

            if (promo.EndDate < DateTime.Now)
                throw new InvalidOperationException("Cannot update an expired promotion.");

            if (name != null)
            {
                if (string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("Promotion name cannot be empty.");
                if (name.Length > 100)
                    throw new ArgumentException("Promotion name cannot exceed 100 characters.");
                if (_promotions.Any(p => p.Id != promotionId &&
                    p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"A promotion named '{name}' already exists.");
                promo.Name = name;
            }

            var newStart = startDate ?? promo.StartDate;
            var newEnd = endDate ?? promo.EndDate;

            if (startDate.HasValue || endDate.HasValue)
            {
                if (newEnd <= newStart)
                    throw new ArgumentException("End date must be after start date.");
                promo.StartDate = newStart;
                promo.EndDate = newEnd;
            }

            if (discountType.HasValue || discountValue.HasValue)
            {
                var type = discountType ?? promo.DiscountType;
                var value = discountValue ?? promo.DiscountValue;
                ValidateDiscount(type, value);
                promo.DiscountType = type;
                promo.DiscountValue = value;
            }

            if (eligibleGenres != null) promo.EligibleGenres = eligibleGenres;
            if (isStackable.HasValue) promo.IsStackable = isStackable.Value;
            if (isEnabled.HasValue) promo.IsEnabled = isEnabled.Value;
            if (description != null) promo.Description = description;

            return promo;
        }

        /// <summary>
        /// Deletes a promotion by ID.
        /// </summary>
        public bool DeletePromotion(int promotionId)
        {
            return _promotions.RemoveAll(p => p.Id == promotionId) > 0;
        }

        /// <summary>
        /// Gets a promotion by ID.
        /// </summary>
        public SeasonalPromotion GetPromotionById(int promotionId)
        {
            var promo = _promotions.FirstOrDefault(p => p.Id == promotionId);
            if (promo == null)
                throw new KeyNotFoundException($"Promotion {promotionId} not found.");
            return promo;
        }

        /// <summary>
        /// Gets all promotions.
        /// </summary>
        public IReadOnlyList<SeasonalPromotion> GetAllPromotions()
        {
            return _promotions.AsReadOnly();
        }

        // ── Active Promotion Queries ────────────────────────────────

        /// <summary>
        /// Gets all promotions active on a specific date.
        /// </summary>
        public List<SeasonalPromotion> GetActivePromotions(DateTime? asOf = null)
        {
            var date = asOf ?? DateTime.Now;
            return _promotions
                .Where(p => p.IsEnabled && p.StartDate <= date && p.EndDate >= date)
                .Where(p => !p.MaxRedemptions.HasValue || p.RedemptionCount < p.MaxRedemptions.Value)
                .ToList();
        }

        /// <summary>
        /// Gets all promotions applicable to a specific movie on a given date.
        /// A promotion applies if: (1) it's active, (2) the movie's genre is in
        /// eligible genres (or eligible genres is empty = all genres), and
        /// (3) the movie is in eligible movie IDs (or eligible movie IDs is empty = all movies).
        /// </summary>
        public List<SeasonalPromotion> GetPromotionsForMovie(int movieId, DateTime? asOf = null)
        {
            var movie = _movieRepo.GetById(movieId);
            if (movie == null) return new List<SeasonalPromotion>();

            var active = GetActivePromotions(asOf);

            return active.Where(p => IsMovieEligible(p, movie)).ToList();
        }

        /// <summary>
        /// Calculates the best discount available for a movie based on active promotions.
        /// When multiple non-stackable promotions apply, selects the one giving the largest discount.
        /// Stackable promotions are combined additively.
        /// </summary>
        public PromotionDiscount CalculateBestDiscount(int movieId, decimal basePrice, DateTime? asOf = null)
        {
            var applicable = GetPromotionsForMovie(movieId, asOf);
            if (!applicable.Any())
            {
                return new PromotionDiscount
                {
                    OriginalPrice = basePrice,
                    FinalPrice = basePrice,
                    TotalDiscountAmount = 0,
                    AppliedPromotions = new List<AppliedPromotion>()
                };
            }

            var stackable = applicable.Where(p => p.IsStackable).ToList();
            var nonStackable = applicable.Where(p => !p.IsStackable).ToList();

            // Find best non-stackable promotion
            AppliedPromotion bestNonStackable = null;
            foreach (var promo in nonStackable)
            {
                var discount = CalculateDiscountAmount(promo, basePrice);
                if (bestNonStackable == null || discount > bestNonStackable.DiscountAmount)
                {
                    bestNonStackable = new AppliedPromotion
                    {
                        PromotionId = promo.Id,
                        PromotionName = promo.Name,
                        DiscountType = promo.DiscountType,
                        DiscountValue = promo.DiscountValue,
                        DiscountAmount = discount
                    };
                }
            }

            // Calculate total stackable discount
            var appliedList = new List<AppliedPromotion>();
            decimal totalDiscount = 0;

            if (bestNonStackable != null)
            {
                appliedList.Add(bestNonStackable);
                totalDiscount += bestNonStackable.DiscountAmount;
            }

            foreach (var promo in stackable)
            {
                var discount = CalculateDiscountAmount(promo, basePrice);
                appliedList.Add(new AppliedPromotion
                {
                    PromotionId = promo.Id,
                    PromotionName = promo.Name,
                    DiscountType = promo.DiscountType,
                    DiscountValue = promo.DiscountValue,
                    DiscountAmount = discount
                });
                totalDiscount += discount;
            }

            // Cap discount at original price
            totalDiscount = Math.Min(totalDiscount, basePrice);

            return new PromotionDiscount
            {
                OriginalPrice = basePrice,
                FinalPrice = basePrice - totalDiscount,
                TotalDiscountAmount = totalDiscount,
                AppliedPromotions = appliedList
            };
        }

        /// <summary>
        /// Records a redemption for a promotion. Returns false if the promotion
        /// has reached its redemption limit.
        /// </summary>
        public bool RecordRedemption(int promotionId)
        {
            var promo = GetPromotionById(promotionId);

            if (!promo.IsEnabled)
                return false;

            if (promo.MaxRedemptions.HasValue && promo.RedemptionCount >= promo.MaxRedemptions.Value)
                return false;

            promo.RedemptionCount++;
            return true;
        }

        // ── Seasonal Templates ──────────────────────────────────────

        /// <summary>
        /// Creates a pre-configured summer blockbuster promotion.
        /// Action, Adventure, and Sci-Fi at 20% off.
        /// </summary>
        public SeasonalPromotion CreateSummerBlockbuster(int year)
        {
            return CreatePromotion(
                name: $"Summer Blockbuster {year}",
                startDate: new DateTime(year, 6, 1),
                endDate: new DateTime(year, 8, 31),
                discountType: PromotionDiscountType.Percentage,
                discountValue: 20,
                eligibleGenres: new List<Genre> { Genre.Action, Genre.Adventure, Genre.SciFi },
                description: "Summer blockbuster season — Action, Adventure, and Sci-Fi at 20% off!"
            );
        }

        /// <summary>
        /// Creates a holiday season family promotion.
        /// Animation and Comedy at 25% off during December.
        /// </summary>
        public SeasonalPromotion CreateHolidaySpecial(int year)
        {
            return CreatePromotion(
                name: $"Holiday Special {year}",
                startDate: new DateTime(year, 12, 1),
                endDate: new DateTime(year, 12, 31),
                discountType: PromotionDiscountType.Percentage,
                discountValue: 25,
                eligibleGenres: new List<Genre> { Genre.Animation, Genre.Comedy },
                description: "Holiday family fun — Animation and Comedy at 25% off!"
            );
        }

        /// <summary>
        /// Creates a spooky October promotion for horror and thriller genres.
        /// $1 off per rental.
        /// </summary>
        public SeasonalPromotion CreateSpookySeason(int year)
        {
            return CreatePromotion(
                name: $"Spooky Season {year}",
                startDate: new DateTime(year, 10, 1),
                endDate: new DateTime(year, 10, 31),
                discountType: PromotionDiscountType.FlatAmount,
                discountValue: 1.00m,
                eligibleGenres: new List<Genre> { Genre.Horror, Genre.Thriller },
                description: "October scares — Horror and Thriller at $1 off!"
            );
        }

        /// <summary>
        /// Creates an Oscar Season promotion for Drama and Documentary.
        /// 15% off in February.
        /// </summary>
        public SeasonalPromotion CreateOscarSeason(int year)
        {
            return CreatePromotion(
                name: $"Oscar Season {year}",
                startDate: new DateTime(year, 2, 1),
                endDate: new DateTime(year, 2, 28),
                discountType: PromotionDiscountType.Percentage,
                discountValue: 15,
                eligibleGenres: new List<Genre> { Genre.Drama, Genre.Documentary },
                description: "Oscar season — Drama and Documentary at 15% off!"
            );
        }

        /// <summary>
        /// Creates a Valentine's Day romance promotion.
        /// $1.50 off Romance movies during Valentine's week.
        /// </summary>
        public SeasonalPromotion CreateValentinesSpecial(int year)
        {
            return CreatePromotion(
                name: $"Valentine's Special {year}",
                startDate: new DateTime(year, 2, 7),
                endDate: new DateTime(year, 2, 14),
                discountType: PromotionDiscountType.FlatAmount,
                discountValue: 1.50m,
                eligibleGenres: new List<Genre> { Genre.Romance },
                description: "Valentine's week — Romance movies at $1.50 off!"
            );
        }

        // ── Analytics ───────────────────────────────────────────────

        /// <summary>
        /// Gets analytics for a specific promotion.
        /// </summary>
        public PromotionAnalytics GetPromotionAnalytics(int promotionId)
        {
            var promo = GetPromotionById(promotionId);

            var totalDays = Math.Max(1, (int)Math.Ceiling((promo.EndDate - promo.StartDate).TotalDays));
            var elapsed = promo.EndDate < DateTime.Now
                ? totalDays
                : Math.Max(0, (int)Math.Ceiling((DateTime.Now - promo.StartDate).TotalDays));

            var redemptionsPerDay = elapsed > 0
                ? (double)promo.RedemptionCount / elapsed
                : 0;

            var utilizationRate = promo.MaxRedemptions.HasValue
                ? (double)promo.RedemptionCount / promo.MaxRedemptions.Value * 100
                : 0;

            var status = GetPromotionStatus(promo);

            return new PromotionAnalytics
            {
                PromotionId = promo.Id,
                PromotionName = promo.Name,
                Status = status,
                TotalRedemptions = promo.RedemptionCount,
                RedemptionsPerDay = Math.Round(redemptionsPerDay, 2),
                UtilizationRate = Math.Round(utilizationRate, 1),
                DaysTotal = totalDays,
                DaysElapsed = elapsed,
                DaysRemaining = Math.Max(0, totalDays - elapsed)
            };
        }

        /// <summary>
        /// Gets a summary of all promotions with analytics.
        /// </summary>
        public PromotionSummary GetSummary()
        {
            var now = DateTime.Now;
            var all = _promotions;

            return new PromotionSummary
            {
                TotalPromotions = all.Count,
                ActivePromotions = all.Count(p => p.IsEnabled && p.StartDate <= now && p.EndDate >= now),
                UpcomingPromotions = all.Count(p => p.IsEnabled && p.StartDate > now),
                ExpiredPromotions = all.Count(p => p.EndDate < now),
                DisabledPromotions = all.Count(p => !p.IsEnabled),
                TotalRedemptions = all.Sum(p => p.RedemptionCount),
                PromotionDetails = all.Select(p => GetPromotionAnalytics(p.Id)).ToList()
            };
        }

        // ── Private Helpers ─────────────────────────────────────────

        private bool IsMovieEligible(SeasonalPromotion promo, Movie movie)
        {
            // If specific movie IDs are listed, movie must be in the list
            if (promo.EligibleMovieIds.Count > 0 && !promo.EligibleMovieIds.Contains(movie.Id))
                return false;

            // If specific genres are listed, movie's genre must match
            if (promo.EligibleGenres.Count > 0 && movie.Genre.HasValue &&
                !promo.EligibleGenres.Contains(movie.Genre.Value))
                return false;

            // If genres are listed but movie has no genre, it's not eligible
            if (promo.EligibleGenres.Count > 0 && !movie.Genre.HasValue)
                return false;

            return true;
        }

        private decimal CalculateDiscountAmount(SeasonalPromotion promo, decimal basePrice)
        {
            switch (promo.DiscountType)
            {
                case PromotionDiscountType.Percentage:
                    return Math.Round(basePrice * promo.DiscountValue / 100, 2);

                case PromotionDiscountType.FlatAmount:
                    return Math.Min(promo.DiscountValue, basePrice);

                case PromotionDiscountType.BuyOneGetOneFree:
                    // BOGO: effectively 50% off one rental
                    return Math.Round(basePrice * 0.50m, 2);

                default:
                    return 0;
            }
        }

        private void ValidateDiscount(PromotionDiscountType type, decimal value)
        {
            if (value <= 0)
                throw new ArgumentException("Discount value must be positive.");

            switch (type)
            {
                case PromotionDiscountType.Percentage:
                    if (value > MaxDiscountPercent)
                        throw new ArgumentException(
                            $"Percentage discount cannot exceed {MaxDiscountPercent}%.");
                    break;

                case PromotionDiscountType.FlatAmount:
                    if (value > MaxFlatDiscount)
                        throw new ArgumentException(
                            $"Flat discount cannot exceed ${MaxFlatDiscount}.");
                    break;

                case PromotionDiscountType.BuyOneGetOneFree:
                    // Value is ignored for BOGO but must be positive
                    break;
            }
        }

        private string GetPromotionStatus(SeasonalPromotion promo)
        {
            if (!promo.IsEnabled) return "Disabled";
            var now = DateTime.Now;
            if (now < promo.StartDate) return "Upcoming";
            if (now > promo.EndDate) return "Expired";
            if (promo.MaxRedemptions.HasValue && promo.RedemptionCount >= promo.MaxRedemptions.Value)
                return "Sold Out";
            return "Active";
        }
    }

    // ── Models ──────────────────────────────────────────────────────

    /// <summary>
    /// A calendar-driven promotional discount that activates and expires
    /// automatically based on date ranges.
    /// </summary>
    public class SeasonalPromotion
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public PromotionDiscountType DiscountType { get; set; }
        public decimal DiscountValue { get; set; }
        public List<Genre> EligibleGenres { get; set; } = new List<Genre>();
        public List<int> EligibleMovieIds { get; set; } = new List<int>();
        public bool IsStackable { get; set; }
        public int? MaxRedemptions { get; set; }
        public int RedemptionCount { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Type of promotional discount.
    /// </summary>
    public enum PromotionDiscountType
    {
        /// <summary>Percentage off the base price.</summary>
        Percentage = 1,

        /// <summary>Fixed dollar amount off.</summary>
        FlatAmount = 2,

        /// <summary>Buy one, get one free (50% off).</summary>
        BuyOneGetOneFree = 3
    }

    /// <summary>
    /// Result of calculating the best available discount for a movie.
    /// </summary>
    public class PromotionDiscount
    {
        public decimal OriginalPrice { get; set; }
        public decimal FinalPrice { get; set; }
        public decimal TotalDiscountAmount { get; set; }
        public List<AppliedPromotion> AppliedPromotions { get; set; }

        public bool HasDiscount => TotalDiscountAmount > 0;
        public decimal DiscountPercentage => OriginalPrice > 0
            ? Math.Round(TotalDiscountAmount / OriginalPrice * 100, 1)
            : 0;
    }

    /// <summary>
    /// Details of a single applied promotion in a discount calculation.
    /// </summary>
    public class AppliedPromotion
    {
        public int PromotionId { get; set; }
        public string PromotionName { get; set; }
        public PromotionDiscountType DiscountType { get; set; }
        public decimal DiscountValue { get; set; }
        public decimal DiscountAmount { get; set; }
    }

    /// <summary>
    /// Analytics snapshot for a single promotion.
    /// </summary>
    public class PromotionAnalytics
    {
        public int PromotionId { get; set; }
        public string PromotionName { get; set; }
        public string Status { get; set; }
        public int TotalRedemptions { get; set; }
        public double RedemptionsPerDay { get; set; }
        public double UtilizationRate { get; set; }
        public int DaysTotal { get; set; }
        public int DaysElapsed { get; set; }
        public int DaysRemaining { get; set; }
    }

    /// <summary>
    /// Overview of all promotions in the system.
    /// </summary>
    public class PromotionSummary
    {
        public int TotalPromotions { get; set; }
        public int ActivePromotions { get; set; }
        public int UpcomingPromotions { get; set; }
        public int ExpiredPromotions { get; set; }
        public int DisabledPromotions { get; set; }
        public int TotalRedemptions { get; set; }
        public List<PromotionAnalytics> PromotionDetails { get; set; }
    }
}
