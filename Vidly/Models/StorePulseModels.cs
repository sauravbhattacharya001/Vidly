using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Complete store health pulse report — aggregates signals from inventory,
    /// revenue, customer activity, and operations into a unified health score
    /// with anomaly detection and auto-generated action items.
    /// </summary>
    public class StorePulseReport
    {
        public DateTime GeneratedAt { get; set; }
        public int OverallHealthScore { get; set; }
        public string HealthGrade { get; set; }
        public List<PulseSignal> Signals { get; set; }
        public List<PulseAnomaly> Anomalies { get; set; }
        public List<PulseActionItem> ActionItems { get; set; }
        public PulseTrend Trend { get; set; }

        public StorePulseReport()
        {
            Signals = new List<PulseSignal>();
            Anomalies = new List<PulseAnomaly>();
            ActionItems = new List<PulseActionItem>();
        }
    }

    /// <summary>
    /// A single health signal measuring one aspect of store operations.
    /// </summary>
    public class PulseSignal
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public int Score { get; set; }
        public string Status { get; set; }
        public string Description { get; set; }
        public Dictionary<string, object> Metrics { get; set; }

        public PulseSignal()
        {
            Metrics = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// An anomaly detected by comparing current metrics against historical baselines.
    /// </summary>
    public class PulseAnomaly
    {
        public string SignalName { get; set; }
        public string Type { get; set; }
        public string Severity { get; set; }
        public string Description { get; set; }
        public double DeviationPercent { get; set; }
        public DateTime DetectedAt { get; set; }
    }

    /// <summary>
    /// An auto-generated action item based on signal analysis.
    /// </summary>
    public class PulseActionItem
    {
        public int Priority { get; set; }
        public string Category { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Impact { get; set; }
        public bool IsAutomatable { get; set; }
    }

    /// <summary>
    /// Overall health trend compared to previous period.
    /// </summary>
    public class PulseTrend
    {
        public string Direction { get; set; }
        public int ScoreChange { get; set; }
        public string Summary { get; set; }
    }
}
