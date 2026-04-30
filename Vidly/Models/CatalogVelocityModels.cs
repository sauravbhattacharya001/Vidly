using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Lifecycle phase a movie passes through in the catalog.
    /// </summary>
    public enum CatalogPhase
    {
        /// <summary>Recently added, building initial demand (0-30 days).</summary>
        NewArrival = 1,

        /// <summary>High rental velocity, strong demand.</summary>
        Hot = 2,

        /// <summary>Consistent, predictable rental rate.</summary>
        Steady = 3,

        /// <summary>Rental rate dropping below historical average.</summary>
        Declining = 4,

        /// <summary>Very few or no rentals in recent period.</summary>
        Dormant = 5,

        /// <summary>Recently revived — demand spike after dormancy.</summary>
        Resurgent = 6
    }

    /// <summary>
    /// Autonomous action the engine recommends for a movie based on its velocity.
    /// </summary>
    public enum VelocityAction
    {
        /// <summary>No action needed — healthy trajectory.</summary>
        None = 0,

        /// <summary>Feature prominently — capitalize on momentum.</summary>
        Promote = 1,

        /// <summary>Apply discount to stimulate demand.</summary>
        Discount = 2,

        /// <summary>Bundle with popular titles to boost exposure.</summary>
        Bundle = 3,

        /// <summary>Acquire additional copies to meet demand.</summary>
        Restock = 4,

        /// <summary>Consider removing from active catalog.</summary>
        Retire = 5,

        /// <summary>Investigate sudden velocity change.</summary>
        Investigate = 6
    }

    /// <summary>
    /// Velocity profile for a single movie — its lifecycle metrics and recommended action.
    /// </summary>
    public class MovieVelocityProfile
    {
        /// <summary>Movie identifier.</summary>
        public int MovieId { get; set; }

        /// <summary>Movie name for display.</summary>
        public string MovieName { get; set; }

        /// <summary>Movie genre.</summary>
        public Genre? Genre { get; set; }

        /// <summary>Current lifecycle phase.</summary>
        public CatalogPhase Phase { get; set; }

        /// <summary>Previous phase (for transition detection).</summary>
        public CatalogPhase? PreviousPhase { get; set; }

        /// <summary>
        /// Velocity score 0-100. Higher means faster rental turnover relative to catalog average.
        /// </summary>
        public double VelocityScore { get; set; }

        /// <summary>
        /// Acceleration: positive = gaining momentum, negative = losing momentum.
        /// Measured as week-over-week velocity change.
        /// </summary>
        public double Acceleration { get; set; }

        /// <summary>Total rentals in the analysis window (last 90 days).</summary>
        public int RentalsInWindow { get; set; }

        /// <summary>Rentals in the most recent period (last 7 days).</summary>
        public int RecentRentals { get; set; }

        /// <summary>Rentals in the prior period (8-14 days ago).</summary>
        public int PriorPeriodRentals { get; set; }

        /// <summary>Days since last rental.</summary>
        public int DaysSinceLastRental { get; set; }

        /// <summary>Days in catalog (since release or first rental).</summary>
        public int DaysInCatalog { get; set; }

        /// <summary>Recommended autonomous action.</summary>
        public VelocityAction RecommendedAction { get; set; }

        /// <summary>Confidence in the recommendation (0.0-1.0).</summary>
        public double ActionConfidence { get; set; }

        /// <summary>Human-readable reasoning for the recommendation.</summary>
        public string ActionReasoning { get; set; }

        /// <summary>Whether this movie is at risk of becoming dormant.</summary>
        public bool AtRisk { get; set; }

        /// <summary>Estimated days until phase transition (negative = already transitioned).</summary>
        public int? EstimatedDaysToNextPhase { get; set; }
    }

    /// <summary>
    /// Fleet-level catalog velocity report.
    /// </summary>
    public class CatalogVelocityReport
    {
        /// <summary>When this report was generated.</summary>
        public DateTime GeneratedAt { get; set; }

        /// <summary>Analysis window in days.</summary>
        public int WindowDays { get; set; }

        /// <summary>All movie velocity profiles.</summary>
        public List<MovieVelocityProfile> Profiles { get; set; }

        /// <summary>Phase distribution across catalog.</summary>
        public Dictionary<CatalogPhase, int> PhaseDistribution { get; set; }

        /// <summary>Overall catalog health score 0-100.</summary>
        public double CatalogHealthScore { get; set; }

        /// <summary>Average velocity across all movies.</summary>
        public double AverageVelocity { get; set; }

        /// <summary>Movies requiring immediate attention.</summary>
        public List<MovieVelocityProfile> UrgentActions { get; set; }

        /// <summary>Phase transitions detected since last analysis.</summary>
        public List<PhaseTransition> RecentTransitions { get; set; }

        /// <summary>Genre-level velocity summary.</summary>
        public List<GenreVelocitySummary> GenreBreakdown { get; set; }

        /// <summary>Autonomous insights generated from the analysis.</summary>
        public List<string> Insights { get; set; }
    }

    /// <summary>
    /// Records a phase transition event for a movie.
    /// </summary>
    public class PhaseTransition
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public CatalogPhase FromPhase { get; set; }
        public CatalogPhase ToPhase { get; set; }
        public string Trigger { get; set; }
    }

    /// <summary>
    /// Velocity summary for a genre.
    /// </summary>
    public class GenreVelocitySummary
    {
        public Genre Genre { get; set; }
        public int MovieCount { get; set; }
        public double AverageVelocity { get; set; }
        public int HotCount { get; set; }
        public int DormantCount { get; set; }
        public double HealthScore { get; set; }
    }

    /// <summary>
    /// Configuration for the velocity engine.
    /// </summary>
    public class VelocityEngineConfig
    {
        /// <summary>Analysis window in days (default 90).</summary>
        public int WindowDays { get; set; }

        /// <summary>Days a movie is considered "new" (default 30).</summary>
        public int NewArrivalDays { get; set; }

        /// <summary>Days without rental before considered dormant (default 30).</summary>
        public int DormantThresholdDays { get; set; }

        /// <summary>Minimum velocity score to be considered "hot" (default 70).</summary>
        public double HotThreshold { get; set; }

        /// <summary>Velocity score below which is "declining" (default 30).</summary>
        public double DecliningThreshold { get; set; }

        /// <summary>Acceleration spike threshold for resurgence detection (default 3.0).</summary>
        public double ResurgenceAccelerationThreshold { get; set; }

        public VelocityEngineConfig()
        {
            WindowDays = 90;
            NewArrivalDays = 30;
            DormantThresholdDays = 30;
            HotThreshold = 70.0;
            DecliningThreshold = 30.0;
            ResurgenceAccelerationThreshold = 3.0;
        }
    }
}
