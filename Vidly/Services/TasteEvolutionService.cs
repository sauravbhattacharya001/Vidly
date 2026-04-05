using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Analyzes how a customer's movie taste evolves over time.
    /// Detects genre drift, predicts future preferences using momentum,
    /// and proactively suggests movies matching their evolving profile.
    /// </summary>
    public class TasteEvolutionService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IClock _clock;

        private static readonly Dictionary<string, (string Persona, string Emoji, string Trait)> _personas =
            new Dictionary<string, (string, string, string)>
            {
                ["explorer"] = ("Genre Explorer", "🧭", "You love variety — always trying new genres."),
                ["loyalist"] = ("Genre Loyalist", "🎯", "You know what you like and stick with it."),
                ["shifter"] = ("Taste Shifter", "🌊", "Your preferences are actively evolving."),
                ["omnivore"] = ("Movie Omnivore", "🍿", "You enjoy everything equally — a true cinephile."),
                ["newcomer"] = ("Fresh Eyes", "👀", "Just getting started — your taste is forming.")
            };

        private static readonly Dictionary<Genre, string> _genreEmoji = new Dictionary<Genre, string>
        {
            [Genre.Action] = "💥",
            [Genre.Comedy] = "😂",
            [Genre.Drama] = "🎭",
            [Genre.Horror] = "👻",
            [Genre.SciFi] = "🚀",
            [Genre.Animation] = "🎨",
            [Genre.Thriller] = "🔪",
            [Genre.Romance] = "💕",
            [Genre.Documentary] = "📚",
            [Genre.Adventure] = "⛰️"
        };

        public TasteEvolutionService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IClock clock)
        {
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// Generates a complete taste evolution report for a customer.
        /// </summary>
        public TasteEvolutionReport Analyze(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new KeyNotFoundException($"Customer {customerId} not found.");

            var rentals = _rentalRepository.GetByCustomer(customerId);
            var movies = _movieRepository.GetAll();
            var movieLookup = movies.ToDictionary(m => m.Id);

            // Enrich rentals with movie data
            var enriched = rentals
                .Where(r => movieLookup.ContainsKey(r.MovieId) && movieLookup[r.MovieId].Genre.HasValue)
                .Select(r => new { Rental = r, Movie = movieLookup[r.MovieId] })
                .OrderBy(x => x.Rental.RentalDate)
                .ToList();

            var report = new TasteEvolutionReport
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                TotalRentals = enriched.Count
            };

            if (enriched.Count < 2)
            {
                var persona = _personas["newcomer"];
                report.TastePersona = persona.Persona;
                report.PersonaEmoji = persona.Emoji;
                report.Insight = "Rent a few more movies and we'll start tracking your taste evolution!";
                return report;
            }

            // Build timeline snapshots (quarterly periods)
            report.Timeline = BuildTimeline(enriched.Select(x => (x.Rental.RentalDate, x.Movie.Genre.Value)).ToList());

            // Detect genre drift
            report.Drifts = DetectDrift(report.Timeline);

            // Predict future preferences
            report.Predictions = PredictFuture(report.Timeline);

            // Generate proactive suggestions
            report.Suggestions = GenerateSuggestions(report.Predictions, report.Drifts, enriched.Select(x => x.Movie.Id).ToHashSet(), movies);

            // Classify taste persona
            ClassifyPersona(report);

            // Generate natural language insight
            report.Insight = GenerateInsight(report);

            return report;
        }

        private List<GenreSnapshot> BuildTimeline(List<(DateTime Date, Genre Genre)> rentals)
        {
            var snapshots = new List<GenreSnapshot>();
            if (!rentals.Any()) return snapshots;

            var start = rentals.First().Date;
            var end = _clock.Now;
            var current = new DateTime(start.Year, ((start.Month - 1) / 3) * 3 + 1, 1);

            while (current < end)
            {
                var periodEnd = current.AddMonths(3);
                var periodRentals = rentals.Where(r => r.Date >= current && r.Date < periodEnd).ToList();

                if (periodRentals.Any())
                {
                    var total = (double)periodRentals.Count;
                    var weights = periodRentals
                        .GroupBy(r => r.Genre)
                        .ToDictionary(g => g.Key, g => g.Count() / total);

                    snapshots.Add(new GenreSnapshot
                    {
                        Period = $"{current:yyyy} Q{((current.Month - 1) / 3) + 1}",
                        PeriodStart = current,
                        PeriodEnd = periodEnd,
                        RentalCount = periodRentals.Count,
                        GenreWeights = weights,
                        TopGenre = weights.OrderByDescending(w => w.Value).First().Key
                    });
                }

                current = periodEnd;
            }

            return snapshots;
        }

        private List<GenreDrift> DetectDrift(List<GenreSnapshot> timeline)
        {
            var drifts = new List<GenreDrift>();
            if (timeline.Count < 2) return drifts;

            // Compare first half vs second half of timeline
            int mid = timeline.Count / 2;
            var earlySnapshots = timeline.Take(mid).ToList();
            var recentSnapshots = timeline.Skip(mid).ToList();

            var allGenres = timeline.SelectMany(s => s.GenreWeights.Keys).Distinct();

            foreach (var genre in allGenres)
            {
                double earlyAvg = earlySnapshots
                    .Where(s => s.GenreWeights.ContainsKey(genre))
                    .Select(s => s.GenreWeights[genre])
                    .DefaultIfEmpty(0)
                    .Average();

                double recentAvg = recentSnapshots
                    .Where(s => s.GenreWeights.ContainsKey(genre))
                    .Select(s => s.GenreWeights[genre])
                    .DefaultIfEmpty(0)
                    .Average();

                double delta = recentAvg - earlyAvg;
                string direction = Math.Abs(delta) < 0.05 ? "Stable" : (delta > 0 ? "Rising" : "Falling");
                string emoji = direction == "Rising" ? "📈" : (direction == "Falling" ? "📉" : "➡️");

                drifts.Add(new GenreDrift
                {
                    Genre = genre,
                    GenreName = genre.ToString(),
                    EarlyWeight = Math.Round(earlyAvg * 100, 1),
                    RecentWeight = Math.Round(recentAvg * 100, 1),
                    Delta = Math.Round(delta * 100, 1),
                    Direction = direction,
                    Emoji = emoji
                });
            }

            return drifts.OrderByDescending(d => Math.Abs(d.Delta)).ToList();
        }

        private List<GenrePrediction> PredictFuture(List<GenreSnapshot> timeline)
        {
            var predictions = new List<GenrePrediction>();
            if (timeline.Count < 2) return predictions;

            var allGenres = timeline.SelectMany(s => s.GenreWeights.Keys).Distinct();
            var recent = timeline.Last();

            foreach (var genre in allGenres)
            {
                // Simple linear momentum: weighted recent trend
                var weights = timeline
                    .Select((s, i) => new
                    {
                        Weight = s.GenreWeights.ContainsKey(genre) ? s.GenreWeights[genre] : 0.0,
                        Index = i
                    })
                    .ToList();

                double currentWeight = recent.GenreWeights.ContainsKey(genre) ? recent.GenreWeights[genre] : 0;

                // Calculate momentum using last 3 data points (or all if fewer)
                var tail = weights.Skip(Math.Max(0, weights.Count - 3)).ToList();
                double momentum = 0;
                if (tail.Count >= 2)
                {
                    momentum = (tail.Last().Weight - tail.First().Weight) / tail.Count;
                }

                double predicted = Math.Max(0, Math.Min(1, currentWeight + momentum));

                // Confidence based on data consistency
                double variance = weights.Select(w => w.Weight).DefaultIfEmpty(0).Select(w => Math.Pow(w - currentWeight, 2)).Average();
                double confidence = Math.Max(0.1, Math.Min(0.95, 1.0 - Math.Sqrt(variance) * 2));

                predictions.Add(new GenrePrediction
                {
                    Genre = genre,
                    GenreName = genre.ToString(),
                    CurrentWeight = Math.Round(currentWeight * 100, 1),
                    PredictedWeight = Math.Round(predicted * 100, 1),
                    Momentum = Math.Round(momentum * 100, 1),
                    Confidence = Math.Round(confidence * 100, 1)
                });
            }

            return predictions.OrderByDescending(p => p.PredictedWeight).ToList();
        }

        private List<TasteSuggestion> GenerateSuggestions(
            List<GenrePrediction> predictions,
            List<GenreDrift> drifts,
            HashSet<int> rentedMovieIds,
            IReadOnlyList<Movie> allMovies)
        {
            var suggestions = new List<TasteSuggestion>();

            // Find rising genres — these are emerging tastes
            var risingGenres = drifts
                .Where(d => d.Direction == "Rising")
                .Select(d => d.Genre)
                .ToHashSet();

            // Top predicted genres
            var topPredicted = predictions
                .Where(p => p.PredictedWeight > 5)
                .OrderByDescending(p => p.PredictedWeight)
                .Take(3)
                .Select(p => p.Genre)
                .ToHashSet();

            var candidates = allMovies
                .Where(m => m.Genre.HasValue && !rentedMovieIds.Contains(m.Id))
                .ToList();

            foreach (var movie in candidates)
            {
                var genre = movie.Genre.Value;
                var prediction = predictions.FirstOrDefault(p => p.Genre == genre);
                if (prediction == null) continue;

                bool isEmerging = risingGenres.Contains(genre);
                bool isTopPredicted = topPredicted.Contains(genre);

                if (!isEmerging && !isTopPredicted) continue;

                double score = prediction.PredictedWeight;
                if (isEmerging) score *= 1.3; // Boost emerging taste
                if (movie.Rating.HasValue) score *= (1 + movie.Rating.Value * 0.1);

                string reason = isEmerging
                    ? $"Matches your emerging interest in {genre} {(_genreEmoji.ContainsKey(genre) ? _genreEmoji[genre] : "")}"
                    : $"Fits your predicted taste for {genre}";

                suggestions.Add(new TasteSuggestion
                {
                    Movie = movie,
                    RelevanceScore = Math.Round(score, 1),
                    Reason = reason,
                    MatchedGenre = genre,
                    IsEmergingTaste = isEmerging
                });
            }

            return suggestions.OrderByDescending(s => s.RelevanceScore).Take(10).ToList();
        }

        private void ClassifyPersona(TasteEvolutionReport report)
        {
            var drifts = report.Drifts;
            var timeline = report.Timeline;

            if (timeline.Count < 2)
            {
                var p = _personas["newcomer"];
                report.TastePersona = p.Persona;
                report.PersonaEmoji = p.Emoji;
                return;
            }

            // Count how many genres appear across all snapshots
            var uniqueGenres = timeline.SelectMany(s => s.GenreWeights.Keys).Distinct().Count();
            var avgGenresPerPeriod = timeline.Average(s => s.GenreWeights.Count);
            var risingCount = drifts.Count(d => d.Direction == "Rising");
            var fallingCount = drifts.Count(d => d.Direction == "Falling");
            var totalDrift = drifts.Sum(d => Math.Abs(d.Delta));

            string key;
            if (totalDrift < 10 && avgGenresPerPeriod >= 3)
                key = "omnivore";
            else if (totalDrift < 10 && avgGenresPerPeriod < 3)
                key = "loyalist";
            else if (risingCount >= 2 || totalDrift > 30)
                key = "shifter";
            else
                key = "explorer";

            var persona = _personas[key];
            report.TastePersona = persona.Persona;
            report.PersonaEmoji = persona.Emoji;
        }

        private string GenerateInsight(TasteEvolutionReport report)
        {
            var rising = report.Drifts.Where(d => d.Direction == "Rising").OrderByDescending(d => d.Delta).FirstOrDefault();
            var falling = report.Drifts.Where(d => d.Direction == "Falling").OrderByDescending(d => Math.Abs(d.Delta)).FirstOrDefault();
            var topPrediction = report.Predictions.FirstOrDefault();

            var parts = new List<string>();

            if (rising != null && rising.Delta > 5)
                parts.Add($"You've been gravitating toward {rising.GenreName} lately (up {rising.Delta:+0.#}%).");

            if (falling != null && Math.Abs(falling.Delta) > 5)
                parts.Add($"Your interest in {falling.GenreName} has cooled off ({falling.Delta:0.#}%).");

            if (topPrediction != null)
                parts.Add($"We predict {topPrediction.GenreName} will be your top pick next quarter ({topPrediction.Confidence}% confidence).");

            if (report.Suggestions.Any(s => s.IsEmergingTaste))
                parts.Add($"We found {report.Suggestions.Count(s => s.IsEmergingTaste)} movies matching your emerging taste — check them out!");

            return parts.Any()
                ? string.Join(" ", parts)
                : "Keep renting and we'll uncover your taste evolution!";
        }
    }
}
