using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Risk assessment for a single active rental predicting likelihood of late return.
    /// </summary>
    public class LateReturnPrediction
    {
        public int RentalId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int MovieId { get; set; }
        public string MovieName { get; set; }

        [DataType(DataType.Date)]
        public DateTime RentalDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime DueDate { get; set; }

        /// <summary>
        /// Days remaining until due date (negative = already overdue).
        /// </summary>
        public int DaysRemaining { get; set; }

        /// <summary>
        /// Overall risk score from 0 (no risk) to 100 (very high risk).
        /// </summary>
        public int RiskScore { get; set; }

        /// <summary>
        /// Human-readable risk level.
        /// </summary>
        public RiskLevel Level { get; set; }

        /// <summary>
        /// Individual risk factors that contributed to the score.
        /// </summary>
        public List<RiskFactor> Factors { get; set; } = new List<RiskFactor>();

        /// <summary>
        /// Suggested proactive actions for staff.
        /// </summary>
        public List<string> SuggestedActions { get; set; } = new List<string>();

        /// <summary>
        /// Estimated late fee if returned at current trajectory.
        /// </summary>
        [DataType(DataType.Currency)]
        public decimal EstimatedLateFee { get; set; }
    }

    /// <summary>
    /// A single contributing factor to the risk score.
    /// </summary>
    public class RiskFactor
    {
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>
        /// Points contributed to the risk score (0-30).
        /// </summary>
        public int Points { get; set; }
    }

    public enum RiskLevel
    {
        [Display(Name = "Low")]
        Low = 1,

        [Display(Name = "Medium")]
        Medium = 2,

        [Display(Name = "High")]
        High = 3,

        [Display(Name = "Critical")]
        Critical = 4
    }

    /// <summary>
    /// Summary statistics for the prediction dashboard.
    /// </summary>
    public class PredictionSummary
    {
        public int TotalActiveRentals { get; set; }
        public int LowRisk { get; set; }
        public int MediumRisk { get; set; }
        public int HighRisk { get; set; }
        public int CriticalRisk { get; set; }
        public decimal TotalEstimatedLateFees { get; set; }

        /// <summary>
        /// Average risk score across all active rentals.
        /// </summary>
        public double AverageRiskScore { get; set; }
    }
}
