using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    // ================================================================
    //  Customer Lifetime Trajectory Engine — model classes
    // ================================================================

    /// <summary>
    /// Full fleet trajectory report produced by the Trajectory Engine.
    /// </summary>
    public class TrajectoryReport
    {
        public DateTime GeneratedAt { get; set; }
        public List<CustomerTrajectory> Trajectories { get; set; } = new List<CustomerTrajectory>();
        public FleetTrajectoryHealth FleetHealth { get; set; } = new FleetTrajectoryHealth();
        public List<string> Insights { get; set; } = new List<string>();

        /// <summary>Composite trajectory health score 0-100.</summary>
        public int TrajectoryScore { get; set; }
    }

    /// <summary>
    /// Per-customer trajectory prediction across all 7 engines.
    /// </summary>
    public class CustomerTrajectory
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public RentalVelocityForecast Velocity { get; set; } = new RentalVelocityForecast();
        public GenreEvolution GenreEvolution { get; set; } = new GenreEvolution();
        public SpendingTrajectory Spending { get; set; } = new SpendingTrajectory();
        public LifecyclePhaseResult Lifecycle { get; set; } = new LifecyclePhaseResult();
        public ChurnRiskResult ChurnRisk { get; set; } = new ChurnRiskResult();
        public LifetimeValueProjection LTV { get; set; } = new LifetimeValueProjection();
        public List<string> Insights { get; set; } = new List<string>();
    }

    /// <summary>Predicted rental timing based on historical interval analysis.</summary>
    public class RentalVelocityForecast
    {
        /// <summary>Exponential moving average of inter-rental intervals (days).</summary>
        public double AvgIntervalDays { get; set; }

        /// <summary>Predicted date of next rental (null if insufficient data).</summary>
        public DateTime? PredictedNextRental { get; set; }

        /// <summary>Confidence in the prediction (0-100).</summary>
        public double ConfidencePercent { get; set; }

        /// <summary>Accelerating | Steady | Decelerating | Stalled</summary>
        public string Trend { get; set; } = "Stalled";
    }

    /// <summary>Genre preference evolution and prediction.</summary>
    public class GenreEvolution
    {
        /// <summary>Current genre weights (genre name → 0-1).</summary>
        public Dictionary<string, double> CurrentPreferences { get; set; } = new Dictionary<string, double>();

        /// <summary>Predicted future genre weights.</summary>
        public Dictionary<string, double> PredictedPreferences { get; set; } = new Dictionary<string, double>();

        /// <summary>Explorer | Loyal | Shifting | Narrowing</summary>
        public string Pattern { get; set; } = "Explorer";

        /// <summary>Genres gaining share.</summary>
        public List<string> EmergingGenres { get; set; } = new List<string>();

        /// <summary>Genres losing share.</summary>
        public List<string> FadingGenres { get; set; } = new List<string>();
    }

    /// <summary>Spending velocity and forecast.</summary>
    public class SpendingTrajectory
    {
        public decimal AvgMonthlySpend { get; set; }

        /// <summary>Rate of change in $/month (positive = rising).</summary>
        public decimal SpendVelocity { get; set; }

        public decimal ForecastedNextMonthSpend { get; set; }

        /// <summary>Rising | Stable | Declining</summary>
        public string Trend { get; set; } = "Stable";
    }

    /// <summary>Customer lifecycle phases.</summary>
    public enum LifecyclePhase
    {
        Discovery = 1,
        Growing = 2,
        Loyal = 3,
        Plateaued = 4,
        Declining = 5,
        Dormant = 6,
        Churned = 7
    }

    /// <summary>Lifecycle phase classification result.</summary>
    public class LifecyclePhaseResult
    {
        public LifecyclePhase Phase { get; set; } = LifecyclePhase.Discovery;
        public double Confidence { get; set; }
        public int DaysInPhase { get; set; }
        public LifecyclePhase? PredictedNextPhase { get; set; }
    }

    /// <summary>Churn risk tiers.</summary>
    public enum ChurnRiskTier
    {
        Safe = 1,
        Watch = 2,
        Warning = 3,
        Critical = 4,
        Lost = 5
    }

    /// <summary>Churn risk assessment.</summary>
    public class ChurnRiskResult
    {
        /// <summary>Risk score 0-100 (higher = more likely to churn).</summary>
        public int RiskScore { get; set; }
        public ChurnRiskTier Tier { get; set; } = ChurnRiskTier.Safe;
        public List<string> RiskFactors { get; set; } = new List<string>();
    }

    /// <summary>Projected remaining customer lifetime value.</summary>
    public class LifetimeValueProjection
    {
        public decimal ProjectedRevenue30Days { get; set; }
        public decimal ProjectedRevenue60Days { get; set; }
        public decimal ProjectedRevenue90Days { get; set; }
        public decimal ProjectedRevenue180Days { get; set; }
        public decimal HistoricalLTV { get; set; }
    }

    /// <summary>Fleet-wide trajectory health summary.</summary>
    public class FleetTrajectoryHealth
    {
        public int TotalCustomers { get; set; }
        public Dictionary<string, int> PhaseDistribution { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> ChurnRiskDistribution { get; set; } = new Dictionary<string, int>();
        public decimal TotalProjectedRevenue90Days { get; set; }

        /// <summary>Fleet health 0-100.</summary>
        public int HealthScore { get; set; }
    }
}
