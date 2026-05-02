using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    // ── Configuration ───────────────────────────────────────────────

    public class ContagionEngineConfig
    {
        /// <summary>Analysis window in days.</summary>
        public int WindowDays { get; set; } = 90;

        /// <summary>Max days between rentals to count as a contagion event.</summary>
        public int ContagionWindowDays { get; set; } = 14;

        /// <summary>Minimum shared-movie co-rentals to establish a social link.</summary>
        public int MinCoRentals { get; set; } = 2;

        /// <summary>Score threshold for influencer classification.</summary>
        public double MinInfluenceScore { get; set; } = 30.0;

        /// <summary>Number of top items in rankings.</summary>
        public int TopN { get; set; } = 10;
    }

    // ── Enums ───────────────────────────────────────────────────────

    public enum InfluencerTier { SuperSpreader, Influencer, Connector, Follower, Immune }

    public enum ContagionClassification { Pandemic, Epidemic, Endemic, Sporadic }

    // ── Result models ───────────────────────────────────────────────

    public class SocialEdge
    {
        public int CustomerId1 { get; set; }
        public int CustomerId2 { get; set; }
        public string CustomerName1 { get; set; }
        public string CustomerName2 { get; set; }
        public int CoRentalCount { get; set; }
        public List<string> SharedMovies { get; set; } = new List<string>();
    }

    public class ContagionEvent
    {
        public int PatientZeroId { get; set; }
        public string PatientZeroName { get; set; }
        public int InfectedId { get; set; }
        public string InfectedName { get; set; }
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public string Genre { get; set; }
        public DateTime OriginalRentalDate { get; set; }
        public DateTime ContagionDate { get; set; }
        public int DaysToContagion { get; set; }
    }

    public class InfluencerProfile
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public double Score { get; set; }
        public int UniqueInfluenced { get; set; }
        public double ContagionRate { get; set; }
        public double GenreDiversity { get; set; }
        public double AvgContagionSpeed { get; set; }
        public InfluencerTier Tier { get; set; }
    }

    public class GenreContagion
    {
        public string Genre { get; set; }
        public double R0 { get; set; }
        public int TotalPrimaryRentals { get; set; }
        public int TotalSecondaryRentals { get; set; }
        public double AvgDaysToSpread { get; set; }
        public ContagionClassification Classification { get; set; }
    }

    public class ChainLink
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string MovieName { get; set; }
        public DateTime RentalDate { get; set; }
        public int DaysFromPrevious { get; set; }
    }

    public class ContagionChain
    {
        public List<ChainLink> Links { get; set; } = new List<ChainLink>();
        public int Length { get; set; }
        public string Genre { get; set; }
        public string PatientZeroName { get; set; }
    }

    public class SocialProofRecommendation
    {
        public int TargetCustomerId { get; set; }
        public string TargetCustomerName { get; set; }
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public string Reason { get; set; }
        public double ConfidenceScore { get; set; }
        public List<string> InfluencerNames { get; set; } = new List<string>();
    }

    public class ContagionReport
    {
        public DateTime GeneratedAt { get; set; }
        public int HealthScore { get; set; }
        public List<SocialEdge> SocialNetwork { get; set; } = new List<SocialEdge>();
        public List<ContagionEvent> ContagionEvents { get; set; } = new List<ContagionEvent>();
        public List<InfluencerProfile> Influencers { get; set; } = new List<InfluencerProfile>();
        public List<GenreContagion> GenreContagions { get; set; } = new List<GenreContagion>();
        public List<ContagionChain> Chains { get; set; } = new List<ContagionChain>();
        public List<SocialProofRecommendation> Recommendations { get; set; } = new List<SocialProofRecommendation>();
        public List<string> Insights { get; set; } = new List<string>();
    }

    // ── Service ─────────────────────────────────────────────────────

    /// <summary>
    /// Autonomous Rental Contagion Engine — tracks social influence patterns
    /// in rental behavior, identifying how movies spread virally through the
    /// customer network, scoring influencers, measuring genre R0, and
    /// generating social-proof recommendations.
    /// </summary>
    public class RentalContagionService
    {
        private readonly IRentalRepository _rentalRepo;
        private readonly ICustomerRepository _customerRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly IClock _clock;
        private readonly ContagionEngineConfig _config;

        public RentalContagionService(
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo,
            IMovieRepository movieRepo,
            IClock clock,
            ContagionEngineConfig config = null)
        {
            if (rentalRepo == null) throw new ArgumentNullException("rentalRepo");
            if (customerRepo == null) throw new ArgumentNullException("customerRepo");
            if (movieRepo == null) throw new ArgumentNullException("movieRepo");
            if (clock == null) throw new ArgumentNullException("clock");
            _rentalRepo = rentalRepo;
            _customerRepo = customerRepo;
            _movieRepo = movieRepo;
            _clock = clock;
            _config = config ?? new ContagionEngineConfig();
        }

        /// <summary>
        /// Run the full contagion analysis pipeline and return a comprehensive report.
        /// </summary>
        public ContagionReport Analyze()
        {
            var now = _clock.Now;
            var windowStart = now.AddDays(-_config.WindowDays);

            var allRentals = _rentalRepo.GetAll();
            var allCustomers = _customerRepo.GetAll();
            var allMovies = _movieRepo.GetAll();

            var windowRentals = allRentals
                .Where(r => r.RentalDate >= windowStart && r.RentalDate <= now)
                .ToList();

            var customerLookup = allCustomers.ToDictionary(c => c.Id);
            var movieLookup = allMovies.ToDictionary(m => m.Id);

            // Engine 1: Build social network
            var socialNetwork = BuildSocialNetwork(windowRentals, customerLookup, movieLookup);

            // Engine 2: Detect contagion events
            var linkedPairs = new HashSet<string>(
                socialNetwork.Select(e => EdgeKey(e.CustomerId1, e.CustomerId2)));
            var contagionEvents = DetectContagionEvents(
                windowRentals, customerLookup, movieLookup, linkedPairs);

            // Engine 3: Score influencers
            var influencers = ScoreInfluencers(
                contagionEvents, windowRentals, customerLookup, allCustomers);

            // Engine 4: Genre contagion map
            var genreContagions = MapGenreContagion(contagionEvents);

            // Engine 5: Contagion chains
            var chains = BuildContagionChains(contagionEvents, customerLookup);

            // Engine 6: Social proof recommendations
            var recommendations = GenerateSocialProof(
                contagionEvents, windowRentals, customerLookup, movieLookup, influencers);

            // Engine 7: Insights
            var insights = GenerateInsights(
                socialNetwork, contagionEvents, influencers, genreContagions, chains, allCustomers);

            // Health score
            var healthScore = ComputeHealthScore(
                socialNetwork, contagionEvents, influencers, genreContagions, allCustomers);

            return new ContagionReport
            {
                GeneratedAt = now,
                HealthScore = healthScore,
                SocialNetwork = socialNetwork,
                ContagionEvents = contagionEvents,
                Influencers = influencers.OrderByDescending(i => i.Score).Take(_config.TopN).ToList(),
                GenreContagions = genreContagions,
                Chains = chains.OrderByDescending(c => c.Length).Take(_config.TopN).ToList(),
                Recommendations = recommendations.Take(_config.TopN).ToList(),
                Insights = insights
            };
        }

        // ── Engine 1: Social Network Builder ────────────────────────

        private List<SocialEdge> BuildSocialNetwork(
            List<Rental> rentals,
            Dictionary<int, Customer> customers,
            Dictionary<int, Movie> movies)
        {
            // Group rentals by movie
            var rentalsByMovie = rentals.GroupBy(r => r.MovieId).ToDictionary(g => g.Key, g => g.ToList());

            // Track co-rentals between customer pairs
            var pairCoRentals = new Dictionary<string, List<string>>();

            foreach (var kvp in rentalsByMovie)
            {
                var movieRentals = kvp.Value;
                var movieName = movies.ContainsKey(kvp.Key) ? movies[kvp.Key].Name : "Unknown";

                // For each pair of customers who rented this movie within the contagion window
                for (int i = 0; i < movieRentals.Count; i++)
                {
                    for (int j = i + 1; j < movieRentals.Count; j++)
                    {
                        var r1 = movieRentals[i];
                        var r2 = movieRentals[j];
                        if (r1.CustomerId == r2.CustomerId) continue;

                        var daysDiff = Math.Abs((r1.RentalDate - r2.RentalDate).TotalDays);
                        if (daysDiff <= _config.ContagionWindowDays)
                        {
                            var key = EdgeKey(r1.CustomerId, r2.CustomerId);
                            if (!pairCoRentals.ContainsKey(key))
                                pairCoRentals[key] = new List<string>();
                            if (!pairCoRentals[key].Contains(movieName))
                                pairCoRentals[key].Add(movieName);
                        }
                    }
                }
            }

            var edges = new List<SocialEdge>();
            foreach (var kvp in pairCoRentals)
            {
                if (kvp.Value.Count < _config.MinCoRentals) continue;

                var ids = kvp.Key.Split('-');
                var id1 = int.Parse(ids[0]);
                var id2 = int.Parse(ids[1]);

                edges.Add(new SocialEdge
                {
                    CustomerId1 = id1,
                    CustomerId2 = id2,
                    CustomerName1 = customers.ContainsKey(id1) ? customers[id1].Name : "Unknown",
                    CustomerName2 = customers.ContainsKey(id2) ? customers[id2].Name : "Unknown",
                    CoRentalCount = kvp.Value.Count,
                    SharedMovies = kvp.Value
                });
            }

            return edges.OrderByDescending(e => e.CoRentalCount).ToList();
        }

        // ── Engine 2: Contagion Event Detector ──────────────────────

        private List<ContagionEvent> DetectContagionEvents(
            List<Rental> rentals,
            Dictionary<int, Customer> customers,
            Dictionary<int, Movie> movies,
            HashSet<string> linkedPairs)
        {
            var events = new List<ContagionEvent>();
            var rentalsByMovie = rentals.GroupBy(r => r.MovieId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.RentalDate).ToList());

            foreach (var kvp in rentalsByMovie)
            {
                var movieRentals = kvp.Value;
                if (movieRentals.Count < 2) continue;

                Movie movie = null;
                if (movies.ContainsKey(kvp.Key)) movie = movies[kvp.Key];

                for (int i = 0; i < movieRentals.Count; i++)
                {
                    for (int j = i + 1; j < movieRentals.Count; j++)
                    {
                        var primary = movieRentals[i];
                        var secondary = movieRentals[j];
                        if (primary.CustomerId == secondary.CustomerId) continue;

                        var days = (int)(secondary.RentalDate - primary.RentalDate).TotalDays;
                        if (days < 0 || days > _config.ContagionWindowDays) continue;

                        // Must have social link
                        var key = EdgeKey(primary.CustomerId, secondary.CustomerId);
                        if (!linkedPairs.Contains(key)) continue;

                        events.Add(new ContagionEvent
                        {
                            PatientZeroId = primary.CustomerId,
                            PatientZeroName = customers.ContainsKey(primary.CustomerId) ? customers[primary.CustomerId].Name : "Unknown",
                            InfectedId = secondary.CustomerId,
                            InfectedName = customers.ContainsKey(secondary.CustomerId) ? customers[secondary.CustomerId].Name : "Unknown",
                            MovieId = kvp.Key,
                            MovieName = movie != null ? movie.Name : "Unknown",
                            Genre = movie != null && movie.Genre.HasValue ? movie.Genre.Value.ToString() : "Unknown",
                            OriginalRentalDate = primary.RentalDate,
                            ContagionDate = secondary.RentalDate,
                            DaysToContagion = days
                        });
                    }
                }
            }

            return events.OrderBy(e => e.ContagionDate).ToList();
        }

        // ── Engine 3: Influencer Scorer ─────────────────────────────

        private List<InfluencerProfile> ScoreInfluencers(
            List<ContagionEvent> events,
            List<Rental> rentals,
            Dictionary<int, Customer> customers,
            IReadOnlyList<Customer> allCustomers)
        {
            var profiles = new List<InfluencerProfile>();

            // Group events by patient zero
            var byPatientZero = events.GroupBy(e => e.PatientZeroId).ToDictionary(g => g.Key, g => g.ToList());

            // Total rentals per customer for rate calculation
            var rentalCountByCustomer = rentals.GroupBy(r => r.CustomerId).ToDictionary(g => g.Key, g => g.Count());

            foreach (var customer in allCustomers)
            {
                var custEvents = byPatientZero.ContainsKey(customer.Id) ? byPatientZero[customer.Id] : new List<ContagionEvent>();
                var totalRentals = rentalCountByCustomer.ContainsKey(customer.Id) ? rentalCountByCustomer[customer.Id] : 0;

                if (totalRentals == 0) continue;

                var uniqueInfluenced = custEvents.Select(e => e.InfectedId).Distinct().Count();
                var contagionRate = totalRentals > 0 ? (double)custEvents.Count / totalRentals : 0;
                var genresInfluenced = custEvents.Select(e => e.Genre).Distinct().Count();
                var totalGenres = rentals.Where(r => r.CustomerId == customer.Id)
                    .Select(r => r.MovieId).Distinct().Count();
                var genreDiversity = totalGenres > 0 ? (double)genresInfluenced / Math.Max(totalGenres, 1) : 0;
                var avgSpeed = custEvents.Any() ? custEvents.Average(e => e.DaysToContagion) : _config.ContagionWindowDays;

                // Score: 40% unique influenced, 25% contagion rate, 20% genre diversity, 15% speed
                var maxInfluenced = Math.Max(byPatientZero.Values.Max(v => v.Select(e => e.InfectedId).Distinct().Count()), 1);
                var influencedScore = Math.Min((double)uniqueInfluenced / maxInfluenced * 100, 100);
                var rateScore = Math.Min(contagionRate * 100, 100);
                var diversityScore = genreDiversity * 100;
                var speedScore = _config.ContagionWindowDays > 0
                    ? (1.0 - avgSpeed / _config.ContagionWindowDays) * 100
                    : 0;
                speedScore = Math.Max(speedScore, 0);

                var score = influencedScore * 0.40 + rateScore * 0.25 + diversityScore * 0.20 + speedScore * 0.15;
                score = Math.Min(Math.Max(Math.Round(score, 1), 0), 100);

                var tier = ClassifyTier(score, uniqueInfluenced);

                profiles.Add(new InfluencerProfile
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    Score = score,
                    UniqueInfluenced = uniqueInfluenced,
                    ContagionRate = Math.Round(contagionRate, 3),
                    GenreDiversity = Math.Round(genreDiversity, 3),
                    AvgContagionSpeed = Math.Round(avgSpeed, 1),
                    Tier = tier
                });
            }

            return profiles;
        }

        private InfluencerTier ClassifyTier(double score, int uniqueInfluenced)
        {
            if (score >= 75 && uniqueInfluenced >= 3) return InfluencerTier.SuperSpreader;
            if (score >= 50) return InfluencerTier.Influencer;
            if (score >= 25) return InfluencerTier.Connector;
            if (uniqueInfluenced > 0) return InfluencerTier.Follower;
            return InfluencerTier.Immune;
        }

        // ── Engine 4: Genre Contagion Mapper ────────────────────────

        private List<GenreContagion> MapGenreContagion(List<ContagionEvent> events)
        {
            var byGenre = events.GroupBy(e => e.Genre).Where(g => g.Key != "Unknown");
            var results = new List<GenreContagion>();

            foreach (var group in byGenre)
            {
                var genreEvents = group.ToList();
                var primaryCount = genreEvents.Select(e => new { e.PatientZeroId, e.MovieId }).Distinct().Count();
                var secondaryCount = genreEvents.Count;
                var r0 = primaryCount > 0 ? (double)secondaryCount / primaryCount : 0;
                var avgDays = genreEvents.Average(e => e.DaysToContagion);

                var classification = ContagionClassification.Sporadic;
                if (r0 >= 3.0) classification = ContagionClassification.Pandemic;
                else if (r0 >= 2.0) classification = ContagionClassification.Epidemic;
                else if (r0 >= 1.0) classification = ContagionClassification.Endemic;

                results.Add(new GenreContagion
                {
                    Genre = group.Key,
                    R0 = Math.Round(r0, 2),
                    TotalPrimaryRentals = primaryCount,
                    TotalSecondaryRentals = secondaryCount,
                    AvgDaysToSpread = Math.Round(avgDays, 1),
                    Classification = classification
                });
            }

            return results.OrderByDescending(g => g.R0).ToList();
        }

        // ── Engine 5: Contagion Chain Tracker ───────────────────────

        private List<ContagionChain> BuildContagionChains(
            List<ContagionEvent> events,
            Dictionary<int, Customer> customers)
        {
            // Build adjacency: for each movie+genre, build directed graph of contagion
            var chains = new List<ContagionChain>();
            var eventsByMovie = events.GroupBy(e => e.MovieId);

            foreach (var movieGroup in eventsByMovie)
            {
                var movieEvents = movieGroup.OrderBy(e => e.OriginalRentalDate).ToList();
                if (movieEvents.Count == 0) continue;

                // Build adjacency list
                var adjacency = new Dictionary<int, List<ContagionEvent>>();
                foreach (var evt in movieEvents)
                {
                    if (!adjacency.ContainsKey(evt.PatientZeroId))
                        adjacency[evt.PatientZeroId] = new List<ContagionEvent>();
                    adjacency[evt.PatientZeroId].Add(evt);
                }

                // Find roots (patient zeros not infected by anyone for this movie)
                var infected = new HashSet<int>(movieEvents.Select(e => e.InfectedId));
                var roots = adjacency.Keys.Where(k => !infected.Contains(k)).ToList();
                if (!roots.Any()) roots = new List<int> { movieEvents.First().PatientZeroId };

                foreach (var root in roots)
                {
                    var chain = new List<ChainLink>();
                    var visited = new HashSet<int>();
                    BuildChainDFS(root, adjacency, customers, chain, visited, movieEvents.First().MovieName, DateTime.MinValue);

                    if (chain.Count >= 2)
                    {
                        chains.Add(new ContagionChain
                        {
                            Links = chain,
                            Length = chain.Count,
                            Genre = movieEvents.First().Genre,
                            PatientZeroName = customers.ContainsKey(root) ? customers[root].Name : "Unknown"
                        });
                    }
                }
            }

            return chains;
        }

        private void BuildChainDFS(
            int customerId,
            Dictionary<int, List<ContagionEvent>> adjacency,
            Dictionary<int, Customer> customers,
            List<ChainLink> chain,
            HashSet<int> visited,
            string movieName,
            DateTime prevDate)
        {
            if (visited.Contains(customerId)) return;
            visited.Add(customerId);

            var daysFromPrev = prevDate == DateTime.MinValue ? 0 : 0;
            if (adjacency.ContainsKey(customerId) && adjacency[customerId].Any())
            {
                var firstEvent = adjacency[customerId].First();
                var rentalDate = firstEvent.OriginalRentalDate;
                daysFromPrev = prevDate == DateTime.MinValue ? 0 : (int)(rentalDate - prevDate).TotalDays;

                chain.Add(new ChainLink
                {
                    CustomerId = customerId,
                    CustomerName = customers.ContainsKey(customerId) ? customers[customerId].Name : "Unknown",
                    MovieName = movieName,
                    RentalDate = rentalDate,
                    DaysFromPrevious = Math.Max(daysFromPrev, 0)
                });

                // Follow the chain to each infected customer
                foreach (var evt in adjacency[customerId])
                {
                    BuildChainDFS(evt.InfectedId, adjacency, customers, chain, visited, movieName, evt.ContagionDate);
                }
            }
            else
            {
                // Leaf node — find the event where this customer was infected
                var asInfected = adjacency.Values.SelectMany(v => v).FirstOrDefault(e => e.InfectedId == customerId);
                var rentalDate = asInfected != null ? asInfected.ContagionDate : prevDate;
                daysFromPrev = prevDate == DateTime.MinValue ? 0 : (int)(rentalDate - prevDate).TotalDays;

                chain.Add(new ChainLink
                {
                    CustomerId = customerId,
                    CustomerName = customers.ContainsKey(customerId) ? customers[customerId].Name : "Unknown",
                    MovieName = movieName,
                    RentalDate = rentalDate,
                    DaysFromPrevious = Math.Max(daysFromPrev, 0)
                });
            }
        }

        // ── Engine 6: Social Proof Generator ────────────────────────

        private List<SocialProofRecommendation> GenerateSocialProof(
            List<ContagionEvent> events,
            List<Rental> rentals,
            Dictionary<int, Customer> customers,
            Dictionary<int, Movie> movies,
            List<InfluencerProfile> influencers)
        {
            var recommendations = new List<SocialProofRecommendation>();
            var influencerLookup = influencers.Where(i => i.Score >= _config.MinInfluenceScore)
                .ToDictionary(i => i.CustomerId);

            // For each customer, find movies rented by their social connections that they haven't rented
            var rentalsByCustomer = rentals.GroupBy(r => r.CustomerId)
                .ToDictionary(g => g.Key, g => new HashSet<int>(g.Select(r => r.MovieId)));

            // Build connections from contagion events
            var connections = new Dictionary<int, HashSet<int>>();
            foreach (var evt in events)
            {
                if (!connections.ContainsKey(evt.InfectedId))
                    connections[evt.InfectedId] = new HashSet<int>();
                connections[evt.InfectedId].Add(evt.PatientZeroId);

                if (!connections.ContainsKey(evt.PatientZeroId))
                    connections[evt.PatientZeroId] = new HashSet<int>();
                connections[evt.PatientZeroId].Add(evt.InfectedId);
            }

            foreach (var custId in connections.Keys)
            {
                var myMovies = rentalsByCustomer.ContainsKey(custId) ? rentalsByCustomer[custId] : new HashSet<int>();
                var friendMovies = new Dictionary<int, List<int>>(); // movieId -> list of friend customerIds

                foreach (var friendId in connections[custId])
                {
                    var friendRentals = rentalsByCustomer.ContainsKey(friendId) ? rentalsByCustomer[friendId] : new HashSet<int>();
                    foreach (var movieId in friendRentals)
                    {
                        if (myMovies.Contains(movieId)) continue;
                        if (!friendMovies.ContainsKey(movieId))
                            friendMovies[movieId] = new List<int>();
                        friendMovies[movieId].Add(friendId);
                    }
                }

                foreach (var kvp in friendMovies.OrderByDescending(fm => fm.Value.Count).Take(3))
                {
                    if (!movies.ContainsKey(kvp.Key)) continue;
                    var movie = movies[kvp.Key];
                    var influencerFriends = kvp.Value.Where(f => influencerLookup.ContainsKey(f)).ToList();
                    var friendNames = kvp.Value
                        .Where(f => customers.ContainsKey(f))
                        .Select(f => customers[f].Name).ToList();

                    var confidence = Math.Min(kvp.Value.Count * 25.0 + influencerFriends.Count * 15.0, 100);

                    recommendations.Add(new SocialProofRecommendation
                    {
                        TargetCustomerId = custId,
                        TargetCustomerName = customers.ContainsKey(custId) ? customers[custId].Name : "Unknown",
                        MovieId = kvp.Key,
                        MovieName = movie.Name,
                        Reason = string.Format("{0} in your circle rented \"{1}\"",
                            kvp.Value.Count, movie.Name),
                        ConfidenceScore = Math.Round(confidence, 1),
                        InfluencerNames = friendNames
                    });
                }
            }

            return recommendations.OrderByDescending(r => r.ConfidenceScore).ToList();
        }

        // ── Engine 7: Insight Generator ─────────────────────────────

        private List<string> GenerateInsights(
            List<SocialEdge> network,
            List<ContagionEvent> events,
            List<InfluencerProfile> influencers,
            List<GenreContagion> genreContagions,
            List<ContagionChain> chains,
            IReadOnlyList<Customer> allCustomers)
        {
            var insights = new List<string>();

            // Network size
            var connectedCustomers = new HashSet<int>();
            foreach (var edge in network)
            {
                connectedCustomers.Add(edge.CustomerId1);
                connectedCustomers.Add(edge.CustomerId2);
            }
            var connectivity = allCustomers.Count > 0
                ? (double)connectedCustomers.Count / allCustomers.Count * 100
                : 0;
            insights.Add(string.Format(
                "Social network covers {0} of {1} customers ({2:F1}% connectivity)",
                connectedCustomers.Count, allCustomers.Count, connectivity));

            // Contagion volume
            if (events.Any())
            {
                insights.Add(string.Format(
                    "{0} contagion events detected — avg {1:F1} days to spread",
                    events.Count, events.Average(e => e.DaysToContagion)));
            }
            else
            {
                insights.Add("No contagion events detected in the analysis window");
            }

            // Super spreaders
            var superSpreaders = influencers.Where(i => i.Tier == InfluencerTier.SuperSpreader).ToList();
            if (superSpreaders.Any())
            {
                insights.Add(string.Format(
                    "{0} super-spreader(s) identified — top: {1} (score {2:F1})",
                    superSpreaders.Count,
                    superSpreaders.OrderByDescending(s => s.Score).First().CustomerName,
                    superSpreaders.Max(s => s.Score)));
            }

            // Immune customers
            var immune = influencers.Count(i => i.Tier == InfluencerTier.Immune);
            if (immune > 0)
            {
                insights.Add(string.Format(
                    "{0} customer(s) show immunity — active renters unaffected by social influence",
                    immune));
            }

            // Most viral genre
            var topGenre = genreContagions.OrderByDescending(g => g.R0).FirstOrDefault();
            if (topGenre != null)
            {
                insights.Add(string.Format(
                    "Most viral genre: {0} (R0={1:F2}, {2})",
                    topGenre.Genre, topGenre.R0, topGenre.Classification));
            }

            // Longest chain
            var longest = chains.OrderByDescending(c => c.Length).FirstOrDefault();
            if (longest != null)
            {
                insights.Add(string.Format(
                    "Longest contagion chain: {0} links starting from {1} ({2})",
                    longest.Length, longest.PatientZeroName, longest.Genre));
            }

            return insights;
        }

        // ── Health Score ────────────────────────────────────────────

        private int ComputeHealthScore(
            List<SocialEdge> network,
            List<ContagionEvent> events,
            List<InfluencerProfile> influencers,
            List<GenreContagion> genreContagions,
            IReadOnlyList<Customer> allCustomers)
        {
            // 25% network connectivity
            var connectedCustomers = new HashSet<int>();
            foreach (var edge in network)
            {
                connectedCustomers.Add(edge.CustomerId1);
                connectedCustomers.Add(edge.CustomerId2);
            }
            var connectivityScore = allCustomers.Count > 0
                ? Math.Min((double)connectedCustomers.Count / allCustomers.Count * 100, 100) * 0.25
                : 0;

            // 25% contagion activity
            var activityScore = Math.Min(events.Count * 5.0, 100) * 0.25;

            // 25% influencer distribution
            var hasInfluencers = influencers.Count(i => i.Score >= _config.MinInfluenceScore);
            var influencerScore = Math.Min(hasInfluencers * 20.0, 100) * 0.25;

            // 25% genre diversity
            var genreDiversity = Math.Min(genreContagions.Count * 25.0, 100) * 0.25;

            var total = connectivityScore + activityScore + influencerScore + genreDiversity;
            return (int)Math.Min(Math.Max(Math.Round(total), 0), 100);
        }

        // ── Helpers ─────────────────────────────────────────────────

        private static string EdgeKey(int id1, int id2)
        {
            var min = Math.Min(id1, id2);
            var max = Math.Max(id1, id2);
            return string.Format("{0}-{1}", min, max);
        }
    }
}
