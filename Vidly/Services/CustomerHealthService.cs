using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidly.Services
{
    #region Result Models

    public enum HealthTier { Thriving, Healthy, AtRisk, Critical, Churned }
    public enum TrendDirection { Improving, Stable, Declining }

    public class DimensionScore
    {
        public string Name { get; set; }
        public double Score { get; set; }
        public double Max { get; set; }
        public string Detail { get; set; }
    }

    public class CustomerHealthResult
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public double HealthScore { get; set; }
        public HealthTier Tier { get; set; }
        public TrendDirection Trend { get; set; }
        public List<DimensionScore> Dimensions { get; set; }
        public List<string> Recommendations { get; set; }
        public List<SimulatedRental> RecentRentals { get; set; }
        public Dictionary<string, int> GenreDistribution { get; set; }
    }

    public class FleetHealthResult
    {
        public double OverallScore { get; set; }
        public int TotalCustomers { get; set; }
        public Dictionary<HealthTier, int> TierDistribution { get; set; }
        public int Improving { get; set; }
        public int Stable { get; set; }
        public int Declining { get; set; }
        public List<CustomerHealthResult> AllCustomers { get; set; }
    }

    public class HealthAlert
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public HealthTier PreviousTier { get; set; }
        public HealthTier CurrentTier { get; set; }
        public double HealthScore { get; set; }
        public string Message { get; set; }
    }

    public class SimulatedRental
    {
        public string MovieName { get; set; }
        public string Genre { get; set; }
        public DateTime DateRented { get; set; }
        public DateTime? DateReturned { get; set; }
        public bool WasLate { get; set; }
        public decimal AmountPaid { get; set; }
    }

    #endregion

    public class CustomerHealthService
    {
        private static readonly Random Rng = new Random(42);
        private static readonly string[] Genres = { "Action", "Comedy", "Drama", "Sci-Fi", "Horror", "Romance", "Thriller", "Documentary", "Animation", "Fantasy" };
        private static readonly string[] FirstNames = { "Alice", "Bob", "Carlos", "Diana", "Erik", "Fiona", "George", "Hannah", "Ivan", "Julia", "Kevin", "Laura", "Marcus", "Nina", "Oscar", "Priya", "Quinn", "Rachel", "Sam", "Tara" };
        private static readonly string[] LastNames = { "Smith", "Johnson", "Chen", "Garcia", "Kim", "Patel", "Wilson", "Brown", "Lee", "Davis" };
        private static readonly string[] MovieAdj = { "The Great", "Return of", "Last", "Dark", "Golden", "Silent", "Eternal", "Mystic", "Iron", "Crystal" };
        private static readonly string[] MovieNoun = { "Journey", "Storm", "Legacy", "Shadow", "Dawn", "Horizon", "Echo", "Flame", "Tide", "Dream" };

        private readonly List<SimulatedCustomer> _customers;
        private readonly Dictionary<int, HealthTier> _previousTiers;

        private class SimulatedCustomer
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public List<SimulatedRental> Rentals { get; set; }
        }

        public CustomerHealthService()
        {
            _customers = GenerateCustomers(20);
            _previousTiers = new Dictionary<int, HealthTier>();
            // Simulate previous tiers slightly better than current for some
            foreach (var c in _customers)
            {
                var score = ComputeScore(c);
                var tier = ClassifyTier(score.total);
                // 30% chance previous tier was one level better
                if (Rng.NextDouble() < 0.3 && (int)tier > 0)
                    _previousTiers[c.Id] = (HealthTier)((int)tier - 1);
                else
                    _previousTiers[c.Id] = tier;
            }
        }

        public CustomerHealthResult GetCustomerHealth(int customerId)
        {
            var c = _customers.FirstOrDefault(x => x.Id == customerId);
            if (c == null) return null;
            return BuildResult(c);
        }

        public FleetHealthResult GetFleetHealth()
        {
            var results = _customers.Select(BuildResult).ToList();
            var tierDist = Enum.GetValues(typeof(HealthTier)).Cast<HealthTier>()
                .ToDictionary(t => t, t => results.Count(r => r.Tier == t));
            return new FleetHealthResult
            {
                OverallScore = results.Any() ? Math.Round(results.Average(r => r.HealthScore), 1) : 0,
                TotalCustomers = results.Count,
                TierDistribution = tierDist,
                Improving = results.Count(r => r.Trend == TrendDirection.Improving),
                Stable = results.Count(r => r.Trend == TrendDirection.Stable),
                Declining = results.Count(r => r.Trend == TrendDirection.Declining),
                AllCustomers = results.OrderByDescending(r => r.HealthScore).ToList()
            };
        }

        public Dictionary<HealthTier, int> GetTierDistribution()
        {
            return GetFleetHealth().TierDistribution;
        }

        public List<HealthAlert> AutoMonitor()
        {
            var alerts = new List<HealthAlert>();
            foreach (var c in _customers)
            {
                var result = BuildResult(c);
                if (_previousTiers.ContainsKey(c.Id) && result.Tier > _previousTiers[c.Id])
                {
                    alerts.Add(new HealthAlert
                    {
                        CustomerId = c.Id,
                        CustomerName = c.Name,
                        PreviousTier = _previousTiers[c.Id],
                        CurrentTier = result.Tier,
                        HealthScore = result.HealthScore,
                        Message = $"{c.Name} dropped from {_previousTiers[c.Id]} to {result.Tier} (score: {result.HealthScore})"
                    });
                }
            }
            return alerts.OrderBy(a => a.HealthScore).ToList();
        }

        public List<string> GetRecommendations(int customerId)
        {
            var r = GetCustomerHealth(customerId);
            return r?.Recommendations ?? new List<string>();
        }

        public TrendDirection GetHealthTrend(int customerId)
        {
            var c = _customers.FirstOrDefault(x => x.Id == customerId);
            if (c == null) return TrendDirection.Stable;
            var score = ComputeScore(c);
            var currentTier = ClassifyTier(score.total);
            if (!_previousTiers.ContainsKey(customerId)) return TrendDirection.Stable;
            var prev = _previousTiers[customerId];
            if (currentTier < prev) return TrendDirection.Improving;
            if (currentTier > prev) return TrendDirection.Declining;
            return TrendDirection.Stable;
        }

        #region Private Helpers

        private CustomerHealthResult BuildResult(SimulatedCustomer c)
        {
            var (total, dims) = ComputeScore(c);
            var tier = ClassifyTier(total);
            var trend = TrendDirection.Stable;
            if (_previousTiers.ContainsKey(c.Id))
            {
                var prev = _previousTiers[c.Id];
                if (tier < prev) trend = TrendDirection.Improving;
                else if (tier > prev) trend = TrendDirection.Declining;
            }

            var genreDist = c.Rentals.GroupBy(r => r.Genre)
                .ToDictionary(g => g.Key, g => g.Count());

            return new CustomerHealthResult
            {
                CustomerId = c.Id,
                CustomerName = c.Name,
                HealthScore = total,
                Tier = tier,
                Trend = trend,
                Dimensions = dims,
                Recommendations = GenerateRecommendations(c, dims, tier),
                RecentRentals = c.Rentals.OrderByDescending(r => r.DateRented).Take(10).ToList(),
                GenreDistribution = genreDist
            };
        }

        private (double total, List<DimensionScore> dims) ComputeScore(SimulatedCustomer c)
        {
            var now = DateTime.UtcNow;
            var rentals = c.Rentals;

            // Rental Frequency (0-25)
            int last30 = rentals.Count(r => (now - r.DateRented).TotalDays <= 30);
            int last60 = rentals.Count(r => (now - r.DateRented).TotalDays <= 60);
            int last90 = rentals.Count(r => (now - r.DateRented).TotalDays <= 90);
            double freqScore = Math.Min(25, (last30 * 3.0 + last60 * 1.5 + last90 * 0.5));
            freqScore = Math.Round(freqScore, 1);

            // Return Behavior (0-25)
            var returned = rentals.Where(r => r.DateReturned.HasValue).ToList();
            double onTimeRate = returned.Any() ? returned.Count(r => !r.WasLate) / (double)returned.Count : 1.0;
            double returnScore = Math.Round(onTimeRate * 25, 1);

            // Spending Trend (0-25)
            decimal spend30 = rentals.Where(r => (now - r.DateRented).TotalDays <= 30).Sum(r => r.AmountPaid);
            decimal spend90 = rentals.Where(r => (now - r.DateRented).TotalDays <= 90).Sum(r => r.AmountPaid);
            double avgMonthly90 = spend90 > 0 ? (double)(spend90 / 3m) : 0;
            double spendRatio = avgMonthly90 > 0 ? (double)spend30 / avgMonthly90 : ((double)spend30 > 0 ? 2.0 : 0);
            double spendScore = Math.Round(Math.Min(25, spendRatio * 12.5), 1);

            // Engagement (0-25)
            double recencyDays = rentals.Any() ? (now - rentals.Max(r => r.DateRented)).TotalDays : 999;
            double recencyScore = Math.Max(0, 12.5 - recencyDays * 0.2);
            int genreCount = rentals.Select(r => r.Genre).Distinct().Count();
            double diversityScore = Math.Min(12.5, genreCount * 2.5);
            double engageScore = Math.Round(recencyScore + diversityScore, 1);

            double total = Math.Round(freqScore + returnScore + spendScore + engageScore, 1);
            total = Math.Min(100, Math.Max(0, total));

            var dims = new List<DimensionScore>
            {
                new DimensionScore { Name = "Rental Frequency", Score = freqScore, Max = 25, Detail = $"{last30} rentals in 30d, {last90} in 90d" },
                new DimensionScore { Name = "Return Behavior", Score = returnScore, Max = 25, Detail = $"{onTimeRate:P0} on-time rate" },
                new DimensionScore { Name = "Spending Trend", Score = spendScore, Max = 25, Detail = $"${spend30:F0} last 30d vs ${avgMonthly90:F0}/mo avg" },
                new DimensionScore { Name = "Engagement", Score = engageScore, Max = 25, Detail = $"{recencyDays:F0}d since last rental, {genreCount} genres" }
            };

            return (total, dims);
        }

        private static HealthTier ClassifyTier(double score)
        {
            if (score >= 80) return HealthTier.Thriving;
            if (score >= 60) return HealthTier.Healthy;
            if (score >= 40) return HealthTier.AtRisk;
            if (score >= 20) return HealthTier.Critical;
            return HealthTier.Churned;
        }

        private List<string> GenerateRecommendations(SimulatedCustomer c, List<DimensionScore> dims, HealthTier tier)
        {
            var recs = new List<string>();
            var freq = dims[0];
            var ret = dims[1];
            var spend = dims[2];
            var engage = dims[3];

            if (freq.Score < 10) recs.Add("🎬 Send win-back offer — no recent rental activity detected");
            if (ret.Score < 15) recs.Add("⏰ Late return trend detected — send friendly reminder about due dates");
            if (spend.Score < 10) recs.Add("💰 Offer bundle discount to boost spending");
            if (engage.Score < 10) recs.Add("📧 Re-engagement campaign — customer has gone quiet");

            var genres = c.Rentals.Select(r => r.Genre).Distinct().ToList();
            if (genres.Count <= 2 && genres.Count > 0)
                recs.Add($"🎭 Offer genre discovery discount — customer only rents {string.Join(" & ", genres)}");

            int totalRentals = c.Rentals.Count;
            if (totalRentals >= 50) recs.Add($"🏆 Congratulate on {totalRentals} rental milestone!");
            else if (totalRentals >= 25) recs.Add($"🎉 Approaching milestone — {totalRentals} rentals, celebrate at 50!");

            if (tier == HealthTier.Thriving) recs.Add("⭐ VIP candidate — consider loyalty program upgrade");
            if (tier == HealthTier.Churned) recs.Add("🚨 Urgent: personal outreach needed to prevent permanent loss");

            if (!recs.Any()) recs.Add("✅ Customer is healthy — maintain current engagement");

            return recs;
        }

        private List<SimulatedCustomer> GenerateCustomers(int count)
        {
            var customers = new List<SimulatedCustomer>();
            var rng = new Random(42);
            for (int i = 1; i <= count; i++)
            {
                var name = FirstNames[rng.Next(FirstNames.Length)] + " " + LastNames[rng.Next(LastNames.Length)];
                var rentalCount = rng.Next(3, 60);
                var rentals = new List<SimulatedRental>();

                // Vary activity patterns
                int activityProfile = rng.Next(5); // 0=active, 1=declining, 2=churned, 3=new, 4=steady
                for (int j = 0; j < rentalCount; j++)
                {
                    int daysAgo;
                    switch (activityProfile)
                    {
                        case 0: daysAgo = rng.Next(1, 45); break;
                        case 1: daysAgo = rng.Next(20, 120); break;
                        case 2: daysAgo = rng.Next(60, 300); break;
                        case 3: daysAgo = rng.Next(1, 30); break;
                        default: daysAgo = rng.Next(1, 180); break;
                    }

                    var dateRented = DateTime.UtcNow.AddDays(-daysAgo);
                    bool returned = rng.NextDouble() < 0.85;
                    bool late = returned && rng.NextDouble() < (activityProfile == 1 ? 0.4 : 0.15);
                    var genre = Genres[rng.Next(activityProfile == 0 ? Genres.Length : Math.Min(3, Genres.Length))];

                    rentals.Add(new SimulatedRental
                    {
                        MovieName = MovieAdj[rng.Next(MovieAdj.Length)] + " " + MovieNoun[rng.Next(MovieNoun.Length)],
                        Genre = genre,
                        DateRented = dateRented,
                        DateReturned = returned ? dateRented.AddDays(rng.Next(1, late ? 14 : 5)) : (DateTime?)null,
                        WasLate = late,
                        AmountPaid = Math.Round((decimal)(rng.NextDouble() * 4 + 2), 2)
                    });
                }

                customers.Add(new SimulatedCustomer { Id = i, Name = name, Rentals = rentals });
            }
            return customers;
        }

        #endregion
    }
}
