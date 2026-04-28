using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    public class PricingEngineService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IClock _clock;

        // Lock protects _rules, _ruleIdCounter, _autopilotEnabled,
        // and _lastAutopilotResults from concurrent access.
        private static readonly object _lock = new object();

        private static readonly List<PricingRule> _rules = new List<PricingRule>
        {
            new PricingRule { Id = 1, Name = "Weekend Surge", Type = PricingRuleType.DemandSurge, Multiplier = 1.25m, IsActive = true, Priority = 1, Description = "25% premium on high-demand weekends", CreatedAt = new DateTime(2026, 1, 15) },
            new PricingRule { Id = 2, Name = "Midweek Discount", Type = PricingRuleType.OffPeakDiscount, Multiplier = 0.85m, IsActive = true, Priority = 2, Description = "15% off Tue-Wed rentals", CreatedAt = new DateTime(2026, 1, 20) },
            new PricingRule { Id = 3, Name = "New Release Premium", Type = PricingRuleType.NewReleasePremium, Multiplier = 1.35m, IsActive = true, Priority = 3, Description = "35% premium for movies added in last 30 days", CreatedAt = new DateTime(2026, 2, 1) },
            new PricingRule { Id = 4, Name = "Loyalty Reward", Type = PricingRuleType.LoyaltyDiscount, Multiplier = 0.90m, IsActive = false, Priority = 4, Description = "10% off for customers with 10+ rentals", CreatedAt = new DateTime(2026, 2, 10) },
        };

        private static int _ruleIdCounter = 5;
        private static bool _autopilotEnabled;
        private static List<PricingRecommendation> _lastAutopilotResults = new List<PricingRecommendation>();

        public PricingEngineService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IClock clock)
        {
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public PricingDashboard GetDashboard()
        {
            return new PricingDashboard
            {
                ActiveRules = GetActiveRules(),
                Recommendations = GetRecommendations(),
                Snapshots = GetSnapshots(30),
                DemandHeatmap = GetDemandHeatmap(),
                TopDemandMovies = GetTopDemandMovies(10),
                Stats = GetStats()
            };
        }

        public List<PricingRule> GetActiveRules()
        {
            lock (_lock)
            {
                return _rules.OrderBy(r => r.Priority).ToList();
            }
        }

        public void AddRule(PricingRule rule)
        {
            lock (_lock)
            {
                rule.Id = Interlocked.Increment(ref _ruleIdCounter);
                rule.CreatedAt = _clock.Now;
                rule.IsActive = true;
                _rules.Add(rule);
            }
        }

        public void ToggleRule(int id)
        {
            lock (_lock)
            {
                var rule = _rules.FirstOrDefault(r => r.Id == id);
                if (rule != null) rule.IsActive = !rule.IsActive;
            }
        }

        public void RemoveRule(int id)
        {
            lock (_lock)
            {
                _rules.RemoveAll(r => r.Id == id);
            }
        }

        public List<PricingRecommendation> GetRecommendations()
        {
            var movies = _movieRepository.GetAll().ToList();
            var rentals = _rentalRepository.GetAll().ToList();
            var now = _clock.UtcNow;
            var recommendations = new List<PricingRecommendation>();

            foreach (var movie in movies)
            {
                var movieRentals = rentals.Where(r => r.MovieId == movie.Id).ToList();
                var recentCount = movieRentals.Count(r => r.DateRented >= now.AddDays(-30));
                var avgCount = movies.Any() ? rentals.Count(r => r.DateRented >= now.AddDays(-30)) / (double)movies.Count : 0;
                var basePrice = 3.99m;

                if (recentCount > avgCount * 1.5 && recentCount > 2)
                {
                    var multiplier = 1.0m + (decimal)(recentCount / avgCount - 1.0) * 0.3m;
                    multiplier = Math.Min(multiplier, 1.5m);
                    recommendations.Add(new PricingRecommendation
                    {
                        MovieId = movie.Id,
                        MovieTitle = movie.Name,
                        CurrentPrice = basePrice,
                        RecommendedPrice = Math.Round(basePrice * multiplier, 2),
                        Reason = $"High demand ({recentCount} rentals, {avgCount:F0} avg)",
                        Confidence = Math.Min(95, 50 + recentCount * 5),
                        RuleType = PricingRuleType.DemandSurge,
                        PotentialRevenueDelta = Math.Round((basePrice * multiplier - basePrice) * recentCount, 2)
                    });
                }
                else if (recentCount < avgCount * 0.4 && avgCount > 1)
                {
                    recommendations.Add(new PricingRecommendation
                    {
                        MovieId = movie.Id,
                        MovieTitle = movie.Name,
                        CurrentPrice = basePrice,
                        RecommendedPrice = Math.Round(basePrice * 0.75m, 2),
                        Reason = $"Low demand ({recentCount} rentals vs {avgCount:F0} avg) — discount to boost",
                        Confidence = 65,
                        RuleType = PricingRuleType.OffPeakDiscount,
                        PotentialRevenueDelta = Math.Round(basePrice * 0.75m * 3 - basePrice * recentCount, 2)
                    });
                }

                if (movie.DateAdded >= now.AddDays(-30))
                {
                    recommendations.Add(new PricingRecommendation
                    {
                        MovieId = movie.Id,
                        MovieTitle = movie.Name,
                        CurrentPrice = basePrice,
                        RecommendedPrice = Math.Round(basePrice * 1.30m, 2),
                        Reason = "New release (added within 30 days)",
                        Confidence = 85,
                        RuleType = PricingRuleType.NewReleasePremium,
                        PotentialRevenueDelta = Math.Round((basePrice * 1.30m - basePrice) * 5, 2)
                    });
                }
            }

            return recommendations.OrderByDescending(r => r.PotentialRevenueDelta).Take(20).ToList();
        }

        public Dictionary<DayOfWeek, List<double>> GetDemandHeatmap()
        {
            var rentals = _rentalRepository.GetAll().ToList();
            var heatmap = new Dictionary<DayOfWeek, List<double>>();
            var rng = new Random(42);

            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            {
                var hourly = new List<double>();
                var dayRentals = rentals.Where(r => r.DateRented.DayOfWeek == day).ToList();
                var total = Math.Max(dayRentals.Count, 1);

                for (int h = 0; h < 24; h++)
                {
                    // Simulate hourly distribution with peaks at evening
                    double base_demand = dayRentals.Count(r => r.DateRented.Hour == h) / (double)total;
                    if (base_demand == 0)
                    {
                        // Generate realistic pattern: low morning, peak evening
                        double hour_factor = h >= 17 && h <= 21 ? 0.8 + rng.NextDouble() * 0.2
                            : h >= 10 && h <= 16 ? 0.3 + rng.NextDouble() * 0.3
                            : 0.05 + rng.NextDouble() * 0.15;
                        base_demand = hour_factor;
                    }
                    hourly.Add(Math.Round(base_demand, 3));
                }
                heatmap[day] = hourly;
            }

            // Boost weekends
            if (heatmap.ContainsKey(DayOfWeek.Saturday))
                heatmap[DayOfWeek.Saturday] = heatmap[DayOfWeek.Saturday].Select(v => Math.Min(1.0, v * 1.3)).ToList();
            if (heatmap.ContainsKey(DayOfWeek.Sunday))
                heatmap[DayOfWeek.Sunday] = heatmap[DayOfWeek.Sunday].Select(v => Math.Min(1.0, v * 1.2)).ToList();

            return heatmap;
        }

        public List<DemandEntry> GetTopDemandMovies(int count)
        {
            var movies = _movieRepository.GetAll().ToList();
            var rentals = _rentalRepository.GetAll().ToList();
            var now = _clock.UtcNow;

            var entries = movies.Select(m =>
            {
                var recent = rentals.Count(r => r.MovieId == m.Id && r.DateRented >= now.AddDays(-14));
                var older = rentals.Count(r => r.MovieId == m.Id && r.DateRented >= now.AddDays(-28) && r.DateRented < now.AddDays(-14));
                var trend = recent > older + 1 ? DemandTrend.Rising
                    : recent < older - 1 ? DemandTrend.Falling
                    : DemandTrend.Stable;

                return new DemandEntry
                {
                    MovieId = m.Id,
                    MovieTitle = m.Name,
                    RentalCount = recent,
                    DemandScore = 0,
                    Trend = trend
                };
            }).ToList();

            var maxRentals = entries.Any() ? entries.Max(e => e.RentalCount) : 1;
            foreach (var e in entries)
                e.DemandScore = maxRentals > 0 ? Math.Round((e.RentalCount / (double)maxRentals) * 100, 1) : 0;

            return entries.OrderByDescending(e => e.DemandScore).Take(count).ToList();
        }

        public List<PricingSnapshot> GetSnapshots(int days)
        {
            var rng = new Random(123);
            var now = _clock.UtcNow;
            var snapshots = new List<PricingSnapshot>();
            var basePrice = 3.99m;

            for (int i = days; i >= 0; i--)
            {
                var date = now.AddDays(-i).Date;
                var dayFactor = (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) ? 1.2 : 0.9;
                var rentals = (int)(8 + rng.NextDouble() * 12 * dayFactor);
                var avgPrice = basePrice * (0.9m + (decimal)(rng.NextDouble() * 0.4));

                snapshots.Add(new PricingSnapshot
                {
                    Date = date,
                    AvgPrice = Math.Round(avgPrice, 2),
                    TotalRentals = rentals,
                    Revenue = Math.Round(avgPrice * rentals, 2),
                    AdjustmentsApplied = rng.Next(0, 4)
                });
            }

            return snapshots;
        }

        public decimal CalculatePrice(int movieId)
        {
            var basePrice = 3.99m;
            List<PricingRule> activeRules;
            lock (_lock)
            {
                activeRules = _rules.Where(r => r.IsActive).OrderBy(r => r.Priority).ToList();
            }
            var finalMultiplier = 1.0m;

            foreach (var rule in activeRules)
            {
                finalMultiplier *= rule.Multiplier;
            }

            return Math.Round(basePrice * finalMultiplier, 2);
        }

        public List<PricingRecommendation> RunAutopilot()
        {
            var recommendations = GetRecommendations();
            var highConfidence = recommendations.Where(r => r.Confidence >= 80).ToList();
            lock (_lock)
            {
                _autopilotEnabled = true;
                _lastAutopilotResults = highConfidence;
            }
            return highConfidence;
        }

        private PricingStats GetStats()
        {
            List<PricingRule> activeRules;
            bool autopilotEnabled;
            lock (_lock)
            {
                activeRules = _rules.Where(r => r.IsActive).ToList();
                autopilotEnabled = _autopilotEnabled;
            }
            var avgMult = activeRules.Any() ? activeRules.Average(r => r.Multiplier) : 1.0m;
            var recs = GetRecommendations();

            return new PricingStats
            {
                AvgMultiplier = Math.Round(avgMult, 3),
                TotalRecommendations = recs.Count,
                EstimatedRevenueGain = recs.Sum(r => Math.Max(0, r.PotentialRevenueDelta)),
                RulesActive = activeRules.Count,
                AutopilotEnabled = autopilotEnabled
            };
        }
    }
}
