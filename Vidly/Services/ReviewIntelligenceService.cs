using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Agentic reputation advisor over customer reviews.
    /// For every movie with review activity it:
    ///   1. Classifies each review via lexicon-based sentiment + star alignment,
    ///   2. Detects star/text mismatches (e.g. 5★ + "terrible", 1★ + "love it"),
    ///   3. Scores per-movie reputation health (0-100) and assigns a health tier,
    ///   4. Detects reputation trend (recent window vs. earlier baseline),
    ///   5. Synthesises a prioritised playbook of recommended actions
    ///      (P0 = act now, P1 = this week, P2 = nice-to-have).
    ///
    /// Pure read-only analysis — no DB mutation. All outputs are deterministic
    /// for a given (reviews, asOfDate, config) input so tests stay stable.
    ///
    /// Style mirrors other agentic Vidly services (ChurnPredictorService,
    /// EngagementDecayService, AnomalyWatchdogService).
    /// </summary>
    public class ReviewIntelligenceService
    {
        private readonly IReviewRepository _reviewRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly ReviewIntelligenceConfig _config;

        // Lexicons — small, opinionated, intentionally short so the
        // classifier stays predictable and easy to reason about in tests.
        private static readonly HashSet<string> PositiveWords = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "love", "loved", "amazing", "great", "excellent", "fantastic",
            "wonderful", "brilliant", "favorite", "favourite", "perfect",
            "masterpiece", "stunning", "best", "awesome", "incredible",
            "enjoy", "enjoyed", "fun", "delightful", "recommend", "recommended"
        };

        private static readonly HashSet<string> NegativeWords = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "hate", "hated", "terrible", "awful", "boring", "worst",
            "bad", "poor", "disappointing", "disappointed", "waste",
            "horrible", "trash", "garbage", "dull", "slow", "confusing",
            "weak", "skip", "avoid", "regret", "ridiculous"
        };

        private static readonly HashSet<string> NegationWords = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "not", "no", "never", "n't", "isn't", "wasn't",
            "didn't", "don't", "doesn't", "wouldn't", "shouldn't"
        };

        public ReviewIntelligenceService(
            IReviewRepository reviewRepo,
            IMovieRepository movieRepo,
            ReviewIntelligenceConfig config = null)
        {
            _reviewRepo = reviewRepo
                ?? throw new ArgumentNullException(nameof(reviewRepo));
            _movieRepo = movieRepo
                ?? throw new ArgumentNullException(nameof(movieRepo));
            _config = config ?? new ReviewIntelligenceConfig();
        }

        // ── Single review classification ─────────────────────────────

        /// <summary>
        /// Classify one review by combining lexicon sentiment with the star
        /// rating. Returns sentiment label, text-sentiment score in [-1, 1],
        /// alignment ("aligned" / "mismatch" / "ambiguous") and a confidence.
        /// </summary>
        public ReviewSentiment Classify(Review review)
        {
            if (review == null) throw new ArgumentNullException(nameof(review));

            var (textScore, posHits, negHits) = ScoreText(review.ReviewText);

            // Text sentiment label
            string textLabel;
            if (string.IsNullOrWhiteSpace(review.ReviewText) || (posHits == 0 && negHits == 0))
                textLabel = "neutral";
            else if (textScore >= 0.25) textLabel = "positive";
            else if (textScore <= -0.25) textLabel = "negative";
            else textLabel = "mixed";

            // Star bucket
            string starLabel;
            if (review.Stars >= 4) starLabel = "positive";
            else if (review.Stars <= 2) starLabel = "negative";
            else starLabel = "neutral";

            // Alignment
            string alignment;
            if (textLabel == "neutral" || textLabel == "mixed")
                alignment = "ambiguous";
            else if (textLabel == starLabel)
                alignment = "aligned";
            else if ((textLabel == "positive" && starLabel == "neutral") ||
                     (textLabel == "negative" && starLabel == "neutral"))
                alignment = "ambiguous";
            else
                alignment = "mismatch";

            // Confidence: more lexicon hits ⇒ more confident
            int hits = posHits + negHits;
            double confidence = Math.Min(1.0, hits / 4.0);
            if (string.IsNullOrWhiteSpace(review.ReviewText)) confidence = 0;

            return new ReviewSentiment
            {
                ReviewId = review.Id,
                CustomerId = review.CustomerId,
                MovieId = review.MovieId,
                Stars = review.Stars,
                TextScore = Math.Round(textScore, 3),
                TextLabel = textLabel,
                StarLabel = starLabel,
                Alignment = alignment,
                Confidence = Math.Round(confidence, 2),
                PositiveHits = posHits,
                NegativeHits = negHits,
                CreatedDate = review.CreatedDate
            };
        }

        // ── Per-movie analysis ───────────────────────────────────────

        /// <summary>
        /// Build a reputation report for a single movie.
        /// </summary>
        public MovieReputationReport AnalyzeMovie(int movieId, DateTime asOfDate)
        {
            var movie = _movieRepo.GetById(movieId);
            if (movie == null)
                throw new ArgumentException($"Movie {movieId} not found.", nameof(movieId));

            var reviews = _reviewRepo.GetByMovie(movieId);
            return BuildReport(movie, reviews, asOfDate);
        }

        /// <summary>
        /// Build reputation reports for every movie that has at least one review.
        /// Ordered worst-first (lowest health score) so the caller can act
        /// on the highest-risk titles first.
        /// </summary>
        public IReadOnlyList<MovieReputationReport> AnalyzeAll(DateTime asOfDate)
        {
            var allReviews = _reviewRepo.GetAll();
            if (allReviews == null || allReviews.Count == 0)
                return new List<MovieReputationReport>().AsReadOnly();

            var movieLookup = _movieRepo.GetAll().ToDictionary(m => m.Id);
            var reports = new List<MovieReputationReport>();

            foreach (var grp in allReviews.GroupBy(r => r.MovieId))
            {
                if (!movieLookup.TryGetValue(grp.Key, out var movie)) continue;
                reports.Add(BuildReport(movie, grp.ToList(), asOfDate));
            }

            return reports
                .OrderBy(r => r.HealthScore)
                .ThenByDescending(r => r.TotalReviews)
                .ToList()
                .AsReadOnly();
        }

        // ── Catalogue-wide playbook ──────────────────────────────────

        /// <summary>
        /// Generate a prioritised playbook across the catalogue: the most
        /// impactful actions surfaced first (P0 → P1 → P2), bounded by
        /// <see cref="ReviewIntelligenceConfig.MaxPlaybookActions"/>.
        /// </summary>
        public ReputationPlaybook GeneratePlaybook(DateTime asOfDate)
        {
            var reports = AnalyzeAll(asOfDate);

            var actions = new List<PlaybookAction>();
            foreach (var r in reports)
            {
                foreach (var a in r.Actions)
                {
                    actions.Add(new PlaybookAction
                    {
                        MovieId = r.MovieId,
                        MovieName = r.MovieName,
                        Priority = a.Priority,
                        Action = a.Action,
                        Rationale = a.Rationale
                    });
                }
            }

            // Stable, deterministic ordering: P0 first, then by lowest health
            // score (worst movie first), then by action text for tie-breaks.
            var ordered = actions
                .OrderBy(a => PriorityRank(a.Priority))
                .ThenBy(a =>
                {
                    var r = reports.FirstOrDefault(x => x.MovieId == a.MovieId);
                    return r?.HealthScore ?? 100;
                })
                .ThenBy(a => a.Action, StringComparer.Ordinal)
                .Take(_config.MaxPlaybookActions)
                .ToList();

            // Catalogue health = simple weighted mean of per-movie health
            // weighted by review count (movies with more signal count more).
            double catalogueHealth = 100;
            int totalReviews = reports.Sum(r => r.TotalReviews);
            if (totalReviews > 0)
            {
                double weighted = reports.Sum(r => r.HealthScore * (double)r.TotalReviews);
                catalogueHealth = Math.Round(weighted / totalReviews, 1);
            }

            return new ReputationPlaybook
            {
                GeneratedAt = asOfDate,
                CatalogueHealthScore = catalogueHealth,
                MoviesAnalyzed = reports.Count,
                AtRiskMovies = reports.Count(r => r.HealthTier == ReputationTier.AtRisk
                                                || r.HealthTier == ReputationTier.Crisis),
                Reports = reports,
                Actions = ordered.AsReadOnly()
            };
        }

        // ── Renderers ────────────────────────────────────────────────

        /// <summary>Compact plain-text summary suitable for ops console / email.</summary>
        public string RenderText(ReputationPlaybook playbook)
        {
            if (playbook == null) throw new ArgumentNullException(nameof(playbook));
            var sb = new StringBuilder();
            sb.AppendLine("REVIEW INTELLIGENCE — REPUTATION PLAYBOOK");
            sb.AppendLine($"Generated: {playbook.GeneratedAt:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"Catalogue health: {playbook.CatalogueHealthScore}/100  " +
                          $"({playbook.MoviesAnalyzed} movies, {playbook.AtRiskMovies} at risk)");
            sb.AppendLine();

            if (playbook.Actions.Count == 0)
            {
                sb.AppendLine("No actions recommended — catalogue reputation is healthy.");
            }
            else
            {
                sb.AppendLine("Recommended actions:");
                foreach (var a in playbook.Actions)
                {
                    sb.AppendLine($"  [{a.Priority}] {a.MovieName}: {a.Action}");
                    sb.AppendLine($"        ↳ {a.Rationale}");
                }
            }
            return sb.ToString();
        }

        /// <summary>Markdown-formatted report.</summary>
        public string RenderMarkdown(ReputationPlaybook playbook)
        {
            if (playbook == null) throw new ArgumentNullException(nameof(playbook));
            var sb = new StringBuilder();
            sb.AppendLine("# Review Intelligence — Reputation Playbook");
            sb.AppendLine();
            sb.AppendLine($"- **Generated:** {playbook.GeneratedAt:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"- **Catalogue health:** {playbook.CatalogueHealthScore}/100");
            sb.AppendLine($"- **Movies analyzed:** {playbook.MoviesAnalyzed}");
            sb.AppendLine($"- **At-risk movies:** {playbook.AtRiskMovies}");
            sb.AppendLine();

            sb.AppendLine("## Actions");
            if (playbook.Actions.Count == 0)
            {
                sb.AppendLine("_No actions recommended._");
            }
            else
            {
                sb.AppendLine("| Priority | Movie | Action | Rationale |");
                sb.AppendLine("|----------|-------|--------|-----------|");
                foreach (var a in playbook.Actions)
                {
                    sb.AppendLine($"| {a.Priority} | {Escape(a.MovieName)} | {Escape(a.Action)} | {Escape(a.Rationale)} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Per-movie health");
            sb.AppendLine("| Movie | Health | Tier | Avg★ | Reviews | Mismatches | Trend |");
            sb.AppendLine("|-------|--------|------|------|---------|------------|-------|");
            foreach (var r in playbook.Reports)
            {
                sb.AppendLine($"| {Escape(r.MovieName)} | {r.HealthScore} | {r.HealthTier} | " +
                              $"{r.AverageStars:0.0} | {r.TotalReviews} | {r.MismatchCount} | {r.Trend} |");
            }

            return sb.ToString();
        }

        // ── Internals ────────────────────────────────────────────────

        private MovieReputationReport BuildReport(
            Movie movie, IReadOnlyList<Review> reviews, DateTime asOfDate)
        {
            var sentiments = reviews.Select(Classify).ToList();
            int total = sentiments.Count;
            double avgStars = total > 0 ? sentiments.Average(s => (double)s.Stars) : 0;

            int positives = sentiments.Count(s => s.Alignment == "aligned"
                                                 && s.TextLabel == "positive");
            int negatives = sentiments.Count(s => s.Alignment == "aligned"
                                                 && s.TextLabel == "negative");
            int mismatches = sentiments.Count(s => s.Alignment == "mismatch");

            // Suspicious mismatches: high-star reviews whose text is clearly
            // negative — common pattern for fake/promo reviews.
            var suspicious = sentiments
                .Where(s => s.Stars >= 4 && s.TextLabel == "negative")
                .Select(s => s.ReviewId)
                .ToList();

            // Trend: split into "recent" (within RecentWindowDays of asOfDate)
            // vs "baseline" (everything older). If we don't have a baseline
            // we report Trend = Stable.
            var trend = ComputeTrend(sentiments, asOfDate);

            // Health score: starts at 100, penalised by negative density,
            // mismatch density, and downward trend; rewarded for volume.
            double health = 100;
            if (total > 0)
            {
                double negDensity = (double)negatives / total;
                double misDensity = (double)mismatches / total;
                health -= negDensity * 50.0;       // up to -50 for all-negative
                health -= misDensity * 20.0;       // up to -20 for all-mismatch
                health += Math.Min(10, total) * 0.5; // +5 for ≥10 reviews (signal bonus)

                // Star-anchored floor — average stars shouldn't be ignored
                health -= Math.Max(0, (3.0 - avgStars)) * 10;

                if (trend == ReputationTrend.Declining) health -= 8;
                if (trend == ReputationTrend.Improving) health += 4;
            }
            health = Math.Max(0, Math.Min(100, Math.Round(health, 1)));

            ReputationTier tier;
            if (total == 0) tier = ReputationTier.Unknown;
            else if (health >= 80) tier = ReputationTier.Healthy;
            else if (health >= 60) tier = ReputationTier.Watch;
            else if (health >= 40) tier = ReputationTier.AtRisk;
            else tier = ReputationTier.Crisis;

            var actions = RecommendActions(movie, total, avgStars, negatives,
                                           mismatches, suspicious.Count, trend, tier);

            return new MovieReputationReport
            {
                MovieId = movie.Id,
                MovieName = movie.Name,
                Genre = movie.Genre,
                TotalReviews = total,
                AverageStars = Math.Round(avgStars, 2),
                PositiveCount = positives,
                NegativeCount = negatives,
                MismatchCount = mismatches,
                SuspiciousReviewIds = suspicious.AsReadOnly(),
                Trend = trend,
                HealthScore = health,
                HealthTier = tier,
                Actions = actions.AsReadOnly(),
                Sentiments = sentiments.AsReadOnly()
            };
        }

        private ReputationTrend ComputeTrend(
            List<ReviewSentiment> sentiments, DateTime asOfDate)
        {
            var window = TimeSpan.FromDays(_config.RecentWindowDays);
            var recent = sentiments.Where(s => asOfDate - s.CreatedDate <= window).ToList();
            var baseline = sentiments.Where(s => asOfDate - s.CreatedDate > window).ToList();

            if (recent.Count < 2 || baseline.Count < 2)
                return ReputationTrend.Stable;

            double recentAvg = recent.Average(s => (double)s.Stars);
            double baseAvg = baseline.Average(s => (double)s.Stars);
            double delta = recentAvg - baseAvg;

            if (delta <= -_config.TrendDeltaThreshold) return ReputationTrend.Declining;
            if (delta >= _config.TrendDeltaThreshold) return ReputationTrend.Improving;
            return ReputationTrend.Stable;
        }

        private List<MovieAction> RecommendActions(
            Movie movie, int total, double avgStars, int negatives,
            int mismatches, int suspicious, ReputationTrend trend,
            ReputationTier tier)
        {
            var actions = new List<MovieAction>();

            if (total == 0)
            {
                actions.Add(new MovieAction
                {
                    Priority = "P2",
                    Action = "Solicit first reviews",
                    Rationale = "No reviews yet — invite recent renters to rate this title."
                });
                return actions;
            }

            // Crisis / at-risk titles need immediate visibility action.
            if (tier == ReputationTier.Crisis)
            {
                actions.Add(new MovieAction
                {
                    Priority = "P0",
                    Action = "Pull from front-page promotion",
                    Rationale = $"Health {tier} (avg {avgStars:0.0}★ across {total} reviews) — " +
                                "do not feature until reputation recovers."
                });
            }
            else if (tier == ReputationTier.AtRisk)
            {
                actions.Add(new MovieAction
                {
                    Priority = "P1",
                    Action = "Demote in recommendation ranking",
                    Rationale = $"At-risk reputation ({avgStars:0.0}★, {negatives} negative) — " +
                                "rank below healthier alternatives in suggestions."
                });
            }

            if (suspicious >= _config.SuspiciousReviewFlagThreshold)
            {
                actions.Add(new MovieAction
                {
                    Priority = "P0",
                    Action = $"Flag {suspicious} suspicious reviews for moderation",
                    Rationale = "High-star reviews whose text reads as clearly negative — " +
                                "possible fake / coerced ratings."
                });
            }

            if (mismatches >= Math.Max(2, total / 4))
            {
                actions.Add(new MovieAction
                {
                    Priority = "P1",
                    Action = "Review moderation triage on text/star mismatches",
                    Rationale = $"{mismatches} of {total} reviews have text/star mismatch — " +
                                "moderator should sanity-check before they sway the average."
                });
            }

            if (trend == ReputationTrend.Declining
                && tier != ReputationTier.Crisis)
            {
                actions.Add(new MovieAction
                {
                    Priority = "P1",
                    Action = "Investigate recent decline",
                    Rationale = $"Recent {_config.RecentWindowDays}-day window is " +
                                "trending worse than the baseline — check for copy " +
                                "condition issues or new pricing complaints."
                });
            }

            if (tier == ReputationTier.Healthy && total >= 5
                && trend != ReputationTrend.Declining)
            {
                actions.Add(new MovieAction
                {
                    Priority = "P2",
                    Action = "Boost in 'crowd favourites' shelf",
                    Rationale = $"Healthy reputation ({avgStars:0.0}★ over {total} reviews) — " +
                                "good candidate for crowd-favourites promotion."
                });
            }

            if (total < _config.LowVolumeThreshold && tier != ReputationTier.Crisis)
            {
                actions.Add(new MovieAction
                {
                    Priority = "P2",
                    Action = "Solicit additional reviews",
                    Rationale = $"Only {total} review(s) — request feedback from recent renters " +
                                "to firm up the signal before acting."
                });
            }

            return actions;
        }

        private (double score, int posHits, int negHits) ScoreText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return (0, 0, 0);

            // Tokenize on whitespace/punctuation but keep contractions like "don't".
            var tokens = new List<string>();
            var word = new StringBuilder();
            foreach (var ch in text)
            {
                if (char.IsLetter(ch) || ch == '\'')
                {
                    word.Append(ch);
                }
                else
                {
                    if (word.Length > 0)
                    {
                        tokens.Add(word.ToString());
                        word.Clear();
                    }
                }
            }
            if (word.Length > 0) tokens.Add(word.ToString());

            int pos = 0, neg = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                var tok = tokens[i];
                bool negated = i > 0 && NegationWords.Contains(tokens[i - 1]);

                if (PositiveWords.Contains(tok))
                {
                    if (negated) neg++; else pos++;
                }
                else if (NegativeWords.Contains(tok))
                {
                    if (negated) pos++; else neg++;
                }
            }

            int total = pos + neg;
            if (total == 0) return (0, 0, 0);
            double score = (double)(pos - neg) / total;
            return (score, pos, neg);
        }

        private static int PriorityRank(string p)
        {
            switch (p)
            {
                case "P0": return 0;
                case "P1": return 1;
                case "P2": return 2;
                default:   return 3;
            }
        }

        private static string Escape(string s) =>
            (s ?? string.Empty).Replace("|", "\\|").Replace("\n", " ").Trim();
    }

    // ── Config & DTOs ────────────────────────────────────────────────

    /// <summary>Tunable thresholds for <see cref="ReviewIntelligenceService"/>.</summary>
    public class ReviewIntelligenceConfig
    {
        /// <summary>Days considered "recent" for trend analysis.</summary>
        public int RecentWindowDays { get; set; } = 30;

        /// <summary>Minimum |Δ avg stars| to flag a trend as Improving/Declining.</summary>
        public double TrendDeltaThreshold { get; set; } = 0.5;

        /// <summary>Movies with strictly fewer reviews than this get a "solicit more" action.</summary>
        public int LowVolumeThreshold { get; set; } = 3;

        /// <summary>Number of suspicious high-star+negative-text reviews that triggers moderation.</summary>
        public int SuspiciousReviewFlagThreshold { get; set; } = 1;

        /// <summary>Hard cap on actions in the catalogue-wide playbook.</summary>
        public int MaxPlaybookActions { get; set; } = 50;
    }

    public enum ReputationTier
    {
        Unknown = 0,
        Crisis = 1,
        AtRisk = 2,
        Watch = 3,
        Healthy = 4
    }

    public enum ReputationTrend
    {
        Stable = 0,
        Declining = 1,
        Improving = 2
    }

    public class ReviewSentiment
    {
        public int ReviewId { get; set; }
        public int CustomerId { get; set; }
        public int MovieId { get; set; }
        public int Stars { get; set; }
        public double TextScore { get; set; }      // [-1, 1]
        public string TextLabel { get; set; }      // positive | negative | mixed | neutral
        public string StarLabel { get; set; }      // positive | negative | neutral
        public string Alignment { get; set; }      // aligned | mismatch | ambiguous
        public double Confidence { get; set; }     // [0, 1]
        public int PositiveHits { get; set; }
        public int NegativeHits { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class MovieAction
    {
        public string Priority { get; set; }       // P0 | P1 | P2
        public string Action { get; set; }
        public string Rationale { get; set; }
    }

    public class PlaybookAction
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public string Priority { get; set; }
        public string Action { get; set; }
        public string Rationale { get; set; }
    }

    public class MovieReputationReport
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? Genre { get; set; }
        public int TotalReviews { get; set; }
        public double AverageStars { get; set; }
        public int PositiveCount { get; set; }
        public int NegativeCount { get; set; }
        public int MismatchCount { get; set; }
        public IReadOnlyList<int> SuspiciousReviewIds { get; set; }
        public ReputationTrend Trend { get; set; }
        public double HealthScore { get; set; }
        public ReputationTier HealthTier { get; set; }
        public IReadOnlyList<MovieAction> Actions { get; set; }
        public IReadOnlyList<ReviewSentiment> Sentiments { get; set; }
    }

    public class ReputationPlaybook
    {
        public DateTime GeneratedAt { get; set; }
        public double CatalogueHealthScore { get; set; }
        public int MoviesAnalyzed { get; set; }
        public int AtRiskMovies { get; set; }
        public IReadOnlyList<MovieReputationReport> Reports { get; set; }
        public IReadOnlyList<PlaybookAction> Actions { get; set; }
    }
}
