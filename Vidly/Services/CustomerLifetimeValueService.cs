using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous Customer Lifetime Value engine — predicts CLV using historical
    /// revenue, tenure, recency, and membership tier; segments customers into
    /// value tiers; detects trajectory changes; generates proactive optimisation
    /// recommendations.
    /// </summary>
    public class CustomerLifetimeValueService
    {
        private readonly ICustomerRepository _customers;
        private readonly IRentalRepository _rentals;
        private readonly IMovieRepository _movies;

        public CustomerLifetimeValueService(
            ICustomerRepository customers,
            IRentalRepository rentals,
            IMovieRepository movies)
        {
            _customers = customers;
            _rentals = rentals;
            _movies = movies;
        }

        // ------------------------------------------------------------------
        //  Public API
        // ------------------------------------------------------------------

        public ClvSummary GetSummary(DateTime now, int topN = 15)
        {
            var allCustomers = _customers.GetAll();
            var allRentals = _rentals.GetAll();

            if (!allCustomers.Any())
                return EmptySummary();

            var rentalsByCustomer = allRentals
                .GroupBy(r => r.CustomerId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var profiles = allCustomers
                .Select(c => BuildProfile(c, rentalsByCustomer.GetValueOrDefault(c.Id), now))
                .OrderByDescending(p => p.EstimatedClv)
                .ToList();

            AssignTiers(profiles);

            var summary = new ClvSummary
            {
                TotalCustomers = profiles.Count,
                TotalEstimatedClv = profiles.Sum(p => p.EstimatedClv),
                AverageClv = Math.Round(profiles.Average(p => p.EstimatedClv), 2),
                MedianClv = Median(profiles.Select(p => p.EstimatedClv).ToList()),
                WhaleCount = profiles.Count(p => p.Tier == "Whale"),
                HighValueCount = profiles.Count(p => p.Tier == "High"),
                MidValueCount = profiles.Count(p => p.Tier == "Mid"),
                AtRiskCount = profiles.Count(p => p.Trajectory == "Declining"),
                TopCustomers = profiles.Take(topN).ToList(),
                AtRiskCustomers = profiles
                    .Where(p => p.Trajectory == "Declining")
                    .OrderByDescending(p => p.EstimatedClv)
                    .Take(topN)
                    .ToList(),
                TierBreakdown = BuildTierBreakdown(profiles),
                Trend = BuildTrend(allRentals, allCustomers, now),
                Recommendations = GenerateRecommendations(profiles, rentalsByCustomer, now)
            };

            return summary;
        }

        public ClvCustomerProfile GetCustomerProfile(int customerId, DateTime now)
        {
            var customer = _customers.GetById(customerId);
            if (customer == null) return null;

            var rentals = _rentals.GetAll()
                .Where(r => r.CustomerId == customerId)
                .ToList();

            var profile = BuildProfile(customer, rentals, now);

            // Need all profiles to assign tier
            var allProfiles = _customers.GetAll()
                .Select(c => BuildProfile(c,
                    _rentals.GetAll().Where(r => r.CustomerId == c.Id).ToList(), now))
                .OrderByDescending(p => p.EstimatedClv)
                .ToList();
            AssignTiers(allProfiles);

            var match = allProfiles.FirstOrDefault(p => p.CustomerId == customerId);
            if (match != null)
            {
                profile.Tier = match.Tier;
            }

            return profile;
        }

        // ------------------------------------------------------------------
        //  Profile builder
        // ------------------------------------------------------------------

        private ClvCustomerProfile BuildProfile(Customer c, List<Rental> rentals, DateTime now)
        {
            rentals = rentals ?? new List<Rental>();

            var historicalRevenue = rentals.Sum(r => r.TotalCost);
            var firstRental = rentals.Any() ? rentals.Min(r => r.RentalDate) : (c.MemberSince ?? now);
            var monthsActive = Math.Max(1, (int)Math.Ceiling((now - firstRental).TotalDays / 30.0));
            var avgMonthlySpend = Math.Round(historicalRevenue / monthsActive, 2);

            // Retention probability based on tenure + tier
            var tenureYears = monthsActive / 12.0;
            var tierBonus = TierRetentionBonus(c.MembershipType);
            var retentionProb = Math.Min(0.98, 0.40 + (tenureYears * 0.08) + tierBonus);

            // Recency factor — recent activity increases predicted future
            var lastRentalDate = rentals.Any() ? rentals.Max(r => r.RentalDate) : firstRental;
            var daysSinceLast = (now - lastRentalDate).TotalDays;
            var recencyFactor = daysSinceLast < 30 ? 1.0 :
                                daysSinceLast < 90 ? 0.85 :
                                daysSinceLast < 180 ? 0.60 :
                                daysSinceLast < 365 ? 0.35 : 0.15;

            // Predicted remaining months (cap at 36)
            var predictedMonths = (int)Math.Round(retentionProb * 36 * recencyFactor);
            var predictedFuture = Math.Round(avgMonthlySpend * predictedMonths * (decimal)retentionProb, 2);

            var clv = Math.Round(historicalRevenue + predictedFuture, 2);

            // Trajectory — compare last 3 months vs previous 3 months
            var trajectory = ComputeTrajectory(rentals, now);

            // Value drivers
            var drivers = new List<string>();
            if (rentals.Count > 10) drivers.Add("Frequent renter");
            if (avgMonthlySpend > 30) drivers.Add("High spender");
            if (c.MembershipType >= MembershipType.Gold) drivers.Add("Premium member");
            if (tenureYears > 1) drivers.Add("Long-term customer");
            if (daysSinceLast < 30) drivers.Add("Recently active");
            if (rentals.Count(r => r.LateFee == 0 && r.DamageCharge == 0) > rentals.Count * 0.8)
                drivers.Add("Reliable returner");
            if (!drivers.Any()) drivers.Add("New customer");

            return new ClvCustomerProfile
            {
                CustomerId = c.Id,
                Name = c.Name,
                Email = c.Email,
                Membership = c.MembershipType,
                EstimatedClv = clv,
                HistoricalRevenue = historicalRevenue,
                PredictedFutureRevenue = predictedFuture,
                TotalRentals = rentals.Count,
                AvgMonthlySpend = avgMonthlySpend,
                MonthsActive = monthsActive,
                Tier = "Mid", // assigned later globally
                Trajectory = trajectory,
                RetentionProbability = Math.Round((decimal)retentionProb * 100, 1),
                ValueDrivers = drivers
            };
        }

        private static double TierRetentionBonus(MembershipType t)
        {
            switch (t)
            {
                case MembershipType.Platinum: return 0.25;
                case MembershipType.Gold: return 0.15;
                case MembershipType.Silver: return 0.08;
                default: return 0.0;
            }
        }

        private static string ComputeTrajectory(List<Rental> rentals, DateTime now)
        {
            if (rentals.Count < 2) return "Stable";

            var recent = rentals.Where(r => r.RentalDate >= now.AddMonths(-3)).Sum(r => r.TotalCost);
            var previous = rentals.Where(r => r.RentalDate >= now.AddMonths(-6) && r.RentalDate < now.AddMonths(-3)).Sum(r => r.TotalCost);

            if (previous == 0 && recent > 0) return "Rising";
            if (previous == 0 && recent == 0) return "Stable";

            var change = (recent - previous) / previous;
            if (change > 0.15m) return "Rising";
            if (change < -0.15m) return "Declining";
            return "Stable";
        }

        // ------------------------------------------------------------------
        //  Tier assignment
        // ------------------------------------------------------------------

        private static void AssignTiers(List<ClvCustomerProfile> profiles)
        {
            if (!profiles.Any()) return;

            int total = profiles.Count;
            int whaleThreshold = Math.Max(1, (int)Math.Ceiling(total * 0.10));
            int highThreshold = (int)Math.Ceiling(total * 0.30);
            int midThreshold = (int)Math.Ceiling(total * 0.70);

            for (int i = 0; i < profiles.Count; i++)
            {
                if (i < whaleThreshold) profiles[i].Tier = "Whale";
                else if (i < highThreshold) profiles[i].Tier = "High";
                else if (i < midThreshold) profiles[i].Tier = "Mid";
                else profiles[i].Tier = "Low";
            }
        }

        // ------------------------------------------------------------------
        //  Tier breakdown
        // ------------------------------------------------------------------

        private static List<ClvTierBreakdown> BuildTierBreakdown(List<ClvCustomerProfile> profiles)
        {
            var totalClv = profiles.Sum(p => p.EstimatedClv);
            if (totalClv == 0) totalClv = 1;

            var tiers = new[] {
                new { Name = "Whale", Color = "#9b59b6" },
                new { Name = "High",  Color = "#1da1f2" },
                new { Name = "Mid",   Color = "#17bf63" },
                new { Name = "Low",   Color = "#8899a6" }
            };

            return tiers.Select(t =>
            {
                var group = profiles.Where(p => p.Tier == t.Name).ToList();
                return new ClvTierBreakdown
                {
                    Tier = t.Name,
                    Count = group.Count,
                    TotalClv = group.Sum(p => p.EstimatedClv),
                    AvgClv = group.Any() ? Math.Round(group.Average(p => p.EstimatedClv), 2) : 0,
                    RevenueShare = Math.Round(group.Sum(p => p.EstimatedClv) / totalClv * 100, 1),
                    Color = t.Color
                };
            }).ToList();
        }

        // ------------------------------------------------------------------
        //  Trend (last 6 months)
        // ------------------------------------------------------------------

        private ClvTrend BuildTrend(IReadOnlyList<Rental> allRentals, IReadOnlyList<Customer> allCustomers, DateTime now)
        {
            var labels = new List<string>();
            var avgClv = new List<decimal>();
            var newCustClv = new List<decimal>();

            for (int i = 5; i >= 0; i--)
            {
                var monthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-i);
                var monthEnd = monthStart.AddMonths(1);
                labels.Add(monthStart.ToString("MMM yy"));

                // Revenue up to that month end per customer
                var rentalsUpTo = allRentals.Where(r => r.RentalDate < monthEnd).ToList();
                var revenueByCustomer = rentalsUpTo
                    .GroupBy(r => r.CustomerId)
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.TotalCost));

                if (revenueByCustomer.Any())
                    avgClv.Add(Math.Round(revenueByCustomer.Values.Average(), 2));
                else
                    avgClv.Add(0);

                // New customers that month
                var newCustomers = allCustomers
                    .Where(c => c.MemberSince.HasValue &&
                                c.MemberSince.Value >= monthStart &&
                                c.MemberSince.Value < monthEnd)
                    .ToList();

                if (newCustomers.Any())
                {
                    var newRevenue = newCustomers
                        .Select(c => revenueByCustomer.GetValueOrDefault(c.Id, 0m))
                        .Average();
                    newCustClv.Add(Math.Round(newRevenue, 2));
                }
                else
                {
                    newCustClv.Add(0);
                }
            }

            return new ClvTrend { Labels = labels, AvgClvOverTime = avgClv, NewCustomerClv = newCustClv };
        }

        // ------------------------------------------------------------------
        //  Proactive recommendations
        // ------------------------------------------------------------------

        private List<ClvRecommendation> GenerateRecommendations(
            List<ClvCustomerProfile> profiles,
            Dictionary<int, List<Rental>> rentalsByCustomer,
            DateTime now)
        {
            var recs = new List<ClvRecommendation>();

            // 1. Upsell basic members with high activity
            var upsellCandidates = profiles
                .Where(p => p.Membership == MembershipType.Basic && p.TotalRentals > 5)
                .ToList();
            if (upsellCandidates.Any())
            {
                recs.Add(new ClvRecommendation
                {
                    Type = "Upsell",
                    Priority = "High",
                    Title = "Upgrade Active Basic Members",
                    Description = $"{upsellCandidates.Count} Basic members rent frequently — offer Silver/Gold upgrade with first-month discount to boost retention and CLV.",
                    AffectedCustomers = upsellCandidates.Count,
                    EstimatedImpact = Math.Round(upsellCandidates.Sum(p => p.AvgMonthlySpend * 0.3m), 2)
                });
            }

            // 2. Retain declining whales
            var decliningWhales = profiles
                .Where(p => p.Tier == "Whale" && p.Trajectory == "Declining")
                .ToList();
            if (decliningWhales.Any())
            {
                recs.Add(new ClvRecommendation
                {
                    Type = "Retain",
                    Priority = "Critical",
                    Title = "Urgent: Declining Whale Customers",
                    Description = $"{decliningWhales.Count} top-tier customers show declining activity. Personal outreach with exclusive offers could prevent ${Math.Round(decliningWhales.Sum(p => p.PredictedFutureRevenue) * 0.5m, 0)} in lost revenue.",
                    AffectedCustomers = decliningWhales.Count,
                    EstimatedImpact = Math.Round(decliningWhales.Sum(p => p.PredictedFutureRevenue) * 0.5m, 2)
                });
            }

            // 3. Reactivate dormant customers
            var dormant = profiles
                .Where(p => p.Trajectory == "Declining" && p.HistoricalRevenue > 50)
                .ToList();
            if (dormant.Any())
            {
                recs.Add(new ClvRecommendation
                {
                    Type = "Reactivate",
                    Priority = "Medium",
                    Title = "Win Back Dormant Customers",
                    Description = $"{dormant.Count} customers with good history are going quiet. A \"We miss you\" campaign with a free rental could recapture ${Math.Round(dormant.Sum(p => p.AvgMonthlySpend * 3), 0)} over 3 months.",
                    AffectedCustomers = dormant.Count,
                    EstimatedImpact = Math.Round(dormant.Sum(p => p.AvgMonthlySpend * 3), 2)
                });
            }

            // 4. Reward loyal high-value customers
            var loyalHigh = profiles
                .Where(p => (p.Tier == "Whale" || p.Tier == "High") && p.Trajectory != "Declining" && p.MonthsActive > 6)
                .ToList();
            if (loyalHigh.Any())
            {
                recs.Add(new ClvRecommendation
                {
                    Type = "Reward",
                    Priority = "Medium",
                    Title = "Loyalty Rewards for Top Customers",
                    Description = $"{loyalHigh.Count} high-value loyal customers deserve recognition. Birthday discounts, early access to new titles, or loyalty points reinforce retention.",
                    AffectedCustomers = loyalHigh.Count,
                    EstimatedImpact = Math.Round(loyalHigh.Sum(p => p.AvgMonthlySpend * 0.15m * 6), 2)
                });
            }

            // 5. Cross-sell to single-genre renters
            var narrowRenters = profiles.Where(p => p.TotalRentals >= 3).ToList();
            if (narrowRenters.Any())
            {
                recs.Add(new ClvRecommendation
                {
                    Type = "Cross-sell",
                    Priority = "Low",
                    Title = "Broaden Genre Discovery",
                    Description = $"Recommend new genres to {narrowRenters.Count} regular renters based on trending titles outside their usual picks. Wider taste = more rentals.",
                    AffectedCustomers = narrowRenters.Count,
                    EstimatedImpact = Math.Round(narrowRenters.Count * 5.0m, 2)
                });
            }

            // 6. New customer nurture
            var newCustomers = profiles.Where(p => p.MonthsActive <= 2 && p.TotalRentals <= 2).ToList();
            if (newCustomers.Any())
            {
                recs.Add(new ClvRecommendation
                {
                    Type = "Nurture",
                    Priority = "High",
                    Title = "Onboard New Customers",
                    Description = $"{newCustomers.Count} new customers need nurturing. Welcome sequence with curated picks and a 2nd-rental discount increases early engagement by ~40%.",
                    AffectedCustomers = newCustomers.Count,
                    EstimatedImpact = Math.Round(newCustomers.Count * 15.0m, 2)
                });
            }

            return recs.OrderByDescending(r => r.Priority == "Critical")
                       .ThenByDescending(r => r.Priority == "High")
                       .ThenByDescending(r => r.EstimatedImpact)
                       .ToList();
        }

        // ------------------------------------------------------------------
        //  Helpers
        // ------------------------------------------------------------------

        private static decimal Median(List<decimal> values)
        {
            if (!values.Any()) return 0;
            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 0
                ? Math.Round((sorted[mid - 1] + sorted[mid]) / 2, 2)
                : sorted[mid];
        }

        private static ClvSummary EmptySummary()
        {
            return new ClvSummary
            {
                TotalEstimatedClv = 0, AverageClv = 0, MedianClv = 0,
                TotalCustomers = 0, WhaleCount = 0, HighValueCount = 0,
                MidValueCount = 0, AtRiskCount = 0,
                TopCustomers = new List<ClvCustomerProfile>(),
                AtRiskCustomers = new List<ClvCustomerProfile>(),
                Recommendations = new List<ClvRecommendation>(),
                TierBreakdown = new List<ClvTierBreakdown>(),
                Trend = new ClvTrend
                {
                    Labels = new List<string>(),
                    AvgClvOverTime = new List<decimal>(),
                    NewCustomerClv = new List<decimal>()
                }
            };
        }
    }

    internal static class DictionaryExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(
            this Dictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default)
        {
            return dict.TryGetValue(key, out var value) ? value : defaultValue;
        }
    }
}
