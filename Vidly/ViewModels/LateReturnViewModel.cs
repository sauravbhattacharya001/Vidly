using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the late return prediction dashboard.
    /// </summary>
    public class LateReturnViewModel
    {
        public PredictionSummary Summary { get; set; }
        public List<LateReturnPrediction> Predictions { get; set; }
        public string FilterLevel { get; set; }

        /// <summary>
        /// CSS class for a risk level badge.
        /// </summary>
        public static string GetBadgeClass(RiskLevel level)
        {
            return level switch
            {
                RiskLevel.Critical => "label-danger",
                RiskLevel.High => "label-warning",
                RiskLevel.Medium => "label-info",
                _ => "label-success"
            };
        }

        /// <summary>
        /// Progress bar color class for a risk score.
        /// </summary>
        public static string GetProgressClass(int score)
        {
            return score switch
            {
                >= 70 => "progress-bar-danger",
                >= 45 => "progress-bar-warning",
                >= 20 => "progress-bar-info",
                _ => "progress-bar-success"
            };
        }
    }
}
