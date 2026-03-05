using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages customer membership tiers based on rental activity.
    /// Evaluates customers against tier thresholds, tracks tier changes,
    /// and provides benefits lookup and analytics.
    /// </summary>
    public class MembershipTierService
    {
        private readonly ICustomerRepository _customerRepo;
        private readonly IRentalRepository _rentalRepo;
        private readonly Dictionary<MembershipType, TierConfig> _tierConfigs;
        private readonly List<TierChangeRecord> _changeHistory = new List<TierChangeRecord>();
        private readonly int _evaluationPeriodDays;

        /// <summary>
        /// Creates a new MembershipTierService with default tier configurations.
        /// </summary>
        public MembershipTierService(
            ICustomerRepository customerRepo,
            IRentalRepository rentalRepo,
            int evaluationPeriodDays = 90)
        {
            _customerRepo = customerRepo ?? throw new ArgumentNullException(nameof(customerRepo));
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _evaluationPeriodDays = evaluationPeriodDays > 0 ? evaluationPeriodDays : 90;

            _tierConfigs = new Dictionary<MembershipType, TierConfig>
            {
                [MembershipType.Basic] = new TierConfig
                {
                    MinRentals = 0, MinSpend = 0m, MaxLatePercentage = 1.0,
                    DiscountPercent = 0, MaxConcurrentRentals = 2, GraceDays = 0,
                    FreeReservations = false, PriorityNewReleases = false
                },
                [MembershipType.Silver] = new TierConfig
                {
                    MinRentals = 5, MinSpend = 25m, MaxLatePercentage = 0.40,
                    DiscountPercent = 5, MaxConcurrentRentals = 3, GraceDays = 1,
                    FreeReservations = false, PriorityNewReleases = false
                },
                [MembershipType.Gold] = new TierConfig
                {
                    MinRentals = 15, MinSpend = 75m, MaxLatePercentage = 0.25,
                    DiscountPercent = 10, MaxConcurrentRentals = 5, GraceDays = 2,
                    FreeReservations = true, PriorityNewReleases = false
                },
                [MembershipType.Platinum] = new TierConfig
                {
                    MinRentals = 30, MinSpend = 150m, MaxLatePercentage = 0.15,
                    DiscountPercent = 15, MaxConcurrentRentals = 8, GraceDays = 3,
                    FreeReservations = true, PriorityNewReleases = true
                }
            };
        }

        /// <summary>
        /// Creates a MembershipTierService with custom tier configurations.
        /// </summary>
        public MembershipTierService(
            ICustomerRepository customerRepo,
            IRentalRepository rentalRepo,
            Dictionary<MembershipType, TierConfig> customConfigs,
            int evaluationPeriodDays = 90)
        {
            _customerRepo = customerRepo ?? throw new ArgumentNullException(nameof(customerRepo));
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _evaluationPeriodDays = evaluationPeriodDays > 0 ? evaluationPeriodDays : 90;
            _tierConfigs = customConfigs ?? throw new ArgumentNullException(nameof(customConfigs));

            // Ensure Basic tier exists
            if (!_tierConfigs.ContainsKey(MembershipType.Basic))
                throw new ArgumentException("Tier configs must include a Basic tier.");
        }

        /// <summary>
        /// Gets the benefits for a given tier.
        /// </summary>
        public TierBenefits GetBenefits(MembershipType tier)
        {
            if (!_tierConfigs.TryGetValue(tier, out var config))
                throw new ArgumentException($"No configuration for tier {tier}");

            return new TierBenefits
            {
                Tier = tier,
                TierName = tier.ToString(),
                DiscountPercent = config.DiscountPercent,
                MaxConcurrentRentals = config.MaxConcurrentRentals,
                GraceDays = config.GraceDays,
                FreeReservations = config.FreeReservations,
                PriorityNewReleases = config.PriorityNewReleases
            };
        }

        /// <summary>
        /// Returns a comparison of all tier benefits.
        /// </summary>
        public TierComparison CompareTiers()
        {
            var comparison = new TierComparison();
            foreach (var tier in new[] { MembershipType.Basic, MembershipType.Silver, MembershipType.Gold, MembershipType.Platinum })
            {
                if (_tierConfigs.ContainsKey(tier))
                    comparison.Tiers.Add(GetBenefits(tier));
            }
            return comparison;
        }

        /// <summary>
        /// Evaluates a customer's rental activity and determines their appropriate tier.
        /// </summary>
        public TierEvaluation EvaluateCustomer(int customerId)
        {
            return EvaluateCustomer(customerId, DateTime.Today);
        }

        /// <summary>
        /// Evaluates a customer's tier as of a specific reference date.
        /// </summary>
        public TierEvaluation EvaluateCustomer(int customerId, DateTime referenceDate)
        {
            var customer = _customerRepo.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            var allRentals = _rentalRepo.GetAll();
            var periodStart = referenceDate.AddDays(-_evaluationPeriodDays);
            var customerRentals = allRentals
                .Where(r => r.CustomerId == customerId && r.RentalDate >= periodStart && r.RentalDate <= referenceDate)
                .ToList();

            var totalRentals = customerRentals.Count;
            var totalSpend = customerRentals.Sum(r => r.TotalCost);
            var returnedRentals = customerRentals.Where(r => r.Status == RentalStatus.Returned).ToList();
            var lateReturns = returnedRentals.Count(r => r.ReturnDate.HasValue && r.ReturnDate.Value > r.DueDate);
            var onTimeReturns = returnedRentals.Count - lateReturns;
            var latePercentage = returnedRentals.Count > 0 ? (double)lateReturns / returnedRentals.Count : 0.0;

            // Determine highest qualifying tier (top-down)
            var evaluatedTier = MembershipType.Basic;
            var reason = "Default tier";

            foreach (var tier in new[] { MembershipType.Platinum, MembershipType.Gold, MembershipType.Silver })
            {
                if (!_tierConfigs.TryGetValue(tier, out var config)) continue;

                if (totalRentals >= config.MinRentals &&
                    totalSpend >= config.MinSpend &&
                    latePercentage <= config.MaxLatePercentage)
                {
                    evaluatedTier = tier;
                    reason = $"Qualified: {totalRentals} rentals (≥{config.MinRentals}), " +
                             $"${totalSpend:F2} spend (≥${config.MinSpend:F2}), " +
                             $"{latePercentage:P0} late (≤{config.MaxLatePercentage:P0})";
                    break;
                }
            }

            if (evaluatedTier == MembershipType.Basic)
                reason = BuildBasicReason(totalRentals, totalSpend, latePercentage);

            // Progress to next tier
            var nextTier = GetNextTier(evaluatedTier);
            double progress = 1.0;
            string nextReq = null;

            if (nextTier.HasValue && _tierConfigs.TryGetValue(nextTier.Value, out var nextConfig))
            {
                var rentalProgress = nextConfig.MinRentals > 0
                    ? Math.Min(1.0, (double)totalRentals / nextConfig.MinRentals) : 1.0;
                var spendProgress = nextConfig.MinSpend > 0
                    ? Math.Min(1.0, (double)totalSpend / (double)nextConfig.MinSpend) : 1.0;
                var lateOk = latePercentage <= nextConfig.MaxLatePercentage ? 1.0 : 0.5;

                progress = (rentalProgress + spendProgress + lateOk) / 3.0;

                var needs = new List<string>();
                if (totalRentals < nextConfig.MinRentals)
                    needs.Add($"{nextConfig.MinRentals - totalRentals} more rentals");
                if (totalSpend < nextConfig.MinSpend)
                    needs.Add($"${nextConfig.MinSpend - totalSpend:F2} more spend");
                if (latePercentage > nextConfig.MaxLatePercentage)
                    needs.Add($"reduce late returns to ≤{nextConfig.MaxLatePercentage:P0}");

                nextReq = needs.Count > 0
                    ? $"Need {string.Join(", ", needs)} for {nextTier.Value}"
                    : $"Qualifies for {nextTier.Value}";
            }

            return new TierEvaluation
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                CurrentTier = customer.MembershipType,
                EvaluatedTier = evaluatedTier,
                RentalsInPeriod = totalRentals,
                SpendInPeriod = totalSpend,
                LatePercentage = latePercentage,
                OnTimeReturns = onTimeReturns,
                LateReturns = lateReturns,
                CurrentBenefits = GetBenefits(customer.MembershipType),
                NewBenefits = GetBenefits(evaluatedTier),
                Reason = reason,
                ProgressToNextTier = progress,
                NextTierRequirement = nextReq
            };
        }

        /// <summary>
        /// Evaluates all customers and returns their tier assessments.
        /// </summary>
        public List<TierEvaluation> EvaluateAllCustomers()
        {
            return EvaluateAllCustomers(DateTime.Today);
        }

        /// <summary>
        /// Evaluates all customers as of a specific date.
        /// </summary>
        public List<TierEvaluation> EvaluateAllCustomers(DateTime referenceDate)
        {
            return _customerRepo.GetAll()
                .Select(c => EvaluateCustomer(c.Id, referenceDate))
                .ToList();
        }

        /// <summary>
        /// Applies evaluated tier changes to customers and records the changes.
        /// Returns the list of customers whose tiers changed.
        /// </summary>
        public List<TierChangeRecord> ApplyTierChanges()
        {
            return ApplyTierChanges(DateTime.Today);
        }

        /// <summary>
        /// Applies tier changes as of a specific date.
        /// </summary>
        public List<TierChangeRecord> ApplyTierChanges(DateTime referenceDate)
        {
            var evaluations = EvaluateAllCustomers(referenceDate);
            var changes = new List<TierChangeRecord>();

            foreach (var eval in evaluations.Where(e => e.TierChanged))
            {
                var customer = _customerRepo.GetById(eval.CustomerId);
                if (customer == null) continue;

                var record = new TierChangeRecord
                {
                    CustomerId = eval.CustomerId,
                    CustomerName = eval.CustomerName,
                    OldTier = eval.CurrentTier,
                    NewTier = eval.EvaluatedTier,
                    ChangeDate = referenceDate,
                    Reason = eval.Reason
                };

                customer.MembershipType = eval.EvaluatedTier;
                _customerRepo.Update(customer);

                _changeHistory.Add(record);
                changes.Add(record);
            }

            return changes;
        }

        /// <summary>
        /// Returns the complete tier change history.
        /// </summary>
        public List<TierChangeRecord> GetChangeHistory()
        {
            return _changeHistory.OrderByDescending(c => c.ChangeDate).ToList();
        }

        /// <summary>
        /// Returns tier change history for a specific customer.
        /// </summary>
        public List<TierChangeRecord> GetCustomerHistory(int customerId)
        {
            return _changeHistory
                .Where(c => c.CustomerId == customerId)
                .OrderByDescending(c => c.ChangeDate)
                .ToList();
        }

        /// <summary>
        /// Returns the current tier distribution across all customers.
        /// </summary>
        public TierDistribution GetTierDistribution()
        {
            var customers = _customerRepo.GetAll();
            var dist = new TierDistribution { TotalCustomers = customers.Count };

            foreach (var tier in new[] { MembershipType.Basic, MembershipType.Silver, MembershipType.Gold, MembershipType.Platinum })
            {
                var count = customers.Count(c => c.MembershipType == tier);
                dist.Counts[tier] = count;
                dist.Percentages[tier] = dist.TotalCustomers > 0 ? (double)count / dist.TotalCustomers : 0;
            }

            dist.MostCommonTier = dist.Counts.OrderByDescending(kv => kv.Value).First().Key;
            return dist;
        }

        /// <summary>
        /// Finds customers who are close to upgrading (progress >= threshold).
        /// </summary>
        public List<TierEvaluation> GetNearUpgradeCustomers(double threshold = 0.75)
        {
            return EvaluateAllCustomers()
                .Where(e => !e.TierChanged &&
                            GetNextTier(e.EvaluatedTier).HasValue &&
                            e.ProgressToNextTier >= threshold &&
                            e.ProgressToNextTier < 1.0)
                .OrderByDescending(e => e.ProgressToNextTier)
                .ToList();
        }

        /// <summary>
        /// Finds customers at risk of downgrade (would evaluate lower than current tier).
        /// </summary>
        public List<TierEvaluation> GetAtRiskCustomers()
        {
            return EvaluateAllCustomers()
                .Where(e => e.IsDowngrade)
                .OrderBy(e => (int)e.EvaluatedTier)
                .ToList();
        }

        /// <summary>
        /// Generates a full membership report for a customer.
        /// </summary>
        public MembershipReport GetMembershipReport(int customerId)
        {
            var customer = _customerRepo.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            var allRentals = _rentalRepo.GetAll()
                .Where(r => r.CustomerId == customerId)
                .ToList();
            var returned = allRentals.Where(r => r.Status == RentalStatus.Returned).ToList();
            var lateCount = returned.Count(r => r.ReturnDate.HasValue && r.ReturnDate.Value > r.DueDate);

            var eval = EvaluateCustomer(customerId);

            return new MembershipReport
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                CurrentTier = customer.MembershipType,
                Benefits = GetBenefits(customer.MembershipType),
                LatestEvaluation = eval,
                History = GetCustomerHistory(customerId),
                TotalRentals = allRentals.Count,
                TotalSpend = allRentals.Sum(r => r.TotalCost),
                LifetimeLatePercentage = returned.Count > 0 ? (double)lateCount / returned.Count : 0,
                MemberSince = customer.MemberSince,
                MembershipDays = customer.MemberSince.HasValue
                    ? (int)(DateTime.Today - customer.MemberSince.Value).TotalDays
                    : 0
            };
        }

        /// <summary>
        /// Calculates the discounted daily rate for a customer based on their tier.
        /// </summary>
        public decimal GetDiscountedRate(int customerId, decimal baseRate)
        {
            var customer = _customerRepo.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            var benefits = GetBenefits(customer.MembershipType);
            var discount = benefits.DiscountPercent / 100m;
            return Math.Round(baseRate * (1 - discount), 2);
        }

        /// <summary>
        /// Checks if a customer can rent more movies based on their tier's concurrent limit.
        /// </summary>
        public bool CanRentMore(int customerId)
        {
            var customer = _customerRepo.GetById(customerId);
            if (customer == null) return false;

            var benefits = GetBenefits(customer.MembershipType);
            var activeRentals = _rentalRepo.GetActiveByCustomer(customerId);
            return activeRentals.Count < benefits.MaxConcurrentRentals;
        }

        /// <summary>
        /// Returns remaining rental slots for a customer.
        /// </summary>
        public int GetRemainingSlots(int customerId)
        {
            var customer = _customerRepo.GetById(customerId);
            if (customer == null) return 0;

            var benefits = GetBenefits(customer.MembershipType);
            var activeRentals = _rentalRepo.GetActiveByCustomer(customerId);
            return Math.Max(0, benefits.MaxConcurrentRentals - activeRentals.Count);
        }

        /// <summary>
        /// Generates a text summary of tier distribution and recent changes.
        /// </summary>
        public string GenerateSummaryReport()
        {
            var dist = GetTierDistribution();
            var recentChanges = _changeHistory
                .OrderByDescending(c => c.ChangeDate)
                .Take(10)
                .ToList();

            var lines = new List<string>
            {
                "=== Membership Tier Summary ===",
                $"Total Customers: {dist.TotalCustomers}",
                ""
            };

            foreach (var tier in new[] { MembershipType.Basic, MembershipType.Silver, MembershipType.Gold, MembershipType.Platinum })
            {
                if (dist.Counts.TryGetValue(tier, out var count))
                    lines.Add($"  {tier}: {count} ({dist.Percentages[tier]:P1})");
            }

            lines.Add($"\nMost Common: {dist.MostCommonTier}");

            if (recentChanges.Count > 0)
            {
                lines.Add($"\n--- Recent Changes ({recentChanges.Count}) ---");
                foreach (var c in recentChanges)
                    lines.Add($"  {c.ChangeDate:yyyy-MM-dd} {c.CustomerName}: {c.OldTier} → {c.NewTier}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        // --- Private helpers ---

        private MembershipType? GetNextTier(MembershipType current)
        {
            switch (current)
            {
                case MembershipType.Basic: return MembershipType.Silver;
                case MembershipType.Silver: return MembershipType.Gold;
                case MembershipType.Gold: return MembershipType.Platinum;
                default: return null;
            }
        }

        private string BuildBasicReason(int rentals, decimal spend, double latePct)
        {
            var silverConfig = _tierConfigs[MembershipType.Silver];
            var reasons = new List<string>();

            if (rentals < silverConfig.MinRentals)
                reasons.Add($"only {rentals}/{silverConfig.MinRentals} rentals");
            if (spend < silverConfig.MinSpend)
                reasons.Add($"only ${spend:F2}/${silverConfig.MinSpend:F2} spend");
            if (latePct > silverConfig.MaxLatePercentage)
                reasons.Add($"{latePct:P0} late returns (max {silverConfig.MaxLatePercentage:P0})");

            return reasons.Count > 0
                ? $"Below Silver threshold: {string.Join(", ", reasons)}"
                : "Default tier";
        }
    }
}
