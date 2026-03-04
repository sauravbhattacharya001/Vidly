using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Customer segmentation using RFM (Recency, Frequency, Monetary) analysis.
    /// Scores each customer on how recently they rented (R), how often (F),
    /// and how much they spent (M), then assigns marketing segments.
    /// </summary>
    public class CustomerSegmentationService
    {
        private readonly ICustomerRepository _customerRepo;
        private readonly IRentalRepository _rentalRepo;

        /// <summary>Number of quantile buckets for RFM scoring (1-5).</summary>
        public const int ScoreBuckets = 5;

        public CustomerSegmentationService(
            ICustomerRepository customerRepo,
            IRentalRepository rentalRepo)
        {
            _customerRepo = customerRepo
                ?? throw new ArgumentNullException(nameof(customerRepo));
            _rentalRepo = rentalRepo
                ?? throw new ArgumentNullException(nameof(rentalRepo));
        }

        // ── RFM Scoring ─────────────────────────────────────────────

        /// <summary>
        /// Compute RFM scores for all customers with rental history.
        /// </summary>
        /// <param name="asOfDate">Reference date for recency calculation.</param>
        /// <returns>RFM profiles sorted by composite score descending.</returns>
        public IReadOnlyList<RfmProfile> AnalyzeAll(DateTime asOfDate)
        {
            var customers = _customerRepo.GetAll();
            var allRentals = _rentalRepo.GetAll();

            // Build per-customer raw metrics
            var rawMetrics = new List<(int CustomerId, string Name, int DaysSinceLast, int RentalCount, decimal TotalSpend)>();

            foreach (var c in customers)
            {
                var rentals = allRentals.Where(r => r.CustomerId == c.Id).ToList();
                if (rentals.Count == 0) continue;

                var lastRental = rentals.Max(r => r.RentalDate);
                var daysSince = Math.Max(0, (int)(asOfDate - lastRental).TotalDays);
                var totalSpend = rentals.Sum(r => r.TotalCost);

                rawMetrics.Add((c.Id, c.Name, daysSince, rentals.Count, totalSpend));
            }

            if (rawMetrics.Count == 0)
                return Array.Empty<RfmProfile>();

            // Assign quintile scores (1-5)
            var recencyValues = rawMetrics.Select(m => (double)m.DaysSinceLast).ToList();
            var frequencyValues = rawMetrics.Select(m => (double)m.RentalCount).ToList();
            var monetaryValues = rawMetrics.Select(m => (double)m.TotalSpend).ToList();

            var recencyBreaks = GetQuantileBreaks(recencyValues, ScoreBuckets);
            var frequencyBreaks = GetQuantileBreaks(frequencyValues, ScoreBuckets);
            var monetaryBreaks = GetQuantileBreaks(monetaryValues, ScoreBuckets);

            var profiles = new List<RfmProfile>();
            foreach (var m in rawMetrics)
            {
                // Recency: lower days = higher score (inverted)
                var rScore = ScoreBuckets + 1 - AssignBucket(m.DaysSinceLast, recencyBreaks);
                // Frequency: higher count = higher score
                var fScore = AssignBucket(m.RentalCount, frequencyBreaks);
                // Monetary: higher spend = higher score
                var mScore = AssignBucket((double)m.TotalSpend, monetaryBreaks);

                var segment = ClassifySegment(rScore, fScore, mScore);

                profiles.Add(new RfmProfile
                {
                    CustomerId = m.CustomerId,
                    CustomerName = m.Name,
                    DaysSinceLastRental = m.DaysSinceLast,
                    RentalCount = m.RentalCount,
                    TotalSpend = m.TotalSpend,
                    RecencyScore = rScore,
                    FrequencyScore = fScore,
                    MonetaryScore = mScore,
                    CompositeScore = (rScore + fScore + mScore) / 3.0,
                    Segment = segment
                });
            }

            return profiles.OrderByDescending(p => p.CompositeScore)
                           .ThenBy(p => p.CustomerName)
                           .ToList();
        }

        /// <summary>
        /// Get the RFM profile for a single customer.
        /// </summary>
        public RfmProfile AnalyzeCustomer(int customerId, DateTime asOfDate)
        {
            var all = AnalyzeAll(asOfDate);
            return all.FirstOrDefault(p => p.CustomerId == customerId);
        }

        // ── Segment Queries ─────────────────────────────────────────

        /// <summary>
        /// Get all customers in a specific segment.
        /// </summary>
        public IReadOnlyList<RfmProfile> GetBySegment(CustomerSegment segment, DateTime asOfDate)
        {
            return AnalyzeAll(asOfDate).Where(p => p.Segment == segment).ToList();
        }

        /// <summary>
        /// Get segment distribution summary.
        /// </summary>
        public SegmentSummary GetSummary(DateTime asOfDate)
        {
            var profiles = AnalyzeAll(asOfDate);
            var distribution = new Dictionary<CustomerSegment, SegmentStats>();

            foreach (CustomerSegment seg in Enum.GetValues(typeof(CustomerSegment)))
            {
                var members = profiles.Where(p => p.Segment == seg).ToList();
                distribution[seg] = new SegmentStats
                {
                    Count = members.Count,
                    AverageSpend = members.Count > 0 ? members.Average(p => p.TotalSpend) : 0,
                    AverageFrequency = members.Count > 0 ? members.Average(p => p.RentalCount) : 0,
                    AverageRecencyDays = members.Count > 0 ? members.Average(p => p.DaysSinceLastRental) : 0
                };
            }

            return new SegmentSummary
            {
                TotalCustomersAnalyzed = profiles.Count,
                AsOfDate = asOfDate,
                Distribution = distribution
            };
        }

        /// <summary>
        /// Identify at-risk customers who used to be active but haven't rented recently.
        /// </summary>
        public IReadOnlyList<RfmProfile> GetAtRiskCustomers(DateTime asOfDate)
        {
            return AnalyzeAll(asOfDate)
                .Where(p => p.Segment == CustomerSegment.AtRisk
                         || p.Segment == CustomerSegment.CantLoseThem
                         || p.Segment == CustomerSegment.Hibernating)
                .OrderBy(p => p.RecencyScore)
                .ToList();
        }

        /// <summary>
        /// Get recommended marketing actions for each segment.
        /// </summary>
        public IReadOnlyDictionary<CustomerSegment, string> GetMarketingRecommendations()
        {
            return new Dictionary<CustomerSegment, string>
            {
                [CustomerSegment.Champions] = "Reward them. Offer early access to new releases. Ask for reviews.",
                [CustomerSegment.LoyalCustomers] = "Upsell premium memberships. Recommend related genres.",
                [CustomerSegment.PotentialLoyalists] = "Offer loyalty program enrollment. Personalized recommendations.",
                [CustomerSegment.NewCustomers] = "Welcome series. Provide onboarding tips and first-rental discounts.",
                [CustomerSegment.Promising] = "Create brand awareness. Offer trial promotions.",
                [CustomerSegment.NeedAttention] = "Make limited-time offers. Recommend popular titles they haven't seen.",
                [CustomerSegment.AboutToSleep] = "Send reactivation emails with personalized picks.",
                [CustomerSegment.AtRisk] = "Send strong win-back offers. Survey for feedback on why they left.",
                [CustomerSegment.CantLoseThem] = "Aggressive win-back: personal outreach, premium discounts, exclusive deals.",
                [CustomerSegment.Hibernating] = "Low-cost reactivation attempt. If unresponsive, reduce marketing frequency.",
                [CustomerSegment.Lost] = "Final win-back attempt, then archive. Don't over-invest."
            };
        }

        /// <summary>
        /// Compare two time periods to see how segments shifted.
        /// </summary>
        public IReadOnlyList<SegmentMigration> CompareSegments(DateTime periodA, DateTime periodB)
        {
            var profilesA = AnalyzeAll(periodA);
            var profilesB = AnalyzeAll(periodB);

            var migrations = new List<SegmentMigration>();
            var dictA = profilesA.ToDictionary(p => p.CustomerId);

            foreach (var b in profilesB)
            {
                if (dictA.TryGetValue(b.CustomerId, out var a) && a.Segment != b.Segment)
                {
                    migrations.Add(new SegmentMigration
                    {
                        CustomerId = b.CustomerId,
                        CustomerName = b.CustomerName,
                        FromSegment = a.Segment,
                        ToSegment = b.Segment,
                        ScoreChange = b.CompositeScore - a.CompositeScore
                    });
                }
            }

            return migrations.OrderByDescending(m => Math.Abs(m.ScoreChange)).ToList();
        }

        // ── Internals ───────────────────────────────────────────────

        internal static CustomerSegment ClassifySegment(int r, int f, int m)
        {
            // R, F, M each 1-5. Classification based on standard RFM segment mapping.
            var avg = (r + f + m) / 3.0;

            if (r >= 4 && f >= 4 && m >= 4) return CustomerSegment.Champions;
            if (f >= 4 && m >= 3) return CustomerSegment.LoyalCustomers;
            if (r >= 4 && f >= 2 && f <= 4) return CustomerSegment.PotentialLoyalists;
            if (r >= 4 && f <= 2) return CustomerSegment.NewCustomers;
            if (r >= 3 && f >= 2 && m >= 2) return CustomerSegment.Promising;
            if (r == 3 && f <= 3) return CustomerSegment.NeedAttention;
            if (r == 2 && f >= 2) return CustomerSegment.AboutToSleep;
            if (r == 2 && f <= 2) return CustomerSegment.AtRisk;
            if (r <= 2 && f >= 4) return CustomerSegment.CantLoseThem;
            if (r == 1 && f >= 2) return CustomerSegment.Hibernating;

            return CustomerSegment.Lost;
        }

        internal static List<double> GetQuantileBreaks(List<double> values, int buckets)
        {
            if (values.Count == 0) return new List<double>();
            var sorted = values.OrderBy(v => v).ToList();
            var breaks = new List<double>();

            for (int i = 1; i < buckets; i++)
            {
                var idx = (int)Math.Floor((double)i / buckets * sorted.Count);
                if (idx >= sorted.Count) idx = sorted.Count - 1;
                breaks.Add(sorted[idx]);
            }

            return breaks;
        }

        internal static int AssignBucket(double value, List<double> breaks)
        {
            for (int i = 0; i < breaks.Count; i++)
            {
                if (value <= breaks[i]) return i + 1;
            }
            return breaks.Count + 1; // highest bucket
        }
    }

    // ── DTOs ─────────────────────────────────────────────────────

    /// <summary>
    /// RFM profile for a single customer.
    /// </summary>
    public class RfmProfile
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int DaysSinceLastRental { get; set; }
        public int RentalCount { get; set; }
        public decimal TotalSpend { get; set; }
        /// <summary>Recency score (1=worst, 5=best — most recent).</summary>
        public int RecencyScore { get; set; }
        /// <summary>Frequency score (1=fewest, 5=most rentals).</summary>
        public int FrequencyScore { get; set; }
        /// <summary>Monetary score (1=lowest, 5=highest spend).</summary>
        public int MonetaryScore { get; set; }
        /// <summary>Average of R, F, M scores.</summary>
        public double CompositeScore { get; set; }
        public CustomerSegment Segment { get; set; }

        /// <summary>RFM code as a string, e.g. "5-4-3".</summary>
        public string RfmCode => $"{RecencyScore}-{FrequencyScore}-{MonetaryScore}";
    }

    /// <summary>
    /// Customer segments based on RFM analysis.
    /// </summary>
    public enum CustomerSegment
    {
        Champions,
        LoyalCustomers,
        PotentialLoyalists,
        NewCustomers,
        Promising,
        NeedAttention,
        AboutToSleep,
        AtRisk,
        CantLoseThem,
        Hibernating,
        Lost
    }

    /// <summary>
    /// Summary of segment distribution.
    /// </summary>
    public class SegmentSummary
    {
        public int TotalCustomersAnalyzed { get; set; }
        public DateTime AsOfDate { get; set; }
        public Dictionary<CustomerSegment, SegmentStats> Distribution { get; set; }
    }

    /// <summary>
    /// Statistics for a single segment.
    /// </summary>
    public class SegmentStats
    {
        public int Count { get; set; }
        public decimal AverageSpend { get; set; }
        public double AverageFrequency { get; set; }
        public double AverageRecencyDays { get; set; }
    }

    /// <summary>
    /// Tracks a customer's segment change between two periods.
    /// </summary>
    public class SegmentMigration
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public CustomerSegment FromSegment { get; set; }
        public CustomerSegment ToSegment { get; set; }
        public double ScoreChange { get; set; }
    }
}
