using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Genre Ecosystem Analyzer — autonomous engine that treats the store's genre catalog
    /// as an interconnected ecosystem. Maps genre co-rental relationships, identifies
    /// genre "bridge" customers, detects emerging/declining genre trends, finds genre
    /// deserts (underserved demand), and generates catalog acquisition recommendations.
    /// </summary>
    public class GenreEcosystemService
    {
        private readonly IMovieRepository _movieRepo;
        private readonly IRentalRepository _rentalRepo;
        private readonly ICustomerRepository _customerRepo;
        private readonly IClock _clock;

        private static readonly string[] AllGenres = Enum.GetNames(typeof(Genre));

        public GenreEcosystemService(
            IMovieRepository movieRepo,
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo,
            IClock clock = null)
        {
            _movieRepo = movieRepo ?? throw new ArgumentNullException(nameof(movieRepo));
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _customerRepo = customerRepo ?? throw new ArgumentNullException(nameof(customerRepo));
            _clock = clock ?? new SystemClock();
        }

        // ══════════════════════════════════════════════════════════════
        // Public API
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Generate a full genre ecosystem report.
        /// </summary>
        public GenreEcosystemReport Analyze(int lookbackDays = 180)
        {
            var now = _clock.Now;
            var cutoff = now.AddDays(-lookbackDays);
            var allMovies = _movieRepo.GetAll();
            var allRentals = _rentalRepo.GetAll();
            var allCustomers = _customerRepo.GetAll();

            var recentRentals = allRentals.Where(r => r.RentalDate >= cutoff).ToList();
            var movieLookup = allMovies.ToDictionary(m => m.Id);
            var rentalsByCustomer = recentRentals
                .GroupBy(r => r.CustomerId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var genreNodes = BuildGenreNodes(recentRentals, allMovies, movieLookup);
            var coRentalMatrix = BuildCoRentalMatrix(rentalsByCustomer, movieLookup);
            var bridges = FindBridgeCustomers(rentalsByCustomer, movieLookup, allCustomers);
            var trends = DetectGenreTrends(allRentals, movieLookup, now);
            var deserts = FindGenreDeserts(genreNodes, coRentalMatrix);
            var recommendations = GenerateRecommendations(genreNodes, coRentalMatrix, trends, deserts, bridges);
            var ecosystemHealth = CalculateEcosystemHealth(genreNodes, coRentalMatrix, bridges);

            return new GenreEcosystemReport
            {
                GeneratedAt = now,
                LookbackDays = lookbackDays,
                TotalRentalsAnalyzed = recentRentals.Count,
                TotalCustomersAnalyzed = rentalsByCustomer.Count,
                GenreNodes = genreNodes,
                CoRentalEdges = FlattenMatrix(coRentalMatrix),
                BridgeCustomers = bridges,
                Trends = trends,
                Deserts = deserts,
                Recommendations = recommendations,
                EcosystemHealth = ecosystemHealth
            };
        }

        /// <summary>
        /// Get co-rental affinity between two genres (0.0 = no affinity, 1.0 = perfect).
        /// </summary>
        public double GetGenreAffinity(string genreA, string genreB, int lookbackDays = 180)
        {
            var now = _clock.Now;
            var cutoff = now.AddDays(-lookbackDays);
            var allMovies = _movieRepo.GetAll();
            var recentRentals = _rentalRepo.GetAll().Where(r => r.RentalDate >= cutoff).ToList();
            var movieLookup = allMovies.ToDictionary(m => m.Id);
            var rentalsByCustomer = recentRentals
                .GroupBy(r => r.CustomerId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var matrix = BuildCoRentalMatrix(rentalsByCustomer, movieLookup);
            var key = MakeEdgeKey(genreA, genreB);
            return matrix.ContainsKey(key) ? matrix[key].Affinity : 0.0;
        }

        /// <summary>
        /// Get the top bridge customers (those who connect the most genres).
        /// </summary>
        public List<GenreBridgeCustomer> GetTopBridges(int topN = 10, int lookbackDays = 180)
        {
            var now = _clock.Now;
            var cutoff = now.AddDays(-lookbackDays);
            var allMovies = _movieRepo.GetAll();
            var allCustomers = _customerRepo.GetAll();
            var recentRentals = _rentalRepo.GetAll().Where(r => r.RentalDate >= cutoff).ToList();
            var movieLookup = allMovies.ToDictionary(m => m.Id);
            var rentalsByCustomer = recentRentals
                .GroupBy(r => r.CustomerId)
                .ToDictionary(g => g.Key, g => g.ToList());

            return FindBridgeCustomers(rentalsByCustomer, movieLookup, allCustomers)
                .OrderByDescending(b => b.BridgeScore)
                .Take(topN)
                .ToList();
        }

        // ══════════════════════════════════════════════════════════════
        // Genre Node Analysis
        // ══════════════════════════════════════════════════════════════

        private List<GenreNode> BuildGenreNodes(
            List<Rental> rentals,
            IReadOnlyList<Movie> allMovies,
            Dictionary<int, Movie> movieLookup)
        {
            var genreRentals = new Dictionary<string, List<Rental>>();
            var genreMovies = new Dictionary<string, int>();

            // Count movies per genre
            foreach (var movie in allMovies)
            {
                var g = movie.Genre?.ToString() ?? "Unknown";
                if (!genreMovies.ContainsKey(g)) genreMovies[g] = 0;
                genreMovies[g]++;
            }

            // Group rentals by genre
            foreach (var rental in rentals)
            {
                Movie movie;
                if (!movieLookup.TryGetValue(rental.MovieId, out movie)) continue;
                var g = movie.Genre?.ToString() ?? "Unknown";
                if (!genreRentals.ContainsKey(g)) genreRentals[g] = new List<Rental>();
                genreRentals[g].Add(rental);
            }

            var nodes = new List<GenreNode>();
            var totalRentals = rentals.Count;

            foreach (var genre in AllGenres)
            {
                var gRentals = genreRentals.ContainsKey(genre) ? genreRentals[genre] : new List<Rental>();
                var movieCount = genreMovies.ContainsKey(genre) ? genreMovies[genre] : 0;
                var rentalCount = gRentals.Count;
                var uniqueCustomers = gRentals.Select(r => r.CustomerId).Distinct().Count();

                // Velocity = rentals per movie (how actively each movie is rented)
                var velocity = movieCount > 0 ? (double)rentalCount / movieCount : 0;

                // Market share
                var marketShare = totalRentals > 0 ? (double)rentalCount / totalRentals : 0;

                // Revenue
                var revenue = gRentals.Sum(r => r.DailyRate * Math.Max(1,
                    (int)Math.Ceiling(((r.ReturnDate ?? _clock.Now) - r.RentalDate).TotalDays)));

                // Average rating of genre movies
                var ratedMovies = allMovies.Where(m => m.Genre?.ToString() == genre && m.Rating.HasValue).ToList();
                var avgRating = ratedMovies.Any() ? ratedMovies.Average(m => m.Rating.Value) : 0;

                nodes.Add(new GenreNode
                {
                    Genre = genre,
                    MovieCount = movieCount,
                    RentalCount = rentalCount,
                    UniqueCustomers = uniqueCustomers,
                    Velocity = Math.Round(velocity, 2),
                    MarketShare = Math.Round(marketShare, 4),
                    Revenue = Math.Round(revenue, 2),
                    AverageRating = Math.Round(avgRating, 2),
                    Status = ClassifyGenreStatus(velocity, marketShare)
                });
            }

            return nodes.OrderByDescending(n => n.RentalCount).ToList();
        }

        private string ClassifyGenreStatus(double velocity, double marketShare)
        {
            if (velocity >= 3.0 && marketShare >= 0.15) return "Star";
            if (velocity >= 2.0 && marketShare >= 0.08) return "Growing";
            if (velocity >= 1.0) return "Stable";
            if (marketShare < 0.03) return "Niche";
            return "Declining";
        }

        // ══════════════════════════════════════════════════════════════
        // Co-Rental Matrix
        // ══════════════════════════════════════════════════════════════

        private Dictionary<string, CoRentalEdge> BuildCoRentalMatrix(
            Dictionary<int, List<Rental>> rentalsByCustomer,
            Dictionary<int, Movie> movieLookup)
        {
            var pairCounts = new Dictionary<string, int>();
            var genreCounts = new Dictionary<string, int>();

            foreach (var kvp in rentalsByCustomer)
            {
                var genres = kvp.Value
                    .Select(r =>
                    {
                        Movie m;
                        return movieLookup.TryGetValue(r.MovieId, out m) ? m.Genre?.ToString() : null;
                    })
                    .Where(g => g != null)
                    .Distinct()
                    .OrderBy(g => g)
                    .ToList();

                foreach (var g in genres)
                {
                    if (!genreCounts.ContainsKey(g)) genreCounts[g] = 0;
                    genreCounts[g]++;
                }

                for (int i = 0; i < genres.Count; i++)
                {
                    for (int j = i + 1; j < genres.Count; j++)
                    {
                        var key = MakeEdgeKey(genres[i], genres[j]);
                        if (!pairCounts.ContainsKey(key)) pairCounts[key] = 0;
                        pairCounts[key]++;
                    }
                }
            }

            var edges = new Dictionary<string, CoRentalEdge>();
            foreach (var kvp in pairCounts)
            {
                var parts = kvp.Key.Split('|');
                var countA = genreCounts.ContainsKey(parts[0]) ? genreCounts[parts[0]] : 1;
                var countB = genreCounts.ContainsKey(parts[1]) ? genreCounts[parts[1]] : 1;
                // Jaccard-like affinity
                var union = countA + countB - kvp.Value;
                var affinity = union > 0 ? (double)kvp.Value / union : 0;

                edges[kvp.Key] = new CoRentalEdge
                {
                    GenreA = parts[0],
                    GenreB = parts[1],
                    SharedCustomers = kvp.Value,
                    Affinity = Math.Round(affinity, 4),
                    Strength = affinity >= 0.4 ? "Strong" : affinity >= 0.2 ? "Moderate" : "Weak"
                };
            }

            return edges;
        }

        // ══════════════════════════════════════════════════════════════
        // Bridge Customer Detection
        // ══════════════════════════════════════════════════════════════

        private List<GenreBridgeCustomer> FindBridgeCustomers(
            Dictionary<int, List<Rental>> rentalsByCustomer,
            Dictionary<int, Movie> movieLookup,
            IReadOnlyList<Customer> allCustomers)
        {
            var customerLookup = allCustomers.ToDictionary(c => c.Id);
            var bridges = new List<GenreBridgeCustomer>();

            foreach (var kvp in rentalsByCustomer)
            {
                var genresRented = kvp.Value
                    .Select(r =>
                    {
                        Movie m;
                        return movieLookup.TryGetValue(r.MovieId, out m) ? m.Genre?.ToString() : null;
                    })
                    .Where(g => g != null)
                    .ToList();

                var distinctGenres = genresRented.Distinct().ToList();
                if (distinctGenres.Count < 3) continue; // Need at least 3 genres to be a bridge

                // Bridge score = genre diversity * rental spread entropy
                var genreDist = genresRented.GroupBy(g => g)
                    .Select(g => (double)g.Count() / genresRented.Count)
                    .ToList();
                var entropy = -genreDist.Sum(p => p > 0 ? p * Math.Log(p, 2) : 0);
                var maxEntropy = Math.Log(distinctGenres.Count, 2);
                var evenness = maxEntropy > 0 ? entropy / maxEntropy : 0;

                var bridgeScore = distinctGenres.Count * evenness * Math.Min(kvp.Value.Count / 5.0, 1.0);

                Customer cust;
                var name = customerLookup.TryGetValue(kvp.Key, out cust) ? cust.Name : "Customer " + kvp.Key;

                bridges.Add(new GenreBridgeCustomer
                {
                    CustomerId = kvp.Key,
                    CustomerName = name,
                    GenresExplored = distinctGenres.OrderBy(g => g).ToList(),
                    GenreCount = distinctGenres.Count,
                    TotalRentals = kvp.Value.Count,
                    Evenness = Math.Round(evenness, 3),
                    BridgeScore = Math.Round(bridgeScore, 2),
                    TopGenre = genresRented.GroupBy(g => g).OrderByDescending(g => g.Count()).First().Key,
                    Classification = ClassifyBridge(distinctGenres.Count, evenness)
                });
            }

            return bridges.OrderByDescending(b => b.BridgeScore).ToList();
        }

        private string ClassifyBridge(int genreCount, double evenness)
        {
            if (genreCount >= 7 && evenness >= 0.8) return "Omnivore";
            if (genreCount >= 5 && evenness >= 0.6) return "Explorer";
            if (genreCount >= 3 && evenness >= 0.4) return "Crossover";
            return "Dabbler";
        }

        // ══════════════════════════════════════════════════════════════
        // Genre Trend Detection
        // ══════════════════════════════════════════════════════════════

        private List<GenreTrend> DetectGenreTrends(
            IReadOnlyList<Rental> allRentals,
            Dictionary<int, Movie> movieLookup,
            DateTime now)
        {
            var trends = new List<GenreTrend>();
            // Compare 3 periods: recent (0-60d), mid (60-120d), old (120-180d)
            var recent = allRentals.Where(r => r.RentalDate >= now.AddDays(-60)).ToList();
            var mid = allRentals.Where(r => r.RentalDate >= now.AddDays(-120) && r.RentalDate < now.AddDays(-60)).ToList();
            var old = allRentals.Where(r => r.RentalDate >= now.AddDays(-180) && r.RentalDate < now.AddDays(-120)).ToList();

            foreach (var genre in AllGenres)
            {
                var recentCount = CountGenreRentals(recent, genre, movieLookup);
                var midCount = CountGenreRentals(mid, genre, movieLookup);
                var oldCount = CountGenreRentals(old, genre, movieLookup);

                // Simple linear trend: slope across the 3 periods
                // periods: old=0, mid=1, recent=2
                var counts = new double[] { oldCount, midCount, recentCount };
                var slope = CalculateSlope(counts);
                var momentum = counts[2] - counts[1]; // recent change

                var direction = "Stable";
                if (slope > 1.0) direction = "Rising";
                else if (slope > 0.3) direction = "Warming";
                else if (slope < -1.0) direction = "Falling";
                else if (slope < -0.3) direction = "Cooling";

                // Volatility (standard deviation of counts)
                var mean = counts.Average();
                var variance = counts.Select(c => (c - mean) * (c - mean)).Average();
                var volatility = Math.Sqrt(variance);

                // Forecast next period using linear extrapolation
                var forecast = Math.Max(0, counts[2] + slope);

                trends.Add(new GenreTrend
                {
                    Genre = genre,
                    OldPeriodRentals = oldCount,
                    MidPeriodRentals = midCount,
                    RecentPeriodRentals = recentCount,
                    Slope = Math.Round(slope, 2),
                    Momentum = momentum,
                    Direction = direction,
                    Volatility = Math.Round(volatility, 2),
                    ForecastNextPeriod = (int)Math.Round(forecast),
                    Confidence = volatility < 2.0 ? "High" : volatility < 5.0 ? "Medium" : "Low"
                });
            }

            return trends.OrderByDescending(t => Math.Abs(t.Slope)).ToList();
        }

        private int CountGenreRentals(List<Rental> rentals, string genre, Dictionary<int, Movie> movieLookup)
        {
            return rentals.Count(r =>
            {
                Movie m;
                return movieLookup.TryGetValue(r.MovieId, out m) && m.Genre?.ToString() == genre;
            });
        }

        private double CalculateSlope(double[] values)
        {
            // Simple linear regression slope for evenly-spaced points
            var n = values.Length;
            var xMean = (n - 1) / 2.0;
            var yMean = values.Average();
            var num = 0.0;
            var den = 0.0;
            for (int i = 0; i < n; i++)
            {
                num += (i - xMean) * (values[i] - yMean);
                den += (i - xMean) * (i - xMean);
            }
            return den > 0 ? num / den : 0;
        }

        // ══════════════════════════════════════════════════════════════
        // Genre Desert Detection
        // ══════════════════════════════════════════════════════════════

        private List<GenreDesert> FindGenreDeserts(
            List<GenreNode> nodes,
            Dictionary<string, CoRentalEdge> coRentalMatrix)
        {
            var deserts = new List<GenreDesert>();
            var avgVelocity = nodes.Where(n => n.RentalCount > 0).Select(n => n.Velocity).DefaultIfEmpty(0).Average();

            foreach (var node in nodes)
            {
                // A desert = high demand signal but low supply, OR connected to strong genres but underperforming
                var connectedStrength = coRentalMatrix.Values
                    .Where(e => e.GenreA == node.Genre || e.GenreB == node.Genre)
                    .Sum(e => e.Affinity);

                var isDesert = false;
                var reason = "";

                if (node.MovieCount == 0)
                {
                    isDesert = true;
                    reason = "No movies in catalog";
                }
                else if (node.Velocity > avgVelocity * 1.5 && node.MovieCount < 5)
                {
                    isDesert = true;
                    reason = "High velocity but thin catalog — demand exceeds supply";
                }
                else if (connectedStrength > 1.5 && node.RentalCount < 3)
                {
                    isDesert = true;
                    reason = "Strong cross-genre connections but low rental activity — untapped potential";
                }
                else if (node.UniqueCustomers > 0 && node.MovieCount <= 2 && node.Velocity > avgVelocity)
                {
                    isDesert = true;
                    reason = "Multiple customers competing for very limited inventory";
                }

                if (isDesert)
                {
                    // Estimate demand gap
                    var idealMovieCount = node.Velocity > 0
                        ? (int)Math.Ceiling(node.RentalCount / Math.Max(avgVelocity, 0.5))
                        : 3;
                    var gap = Math.Max(0, idealMovieCount - node.MovieCount);

                    deserts.Add(new GenreDesert
                    {
                        Genre = node.Genre,
                        CurrentMovieCount = node.MovieCount,
                        CurrentVelocity = node.Velocity,
                        Reason = reason,
                        EstimatedMoviesNeeded = gap,
                        ConnectedGenreStrength = Math.Round(connectedStrength, 2),
                        Urgency = gap >= 5 ? "High" : gap >= 2 ? "Medium" : "Low"
                    });
                }
            }

            return deserts.OrderByDescending(d => d.EstimatedMoviesNeeded).ToList();
        }

        // ══════════════════════════════════════════════════════════════
        // Recommendation Engine
        // ══════════════════════════════════════════════════════════════

        private List<EcosystemRecommendation> GenerateRecommendations(
            List<GenreNode> nodes,
            Dictionary<string, CoRentalEdge> coRentalMatrix,
            List<GenreTrend> trends,
            List<GenreDesert> deserts,
            List<GenreBridgeCustomer> bridges)
        {
            var recs = new List<EcosystemRecommendation>();
            var priority = 1;

            // 1. Fill genre deserts
            foreach (var desert in deserts.Take(3))
            {
                recs.Add(new EcosystemRecommendation
                {
                    Priority = priority++,
                    Category = "Catalog Gap",
                    Action = string.Format("Acquire {0} more {1} titles",
                        desert.EstimatedMoviesNeeded, desert.Genre),
                    Rationale = desert.Reason,
                    ExpectedImpact = "High",
                    TargetGenre = desert.Genre
                });
            }

            // 2. Capitalize on rising trends
            foreach (var trend in trends.Where(t => t.Direction == "Rising").Take(2))
            {
                var node = nodes.FirstOrDefault(n => n.Genre == trend.Genre);
                recs.Add(new EcosystemRecommendation
                {
                    Priority = priority++,
                    Category = "Trend Capture",
                    Action = string.Format("Expand {0} catalog — demand up {1}% over 6 months",
                        trend.Genre, trend.RecentPeriodRentals > 0 && trend.OldPeriodRentals > 0
                            ? ((trend.RecentPeriodRentals - trend.OldPeriodRentals) * 100 / Math.Max(trend.OldPeriodRentals, 1))
                            : 0),
                    Rationale = string.Format("Slope {0}, momentum {1}, forecast {2} rentals next period",
                        trend.Slope, trend.Momentum, trend.ForecastNextPeriod),
                    ExpectedImpact = "Medium",
                    TargetGenre = trend.Genre
                });
            }

            // 3. Leverage strong cross-genre connections
            var strongEdges = coRentalMatrix.Values
                .Where(e => e.Strength == "Strong")
                .OrderByDescending(e => e.SharedCustomers)
                .Take(2);
            foreach (var edge in strongEdges)
            {
                recs.Add(new EcosystemRecommendation
                {
                    Priority = priority++,
                    Category = "Cross-Promotion",
                    Action = string.Format("Create {0} + {1} bundle promotion",
                        edge.GenreA, edge.GenreB),
                    Rationale = string.Format("{0} shared customers with {1} affinity",
                        edge.SharedCustomers, edge.Affinity),
                    ExpectedImpact = "Medium",
                    TargetGenre = edge.GenreA + "/" + edge.GenreB
                });
            }

            // 4. Engage bridge customers
            var topBridges = bridges.Where(b => b.Classification == "Omnivore" || b.Classification == "Explorer").Take(3).ToList();
            if (topBridges.Any())
            {
                recs.Add(new EcosystemRecommendation
                {
                    Priority = priority++,
                    Category = "Customer Engagement",
                    Action = string.Format("Launch 'Genre Explorer' program for {0} bridge customers", topBridges.Count),
                    Rationale = "Bridge customers drive cross-genre discovery and have high lifetime value",
                    ExpectedImpact = "Medium",
                    TargetGenre = "All"
                });
            }

            // 5. Address declining genres
            foreach (var trend in trends.Where(t => t.Direction == "Falling").Take(2))
            {
                recs.Add(new EcosystemRecommendation
                {
                    Priority = priority++,
                    Category = "Genre Recovery",
                    Action = string.Format("Investigate {0} decline — consider promotional pricing or curated picks",
                        trend.Genre),
                    Rationale = string.Format("Down {0} rentals period-over-period, slope {1}",
                        Math.Abs(trend.Momentum), trend.Slope),
                    ExpectedImpact = "Low",
                    TargetGenre = trend.Genre
                });
            }

            return recs;
        }

        // ══════════════════════════════════════════════════════════════
        // Ecosystem Health Score
        // ══════════════════════════════════════════════════════════════

        private EcosystemHealth CalculateEcosystemHealth(
            List<GenreNode> nodes,
            Dictionary<string, CoRentalEdge> coRentalMatrix,
            List<GenreBridgeCustomer> bridges)
        {
            // Diversity: Shannon entropy of genre market shares
            var shares = nodes.Where(n => n.MarketShare > 0).Select(n => n.MarketShare).ToList();
            var entropy = shares.Any()
                ? -shares.Sum(p => p > 0 ? p * Math.Log(p, 2) : 0)
                : 0;
            var maxEntropy = shares.Count > 1 ? Math.Log(shares.Count, 2) : 1;
            var diversityScore = maxEntropy > 0 ? entropy / maxEntropy * 100 : 0;

            // Connectivity: proportion of possible genre pairs that have meaningful co-rental
            var possiblePairs = AllGenres.Length * (AllGenres.Length - 1) / 2.0;
            var meaningfulEdges = coRentalMatrix.Values.Count(e => e.Affinity > 0.1);
            var connectivityScore = possiblePairs > 0 ? meaningfulEdges / possiblePairs * 100 : 0;

            // Vibrancy: what fraction of genres have non-zero recent activity
            var activeGenres = nodes.Count(n => n.RentalCount > 0);
            var vibrancyScore = AllGenres.Length > 0 ? (double)activeGenres / AllGenres.Length * 100 : 0;

            // Bridge density: bridge customers as % of total active customers
            var totalActive = nodes.Sum(n => n.UniqueCustomers);
            var bridgeDensity = totalActive > 0 ? (double)bridges.Count / totalActive * 100 : 0;

            var overall = (diversityScore * 0.30 + connectivityScore * 0.25 +
                          vibrancyScore * 0.25 + Math.Min(bridgeDensity * 5, 100) * 0.20);

            return new EcosystemHealth
            {
                OverallScore = (int)Math.Round(Math.Min(overall, 100)),
                DiversityScore = (int)Math.Round(Math.Min(diversityScore, 100)),
                ConnectivityScore = (int)Math.Round(Math.Min(connectivityScore, 100)),
                VibrancyScore = (int)Math.Round(Math.Min(vibrancyScore, 100)),
                BridgeDensityPercent = Math.Round(bridgeDensity, 1),
                Grade = overall >= 80 ? "A" : overall >= 60 ? "B" : overall >= 40 ? "C" : overall >= 20 ? "D" : "F",
                Summary = GenerateHealthSummary(overall, diversityScore, connectivityScore, vibrancyScore, bridgeDensity)
            };
        }

        private string GenerateHealthSummary(double overall, double diversity, double connectivity, double vibrancy, double bridgeDensity)
        {
            var parts = new List<string>();
            if (diversity < 50) parts.Add("Genre catalog is top-heavy — consider diversifying");
            if (connectivity < 30) parts.Add("Genres are siloed — customers rarely cross boundaries");
            if (vibrancy < 60) parts.Add("Several genres show no activity — evaluate relevance");
            if (bridgeDensity < 5) parts.Add("Few bridge customers — consider cross-genre promotions");
            if (!parts.Any()) parts.Add("Healthy ecosystem with good diversity and cross-genre flow");
            return string.Join(". ", parts) + ".";
        }

        // ══════════════════════════════════════════════════════════════
        // Helpers
        // ══════════════════════════════════════════════════════════════

        private static string MakeEdgeKey(string a, string b)
        {
            return string.Compare(a, b, StringComparison.Ordinal) <= 0
                ? a + "|" + b
                : b + "|" + a;
        }

        private static List<CoRentalEdge> FlattenMatrix(Dictionary<string, CoRentalEdge> matrix)
        {
            return matrix.Values.OrderByDescending(e => e.Affinity).ToList();
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Models
    // ══════════════════════════════════════════════════════════════

    public class GenreEcosystemReport
    {
        public DateTime GeneratedAt { get; set; }
        public int LookbackDays { get; set; }
        public int TotalRentalsAnalyzed { get; set; }
        public int TotalCustomersAnalyzed { get; set; }
        public List<GenreNode> GenreNodes { get; set; }
        public List<CoRentalEdge> CoRentalEdges { get; set; }
        public List<GenreBridgeCustomer> BridgeCustomers { get; set; }
        public List<GenreTrend> Trends { get; set; }
        public List<GenreDesert> Deserts { get; set; }
        public List<EcosystemRecommendation> Recommendations { get; set; }
        public EcosystemHealth EcosystemHealth { get; set; }
    }

    public class GenreNode
    {
        public string Genre { get; set; }
        public int MovieCount { get; set; }
        public int RentalCount { get; set; }
        public int UniqueCustomers { get; set; }
        public double Velocity { get; set; }
        public double MarketShare { get; set; }
        public decimal Revenue { get; set; }
        public double AverageRating { get; set; }
        public string Status { get; set; } // Star, Growing, Stable, Niche, Declining
    }

    public class CoRentalEdge
    {
        public string GenreA { get; set; }
        public string GenreB { get; set; }
        public int SharedCustomers { get; set; }
        public double Affinity { get; set; }
        public string Strength { get; set; } // Strong, Moderate, Weak
    }

    public class GenreBridgeCustomer
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public List<string> GenresExplored { get; set; }
        public int GenreCount { get; set; }
        public int TotalRentals { get; set; }
        public double Evenness { get; set; }
        public double BridgeScore { get; set; }
        public string TopGenre { get; set; }
        public string Classification { get; set; } // Omnivore, Explorer, Crossover, Dabbler
    }

    public class GenreTrend
    {
        public string Genre { get; set; }
        public int OldPeriodRentals { get; set; }
        public int MidPeriodRentals { get; set; }
        public int RecentPeriodRentals { get; set; }
        public double Slope { get; set; }
        public int Momentum { get; set; }
        public string Direction { get; set; } // Rising, Warming, Stable, Cooling, Falling
        public double Volatility { get; set; }
        public int ForecastNextPeriod { get; set; }
        public string Confidence { get; set; }
    }

    public class GenreDesert
    {
        public string Genre { get; set; }
        public int CurrentMovieCount { get; set; }
        public double CurrentVelocity { get; set; }
        public string Reason { get; set; }
        public int EstimatedMoviesNeeded { get; set; }
        public double ConnectedGenreStrength { get; set; }
        public string Urgency { get; set; }
    }

    public class EcosystemRecommendation
    {
        public int Priority { get; set; }
        public string Category { get; set; }
        public string Action { get; set; }
        public string Rationale { get; set; }
        public string ExpectedImpact { get; set; }
        public string TargetGenre { get; set; }
    }

    public class EcosystemHealth
    {
        public int OverallScore { get; set; }
        public int DiversityScore { get; set; }
        public int ConnectivityScore { get; set; }
        public int VibrancyScore { get; set; }
        public double BridgeDensityPercent { get; set; }
        public string Grade { get; set; }
        public string Summary { get; set; }
    }
}
