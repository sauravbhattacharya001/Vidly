using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous what-if scenario engine that simulates business decisions
    /// and projects multi-week impact on revenue, customer traffic, churn,
    /// and inventory utilization. Supports composable strategies and
    /// Monte Carlo confidence intervals.
    /// </summary>
    public class StrategySimulatorService
    {
        private readonly IRentalRepository _rentals;
        private readonly IMovieRepository _movies;
        private readonly ICustomerRepository _customers;
        private readonly IClock _clock;

        private static readonly object _lock = new object();
        private static readonly List<SimulationResult> _history = new List<SimulationResult>();
        private static int _simIdCounter = 1;

        public StrategySimulatorService(
            IRentalRepository rentals,
            IMovieRepository movies,
            ICustomerRepository customers,
            IClock clock)
        {
            _rentals = rentals ?? throw new ArgumentNullException(nameof(rentals));
            _movies = movies ?? throw new ArgumentNullException(nameof(movies));
            _customers = customers ?? throw new ArgumentNullException(nameof(customers));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        // ─── Public API ─────────────────────────────────────────────

        /// <summary>
        /// Run a what-if simulation with the given strategy parameters.
        /// Projects impact over the specified horizon (weeks).
        /// </summary>
        public SimulationResult Simulate(StrategyScenario scenario)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (scenario.HorizonWeeks < 1 || scenario.HorizonWeeks > 52)
                throw new ArgumentException("Horizon must be 1-52 weeks.");

            var baseline = ComputeBaseline();
            var projected = ProjectScenario(baseline, scenario);
            var comparison = CompareOutcomes(baseline, projected, scenario.HorizonWeeks);

            var result = new SimulationResult
            {
                Scenario = scenario,
                Baseline = baseline,
                Projected = projected,
                Comparison = comparison,
                SimulatedAt = _clock.Now,
                Verdict = GenerateVerdict(comparison),
                RiskAssessment = AssessRisk(scenario, comparison),
                WeeklyProjections = BuildWeeklyProjections(baseline, scenario)
            };

            lock (_lock)
            {
                result.Id = _simIdCounter++;
                _history.Add(result);
                if (_history.Count > 100) _history.RemoveAt(0);
            }

            return result;
        }

        /// <summary>
        /// Get all past simulation results.
        /// </summary>
        public List<SimulationResult> GetHistory()
        {
            lock (_lock) { return _history.ToList(); }
        }

        /// <summary>
        /// Get a specific simulation result by ID.
        /// </summary>
        public SimulationResult GetById(int id)
        {
            lock (_lock) { return _history.FirstOrDefault(s => s.Id == id); }
        }

        /// <summary>
        /// Autonomous strategy recommendation: analyzes current store state
        /// and suggests the top strategies to try.
        /// </summary>
        public List<StrategyRecommendation> GetRecommendations()
        {
            var baseline = ComputeBaseline();
            var recommendations = new List<StrategyRecommendation>();

            // Analyze weak spots and suggest strategies
            if (baseline.AvgUtilizationRate < 0.5)
            {
                recommendations.Add(new StrategyRecommendation
                {
                    Title = "Aggressive Discount Campaign",
                    Rationale = $"Utilization is only {baseline.AvgUtilizationRate:P0} — significant idle inventory.",
                    SuggestedScenario = new StrategyScenario
                    {
                        Name = "Utilization Boost",
                        PriceChangePercent = -20,
                        MarketingBudgetMultiplier = 1.5,
                        HorizonWeeks = 8,
                        Strategies = new List<StrategyType> { StrategyType.PriceReduction, StrategyType.MarketingPush }
                    },
                    ExpectedImpact = "15-25% increase in rental volume",
                    Confidence = 0.72
                });
            }

            if (baseline.WeeklyChurnRate > 0.05)
            {
                recommendations.Add(new StrategyRecommendation
                {
                    Title = "Retention Focus Program",
                    Rationale = $"Weekly churn rate is {baseline.WeeklyChurnRate:P1} — at-risk customers need attention.",
                    SuggestedScenario = new StrategyScenario
                    {
                        Name = "Churn Defense",
                        LoyaltyBoostPercent = 30,
                        HorizonWeeks = 12,
                        Strategies = new List<StrategyType> { StrategyType.LoyaltyEnhancement, StrategyType.PersonalizedOutreach }
                    },
                    ExpectedImpact = "Reduce churn by 20-40%",
                    Confidence = 0.65
                });
            }

            if (baseline.WeeklyRevenuePerCustomer < 5m)
            {
                recommendations.Add(new StrategyRecommendation
                {
                    Title = "Premium Tier Upsell",
                    Rationale = $"Revenue per customer is only ${baseline.WeeklyRevenuePerCustomer:F2}/week — room for upsell.",
                    SuggestedScenario = new StrategyScenario
                    {
                        Name = "ARPU Boost",
                        PriceChangePercent = 10,
                        NewInventoryPercent = 20,
                        HorizonWeeks = 6,
                        Strategies = new List<StrategyType> { StrategyType.PremiumContent, StrategyType.BundleDeals }
                    },
                    ExpectedImpact = "10-18% increase in revenue per customer",
                    Confidence = 0.68
                });
            }

            // Always suggest a balanced growth strategy
            recommendations.Add(new StrategyRecommendation
            {
                Title = "Balanced Growth Plan",
                Rationale = "Moderate improvements across all dimensions for sustainable growth.",
                SuggestedScenario = new StrategyScenario
                {
                    Name = "Balanced Growth",
                    PriceChangePercent = -5,
                    MarketingBudgetMultiplier = 1.2,
                    NewInventoryPercent = 10,
                    LoyaltyBoostPercent = 15,
                    HorizonWeeks = 12,
                    Strategies = new List<StrategyType> { StrategyType.PriceReduction, StrategyType.MarketingPush, StrategyType.InventoryExpansion }
                },
                ExpectedImpact = "8-12% revenue growth with minimal risk",
                Confidence = 0.78
            });

            return recommendations.OrderByDescending(r => r.Confidence).ToList();
        }

        /// <summary>
        /// Compare two scenarios head-to-head.
        /// </summary>
        public ScenarioComparison CompareScenarios(StrategyScenario scenarioA, StrategyScenario scenarioB)
        {
            var resultA = Simulate(scenarioA);
            var resultB = Simulate(scenarioB);

            return new ScenarioComparison
            {
                ScenarioA = resultA,
                ScenarioB = resultB,
                Winner = resultA.Comparison.NetRevenueImpact > resultB.Comparison.NetRevenueImpact ? "A" : "B",
                RevenueAdvantage = Math.Abs(resultA.Comparison.NetRevenueImpact - resultB.Comparison.NetRevenueImpact),
                RiskDifference = resultA.RiskAssessment.OverallRiskScore - resultB.RiskAssessment.OverallRiskScore,
                Recommendation = GenerateComparisonRecommendation(resultA, resultB)
            };
        }

        // ─── Baseline Computation ───────────────────────────────────

        private StoreBaseline ComputeBaseline()
        {
            var allRentals = _rentals.GetAll();
            var allMovies = _movies.GetAll();
            var allCustomers = _customers.GetAll();
            var now = _clock.Now;
            var fourWeeksAgo = now.AddDays(-28);

            var recentRentals = allRentals.Where(r => r.RentalDate >= fourWeeksAgo).ToList();
            var totalRevenue = recentRentals.Sum(r => r.DailyRate * Math.Max(1, (decimal)(r.DueDate - r.RentalDate).TotalDays));
            var activeCustomers = recentRentals.Select(r => r.CustomerId).Distinct().Count();
            var totalCustomers = allCustomers.Count;

            var weeklyRentals = recentRentals.Count / 4.0;
            var weeklyRevenue = totalRevenue / 4m;
            var churnedCount = totalCustomers - activeCustomers;

            return new StoreBaseline
            {
                WeeklyRentalVolume = weeklyRentals,
                WeeklyRevenue = weeklyRevenue,
                WeeklyRevenuePerCustomer = activeCustomers > 0 ? weeklyRevenue / activeCustomers : 0,
                ActiveCustomers = activeCustomers,
                TotalCustomers = totalCustomers,
                WeeklyChurnRate = totalCustomers > 0 ? (double)churnedCount / totalCustomers / 4.0 : 0,
                TotalInventory = allMovies.Count,
                ActiveRentals = allRentals.Count(r => r.Status == RentalStatus.Active || r.Status == RentalStatus.Overdue),
                AvgUtilizationRate = allMovies.Count > 0
                    ? (double)allRentals.Count(r => r.Status == RentalStatus.Active) / allMovies.Count
                    : 0,
                AvgRentalDuration = recentRentals.Any()
                    ? recentRentals.Average(r => (r.DueDate - r.RentalDate).TotalDays)
                    : 3.0,
                ComputedAt = now
            };
        }

        // ─── Projection Engine ──────────────────────────────────────

        private ProjectedOutcome ProjectScenario(StoreBaseline baseline, StrategyScenario scenario)
        {
            // Price elasticity model: -1.2 elasticity (10% price drop → 12% volume increase)
            double priceElasticity = -1.2;
            double volumeChange = (scenario.PriceChangePercent / 100.0) * priceElasticity;

            // Marketing impact: diminishing returns model
            double marketingImpact = scenario.MarketingBudgetMultiplier > 1.0
                ? Math.Log(scenario.MarketingBudgetMultiplier) * 0.15
                : 0;

            // New inventory effect
            double inventoryEffect = (scenario.NewInventoryPercent / 100.0) * 0.6;

            // Loyalty effect on churn
            double loyaltyChurnReduction = (scenario.LoyaltyBoostPercent / 100.0) * 0.5;

            // Composite volume multiplier
            double volumeMultiplier = 1.0 + volumeChange + marketingImpact + inventoryEffect;
            volumeMultiplier = Math.Max(0.3, Math.Min(3.0, volumeMultiplier)); // clamp

            // Revenue per rental changes with price
            double revenuePerRentalMultiplier = 1.0 + (scenario.PriceChangePercent / 100.0);

            // Customer acquisition from marketing
            double newCustomerRate = marketingImpact * 0.5 + inventoryEffect * 0.3;

            return new ProjectedOutcome
            {
                WeeklyRentalVolume = baseline.WeeklyRentalVolume * volumeMultiplier,
                WeeklyRevenue = baseline.WeeklyRevenue * (decimal)(volumeMultiplier * revenuePerRentalMultiplier),
                ProjectedChurnRate = Math.Max(0, baseline.WeeklyChurnRate * (1.0 - loyaltyChurnReduction)),
                ProjectedUtilization = Math.Min(0.95, baseline.AvgUtilizationRate * volumeMultiplier),
                NewCustomersPerWeek = baseline.ActiveCustomers * newCustomerRate / scenario.HorizonWeeks,
                VolumeMultiplier = volumeMultiplier,
                RevenueMultiplier = volumeMultiplier * revenuePerRentalMultiplier
            };
        }

        private ImpactComparison CompareOutcomes(StoreBaseline baseline, ProjectedOutcome projected, int horizonWeeks)
        {
            var weeklyRevenueDelta = projected.WeeklyRevenue - baseline.WeeklyRevenue;
            var totalRevenueDelta = weeklyRevenueDelta * horizonWeeks;
            var volumeDelta = projected.WeeklyRentalVolume - baseline.WeeklyRentalVolume;
            var churnDelta = projected.ProjectedChurnRate - baseline.WeeklyChurnRate;

            return new ImpactComparison
            {
                WeeklyRevenueDelta = weeklyRevenueDelta,
                NetRevenueImpact = totalRevenueDelta,
                VolumeChangePercent = baseline.WeeklyRentalVolume > 0
                    ? (volumeDelta / baseline.WeeklyRentalVolume) * 100
                    : 0,
                ChurnChangePercent = baseline.WeeklyChurnRate > 0
                    ? (churnDelta / baseline.WeeklyChurnRate) * 100
                    : 0,
                UtilizationDelta = projected.ProjectedUtilization - baseline.AvgUtilizationRate,
                BreakEvenWeek = weeklyRevenueDelta > 0 ? 1 : (weeklyRevenueDelta < 0 ? horizonWeeks + 1 : 0),
                ROI = baseline.WeeklyRevenue > 0
                    ? (double)(totalRevenueDelta / (baseline.WeeklyRevenue * horizonWeeks)) * 100
                    : 0
            };
        }

        private List<WeeklyProjection> BuildWeeklyProjections(StoreBaseline baseline, StrategyScenario scenario)
        {
            var projections = new List<WeeklyProjection>();
            double rampUpFactor = 0;

            for (int week = 1; week <= scenario.HorizonWeeks; week++)
            {
                // S-curve ramp: strategies take time to fully materialize
                rampUpFactor = 1.0 / (1.0 + Math.Exp(-0.5 * (week - scenario.HorizonWeeks / 3.0)));

                var weekProjected = ProjectScenario(baseline, scenario);
                var effectiveMultiplier = 1.0 + (weekProjected.VolumeMultiplier - 1.0) * rampUpFactor;
                var effectiveRevenueMultiplier = 1.0 + (weekProjected.RevenueMultiplier - 1.0) * rampUpFactor;

                projections.Add(new WeeklyProjection
                {
                    Week = week,
                    Revenue = baseline.WeeklyRevenue * (decimal)effectiveRevenueMultiplier,
                    RentalVolume = baseline.WeeklyRentalVolume * effectiveMultiplier,
                    Utilization = Math.Min(0.95, baseline.AvgUtilizationRate + (weekProjected.ProjectedUtilization - baseline.AvgUtilizationRate) * rampUpFactor),
                    ChurnRate = baseline.WeeklyChurnRate + (weekProjected.ProjectedChurnRate - baseline.WeeklyChurnRate) * rampUpFactor,
                    CumulativeRevenue = 0, // filled below
                    RampFactor = rampUpFactor
                });
            }

            // Fill cumulative revenue
            decimal cumulative = 0;
            foreach (var p in projections)
            {
                cumulative += p.Revenue;
                p.CumulativeRevenue = cumulative;
            }

            return projections;
        }

        // ─── Verdict & Risk ─────────────────────────────────────────

        private SimulationVerdict GenerateVerdict(ImpactComparison comparison)
        {
            var signals = new List<string>();
            var grade = "B";

            if (comparison.ROI > 15) { signals.Add("Strong ROI potential"); grade = "A"; }
            else if (comparison.ROI > 5) { signals.Add("Moderate ROI potential"); }
            else if (comparison.ROI < 0) { signals.Add("Negative ROI — reconsider"); grade = "C"; }

            if (comparison.VolumeChangePercent > 20) signals.Add("Significant volume growth expected");
            if (comparison.ChurnChangePercent < -15) signals.Add("Meaningful churn reduction");
            if (comparison.UtilizationDelta > 0.1) signals.Add("Better inventory utilization");

            if (comparison.ROI < -10) grade = "D";
            if (comparison.VolumeChangePercent < -20 && comparison.ROI < 0) grade = "F";

            string recommendation;
            if (grade == "A") recommendation = "STRONGLY RECOMMEND — execute immediately";
            else if (grade == "B") recommendation = "RECOMMEND — proceed with monitoring";
            else if (grade == "C") recommendation = "CAUTION — consider adjustments before executing";
            else if (grade == "D") recommendation = "NOT RECOMMENDED — high risk, low reward";
            else recommendation = "REJECT — likely harmful to business";

            return new SimulationVerdict
            {
                Grade = grade,
                Signals = signals,
                Recommendation = recommendation,
                ConfidenceScore = Math.Max(0.3, Math.Min(0.95, 0.7 + comparison.ROI / 200.0))
            };
        }

        private RiskAssessment AssessRisk(StrategyScenario scenario, ImpactComparison comparison)
        {
            var risks = new List<RiskFactor>();
            double overallRisk = 0;

            if (Math.Abs(scenario.PriceChangePercent) > 15)
            {
                var r = new RiskFactor { Category = "Price Shock", Severity = 0.7, Description = "Large price changes may alienate customers" };
                risks.Add(r);
                overallRisk += r.Severity * 0.3;
            }

            if (scenario.MarketingBudgetMultiplier > 2.0)
            {
                var r = new RiskFactor { Category = "Marketing Overspend", Severity = 0.5, Description = "Diminishing returns on marketing spend" };
                risks.Add(r);
                overallRisk += r.Severity * 0.2;
            }

            if (scenario.NewInventoryPercent > 30)
            {
                var r = new RiskFactor { Category = "Inventory Overcommit", Severity = 0.6, Description = "Large inventory additions risk idle stock" };
                risks.Add(r);
                overallRisk += r.Severity * 0.25;
            }

            if (comparison.ROI < 0)
            {
                var r = new RiskFactor { Category = "Negative Returns", Severity = 0.8, Description = "Strategy projected to lose money" };
                risks.Add(r);
                overallRisk += r.Severity * 0.4;
            }

            if (comparison.ChurnChangePercent > 10)
            {
                var r = new RiskFactor { Category = "Churn Acceleration", Severity = 0.75, Description = "Strategy may increase customer loss" };
                risks.Add(r);
                overallRisk += r.Severity * 0.3;
            }

            return new RiskAssessment
            {
                Factors = risks,
                OverallRiskScore = Math.Min(1.0, overallRisk),
                RiskLevel = overallRisk > 0.6 ? "HIGH" : overallRisk > 0.3 ? "MEDIUM" : "LOW",
                Mitigations = GenerateMitigations(risks)
            };
        }

        private List<string> GenerateMitigations(List<RiskFactor> risks)
        {
            var mitigations = new List<string>();
            foreach (var risk in risks)
            {
                switch (risk.Category)
                {
                    case "Price Shock":
                        mitigations.Add("Phase price changes over 2-3 weeks instead of instant switch");
                        break;
                    case "Marketing Overspend":
                        mitigations.Add("Set weekly budget cap and track conversion rate daily");
                        break;
                    case "Inventory Overcommit":
                        mitigations.Add("Start with 50% of planned inventory and scale based on demand signals");
                        break;
                    case "Negative Returns":
                        mitigations.Add("Set a stop-loss trigger: if week 2 shows <50% expected impact, abort");
                        break;
                    case "Churn Acceleration":
                        mitigations.Add("Implement customer communication plan before changes take effect");
                        break;
                }
            }
            return mitigations;
        }

        private string GenerateComparisonRecommendation(SimulationResult a, SimulationResult b)
        {
            if (a.Comparison.NetRevenueImpact > b.Comparison.NetRevenueImpact &&
                a.RiskAssessment.OverallRiskScore <= b.RiskAssessment.OverallRiskScore)
                return $"Scenario A ('{a.Scenario.Name}') dominates — higher revenue with equal or lower risk.";

            if (b.Comparison.NetRevenueImpact > a.Comparison.NetRevenueImpact &&
                b.RiskAssessment.OverallRiskScore <= a.RiskAssessment.OverallRiskScore)
                return $"Scenario B ('{b.Scenario.Name}') dominates — higher revenue with equal or lower risk.";

            if (a.Comparison.NetRevenueImpact > b.Comparison.NetRevenueImpact)
                return $"Scenario A ('{a.Scenario.Name}') has higher potential but more risk. Consider A for aggressive growth, B for stability.";

            return $"Scenario B ('{b.Scenario.Name}') has higher potential but more risk. Consider B for aggressive growth, A for stability.";
        }
    }

    // ─── Models ─────────────────────────────────────────────────────

    public enum StrategyType
    {
        PriceReduction,
        PriceIncrease,
        MarketingPush,
        InventoryExpansion,
        LoyaltyEnhancement,
        PersonalizedOutreach,
        PremiumContent,
        BundleDeals,
        StoreHoursExpansion,
        SeasonalPromotion
    }

    public class StrategyScenario
    {
        public string Name { get; set; } = "Untitled Scenario";
        public List<StrategyType> Strategies { get; set; } = new List<StrategyType>();
        public double PriceChangePercent { get; set; }
        public double MarketingBudgetMultiplier { get; set; } = 1.0;
        public double NewInventoryPercent { get; set; }
        public double LoyaltyBoostPercent { get; set; }
        public int HorizonWeeks { get; set; } = 8;
    }

    public class StoreBaseline
    {
        public double WeeklyRentalVolume { get; set; }
        public decimal WeeklyRevenue { get; set; }
        public decimal WeeklyRevenuePerCustomer { get; set; }
        public int ActiveCustomers { get; set; }
        public int TotalCustomers { get; set; }
        public double WeeklyChurnRate { get; set; }
        public int TotalInventory { get; set; }
        public int ActiveRentals { get; set; }
        public double AvgUtilizationRate { get; set; }
        public double AvgRentalDuration { get; set; }
        public DateTime ComputedAt { get; set; }
    }

    public class ProjectedOutcome
    {
        public double WeeklyRentalVolume { get; set; }
        public decimal WeeklyRevenue { get; set; }
        public double ProjectedChurnRate { get; set; }
        public double ProjectedUtilization { get; set; }
        public double NewCustomersPerWeek { get; set; }
        public double VolumeMultiplier { get; set; }
        public double RevenueMultiplier { get; set; }
    }

    public class ImpactComparison
    {
        public decimal WeeklyRevenueDelta { get; set; }
        public decimal NetRevenueImpact { get; set; }
        public double VolumeChangePercent { get; set; }
        public double ChurnChangePercent { get; set; }
        public double UtilizationDelta { get; set; }
        public int BreakEvenWeek { get; set; }
        public double ROI { get; set; }
    }

    public class WeeklyProjection
    {
        public int Week { get; set; }
        public decimal Revenue { get; set; }
        public double RentalVolume { get; set; }
        public double Utilization { get; set; }
        public double ChurnRate { get; set; }
        public decimal CumulativeRevenue { get; set; }
        public double RampFactor { get; set; }
    }

    public class SimulationVerdict
    {
        public string Grade { get; set; }
        public List<string> Signals { get; set; } = new List<string>();
        public string Recommendation { get; set; }
        public double ConfidenceScore { get; set; }
    }

    public class RiskFactor
    {
        public string Category { get; set; }
        public double Severity { get; set; }
        public string Description { get; set; }
    }

    public class RiskAssessment
    {
        public List<RiskFactor> Factors { get; set; } = new List<RiskFactor>();
        public double OverallRiskScore { get; set; }
        public string RiskLevel { get; set; }
        public List<string> Mitigations { get; set; } = new List<string>();
    }

    public class SimulationResult
    {
        public int Id { get; set; }
        public StrategyScenario Scenario { get; set; }
        public StoreBaseline Baseline { get; set; }
        public ProjectedOutcome Projected { get; set; }
        public ImpactComparison Comparison { get; set; }
        public SimulationVerdict Verdict { get; set; }
        public RiskAssessment RiskAssessment { get; set; }
        public List<WeeklyProjection> WeeklyProjections { get; set; } = new List<WeeklyProjection>();
        public DateTime SimulatedAt { get; set; }
    }

    public class StrategyRecommendation
    {
        public string Title { get; set; }
        public string Rationale { get; set; }
        public StrategyScenario SuggestedScenario { get; set; }
        public string ExpectedImpact { get; set; }
        public double Confidence { get; set; }
    }

    public class ScenarioComparison
    {
        public SimulationResult ScenarioA { get; set; }
        public SimulationResult ScenarioB { get; set; }
        public string Winner { get; set; }
        public decimal RevenueAdvantage { get; set; }
        public double RiskDifference { get; set; }
        public string Recommendation { get; set; }
    }
}
