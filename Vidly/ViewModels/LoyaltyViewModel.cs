using System.Collections.Generic;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the loyalty points dashboard, displaying a customer's
    /// points balance, tier info, available rewards, transaction history,
    /// and the store-wide leaderboard.
    /// </summary>
    public class LoyaltyViewModel
    {
        /// <summary>All customers for the selector dropdown.</summary>
        public IEnumerable<Customer> AllCustomers { get; set; }

        /// <summary>Currently selected customer (null if none selected).</summary>
        public Customer SelectedCustomer { get; set; }

        /// <summary>Full loyalty summary for the selected customer.</summary>
        public LoyaltySummary Summary { get; set; }

        /// <summary>Complete transaction history for the selected customer.</summary>
        public List<PointsTransaction> Transactions { get; set; }

        /// <summary>Store-wide loyalty leaderboard (top 10).</summary>
        public List<LeaderboardEntry> Leaderboard { get; set; }

        /// <summary>Status/error message to display.</summary>
        public string StatusMessage { get; set; }

        /// <summary>Whether a redemption was just completed.</summary>
        public bool RedemptionSuccess { get; set; }

        /// <summary>Description of the reward that was just redeemed.</summary>
        public string RedeemedRewardDescription { get; set; }

        /// <summary>
        /// Progress percentage toward the next available reward (0-100).
        /// </summary>
        public int NextRewardProgress { get; set; }

        /// <summary>
        /// Description of the cheapest reward the customer can't yet afford.
        /// </summary>
        public string NextRewardName { get; set; }

        /// <summary>
        /// Points still needed to reach the next reward.
        /// </summary>
        public int PointsToNextReward { get; set; }
    }
}
