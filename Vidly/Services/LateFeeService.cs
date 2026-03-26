using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Manages late fee policies and calculates fees.
    /// </summary>
    public class LateFeeService
    {
        private static readonly List<LateFeePolicy> _policies = new List<LateFeePolicy>
        {
            new LateFeePolicy
            {
                Id = 1,
                Name = "Standard",
                Strategy = LateFeeStrategy.PerDay,
                PerDayRate = 1.50m,
                GracePeriodDays = 1,
                MaxFeeCap = 25.00m,
                IsActive = true
            },
            new LateFeePolicy
            {
                Id = 2,
                Name = "Premium Member",
                Strategy = LateFeeStrategy.PerDay,
                PerDayRate = 0.75m,
                GracePeriodDays = 3,
                MaxFeeCap = 15.00m,
                IsActive = true
            },
            new LateFeePolicy
            {
                Id = 3,
                Name = "Graduated",
                Strategy = LateFeeStrategy.Tiered,
                GracePeriodDays = 1,
                MaxFeeCap = 50.00m,
                IsActive = true,
                Tiers = new List<LateFeeTier>
                {
                    new LateFeeTier { Id = 1, PolicyId = 3, FromDay = 1, ToDay = 3, RatePerDay = 1.00m },
                    new LateFeeTier { Id = 2, PolicyId = 3, FromDay = 4, ToDay = 7, RatePerDay = 2.00m },
                    new LateFeeTier { Id = 3, PolicyId = 3, FromDay = 8, ToDay = null, RatePerDay = 3.00m }
                }
            }
        };
        private static int _nextId = 4;

        public List<LateFeePolicy> GetAllPolicies() =>
            _policies.OrderByDescending(p => p.IsActive).ThenBy(p => p.Name).ToList();

        public LateFeePolicy GetPolicy(int id) =>
            _policies.FirstOrDefault(p => p.Id == id);

        public LateFeePolicy GetActivePolicy() =>
            _policies.FirstOrDefault(p => p.IsActive) ?? _policies.First();

        public void SavePolicy(LateFeePolicy policy)
        {
            if (policy.Id == 0)
            {
                policy.Id = _nextId++;
                policy.CreatedAt = DateTime.Now;
                _policies.Add(policy);
            }
            else
            {
                var existing = _policies.FirstOrDefault(p => p.Id == policy.Id);
                if (existing != null)
                {
                    existing.Name = policy.Name;
                    existing.Strategy = policy.Strategy;
                    existing.FlatFeeAmount = policy.FlatFeeAmount;
                    existing.PerDayRate = policy.PerDayRate;
                    existing.GracePeriodDays = policy.GracePeriodDays;
                    existing.MaxFeeCap = policy.MaxFeeCap;
                    existing.IsActive = policy.IsActive;
                    existing.Tiers = policy.Tiers ?? new List<LateFeeTier>();
                }
            }
        }

        public bool DeletePolicy(int id)
        {
            var policy = _policies.FirstOrDefault(p => p.Id == id);
            if (policy == null) return false;
            _policies.Remove(policy);
            return true;
        }

        /// <summary>
        /// Calculate fee estimate with full breakdown for display.
        /// </summary>
        public LateFeeEstimate CalculateEstimate(int policyId, int daysOverdue)
        {
            var policy = GetPolicy(policyId) ?? GetActivePolicy();
            var chargeableDays = Math.Max(0, daysOverdue - policy.GracePeriodDays);
            var rawFee = policy.Calculate(daysOverdue);

            var estimate = new LateFeeEstimate
            {
                PolicyName = policy.Name,
                Strategy = policy.Strategy,
                DaysOverdue = daysOverdue,
                GracePeriodDays = policy.GracePeriodDays,
                ChargeableDays = chargeableDays,
                Fee = rawFee,
                MaxCap = policy.MaxFeeCap,
                WasCapped = policy.MaxFeeCap > 0 && chargeableDays > 0
                    && CalculateUncapped(policy, chargeableDays) > policy.MaxFeeCap
            };

            if (policy.Strategy == LateFeeStrategy.Tiered && policy.Tiers.Any())
            {
                var sorted = policy.Tiers.OrderBy(t => t.FromDay).ToList();
                int remaining = chargeableDays;
                foreach (var tier in sorted)
                {
                    if (remaining <= 0) break;
                    int tierDays = tier.ToDay.HasValue
                        ? Math.Min(remaining, tier.ToDay.Value - tier.FromDay + 1)
                        : remaining;
                    estimate.TierBreakdowns.Add(new TierBreakdown
                    {
                        TierLabel = tier.ToDay.HasValue
                            ? $"Days {tier.FromDay}–{tier.ToDay}"
                            : $"Days {tier.FromDay}+",
                        Days = tierDays,
                        Rate = tier.RatePerDay,
                        Subtotal = tierDays * tier.RatePerDay
                    });
                    remaining -= tierDays;
                }
            }

            return estimate;
        }

        /// <summary>
        /// Build a fee schedule table for 1..maxDays for a given policy.
        /// </summary>
        public List<LateFeeEstimate> BuildSchedule(int policyId, int maxDays = 30)
        {
            return Enumerable.Range(1, maxDays)
                .Select(d => CalculateEstimate(policyId, d))
                .ToList();
        }

        private decimal CalculateUncapped(LateFeePolicy policy, int chargeableDays)
        {
            var temp = new LateFeePolicy
            {
                Strategy = policy.Strategy,
                FlatFeeAmount = policy.FlatFeeAmount,
                PerDayRate = policy.PerDayRate,
                GracePeriodDays = 0,
                MaxFeeCap = 0,
                Tiers = policy.Tiers
            };
            return temp.Calculate(chargeableDays);
        }
    }
}
