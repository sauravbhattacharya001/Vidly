using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    public class RetirementPlan
    {
        public DateTime GeneratedAt { get; set; }
        public List<MovieRetirementCandidate> Candidates { get; set; }
        public RetirementSummary Summary { get; set; }
        public List<string> ProactiveInsights { get; set; }
    }

    public class MovieRetirementCandidate
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public string Genre { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public int TotalRentals { get; set; }
        public int RecentRentals { get; set; }
        public double DeclineRate { get; set; }
        public int DaysSinceLastRental { get; set; }
        public double RetirementScore { get; set; }
        public string RetirementUrgency { get; set; }
        public string RecommendedAction { get; set; }
        public List<string> Reasons { get; set; }
        public double RevenueLifetime { get; set; }
        public double RevenueRecent { get; set; }
    }

    public class RetirementSummary
    {
        public int TotalMoviesAnalyzed { get; set; }
        public int ImmediateRetirements { get; set; }
        public int SoonRetirements { get; set; }
        public int MonitorList { get; set; }
        public int HealthyCount { get; set; }
        public double EstimatedShelfSpaceSavings { get; set; }
        public double RevenueAtRisk { get; set; }
        public List<GenreRetirementBreakdown> GenreBreakdown { get; set; }
        public List<ReplacementSuggestion> ReplacementSuggestions { get; set; }
    }

    public class GenreRetirementBreakdown
    {
        public string Genre { get; set; }
        public int Total { get; set; }
        public int RetirementCandidates { get; set; }
        public double AverageAge { get; set; }
        public string Health { get; set; }
    }

    public class ReplacementSuggestion
    {
        public string Genre { get; set; }
        public int RetiringCount { get; set; }
        public int SuggestedNewTitles { get; set; }
        public string Rationale { get; set; }
    }
}
