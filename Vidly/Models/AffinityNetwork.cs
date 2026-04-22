using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Movie affinity link: two movies frequently co-rented.
    /// </summary>
    public class MovieAffinity
    {
        public int MovieIdA { get; set; }
        public string MovieNameA { get; set; }
        public int MovieIdB { get; set; }
        public string MovieNameB { get; set; }
        /// <summary>Number of customers who rented both.</summary>
        public int SharedCustomers { get; set; }
        /// <summary>Jaccard similarity: shared / union of customer sets.</summary>
        public double AffinityScore { get; set; }
        public string Strength { get; set; }
    }

    /// <summary>
    /// A cluster of tightly connected movies.
    /// </summary>
    public class MovieCluster
    {
        public int ClusterId { get; set; }
        public string Label { get; set; }
        public List<ClusterMember> Members { get; set; }
        public double Cohesion { get; set; }

        public MovieCluster() { Members = new List<ClusterMember>(); }
    }

    public class ClusterMember
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public string Genre { get; set; }
        public int TotalRentals { get; set; }
        public int Connections { get; set; }
    }

    /// <summary>
    /// Full network summary returned by the service.
    /// </summary>
    public class AffinityNetworkSummary
    {
        public int TotalMovies { get; set; }
        public int TotalLinks { get; set; }
        public int TotalClusters { get; set; }
        public double AverageAffinity { get; set; }
        public List<MovieAffinity> TopAffinities { get; set; }
        public List<MovieAffinity> AllAffinities { get; set; }
        public List<MovieCluster> Clusters { get; set; }
        public List<AffinityInsight> Insights { get; set; }

        public AffinityNetworkSummary()
        {
            TopAffinities = new List<MovieAffinity>();
            AllAffinities = new List<MovieAffinity>();
            Clusters = new List<MovieCluster>();
            Insights = new List<AffinityInsight>();
        }
    }

    /// <summary>
    /// Autonomous insight discovered by the engine.
    /// </summary>
    public class AffinityInsight
    {
        public string Icon { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string ActionType { get; set; }
    }
}
