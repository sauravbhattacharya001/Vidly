using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages a loyalty points program where customers earn points for rentals
    /// and can redeem them for free rentals or discounts. Points scale with
    /// membership tier and rental value.
    /// </summary>
    public class LoyaltyPointsService
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IRentalRepository _rentalRepository;

        // In-memory ledger (would be a DB table in production)
        private readonly List<PointsTransaction> _ledger = new List<PointsTransaction>();

        /// <summary>Base points earned per dollar spent.</summary>
        public const int PointsPerDollar = 10;

        /// <summary>Points needed for a free standard rental.</summary>
        public const int FreeRentalCost = 500;

        /// <summary>Points needed for a 50% discount on one rental.</summary>
        public const int HalfOffCost = 250;

        /// <summary>Bonus points for returning a movie on time or early.</summary>
        public const int OnTimeReturnBonus = RentalPolicyConstants.OnTimeReturnBonus;

        public LoyaltyPointsService(
            ICustomerRepository customerRepository,
            IRentalRepository rentalRepository)
        {
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
        }

        // ── Tier multipliers ─────────────────────────────────────────

        /// <summary>
        /// Points multiplier based on membership tier.
        /// Higher tiers earn points faster.
        /// </summary>
        public static decimal GetTierMultiplier(MembershipType tier)
        {
            switch (tier)
            {
                case MembershipType.Basic: return 1.0m;
                case MembershipType.Silver: return 1.25m;
                case MembershipType.Gold: return 1.5m;
                case MembershipType.Platinum: return 2.0m;
                default: return 1.0m;
            }
        }

        // ── Earn points ──────────────────────────────────────────────

        /// <summary>
        /// Award points for a completed rental based on its total cost and
        /// the customer's membership tier. Only returned rentals are eligible —
        /// active/overdue rentals have a TotalCost that fluctuates with DateTime.Today
        /// and may include unfinalized late fees.
        /// </summary>
        public PointsTransaction EarnPointsForRental(int rentalId)
        {
            var rental = _rentalRepository.GetAll()
                .FirstOrDefault(r => r.Id == rentalId);
            if (rental == null)
                throw new ArgumentException($"Rental {rentalId} not found.");

            // Prevent awarding points on unreturned rentals — their TotalCost
            // is computed from DateTime.Today and grows every day, which would
            // let customers game the system by delaying returns.
            if (rental.Status != RentalStatus.Returned)
                throw new InvalidOperationException(
                    $"Rental {rentalId} has not been returned yet. " +
                    "Points can only be awarded for completed rentals.");

            // Don't double-award
            if (_ledger.Any(t => t.RentalId == rentalId && t.Type == TransactionType.Earned))
                throw new InvalidOperationException(
                    $"Points already awarded for rental {rentalId}.");

            var customer = _customerRepository.GetById(rental.CustomerId);
            if (customer == null)
                throw new ArgumentException($"Customer {rental.CustomerId} not found.");

            var multiplier = GetTierMultiplier(customer.MembershipType);
            var basePoints = (int)Math.Floor(rental.TotalCost * PointsPerDollar);
            var earnedPoints = (int)Math.Floor(basePoints * multiplier);

            // Bonus for on-time return
            var bonus = 0;
            if (rental.Status == RentalStatus.Returned &&
                rental.ReturnDate.HasValue &&
                rental.ReturnDate.Value <= rental.DueDate)
            {
                bonus = OnTimeReturnBonus;
            }

            var totalEarned = earnedPoints + bonus;

            var tx = new PointsTransaction
            {
                Id = _ledger.Count + 1,
                CustomerId = rental.CustomerId,
                RentalId = rentalId,
                Points = totalEarned,
                Type = TransactionType.Earned,
                Description = $"Earned {earnedPoints} pts for rental #{rentalId}" +
                    (bonus > 0 ? $" + {bonus} on-time bonus" : "") +
                    $" ({multiplier:0.##}x {customer.MembershipType} multiplier)",
                Timestamp = DateTime.Now
            };

            _ledger.Add(tx);
            return tx;
        }

        // ── Redeem points ────────────────────────────────────────────

        /// <summary>
        /// Redeem points for a reward. Returns the transaction if successful.
        /// </summary>
        public PointsTransaction RedeemPoints(int customerId, RewardType reward)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            var balance = GetBalance(customerId);
            var cost = GetRewardCost(reward);

            if (balance < cost)
                throw new InvalidOperationException(
                    $"Insufficient points. Need {cost}, have {balance}.");

            var tx = new PointsTransaction
            {
                Id = _ledger.Count + 1,
                CustomerId = customerId,
                RentalId = null,
                Points = -cost,
                Type = TransactionType.Redeemed,
                Description = $"Redeemed {cost} pts for {GetRewardDescription(reward)}",
                Timestamp = DateTime.Now
            };

            _ledger.Add(tx);
            return tx;
        }

        // ── Balance & history ────────────────────────────────────────

        /// <summary>
        /// Get the current points balance for a customer.
        /// </summary>
        public int GetBalance(int customerId)
        {
            return _ledger
                .Where(t => t.CustomerId == customerId)
                .Sum(t => t.Points);
        }

        /// <summary>
        /// Get the full transaction history for a customer.
        /// </summary>
        public List<PointsTransaction> GetHistory(int customerId)
        {
            return _ledger
                .Where(t => t.CustomerId == customerId)
                .OrderByDescending(t => t.Timestamp)
                .ToList();
        }

        /// <summary>
        /// Get a loyalty summary for a customer including balance, tier info,
        /// available rewards, and lifetime stats.
        /// </summary>
        public LoyaltySummary GetSummary(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException($"Customer {customerId} not found.");

            var history = GetHistory(customerId);
            var balance = history.Sum(t => t.Points);
            var totalEarned = history.Where(t => t.Points > 0).Sum(t => t.Points);
            var totalRedeemed = history.Where(t => t.Points < 0).Sum(t => Math.Abs(t.Points));

            var availableRewards = new List<AvailableReward>();
            foreach (RewardType reward in Enum.GetValues(typeof(RewardType)))
            {
                var cost = GetRewardCost(reward);
                availableRewards.Add(new AvailableReward
                {
                    Reward = reward,
                    Description = GetRewardDescription(reward),
                    PointsCost = cost,
                    CanAfford = balance >= cost,
                    PointsNeeded = Math.Max(0, cost - balance)
                });
            }

            return new LoyaltySummary
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                MembershipTier = customer.MembershipType,
                TierMultiplier = GetTierMultiplier(customer.MembershipType),
                CurrentBalance = balance,
                LifetimeEarned = totalEarned,
                LifetimeRedeemed = totalRedeemed,
                TransactionCount = history.Count,
                AvailableRewards = availableRewards,
                RecentTransactions = history.Take(10).ToList()
            };
        }

        // ── Leaderboard ──────────────────────────────────────────────

        /// <summary>
        /// Get a leaderboard of top loyalty members by points balance.
        /// </summary>
        public List<LeaderboardEntry> GetLeaderboard(int top = 10)
        {
            var customerIds = _ledger.Select(t => t.CustomerId).Distinct();
            var entries = new List<LeaderboardEntry>();

            foreach (var id in customerIds)
            {
                var customer = _customerRepository.GetById(id);
                if (customer == null) continue;

                var balance = GetBalance(id);
                var totalEarned = _ledger
                    .Where(t => t.CustomerId == id && t.Points > 0)
                    .Sum(t => t.Points);

                entries.Add(new LeaderboardEntry
                {
                    CustomerId = id,
                    CustomerName = customer.Name,
                    MembershipTier = customer.MembershipType,
                    CurrentBalance = balance,
                    LifetimeEarned = totalEarned
                });
            }

            return entries
                .OrderByDescending(e => e.CurrentBalance)
                .Take(top)
                .Select((e, i) => { e.Rank = i + 1; return e; })
                .ToList();
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>Get the points cost of a reward.</summary>
        public static int GetRewardCost(RewardType reward)
        {
            switch (reward)
            {
                case RewardType.FreeRental: return FreeRentalCost;
                case RewardType.HalfOffRental: return HalfOffCost;
                case RewardType.TierUpgradeBonus: return 1000;
                case RewardType.ExtendedRental: return 150;
                default: return int.MaxValue;
            }
        }

        /// <summary>Get a human-readable reward description.</summary>
        public static string GetRewardDescription(RewardType reward)
        {
            switch (reward)
            {
                case RewardType.FreeRental: return "Free Standard Rental";
                case RewardType.HalfOffRental: return "50% Off Next Rental";
                case RewardType.TierUpgradeBonus: return "Tier Upgrade Bonus";
                case RewardType.ExtendedRental: return "Extended Rental (+3 days)";
                default: return reward.ToString();
            }
        }
    }

    // ── Models ───────────────────────────────────────────────────────

    public enum TransactionType
    {
        Earned = 1,
        Redeemed = 2,
        Bonus = 3,
        Expired = 4
    }

    public enum RewardType
    {
        FreeRental = 1,
        HalfOffRental = 2,
        TierUpgradeBonus = 3,
        ExtendedRental = 4
    }

    public class PointsTransaction
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public int? RentalId { get; set; }
        public int Points { get; set; }
        public TransactionType Type { get; set; }
        public string Description { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class LoyaltySummary
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public MembershipType MembershipTier { get; set; }
        public decimal TierMultiplier { get; set; }
        public int CurrentBalance { get; set; }
        public int LifetimeEarned { get; set; }
        public int LifetimeRedeemed { get; set; }
        public int TransactionCount { get; set; }
        public List<AvailableReward> AvailableRewards { get; set; }
        public List<PointsTransaction> RecentTransactions { get; set; }
    }

    public class AvailableReward
    {
        public RewardType Reward { get; set; }
        public string Description { get; set; }
        public int PointsCost { get; set; }
        public bool CanAfford { get; set; }
        public int PointsNeeded { get; set; }
    }

    public class LeaderboardEntry
    {
        public int Rank { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public MembershipType MembershipTier { get; set; }
        public int CurrentBalance { get; set; }
        public int LifetimeEarned { get; set; }
    }
}
