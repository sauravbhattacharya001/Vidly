using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for insurance-related views.
    /// </summary>
    public class InsuranceViewModel
    {
        // ── Analytics Dashboard ──────────────────────────────────
        public InsuranceAnalytics Analytics { get; set; }
        public decimal UptakeRate { get; set; }

        // ── Policy Lists ─────────────────────────────────────────
        public List<InsurancePolicy> Policies { get; set; } = new List<InsurancePolicy>();
        public InsurancePolicy CurrentPolicy { get; set; }

        // ── Claims ───────────────────────────────────────────────
        public List<InsuranceClaim> Claims { get; set; } = new List<InsuranceClaim>();

        // ── Purchase Flow ────────────────────────────────────────
        public int RentalId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string MovieName { get; set; }
        public Dictionary<InsuranceTier, decimal> Quotes { get; set; }

        // ── File Claim Flow ──────────────────────────────────────
        public int PolicyId { get; set; }

        // ── UI State ─────────────────────────────────────────────
        public string Message { get; set; }
        public bool IsError { get; set; }

        // ── Top Claimers ─────────────────────────────────────────
        public List<KeyValuePair<int, int>> TopClaimers { get; set; } = new List<KeyValuePair<int, int>>();
    }
}
