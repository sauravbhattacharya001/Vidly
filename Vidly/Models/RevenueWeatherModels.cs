using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Weather condition metaphor for revenue state.
    /// </summary>
    public enum WeatherCondition
    {
        Sunny = 1,
        PartlyCloudy = 2,
        Cloudy = 3,
        Rainy = 4,
        Stormy = 5,
        Drought = 6,
        Blizzard = 7,
        Heatwave = 8
    }

    /// <summary>
    /// A detected weather phenomenon in the revenue data.
    /// </summary>
    public class RevenuePhenomenon
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public double Intensity { get; set; }
        public DateTime DetectedAt { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string AffectedArea { get; set; }
        public List<string> ContributingFactors { get; set; }

        public RevenuePhenomenon()
        {
            ContributingFactors = new List<string>();
        }
    }

    /// <summary>
    /// Per-genre microclimate describing revenue conditions in a genre sector.
    /// </summary>
    public class GenreMicroclimate
    {
        public Genre Genre { get; set; }
        public WeatherCondition Condition { get; set; }
        public double Temperature { get; set; }
        public double Humidity { get; set; }
        public double WindSpeed { get; set; }
        public string WindDirection { get; set; }
        public double Pressure { get; set; }
        public string Forecast { get; set; }
        public List<string> Advisories { get; set; }

        public GenreMicroclimate()
        {
            Advisories = new List<string>();
        }
    }

    /// <summary>
    /// Revenue forecast for a future period.
    /// </summary>
    public class RevenueForecast
    {
        public string Period { get; set; }
        public WeatherCondition ExpectedCondition { get; set; }
        public double ConfidencePercent { get; set; }
        public double ExpectedRevenue { get; set; }
        public double TemperatureRangeLow { get; set; }
        public double TemperatureRangeHigh { get; set; }
        public List<string> Watches { get; set; }
        public List<string> Advisories { get; set; }

        public RevenueForecast()
        {
            Watches = new List<string>();
            Advisories = new List<string>();
        }
    }

    /// <summary>
    /// Full revenue weather report — the autonomous weather map for the store.
    /// </summary>
    public class RevenueWeatherReport
    {
        public DateTime GeneratedAt { get; set; }
        public int AnalysisWindowDays { get; set; }
        public WeatherCondition OverallCondition { get; set; }
        public double StoreTemperature { get; set; }
        public string OverallSummary { get; set; }
        public double HealthScore { get; set; }
        public List<RevenuePhenomenon> ActivePhenomena { get; set; }
        public List<RevenuePhenomenon> RecentPhenomena { get; set; }
        public List<GenreMicroclimate> Microclimates { get; set; }
        public List<RevenueForecast> Forecasts { get; set; }
        public List<string> AutonomousInsights { get; set; }
        public List<string> StormWarnings { get; set; }

        public RevenueWeatherReport()
        {
            ActivePhenomena = new List<RevenuePhenomenon>();
            RecentPhenomena = new List<RevenuePhenomenon>();
            Microclimates = new List<GenreMicroclimate>();
            Forecasts = new List<RevenueForecast>();
            AutonomousInsights = new List<string>();
            StormWarnings = new List<string>();
        }
    }

    /// <summary>
    /// Configuration for the Revenue Weather Engine.
    /// </summary>
    public class WeatherEngineConfig
    {
        public int WindowDays { get; set; }
        public double StormThresholdZScore { get; set; }
        public double DroughtThresholdPercent { get; set; }
        public int MinDataPointsForForecast { get; set; }

        public WeatherEngineConfig()
        {
            WindowDays = 90;
            StormThresholdZScore = 2.0;
            DroughtThresholdPercent = 30.0;
            MinDataPointsForForecast = 7;
        }
    }
}
