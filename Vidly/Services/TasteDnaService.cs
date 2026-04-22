using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Autonomous Movie Taste DNA engine. Analyzes rental history to produce
    /// a multi-dimensional preference fingerprint, classifies personality
    /// archetypes, and generates proactive viewing recommendations.
    /// </summary>
    public class TasteDnaService
    {
        private readonly IMovieRepository _movieRepo;
        private readonly IRentalRepository _rentalRepo;
        private readonly ICustomerRepository _customerRepo;

        public TasteDnaService(
            IMovieRepository movieRepo,
            IRentalRepository rentalRepo,
            ICustomerRepository customerRepo)
        {
            _movieRepo = movieRepo ?? throw new ArgumentNullException(nameof(movieRepo));
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _customerRepo = customerRepo ?? throw new ArgumentNullException(nameof(customerRepo));
        }

        // ── Public API ─────────────────────────────────────────────

        /// <summary>Build a full Taste DNA profile for one customer.</summary>
        public TasteDnaProfile BuildProfile(int customerId)
        {
            var customer = _customerRepo.GetById(customerId);
            if (customer == null)
                return null;

            var rentals = _rentalRepo.GetByCustomer(customerId);
            var movies = _movieRepo.GetAll().ToList();
            var movieById = movies.ToDictionary(m => m.Id);

            var rented = rentals
                .Select(r => movieById.TryGetValue(r.MovieId, out var m) ? m : null)
                .Where(m => m != null)
                .ToList();

            var dimensions = ComputeDimensions(rented, movies);
            var archetype = ClassifyArchetype(dimensions);
            var insights = GenerateInsights(dimensions, rented, movies);
            var recommendations = GenerateRecommendations(dimensions, rented, movies);

            return new TasteDnaProfile
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                TotalRentals = rentals.Count,
                Dimensions = dimensions,
                Archetype = archetype,
                Insights = insights,
                Recommendations = recommendations,
                GeneratedAt = DateTime.UtcNow
            };
        }

        /// <summary>Build fleet overview for all customers.</summary>
        public TasteDnaFleet BuildFleet()
        {
            var customers = _customerRepo.GetAll().ToList();
            var rentals = _rentalRepo.GetAll().ToList();
            var movies = _movieRepo.GetAll().ToList();

            var profiles = new List<TasteDnaProfileSummary>();
            var archetypeCounts = new Dictionary<string, int>();
            var movieById = movies.ToDictionary(m => m.Id);

            // Group rentals by customer in a single pass — avoids O(C×R)
            // re-scanning all rentals per customer.
            var rentalsByCustomer = rentals.GroupBy(r => r.CustomerId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var c in customers)
            {
                if (!rentalsByCustomer.TryGetValue(c.Id, out var cRentals)
                    || cRentals.Count == 0) continue;

                var rented = cRentals
                    .Select(r => movieById.TryGetValue(r.MovieId, out var m) ? m : null)
                    .Where(m => m != null)
                    .ToList();

                var dims = ComputeDimensions(rented, movies);
                var arch = ClassifyArchetype(dims);

                if (!archetypeCounts.ContainsKey(arch.Name))
                    archetypeCounts[arch.Name] = 0;
                archetypeCounts[arch.Name]++;

                profiles.Add(new TasteDnaProfileSummary
                {
                    CustomerId = c.Id,
                    CustomerName = c.Name,
                    Rentals = cRentals.Count,
                    ArchetypeName = arch.Name,
                    ArchetypeEmoji = arch.Emoji,
                    DominantDimension = dims.OrderByDescending(d => d.Score).First().Name
                });
            }

            return new TasteDnaFleet
            {
                Profiles = profiles.OrderByDescending(p => p.Rentals).ToList(),
                ArchetypeDistribution = archetypeCounts
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => new ArchetypeCount { Name = kv.Key, Count = kv.Value })
                    .ToList(),
                TotalCustomers = profiles.Count
            };
        }

        // ── Dimension Computation ──────────────────────────────────

        private List<DnaDimension> ComputeDimensions(List<Movie> rented, List<Movie> allMovies)
        {
            return new List<DnaDimension>
            {
                ComputeGenreDiversity(rented),
                ComputeDecadeSpread(rented),
                ComputeRatingAffinity(rented),
                ComputeNewReleaseBias(rented),
                ComputeGenreDepth(rented),
                ComputeRentalVelocity(rented),
                ComputeAdventurousness(rented, allMovies),
                ComputeConsistency(rented)
            };
        }

        private DnaDimension ComputeGenreDiversity(List<Movie> rented)
        {
            if (rented.Count == 0) return Dim("Genre Diversity", 0, "No rentals yet");

            var genres = rented.Where(m => m.Genre.HasValue)
                .Select(m => m.Genre.Value).ToList();
            if (genres.Count == 0) return Dim("Genre Diversity", 0, "No genre data");

            int distinct = genres.Distinct().Count();
            int total = Enum.GetValues(typeof(Genre)).Length;
            double score = Math.Min(1.0, (double)distinct / Math.Max(1, total));

            return Dim("Genre Diversity", score,
                $"{distinct} of {total} genres explored");
        }

        private DnaDimension ComputeDecadeSpread(List<Movie> rented)
        {
            var decades = rented
                .Where(m => m.ReleaseDate.HasValue)
                .Select(m => (m.ReleaseDate.Value.Year / 10) * 10)
                .Distinct().Count();

            double score = Math.Min(1.0, decades / 5.0);
            return Dim("Era Explorer", score,
                $"Spans {decades} decade(s)");
        }

        private DnaDimension ComputeRatingAffinity(List<Movie> rented)
        {
            var ratings = rented.Where(m => m.Rating.HasValue)
                .Select(m => m.Rating.Value).ToList();
            if (ratings.Count == 0) return Dim("Quality Seeker", 0.5, "No ratings");

            double avg = ratings.Average();
            double score = (avg - 1.0) / 4.0; // normalize 1-5 → 0-1
            return Dim("Quality Seeker", score,
                $"Average rating {avg:F1}/5");
        }

        private DnaDimension ComputeNewReleaseBias(List<Movie> rented)
        {
            if (rented.Count == 0) return Dim("Trendsetter", 0, "No data");

            int newCount = rented.Count(m => m.IsNewRelease);
            double score = (double)newCount / rented.Count;
            return Dim("Trendsetter", score,
                $"{newCount} of {rented.Count} are new releases");
        }

        private DnaDimension ComputeGenreDepth(List<Movie> rented)
        {
            var groups = rented.Where(m => m.Genre.HasValue)
                .GroupBy(m => m.Genre.Value).ToList();
            if (groups.Count == 0) return Dim("Genre Depth", 0, "No genre data");

            int maxInGenre = groups.Max(g => g.Count());
            double score = Math.Min(1.0, maxInGenre / 10.0);
            var topGenre = groups.OrderByDescending(g => g.Count()).First().Key;
            return Dim("Genre Depth", score,
                $"Deepest: {topGenre} ({maxInGenre} films)");
        }

        private DnaDimension ComputeRentalVelocity(List<Movie> rented)
        {
            // Based on count alone as proxy
            double score = Math.Min(1.0, rented.Count / 20.0);
            return Dim("Binge Factor", score,
                $"{rented.Count} total rentals");
        }

        private DnaDimension ComputeAdventurousness(List<Movie> rented, List<Movie> allMovies)
        {
            if (rented.Count == 0) return Dim("Adventurousness", 0, "No data");

            // Fraction of catalog explored
            int catalogSize = allMovies.Count;
            double explored = (double)rented.Select(m => m.Id).Distinct().Count() / Math.Max(1, catalogSize);
            double score = Math.Min(1.0, explored * 3); // amplify
            return Dim("Adventurousness", score,
                $"Explored {explored * 100:F0}% of catalog");
        }

        private DnaDimension ComputeConsistency(List<Movie> rented)
        {
            var ratings = rented.Where(m => m.Rating.HasValue)
                .Select(m => (double)m.Rating.Value).ToList();
            if (ratings.Count < 2) return Dim("Consistency", 0.5, "Not enough data");

            double mean = ratings.Average();
            double variance = ratings.Sum(r => (r - mean) * (r - mean)) / ratings.Count;
            double stdDev = Math.Sqrt(variance);

            // Low std-dev = high consistency
            double score = Math.Max(0, 1.0 - (stdDev / 2.0));
            return Dim("Consistency", score,
                $"Rating σ = {stdDev:F2}");
        }

        // ── Archetype Classification ───────────────────────────────

        private TasteArchetype ClassifyArchetype(List<DnaDimension> dims)
        {
            var lookup = dims.ToDictionary(d => d.Name, d => d.Score);
            double Get(string name) => lookup.ContainsKey(name) ? lookup[name] : 0.5;

            double diversity = Get("Genre Diversity");
            double depth = Get("Genre Depth");
            double quality = Get("Quality Seeker");
            double trend = Get("Trendsetter");
            double binge = Get("Binge Factor");
            double adventure = Get("Adventurousness");

            if (diversity > 0.7 && adventure > 0.6)
                return new TasteArchetype
                {
                    Name = "The Explorer",
                    Emoji = "🧭",
                    Description = "Fearlessly roams every genre and decade. No corner of cinema is left unvisited."
                };

            if (depth > 0.7 && diversity < 0.4)
                return new TasteArchetype
                {
                    Name = "The Specialist",
                    Emoji = "🎯",
                    Description = "Deep expertise in a favorite genre. Knows every classic and hidden gem in their niche."
                };

            if (quality > 0.8)
                return new TasteArchetype
                {
                    Name = "The Connoisseur",
                    Emoji = "🍷",
                    Description = "Only the finest films. Gravitates toward critically acclaimed masterpieces."
                };

            if (trend > 0.5)
                return new TasteArchetype
                {
                    Name = "The Trendsetter",
                    Emoji = "🔥",
                    Description = "Always first to watch new releases. Finger on the pulse of what's hot."
                };

            if (binge > 0.7)
                return new TasteArchetype
                {
                    Name = "The Marathoner",
                    Emoji = "🏃",
                    Description = "Devours movies at an impressive pace. A true cinema enthusiast."
                };

            return new TasteArchetype
            {
                Name = "The Balanced Viewer",
                Emoji = "⚖️",
                Description = "A well-rounded palette with no extreme preferences. Enjoys a healthy mix."
            };
        }

        // ── Insights & Recommendations ─────────────────────────────

        private List<string> GenerateInsights(List<DnaDimension> dims, List<Movie> rented, List<Movie> allMovies)
        {
            var insights = new List<string>();
            var lookup = dims.ToDictionary(d => d.Name, d => d.Score);
            double Get(string name) => lookup.ContainsKey(name) ? lookup[name] : 0.5;

            if (Get("Genre Diversity") < 0.3)
                insights.Add("🔍 Your taste is very focused — try branching into a new genre for a surprise hit.");
            if (Get("Genre Diversity") > 0.8)
                insights.Add("🌈 Impressively eclectic taste! You've sampled almost every genre.");
            if (Get("Era Explorer") > 0.7)
                insights.Add("⏳ You appreciate cinema across eras — a true film historian.");
            if (Get("Binge Factor") > 0.8)
                insights.Add("🍿 Prolific viewer! You're in the top tier of rental activity.");
            if (Get("Quality Seeker") > 0.8)
                insights.Add("⭐ You have excellent taste — almost exclusively high-rated films.");
            if (Get("Consistency") > 0.8)
                insights.Add("📐 Remarkably consistent preferences — you know exactly what you like.");
            if (Get("Consistency") < 0.3)
                insights.Add("🎲 Wide-ranging ratings suggest you love experimenting, even with mixed results.");

            // Blind-spot detection
            var rentedGenres = new HashSet<Genre>(rented.Where(m => m.Genre.HasValue).Select(m => m.Genre.Value));
            var missing = Enum.GetValues(typeof(Genre)).Cast<Genre>()
                .Where(g => !rentedGenres.Contains(g)).ToList();
            if (missing.Count > 0 && missing.Count <= 4)
                insights.Add($"🕳️ Genre blind spots: {string.Join(", ", missing)}. Worth exploring!");

            if (insights.Count == 0)
                insights.Add("📊 Your viewing profile is well-balanced with no standout patterns yet.");

            return insights;
        }

        private List<TasteRecommendation> GenerateRecommendations(
            List<DnaDimension> dims, List<Movie> rented, List<Movie> allMovies)
        {
            var recs = new List<TasteRecommendation>();
            var rentedIds = new HashSet<int>(rented.Select(m => m.Id));

            // Recommend from unexplored genres
            var rentedGenres = new HashSet<Genre>(
                rented.Where(m => m.Genre.HasValue).Select(m => m.Genre.Value));

            var unexploredMovies = allMovies
                .Where(m => m.Genre.HasValue && !rentedGenres.Contains(m.Genre.Value) && !rentedIds.Contains(m.Id))
                .OrderByDescending(m => m.Rating ?? 0)
                .Take(3)
                .ToList();

            foreach (var m in unexploredMovies)
            {
                recs.Add(new TasteRecommendation
                {
                    MovieId = m.Id,
                    MovieName = m.Name,
                    Reason = $"Expand into {m.Genre} — a genre you haven't tried yet",
                    Confidence = 0.8
                });
            }

            // Recommend highly rated movies in their top genre
            var topGenre = rented.Where(m => m.Genre.HasValue)
                .GroupBy(m => m.Genre.Value)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();

            var deepDive = allMovies
                .Where(m => m.Genre == topGenre && !rentedIds.Contains(m.Id))
                .OrderByDescending(m => m.Rating ?? 0)
                .Take(2)
                .ToList();

            foreach (var m in deepDive)
            {
                recs.Add(new TasteRecommendation
                {
                    MovieId = m.Id,
                    MovieName = m.Name,
                    Reason = $"Deep dive into your favorite genre: {topGenre}",
                    Confidence = 0.9
                });
            }

            return recs;
        }

        // ── Helpers ────────────────────────────────────────────────

        private static DnaDimension Dim(string name, double score, string detail)
        {
            return new DnaDimension
            {
                Name = name,
                Score = Math.Round(Math.Max(0, Math.Min(1, score)), 3),
                Detail = detail
            };
        }
    }

    // ── DTOs ───────────────────────────────────────────────────────

    public class TasteDnaProfile
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int TotalRentals { get; set; }
        public List<DnaDimension> Dimensions { get; set; }
        public TasteArchetype Archetype { get; set; }
        public List<string> Insights { get; set; }
        public List<TasteRecommendation> Recommendations { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    public class DnaDimension
    {
        public string Name { get; set; }
        public double Score { get; set; }
        public string Detail { get; set; }
    }

    public class TasteArchetype
    {
        public string Name { get; set; }
        public string Emoji { get; set; }
        public string Description { get; set; }
    }

    public class TasteRecommendation
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public string Reason { get; set; }
        public double Confidence { get; set; }
    }

    public class TasteDnaFleet
    {
        public List<TasteDnaProfileSummary> Profiles { get; set; }
        public List<ArchetypeCount> ArchetypeDistribution { get; set; }
        public int TotalCustomers { get; set; }
    }

    public class TasteDnaProfileSummary
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int Rentals { get; set; }
        public string ArchetypeName { get; set; }
        public string ArchetypeEmoji { get; set; }
        public string DominantDimension { get; set; }
    }

    public class ArchetypeCount
    {
        public string Name { get; set; }
        public int Count { get; set; }
    }
}
