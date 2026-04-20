using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    public class CatalogGapDashboard
    {
        public double OverallCoverageScore { get; set; }
        public string CoverageGrade { get; set; }
        public List<GenreGap> GenreGaps { get; set; }
        public List<AcquisitionRecommendation> Recommendations { get; set; }
        public List<DemandSignal> UnmetDemand { get; set; }
        public CatalogHealthSummary Health { get; set; }
        public DateTime AnalyzedAt { get; set; }
    }

    public class GenreGap
    {
        public Genre Genre { get; set; }
        public int MovieCount { get; set; }
        public int RentalCount { get; set; }
        public double DemandRatio { get; set; }
        public double MarketSharePct { get; set; }
        public double CatalogSharePct { get; set; }
        public double GapScore { get; set; }
        public string Verdict { get; set; }
    }

    public class AcquisitionRecommendation
    {
        public Genre Genre { get; set; }
        public string Reason { get; set; }
        public int SuggestedCount { get; set; }
        public string Priority { get; set; }
        public double ExpectedImpact { get; set; }
        public int MinRating { get; set; }
    }

    public class DemandSignal
    {
        public string Pattern { get; set; }
        public string Category { get; set; }
        public double Confidence { get; set; }
        public string ActionItem { get; set; }
    }

    public class CatalogHealthSummary
    {
        public int TotalMovies { get; set; }
        public int TotalRentals { get; set; }
        public int ActiveGenres { get; set; }
        public double AvgRating { get; set; }
        public double FreshnessScore { get; set; }
        public int StaleMovies { get; set; }
        public double DiversityIndex { get; set; }
    }
}
