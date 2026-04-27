using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous Movie Affinity Network engine. Discovers hidden movie
    /// relationships through co-rental pattern analysis, builds an affinity
    /// graph with Jaccard similarity, detects clusters via greedy modularity,
    /// and produces proactive cross-sell / bundling insights.
    /// </summary>
    public class AffinityNetworkService
    {
        private readonly IMovieRepository _movieRepo;
        private readonly IRentalRepository _rentalRepo;
        private readonly ICustomerRepository _customerRepo;

        public AffinityNetworkService(
            IMovieRepository movieRepo,
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo)
        {
            _movieRepo = movieRepo ?? throw new ArgumentNullException(nameof(movieRepo));
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _customerRepo = customerRepo ?? throw new ArgumentNullException(nameof(customerRepo));
        }

        // ── Public API ──────────────────────────────────────────────

        public AffinityNetworkSummary BuildNetwork(int topN = 30)
        {
            // Step 1: Build customer → movies map
            var customers = _customerRepo.GetAll();
            var movieMap = new Dictionary<int, Movie>();
            foreach (var m in _movieRepo.GetAll())
                movieMap[m.Id] = m;

            // customer → set of movie ids they've rented
            var customerMovies = new Dictionary<int, HashSet<int>>();
            // movie → set of customer ids who rented it
            var movieCustomers = new Dictionary<int, HashSet<int>>();

            foreach (var c in customers)
            {
                var rentals = _rentalRepo.GetByCustomer(c.Id);
                if (rentals == null || rentals.Count == 0) continue;
                var movieIds = new HashSet<int>(rentals.Select(r => r.MovieId));
                customerMovies[c.Id] = movieIds;
                foreach (var mid in movieIds)
                {
                    if (!movieCustomers.ContainsKey(mid))
                        movieCustomers[mid] = new HashSet<int>();
                    movieCustomers[mid].Add(c.Id);
                }
            }

            // Step 2: Compute pairwise Jaccard affinity
            var movieIds = movieCustomers.Keys.OrderBy(x => x).ToList();
            var affinities = new List<MovieAffinity>();

            for (int i = 0; i < movieIds.Count; i++)
            {
                for (int j = i + 1; j < movieIds.Count; j++)
                {
                    var a = movieIds[i];
                    var b = movieIds[j];
                    var setA = movieCustomers[a];
                    var setB = movieCustomers[b];
                    // Iterate the smaller set for O(min(|A|,|B|)) intersection
                    var smaller = setA.Count <= setB.Count ? setA : setB;
                    var larger  = setA.Count <= setB.Count ? setB : setA;
                    int shared = 0;
                    foreach (var x in smaller)
                        if (larger.Contains(x)) shared++;
                    if (shared == 0) continue;
                    int union = setA.Count + setB.Count - shared;
                    double jaccard = (double)shared / union;

                    var nameA = movieMap.ContainsKey(a) ? movieMap[a].Name : $"Movie {a}";
                    var nameB = movieMap.ContainsKey(b) ? movieMap[b].Name : $"Movie {b}";

                    affinities.Add(new MovieAffinity
                    {
                        MovieIdA = a,
                        MovieNameA = nameA,
                        MovieIdB = b,
                        MovieNameB = nameB,
                        SharedCustomers = shared,
                        AffinityScore = Math.Round(jaccard, 3),
                        Strength = jaccard >= 0.5 ? "Strong"
                                 : jaccard >= 0.25 ? "Moderate"
                                 : "Weak"
                    });
                }
            }

            affinities = affinities.OrderByDescending(a => a.AffinityScore).ToList();

            // Pre-build affinity lookup: (min,max) movie-id pair → score for O(1) cohesion queries
            var affinityIndex = new Dictionary<long, double>(affinities.Count);
            foreach (var aff in affinities)
            {
                int lo = Math.Min(aff.MovieIdA, aff.MovieIdB);
                int hi = Math.Max(aff.MovieIdA, aff.MovieIdB);
                affinityIndex[((long)lo << 32) | (uint)hi] = aff.AffinityScore;
            }

            // Step 3: Greedy cluster detection (connected components on strong links)
            var strongThreshold = 0.15;
            var adj = new Dictionary<int, HashSet<int>>();
            foreach (var aff in affinities.Where(a => a.AffinityScore >= strongThreshold))
            {
                if (!adj.ContainsKey(aff.MovieIdA)) adj[aff.MovieIdA] = new HashSet<int>();
                if (!adj.ContainsKey(aff.MovieIdB)) adj[aff.MovieIdB] = new HashSet<int>();
                adj[aff.MovieIdA].Add(aff.MovieIdB);
                adj[aff.MovieIdB].Add(aff.MovieIdA);
            }

            var visited = new HashSet<int>();
            var clusters = new List<MovieCluster>();
            int clusterId = 0;

            foreach (var node in adj.Keys.OrderBy(x => x))
            {
                if (visited.Contains(node)) continue;
                clusterId++;
                var component = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(node);
                visited.Add(node);
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    component.Add(cur);
                    if (!adj.ContainsKey(cur)) continue;
                    foreach (var nb in adj[cur])
                    {
                        if (visited.Add(nb))
                            queue.Enqueue(nb);
                    }
                }

                if (component.Count < 2) continue;

                // Determine dominant genre for label
                var genreCounts = new Dictionary<string, int>();
                foreach (var mid in component)
                {
                    if (movieMap.ContainsKey(mid) && movieMap[mid].Genre.HasValue)
                    {
                        var g = movieMap[mid].Genre.Value.ToString();
                        genreCounts[g] = genreCounts.ContainsKey(g) ? genreCounts[g] + 1 : 1;
                    }
                }
                var dominantGenre = genreCounts.Any()
                    ? genreCounts.OrderByDescending(kv => kv.Value).First().Key
                    : "Mixed";

                // Cohesion = average affinity within cluster (O(C²) with O(1) lookups)
                int pairCount = 0;
                double totalAff = 0;
                for (int ci = 0; ci < component.Count; ci++)
                {
                    for (int cj = ci + 1; cj < component.Count; cj++)
                    {
                        int lo = Math.Min(component[ci], component[cj]);
                        int hi = Math.Max(component[ci], component[cj]);
                        long key = ((long)lo << 32) | (uint)hi;
                        double score;
                        if (affinityIndex.TryGetValue(key, out score))
                        {
                            totalAff += score;
                            pairCount++;
                        }
                    }
                }

                clusters.Add(new MovieCluster
                {
                    ClusterId = clusterId,
                    Label = $"{dominantGenre} Cluster #{clusterId}",
                    Cohesion = pairCount > 0 ? Math.Round(totalAff / pairCount, 3) : 0,
                    Members = component.Select(mid =>
                    {
                        var m = movieMap.ContainsKey(mid) ? movieMap[mid] : null;
                        var rentalCount = movieCustomers.ContainsKey(mid)
                            ? movieCustomers[mid].Count : 0;
                        return new ClusterMember
                        {
                            MovieId = mid,
                            MovieName = m?.Name ?? $"Movie {mid}",
                            Genre = m?.Genre?.ToString() ?? "Unknown",
                            TotalRentals = rentalCount,
                            Connections = adj.ContainsKey(mid) ? adj[mid].Count : 0
                        };
                    }).OrderByDescending(cm => cm.Connections).ToList()
                });
            }

            clusters = clusters.OrderByDescending(c => c.Members.Count).ToList();

            // Step 4: Generate autonomous insights
            var insights = new List<AffinityInsight>();

            if (affinities.Any())
            {
                var top = affinities.First();
                insights.Add(new AffinityInsight
                {
                    Icon = "🔗",
                    Title = "Strongest Affinity",
                    Description = $"\"{top.MovieNameA}\" and \"{top.MovieNameB}\" share {top.SharedCustomers} customers (Jaccard: {top.AffinityScore}) — ideal bundle pair.",
                    ActionType = "bundle"
                });
            }

            // Cross-genre surprises
            var crossGenre = affinities
                .Where(a => a.AffinityScore >= 0.2)
                .Where(a =>
                {
                    var gA = movieMap.ContainsKey(a.MovieIdA) ? movieMap[a.MovieIdA].Genre : null;
                    var gB = movieMap.ContainsKey(a.MovieIdB) ? movieMap[a.MovieIdB].Genre : null;
                    return gA.HasValue && gB.HasValue && gA != gB;
                })
                .OrderByDescending(a => a.AffinityScore)
                .FirstOrDefault();

            if (crossGenre != null)
            {
                var gA = movieMap[crossGenre.MovieIdA].Genre;
                var gB = movieMap[crossGenre.MovieIdB].Genre;
                insights.Add(new AffinityInsight
                {
                    Icon = "🎭",
                    Title = "Cross-Genre Discovery",
                    Description = $"Unexpected link: \"{crossGenre.MovieNameA}\" ({gA}) ↔ \"{crossGenre.MovieNameB}\" ({gB}). Consider cross-genre recommendation shelves.",
                    ActionType = "recommend"
                });
            }

            // Isolated movies
            var connectedMovies = new HashSet<int>(adj.Keys);
            var isolated = movieIds.Where(m => !connectedMovies.Contains(m)).ToList();
            if (isolated.Any())
            {
                insights.Add(new AffinityInsight
                {
                    Icon = "🏝️",
                    Title = $"{isolated.Count} Isolated Movie(s)",
                    Description = "These movies have no strong co-rental links. Consider promotional pairing to increase discovery.",
                    ActionType = "promote"
                });
            }

            if (clusters.Count >= 2)
            {
                insights.Add(new AffinityInsight
                {
                    Icon = "🧬",
                    Title = $"{clusters.Count} Natural Clusters Detected",
                    Description = $"Largest cluster: \"{clusters[0].Label}\" with {clusters[0].Members.Count} movies. Use these clusters for themed promotions.",
                    ActionType = "cluster"
                });
            }

            // Hub movies (highest connections)
            var hubMovie = movieIds
                .Where(m => adj.ContainsKey(m))
                .OrderByDescending(m => adj[m].Count)
                .FirstOrDefault();
            if (hubMovie > 0 && adj.ContainsKey(hubMovie) && adj[hubMovie].Count >= 3)
            {
                var name = movieMap.ContainsKey(hubMovie) ? movieMap[hubMovie].Name : $"Movie {hubMovie}";
                insights.Add(new AffinityInsight
                {
                    Icon = "⭐",
                    Title = "Hub Movie",
                    Description = $"\"{name}\" connects to {adj[hubMovie].Count} other movies — a gateway title that drives broader catalog discovery.",
                    ActionType = "feature"
                });
            }

            return new AffinityNetworkSummary
            {
                TotalMovies = movieIds.Count,
                TotalLinks = affinities.Count,
                TotalClusters = clusters.Count,
                AverageAffinity = affinities.Any()
                    ? Math.Round(affinities.Average(a => a.AffinityScore), 3) : 0,
                TopAffinities = affinities.Take(topN).ToList(),
                AllAffinities = affinities,
                Clusters = clusters,
                Insights = insights
            };
        }

        /// <summary>
        /// Get affinity neighbors for a specific movie.
        /// </summary>
        public List<MovieAffinity> GetNeighbors(int movieId, int topN = 10)
        {
            var network = BuildNetwork(100);
            return network.AllAffinities
                .Where(a => a.MovieIdA == movieId || a.MovieIdB == movieId)
                .OrderByDescending(a => a.AffinityScore)
                .Take(topN)
                .ToList();
        }
    }
}
