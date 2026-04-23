using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous RFM-based customer segmentation engine. Analyzes rental history
    /// to classify customers into behavioral segments, tracks segment migration,
    /// and generates proactive campaign recommendations.
    /// </summary>
    public class SegmentationService
    {
        private readonly IMovieRepository _movieRepo;
        private readonly IRentalRepository _rentalRepo;
        private readonly ICustomerRepository _customerRepo;

        // ── Segment definitions ────────────────────────────────────
        private static readonly Dictionary<string, string> SegmentColors =
            new Dictionary<string, string>
            {
                { "Champions",   "#22c55e" },
                { "Loyal",       "#3b82f6" },
                { "Potential",   "#f59e0b" },
                { "At Risk",     "#f97316" },
                { "Hibernating", "#ef4444" }
            };

        public SegmentationService(
            IMovieRepository movieRepo,
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo)
        {
            _movieRepo = movieRepo ?? throw new ArgumentNullException(nameof(movieRepo));
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _customerRepo = customerRepo ?? throw new ArgumentNullException(nameof(customerRepo));
        }

        // ── Public API ─────────────────────────────────────────────

        /// <summary>Build the full fleet segmentation analysis.</summary>
        public SegmentationFleet BuildFleet()
        {
            var customers = _customerRepo.GetAll().ToList();
            var allRentals = _rentalRepo.GetAll().ToList();
            var rentalsByCustomer = allRentals
                .GroupBy(r => r.CustomerId)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.RentalDate).ToList());

            // Compute raw RFM values per customer
            var rawProfiles = new List<RawRfm>();
            foreach (var c in customers)
            {
                rentalsByCustomer.TryGetValue(c.Id, out var rentals);
                var list = rentals ?? new List<Rental>();
                rawProfiles.Add(new RawRfm
                {
                    CustomerId = c.Id,
                    CustomerName = c.Name,
                    TotalRentals = list.Count,
                    TotalSpend = list.Sum(r => r.TotalCost),
                    DaysSinceLast = list.Count > 0
                        ? (DateTime.Today - list.Max(r => r.RentalDate)).TotalDays
                        : 9999,
                    Rentals = list
                });
            }

            // Normalize 0-1
            double maxDays = rawProfiles.Max(p => p.DaysSinceLast);
            double maxFreq = Math.Max(1, rawProfiles.Max(p => p.TotalRentals));
            double maxSpend = Math.Max(1, (double)rawProfiles.Max(p => p.TotalSpend));

            var segments = new List<CustomerSegment>();
            foreach (var raw in rawProfiles)
            {
                double recency = maxDays > 0 ? 1.0 - (raw.DaysSinceLast / maxDays) : 1.0;
                double frequency = raw.TotalRentals / maxFreq;
                double monetary = (double)raw.TotalSpend / maxSpend;
                double rfm = (recency * 0.30 + frequency * 0.35 + monetary * 0.35) * 100;

                string segment = Classify(rfm);
                string prevSegment = ComputePreviousSegment(raw.Rentals, maxDays, maxFreq, maxSpend);
                string migration = GetMigration(prevSegment, segment);

                segments.Add(new CustomerSegment
                {
                    CustomerId = raw.CustomerId,
                    CustomerName = raw.CustomerName,
                    TotalRentals = raw.TotalRentals,
                    TotalSpend = raw.TotalSpend,
                    Recency = Math.Round(recency, 3),
                    Frequency = Math.Round(frequency, 3),
                    Monetary = Math.Round(monetary, 3),
                    RfmScore = Math.Round(rfm, 1),
                    Segment = segment,
                    PreviousSegment = prevSegment,
                    MigrationDirection = migration,
                    CampaignRecommendations = GetCampaigns(segment)
                });
            }

            // Summaries
            var summaries = SegmentColors.Keys.Select(seg =>
            {
                var members = segments.Where(s => s.Segment == seg).ToList();
                return new SegmentSummary
                {
                    Segment = seg,
                    Count = members.Count,
                    Percentage = customers.Count > 0
                        ? Math.Round(100.0 * members.Count / customers.Count, 1) : 0,
                    AvgRfmScore = members.Count > 0 ? Math.Round(members.Average(m => m.RfmScore), 1) : 0,
                    AvgRecency = members.Count > 0 ? Math.Round(members.Average(m => m.Recency), 3) : 0,
                    AvgFrequency = members.Count > 0 ? Math.Round(members.Average(m => m.Frequency), 3) : 0,
                    AvgMonetary = members.Count > 0 ? Math.Round(members.Average(m => m.Monetary), 3) : 0,
                    Color = SegmentColors[seg]
                };
            }).ToList();

            // Migrations
            var migrations = segments
                .Where(s => s.PreviousSegment != s.Segment && s.PreviousSegment != "New")
                .GroupBy(s => new { s.PreviousSegment, s.Segment })
                .Select(g => new MigrationFlow
                {
                    FromSegment = g.Key.PreviousSegment,
                    ToSegment = g.Key.Segment,
                    Count = g.Count()
                })
                .OrderByDescending(m => m.Count)
                .ToList();

            // Health & insights
            int healthy = segments.Count(s => s.Segment == "Champions" || s.Segment == "Loyal");
            double health = customers.Count > 0
                ? Math.Round(100.0 * healthy / customers.Count, 1) : 0;

            var insights = GenerateInsights(segments, summaries, migrations, health);

            return new SegmentationFleet
            {
                Customers = segments.OrderByDescending(s => s.RfmScore).ToList(),
                Summaries = summaries,
                Migrations = migrations,
                TotalCustomers = customers.Count,
                OverallHealthScore = health,
                ProactiveInsights = insights,
                GeneratedAt = DateTime.UtcNow.ToString("O")
            };
        }

        /// <summary>Build a single customer segment profile.</summary>
        public CustomerSegment BuildCustomerSegment(int customerId)
        {
            var fleet = BuildFleet();
            return fleet.Customers.FirstOrDefault(c => c.CustomerId == customerId);
        }

        // ── Private helpers ────────────────────────────────────────

        private static string Classify(double rfm)
        {
            if (rfm > 80) return "Champions";
            if (rfm > 60) return "Loyal";
            if (rfm > 40) return "Potential";
            if (rfm > 20) return "At Risk";
            return "Hibernating";
        }

        private static readonly int[] SegmentRanks = { 0, 1, 2, 3, 4 };
        private static int SegmentRank(string seg)
        {
            switch (seg)
            {
                case "Champions": return 4;
                case "Loyal": return 3;
                case "Potential": return 2;
                case "At Risk": return 1;
                default: return 0;
            }
        }

        private static string GetMigration(string prev, string current)
        {
            if (prev == "New") return "New";
            int p = SegmentRank(prev), c = SegmentRank(current);
            if (c > p) return "Upgraded";
            if (c < p) return "Downgraded";
            return "Stable";
        }

        /// <summary>
        /// Estimate previous segment using only the first half of rental history.
        /// </summary>
        private string ComputePreviousSegment(
            List<Rental> rentals, double maxDays, double maxFreq, double maxSpend)
        {
            if (rentals.Count < 2) return "New";

            int half = rentals.Count / 2;
            var firstHalf = rentals.Take(half).ToList();

            double daysSince = (DateTime.Today - firstHalf.Max(r => r.RentalDate)).TotalDays;
            double recency = maxDays > 0 ? 1.0 - (daysSince / maxDays) : 1.0;
            double frequency = firstHalf.Count / maxFreq;
            double monetary = (double)firstHalf.Sum(r => r.TotalCost) / maxSpend;
            double rfm = (recency * 0.30 + frequency * 0.35 + monetary * 0.35) * 100;
            return Classify(rfm);
        }

        private static List<string> GetCampaigns(string segment)
        {
            switch (segment)
            {
                case "Champions":
                    return new List<string>
                    {
                        "VIP early-access to new releases",
                        "Exclusive loyalty rewards & double points",
                        "Referral bonus program — champions bring champions",
                        "Personalized curator picks email"
                    };
                case "Loyal":
                    return new List<string>
                    {
                        "Upgrade path to VIP tier with next-rental bonus",
                        "Genre-specific bundle deals based on preferences",
                        "Birthday/anniversary rental credits",
                        "Invite to movie night events"
                    };
                case "Potential":
                    return new List<string>
                    {
                        "First-month subscription discount",
                        "Curated 'Starter Pack' recommendations",
                        "Engagement nudge: trending picks this week",
                        "Free rental with 3-rental purchase"
                    };
                case "At Risk":
                    return new List<string>
                    {
                        "Win-back offer: 50% off next rental",
                        "'We miss you' personalized email with top picks",
                        "Survey: what would bring you back?",
                        "Limited-time free trial of premium tier"
                    };
                default: // Hibernating
                    return new List<string>
                    {
                        "Reactivation campaign: free rental credit",
                        "Highlight new inventory since last visit",
                        "Deep discount bundle: 5 rentals for price of 2",
                        "Final outreach before account archival"
                    };
            }
        }

        private static List<string> GenerateInsights(
            List<CustomerSegment> customers,
            List<SegmentSummary> summaries,
            List<MigrationFlow> migrations,
            double health)
        {
            var insights = new List<string>();

            // Health assessment
            if (health >= 60)
                insights.Add($"Strong customer base: {health}% in Champions + Loyal segments.");
            else if (health >= 35)
                insights.Add($"Moderate health: {health}% in top segments — growth opportunity exists.");
            else
                insights.Add($"⚠ Low health score ({health}%) — prioritize retention campaigns immediately.");

            // Largest segment
            var largest = summaries.OrderByDescending(s => s.Count).First();
            insights.Add($"Largest segment: {largest.Segment} ({largest.Count} customers, {largest.Percentage}%).");

            // Migration warnings
            int downgraded = customers.Count(c => c.MigrationDirection == "Downgraded");
            int upgraded = customers.Count(c => c.MigrationDirection == "Upgraded");
            if (downgraded > upgraded && downgraded > 0)
                insights.Add($"⚠ Net negative migration: {downgraded} downgraded vs {upgraded} upgraded. Investigate churn drivers.");
            else if (upgraded > downgraded && upgraded > 0)
                insights.Add($"Positive migration trend: {upgraded} upgraded vs {downgraded} downgraded.");

            // At-risk + hibernating action
            int atRisk = customers.Count(c => c.Segment == "At Risk" || c.Segment == "Hibernating");
            if (atRisk > 0)
                insights.Add($"Action needed: {atRisk} customers in At Risk or Hibernating — deploy win-back campaigns.");

            // Champion opportunity
            var potentialToChamp = customers
                .Where(c => c.Segment == "Loyal" && c.RfmScore > 75)
                .ToList();
            if (potentialToChamp.Count > 0)
                insights.Add($"{potentialToChamp.Count} Loyal customers near Champion threshold — targeted push could convert them.");

            return insights;
        }

        // ── Internal types ─────────────────────────────────────────

        private class RawRfm
        {
            public int CustomerId { get; set; }
            public string CustomerName { get; set; }
            public int TotalRentals { get; set; }
            public decimal TotalSpend { get; set; }
            public double DaysSinceLast { get; set; }
            public List<Rental> Rentals { get; set; }
        }
    }
}
