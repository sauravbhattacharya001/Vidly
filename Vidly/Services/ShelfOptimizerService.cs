using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    // ── Models ──────────────────────────────────────────────────

    public enum ShelfZone
    {
        PrimeShelf,
        DiscoveryZone,
        GenreCluster,
        SeasonalSpotlight,
        Archive
    }

    public enum RecommendationPriority { High, Medium, Low }

    public enum RecommendationType { Move, Cluster, Rotate, Promote, Demote }

    public class ShelfAssignment
    {
        public int MovieId { get; set; }
        public string MovieTitle { get; set; }
        public string GenreName { get; set; }
        public ShelfZone Zone { get; set; }
        public int Position { get; set; }
        public string Reason { get; set; }
        public double Score { get; set; }
    }

    public class ShelfCluster
    {
        public string Name { get; set; }
        public List<int> MovieIds { get; set; } = new List<int>();
        public List<string> MovieTitles { get; set; } = new List<string>();
        public double CoRentalScore { get; set; }
        public ShelfZone SuggestedZone { get; set; }
    }

    public class ProactiveRecommendation
    {
        public RecommendationType Type { get; set; }
        public string Message { get; set; }
        public RecommendationPriority Priority { get; set; }
        public string ImpactEstimate { get; set; }
    }

    public class ShelfPlan
    {
        public List<ShelfAssignment> Assignments { get; set; } = new List<ShelfAssignment>();
        public List<ShelfCluster> Clusters { get; set; } = new List<ShelfCluster>();
        public List<ProactiveRecommendation> Recommendations { get; set; } = new List<ProactiveRecommendation>();
        public double StalenessScore { get; set; }
        public DateTime GeneratedAt { get; set; }
        public Dictionary<ShelfZone, int> ZoneCounts { get; set; } = new Dictionary<ShelfZone, int>();
    }

    // ── Service ─────────────────────────────────────────────────

    /// <summary>
    /// Autonomous Shelf Optimizer — analyzes co-rental patterns, genre heat,
    /// and hidden gems to recommend optimal physical shelf placement.
    /// Detects stale arrangements and proactively suggests rotations.
    /// </summary>
    public class ShelfOptimizerService
    {
        private readonly IMovieRepository _movieRepo;
        private readonly IRentalRepository _rentalRepo;
        private readonly ICustomerRepository _customerRepo;

        public ShelfOptimizerService(
            IMovieRepository movieRepo,
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo)
        {
            _movieRepo = movieRepo ?? throw new ArgumentNullException(nameof(movieRepo));
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _customerRepo = customerRepo ?? throw new ArgumentNullException(nameof(customerRepo));
        }

        // ── Public API ──────────────────────────────────────────

        public ShelfPlan GeneratePlan()
        {
            var movies = _movieRepo.GetAll().ToList();
            var rentals = _rentalRepo.GetAll().ToList();

            var heat = ComputeGenreHeat(rentals, movies);
            var movieHeat = ComputeMovieHeat(rentals);
            var coRentals = ComputeCoRentalMatrix(rentals);
            var hiddenGems = FindHiddenGems(movies, movieHeat);
            var clusters = BuildClusters(movies, coRentals);
            var assignments = AssignShelves(movies, movieHeat, heat, hiddenGems, clusters);
            var recommendations = GenerateRecommendations(movies, movieHeat, heat, coRentals, hiddenGems);
            var staleness = ComputeStaleness(rentals, assignments);

            var plan = new ShelfPlan
            {
                Assignments = assignments,
                Clusters = clusters,
                Recommendations = recommendations,
                StalenessScore = staleness,
                GeneratedAt = DateTime.UtcNow
            };

            foreach (ShelfZone z in Enum.GetValues(typeof(ShelfZone)))
                plan.ZoneCounts[z] = assignments.Count(a => a.Zone == z);

            return plan;
        }

        public List<ShelfAssignment> GetByZone(string zoneName)
        {
            var plan = GeneratePlan();
            ShelfZone zone;
            if (!Enum.TryParse(zoneName, true, out zone))
                return new List<ShelfAssignment>();
            return plan.Assignments.Where(a => a.Zone == zone).ToList();
        }

        public List<ShelfCluster> GetClusters()
        {
            return GeneratePlan().Clusters;
        }

        // ── Genre Heat ──────────────────────────────────────────

        private Dictionary<Genre, double> ComputeGenreHeat(
            List<Rental> rentals, List<Movie> movies)
        {
            var movieGenre = movies
                .Where(m => m.Genre.HasValue)
                .ToDictionary(m => m.Id, m => m.Genre.Value);

            var recent = rentals
                .Where(r => r.RentalDate >= DateTime.Today.AddDays(-30))
                .ToList();

            var counts = new Dictionary<Genre, int>();
            foreach (var r in recent)
            {
                Genre g;
                if (movieGenre.TryGetValue(r.MovieId, out g))
                    counts[g] = counts.TryGetValue(g, out var c) ? c + 1 : 1;
            }

            if (!counts.Any())
                return Enum.GetValues(typeof(Genre)).Cast<Genre>()
                    .ToDictionary(g => g, g => 0.5);

            var max = (double)counts.Values.Max();
            return counts.ToDictionary(kv => kv.Key, kv => kv.Value / Math.Max(max, 1));
        }

        // ── Movie Heat ──────────────────────────────────────────

        private Dictionary<int, double> ComputeMovieHeat(List<Rental> rentals)
        {
            var recent = rentals
                .Where(r => r.RentalDate >= DateTime.Today.AddDays(-30))
                .GroupBy(r => r.MovieId)
                .ToDictionary(g => g.Key, g => (double)g.Count());

            if (!recent.Any()) return new Dictionary<int, double>();

            var max = recent.Values.Max();
            return recent.ToDictionary(kv => kv.Key, kv => kv.Value / Math.Max(max, 1));
        }

        // ── Co-Rental Matrix ────────────────────────────────────

        private Dictionary<string, double> ComputeCoRentalMatrix(List<Rental> rentals)
        {
            // Group rentals by customer, find movies rented by same customer
            var byCustomer = rentals
                .GroupBy(r => r.CustomerId)
                .Where(g => g.Count() >= 2)
                .ToDictionary(g => g.Key, g => g.Select(r => r.MovieId).Distinct().ToList());

            var pairs = new Dictionary<string, int>();
            foreach (var movieIds in byCustomer.Values)
            {
                for (int i = 0; i < movieIds.Count; i++)
                    for (int j = i + 1; j < movieIds.Count; j++)
                    {
                        var key = PairKey(movieIds[i], movieIds[j]);
                        pairs[key] = pairs.TryGetValue(key, out var c) ? c + 1 : 1;
                    }
            }

            if (!pairs.Any()) return new Dictionary<string, double>();
            var max = (double)pairs.Values.Max();
            return pairs.ToDictionary(kv => kv.Key, kv => kv.Value / Math.Max(max, 1));
        }

        private static string PairKey(int a, int b) =>
            a < b ? $"{a}:{b}" : $"{b}:{a}";

        // ── Hidden Gems ─────────────────────────────────────────

        private HashSet<int> FindHiddenGems(List<Movie> movies, Dictionary<int, double> movieHeat)
        {
            // High rated (4+) but low rental heat
            return new HashSet<int>(
                movies
                    .Where(m => m.Rating.HasValue && m.Rating.Value >= 4)
                    .Where(m => !movieHeat.ContainsKey(m.Id) || movieHeat[m.Id] < 0.3)
                    .Select(m => m.Id));
        }

        // ── Clusters ────────────────────────────────────────────

        private List<ShelfCluster> BuildClusters(
            List<Movie> movies, Dictionary<string, double> coRentals)
        {
            var movieById = movies.ToDictionary(m => m.Id);
            var clusters = new List<ShelfCluster>();

            // Find strong co-rental pairs (score > 0.5) and group them
            var strongPairs = coRentals
                .Where(kv => kv.Value >= 0.5)
                .OrderByDescending(kv => kv.Value)
                .ToList();

            var assigned = new HashSet<int>();
            int clusterNum = 1;

            foreach (var pair in strongPairs)
            {
                var parts = pair.Key.Split(':');
                int a = int.Parse(parts[0]);
                int b = int.Parse(parts[1]);

                if (assigned.Contains(a) && assigned.Contains(b)) continue;

                var cluster = new ShelfCluster
                {
                    Name = $"Cluster {clusterNum}",
                    CoRentalScore = pair.Value,
                    SuggestedZone = ShelfZone.GenreCluster
                };

                if (!assigned.Contains(a) && movieById.ContainsKey(a))
                {
                    cluster.MovieIds.Add(a);
                    cluster.MovieTitles.Add(movieById[a].Name);
                    assigned.Add(a);
                }
                if (!assigned.Contains(b) && movieById.ContainsKey(b))
                {
                    cluster.MovieIds.Add(b);
                    cluster.MovieTitles.Add(movieById[b].Name);
                    assigned.Add(b);
                }

                // Pull in other strongly connected movies
                foreach (var other in strongPairs)
                {
                    if (other.Key == pair.Key) continue;
                    var op = other.Key.Split(':');
                    int oa = int.Parse(op[0]);
                    int ob = int.Parse(op[1]);

                    if (cluster.MovieIds.Contains(oa) && !assigned.Contains(ob)
                        && movieById.ContainsKey(ob))
                    {
                        cluster.MovieIds.Add(ob);
                        cluster.MovieTitles.Add(movieById[ob].Name);
                        assigned.Add(ob);
                    }
                    else if (cluster.MovieIds.Contains(ob) && !assigned.Contains(oa)
                        && movieById.ContainsKey(oa))
                    {
                        cluster.MovieIds.Add(oa);
                        cluster.MovieTitles.Add(movieById[oa].Name);
                        assigned.Add(oa);
                    }

                    if (cluster.MovieIds.Count >= 6) break;
                }

                if (cluster.MovieIds.Count >= 2)
                {
                    // Name the cluster by most common genre
                    var genres = cluster.MovieIds
                        .Where(id => movieById.ContainsKey(id) && movieById[id].Genre.HasValue)
                        .GroupBy(id => movieById[id].Genre.Value)
                        .OrderByDescending(g => g.Count())
                        .FirstOrDefault();

                    if (genres != null)
                        cluster.Name = $"{genres.Key} Favorites";

                    clusters.Add(cluster);
                    clusterNum++;
                }

                if (clusters.Count >= 8) break;
            }

            return clusters;
        }

        // ── Assign Shelves ──────────────────────────────────────

        private List<ShelfAssignment> AssignShelves(
            List<Movie> movies,
            Dictionary<int, double> movieHeat,
            Dictionary<Genre, double> genreHeat,
            HashSet<int> hiddenGems,
            List<ShelfCluster> clusters)
        {
            var clusterMovies = new HashSet<int>(clusters.SelectMany(c => c.MovieIds));
            var assignments = new List<ShelfAssignment>();
            int pos = 1;

            foreach (var movie in movies.OrderByDescending(m =>
                movieHeat.TryGetValue(m.Id, out var h) ? h : 0))
            {
                double heat;
                movieHeat.TryGetValue(movie.Id, out heat);

                double gHeat = 0;
                if (movie.Genre.HasValue)
                    genreHeat.TryGetValue(movie.Genre.Value, out gHeat);

                ShelfZone zone;
                string reason;
                double score = heat * 0.7 + gHeat * 0.3;

                if (movie.IsNewRelease)
                {
                    zone = ShelfZone.SeasonalSpotlight;
                    reason = "New release — spotlight placement for visibility";
                    score = Math.Max(score, 0.8);
                }
                else if (heat >= 0.7)
                {
                    zone = ShelfZone.PrimeShelf;
                    reason = $"High demand ({heat:P0} rental heat) — eye-level placement";
                }
                else if (hiddenGems.Contains(movie.Id))
                {
                    zone = ShelfZone.DiscoveryZone;
                    reason = $"Hidden gem (rated {movie.Rating}/5, low rentals) — curated discovery section";
                }
                else if (clusterMovies.Contains(movie.Id))
                {
                    zone = ShelfZone.GenreCluster;
                    reason = "Frequently co-rented — grouped with similar titles";
                }
                else if (heat >= 0.3)
                {
                    zone = ShelfZone.GenreCluster;
                    reason = "Moderate demand — genre section placement";
                }
                else
                {
                    zone = ShelfZone.Archive;
                    reason = "Low recent demand — archive section";
                    score = Math.Max(score, 0.05);
                }

                assignments.Add(new ShelfAssignment
                {
                    MovieId = movie.Id,
                    MovieTitle = movie.Name,
                    GenreName = movie.Genre?.ToString() ?? "Unknown",
                    Zone = zone,
                    Position = pos++,
                    Reason = reason,
                    Score = Math.Round(score, 3)
                });
            }

            return assignments;
        }

        // ── Recommendations ─────────────────────────────────────

        private List<ProactiveRecommendation> GenerateRecommendations(
            List<Movie> movies,
            Dictionary<int, double> movieHeat,
            Dictionary<Genre, double> genreHeat,
            Dictionary<string, double> coRentals,
            HashSet<int> hiddenGems)
        {
            var recs = new List<ProactiveRecommendation>();
            var movieById = movies.ToDictionary(m => m.Id);

            // Promote surging movies
            var surging = movieHeat
                .Where(kv => kv.Value >= 0.6)
                .OrderByDescending(kv => kv.Value)
                .Take(3);

            foreach (var kv in surging)
            {
                if (movieById.TryGetValue(kv.Key, out var m))
                {
                    recs.Add(new ProactiveRecommendation
                    {
                        Type = RecommendationType.Promote,
                        Message = $"Move \"{m.Name}\" to Prime Shelf — demand at {kv.Value:P0}",
                        Priority = kv.Value >= 0.8
                            ? RecommendationPriority.High
                            : RecommendationPriority.Medium,
                        ImpactEstimate = $"+{(int)(kv.Value * 20)}% visibility boost"
                    });
                }
            }

            // Surface hidden gems
            foreach (var gemId in hiddenGems.Take(3))
            {
                if (movieById.TryGetValue(gemId, out var m))
                {
                    recs.Add(new ProactiveRecommendation
                    {
                        Type = RecommendationType.Move,
                        Message = $"Surface \"{m.Name}\" (rated {m.Rating}/5) to Discovery Zone — underexposed gem",
                        Priority = RecommendationPriority.Medium,
                        ImpactEstimate = "Potential 30-50% rental increase"
                    });
                }
            }

            // Suggest clusters for strong co-rentals
            var topPairs = coRentals
                .Where(kv => kv.Value >= 0.6)
                .OrderByDescending(kv => kv.Value)
                .Take(3);

            foreach (var pair in topPairs)
            {
                var parts = pair.Key.Split(':');
                int a = int.Parse(parts[0]);
                int b = int.Parse(parts[1]);
                Movie ma, mb;
                if (movieById.TryGetValue(a, out ma) && movieById.TryGetValue(b, out mb))
                {
                    recs.Add(new ProactiveRecommendation
                    {
                        Type = RecommendationType.Cluster,
                        Message = $"Create cluster: \"{ma.Name}\" + \"{mb.Name}\" — {pair.Value:P0} co-rental rate",
                        Priority = RecommendationPriority.Medium,
                        ImpactEstimate = "Cross-selling opportunity"
                    });
                }
            }

            // Hot genre rotation
            var hotGenre = genreHeat
                .OrderByDescending(kv => kv.Value)
                .FirstOrDefault();

            if (hotGenre.Value >= 0.7)
            {
                recs.Add(new ProactiveRecommendation
                {
                    Type = RecommendationType.Rotate,
                    Message = $"{hotGenre.Key} is trending — expand Prime Shelf allocation for this genre",
                    Priority = RecommendationPriority.High,
                    ImpactEstimate = $"Genre heat at {hotGenre.Value:P0}"
                });
            }

            // Demote cold movies
            var cold = movieHeat
                .Where(kv => kv.Value < 0.1)
                .Take(2);

            foreach (var kv in cold)
            {
                if (movieById.TryGetValue(kv.Key, out var m))
                {
                    recs.Add(new ProactiveRecommendation
                    {
                        Type = RecommendationType.Demote,
                        Message = $"Move \"{m.Name}\" to Archive — near-zero demand",
                        Priority = RecommendationPriority.Low,
                        ImpactEstimate = "Free shelf space for higher-demand titles"
                    });
                }
            }

            return recs.OrderBy(r => r.Priority).ToList();
        }

        // ── Staleness ───────────────────────────────────────────

        private double ComputeStaleness(
            List<Rental> rentals, List<ShelfAssignment> assignments)
        {
            // Staleness increases if recent rental patterns diverge from shelf positions
            var recent = rentals
                .Where(r => r.RentalDate >= DateTime.Today.AddDays(-7))
                .GroupBy(r => r.MovieId)
                .ToDictionary(g => g.Key, g => g.Count());

            if (!recent.Any()) return 20; // Low data = slightly stale

            var primeMovies = new HashSet<int>(
                assignments
                    .Where(a => a.Zone == ShelfZone.PrimeShelf)
                    .Select(a => a.MovieId));

            // Movies with high recent demand NOT on prime shelf = staleness signal
            var topRecent = recent.OrderByDescending(kv => kv.Value).Take(5).ToList();
            int misplaced = topRecent.Count(kv => !primeMovies.Contains(kv.Key));

            return Math.Min(100, misplaced * 20.0);
        }
    }
}
