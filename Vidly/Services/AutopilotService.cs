using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Movie Autopilot — autonomously curates weekly rental queues for customers
    /// by analyzing rental history, taste evolution, and accept/skip feedback.
    /// Learns from user behavior to improve recommendations over time.
    /// </summary>
    public class AutopilotService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IClock _clock;

        private static readonly Dictionary<int, AutopilotProfile> _profiles
            = new Dictionary<int, AutopilotProfile>();
        private static readonly Dictionary<int, List<AutopilotWeeklyQueue>> _queues
            = new Dictionary<int, List<AutopilotWeeklyQueue>>();

        private static readonly string[] Moods =
            { "Exciting", "Relaxing", "Thought-provoking", "Funny", "Intense", "Romantic", "Scary" };

        private static readonly Dictionary<string, Genre[]> MoodGenreMap
            = new Dictionary<string, Genre[]>
            {
                { "Exciting", new[] { Genre.Action, Genre.Adventure, Genre.Thriller } },
                { "Relaxing", new[] { Genre.Comedy, Genre.Romance, Genre.Animation } },
                { "Thought-provoking", new[] { Genre.Drama, Genre.Documentary } },
                { "Funny", new[] { Genre.Comedy, Genre.Animation } },
                { "Intense", new[] { Genre.Thriller, Genre.Horror, Genre.Action } },
                { "Romantic", new[] { Genre.Romance, Genre.Drama } },
                { "Scary", new[] { Genre.Horror, Genre.Thriller } },
            };

        private static readonly Random _rng = new Random();

        public AutopilotService(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository,
            ICustomerRepository customerRepository,
            IClock clock)
        {
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public AutopilotProfile GetOrCreateProfile(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new KeyNotFoundException("Customer not found.");

            if (!_profiles.ContainsKey(customerId))
            {
                _profiles[customerId] = new AutopilotProfile
                {
                    CustomerId = customerId,
                    Enabled = false,
                    MaxQueueSize = 5,
                    CreatedAt = _clock.Now,
                    UpdatedAt = _clock.Now
                };
            }

            return _profiles[customerId];
        }

        public void UpdateProfile(int customerId, AutopilotProfile profile)
        {
            profile.CustomerId = customerId;
            profile.UpdatedAt = _clock.Now;
            _profiles[customerId] = profile;
        }

        public AutopilotWeeklyQueue GenerateWeeklyQueue(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new KeyNotFoundException("Customer not found.");

            var profile = GetOrCreateProfile(customerId);
            var rentals = _rentalRepository.GetByCustomer(customerId);
            var allMovies = _movieRepository.GetAll();

            // 1. Compute genre weights from rental history
            var genreWeights = ComputeGenreWeights(rentals, allMovies);

            // 2. Boost from profile preferences
            foreach (var g in profile.FavoriteGenres)
            {
                if (genreWeights.ContainsKey(g))
                    genreWeights[g] *= 1.5;
                else
                    genreWeights[g] = 0.3;
            }

            // 3. Mood genre boost
            foreach (var mood in profile.MoodPreferences)
            {
                Genre[] moodGenres;
                if (MoodGenreMap.TryGetValue(mood, out moodGenres))
                {
                    foreach (var g in moodGenres)
                    {
                        if (genreWeights.ContainsKey(g))
                            genreWeights[g] *= 1.2;
                        else
                            genreWeights[g] = 0.2;
                    }
                }
            }

            // Normalize weights
            var totalWeight = genreWeights.Values.Sum();
            if (totalWeight > 0)
            {
                var keys = genreWeights.Keys.ToList();
                foreach (var k in keys)
                    genreWeights[k] /= totalWeight;
            }

            // 4. Identify already-rented movies
            var rentedMovieIds = new HashSet<int>(rentals.Select(r => r.MovieId));

            // 5. Score each available movie
            var scored = new List<Tuple<Movie, double, string, string>>();
            var rentalCounts = allMovies.ToDictionary(m => m.Id,
                m => _rentalRepository.GetByMovie(m.Id).Count);
            var maxRentals = rentalCounts.Values.Any() ? rentalCounts.Values.Max() : 1;

            foreach (var movie in allMovies)
            {
                if (rentedMovieIds.Contains(movie.Id)) continue;

                double genreScore = 0;
                if (movie.Genre.HasValue && genreWeights.ContainsKey(movie.Genre.Value))
                    genreScore = genreWeights[movie.Genre.Value];

                double popularityScore = maxRentals > 0
                    ? (double)rentalCounts[movie.Id] / maxRentals : 0;

                double ratingScore = movie.Rating.HasValue ? movie.Rating.Value / 5.0 : 0.5;

                // Surprise factor: inverse of genre weight (genres they haven't explored)
                double surpriseFactor = 1.0;
                if (movie.Genre.HasValue && genreWeights.ContainsKey(movie.Genre.Value))
                    surpriseFactor = 1.0 - genreWeights[movie.Genre.Value];

                // Decade match
                double decadeScore = 0;
                if (!string.IsNullOrEmpty(profile.DecadePreference) && movie.ReleaseDate.HasValue)
                {
                    int decadeStart;
                    if (int.TryParse(profile.DecadePreference.Replace("s", ""), out decadeStart))
                    {
                        int movieDecade = (movie.ReleaseDate.Value.Year / 10) * 10;
                        decadeScore = movieDecade == decadeStart ? 1.0 : 0.0;
                    }
                }

                double total = (genreScore * 0.40)
                             + (popularityScore * 0.15)
                             + (ratingScore * 0.15)
                             + (surpriseFactor * 0.15)
                             + (decadeScore * 0.15);

                // Categorize
                string category;
                string reason;
                if (genreScore > 0.3)
                {
                    category = "Genre Match";
                    reason = string.Format("Matches your taste for {0}",
                        movie.Genre.HasValue ? movie.Genre.Value.ToString() : "movies");
                }
                else if (surpriseFactor > 0.8)
                {
                    category = "Surprise Pick";
                    reason = string.Format("Something different — expand your horizons with {0}",
                        movie.Genre.HasValue ? movie.Genre.Value.ToString() : "this");
                }
                else if (popularityScore > 0.6)
                {
                    category = "Popular Pick";
                    reason = "Trending with other Vidly members";
                }
                else if (ratingScore > 0.7 && popularityScore < 0.3)
                {
                    category = "Hidden Gem";
                    reason = "Highly rated but under-the-radar";
                }
                else
                {
                    category = "Emerging Taste";
                    reason = "Based on your evolving preferences";
                }

                scored.Add(Tuple.Create(movie, total, reason, category));
            }

            scored.Sort((a, b) => b.Item2.CompareTo(a.Item2));

            // 6. Build balanced queue
            int queueSize = Math.Min(profile.MaxQueueSize, scored.Count);
            var picks = new List<AutopilotPick>();
            var usedCategories = new Dictionary<string, int>();

            foreach (var item in scored)
            {
                if (picks.Count >= queueSize) break;

                int catCount;
                usedCategories.TryGetValue(item.Item4, out catCount);

                // Limit any single category to ~40% of queue
                if (catCount >= Math.Max(1, (int)Math.Ceiling(queueSize * 0.4)))
                    continue;

                picks.Add(new AutopilotPick
                {
                    Movie = item.Item1,
                    RelevanceScore = Math.Round(item.Item2, 3),
                    Reason = item.Item3,
                    Category = item.Item4,
                    Accepted = null
                });

                usedCategories[item.Item4] = catCount + 1;
            }

            // Fill remaining if category balancing left gaps
            if (picks.Count < queueSize)
            {
                var pickedIds = new HashSet<int>(picks.Select(p => p.Movie.Id));
                foreach (var item in scored)
                {
                    if (picks.Count >= queueSize) break;
                    if (pickedIds.Contains(item.Item1.Id)) continue;

                    picks.Add(new AutopilotPick
                    {
                        Movie = item.Item1,
                        RelevanceScore = Math.Round(item.Item2, 3),
                        Reason = item.Item3,
                        Category = item.Item4,
                        Accepted = null
                    });
                    pickedIds.Add(item.Item1.Id);
                }
            }

            // Compute prior acceptance rate
            double priorRate = 0;
            List<AutopilotWeeklyQueue> history;
            if (_queues.TryGetValue(customerId, out history) && history.Count > 0)
            {
                int totalPicks = 0, accepted = 0;
                foreach (var q in history)
                {
                    foreach (var p in q.Picks)
                    {
                        if (p.Accepted.HasValue)
                        {
                            totalPicks++;
                            if (p.Accepted.Value) accepted++;
                        }
                    }
                }
                priorRate = totalPicks > 0 ? (double)accepted / totalPicks : 0;
            }

            var queue = new AutopilotWeeklyQueue
            {
                WeekStart = GetWeekStart(_clock.Today),
                Picks = picks,
                GeneratedAt = _clock.Now,
                TotalMoviesConsidered = scored.Count,
                PriorAcceptanceRate = Math.Round(priorRate, 3)
            };

            if (!_queues.ContainsKey(customerId))
                _queues[customerId] = new List<AutopilotWeeklyQueue>();
            _queues[customerId].Add(queue);

            return queue;
        }

        public void AcceptPick(int customerId, int movieId)
        {
            var pick = FindPick(customerId, movieId);
            if (pick != null)
            {
                pick.Accepted = true;
                pick.AcceptedAt = _clock.Now;
            }
        }

        public void SkipPick(int customerId, int movieId)
        {
            var pick = FindPick(customerId, movieId);
            if (pick != null)
            {
                pick.Accepted = false;
                pick.SkippedAt = _clock.Now;
            }
        }

        public List<AutopilotInsight> GetInsights(int customerId)
        {
            var insights = new List<AutopilotInsight>();
            List<AutopilotWeeklyQueue> history;
            if (!_queues.TryGetValue(customerId, out history) || history.Count == 0)
            {
                insights.Add(new AutopilotInsight
                {
                    Type = "Welcome",
                    Message = "Generate your first queue to start learning your preferences!",
                    Confidence = 1.0,
                    DetectedAt = _clock.Now,
                    Emoji = "🚀"
                });
                return insights;
            }

            var allPicks = history.SelectMany(q => q.Picks).ToList();
            var decided = allPicks.Where(p => p.Accepted.HasValue).ToList();
            int accepted = decided.Count(p => p.Accepted.Value);
            int skipped = decided.Count(p => !p.Accepted.Value);

            if (decided.Count >= 3)
            {
                double rate = (double)accepted / decided.Count;
                if (rate > 0.7)
                {
                    insights.Add(new AutopilotInsight
                    {
                        Type = "Learning",
                        Message = string.Format("Acceptance rate is {0:P0} — Autopilot is learning your taste well!", rate),
                        Confidence = rate,
                        DetectedAt = _clock.Now,
                        Emoji = "🎯"
                    });
                }
                else if (rate < 0.3)
                {
                    insights.Add(new AutopilotInsight
                    {
                        Type = "Adjustment",
                        Message = "Low acceptance rate detected. Try updating your genre and mood preferences to help Autopilot calibrate.",
                        Confidence = 0.8,
                        DetectedAt = _clock.Now,
                        Emoji = "🔧"
                    });
                }
            }

            // Genre pattern analysis
            var skippedGenres = decided
                .Where(p => !p.Accepted.Value && p.Movie.Genre.HasValue)
                .GroupBy(p => p.Movie.Genre.Value)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (skippedGenres != null && skippedGenres.Count() >= 2)
            {
                insights.Add(new AutopilotInsight
                {
                    Type = "Pattern",
                    Message = string.Format("You tend to skip {0} movies — Autopilot will suggest fewer.", skippedGenres.Key),
                    Confidence = Math.Min(1.0, skippedGenres.Count() / 5.0),
                    DetectedAt = _clock.Now,
                    Emoji = "📊"
                });
            }

            var acceptedGenres = decided
                .Where(p => p.Accepted.Value && p.Movie.Genre.HasValue)
                .GroupBy(p => p.Movie.Genre.Value)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (acceptedGenres != null && acceptedGenres.Count() >= 2)
            {
                insights.Add(new AutopilotInsight
                {
                    Type = "Favorite",
                    Message = string.Format("You consistently enjoy {0} — it's your strongest genre signal!", acceptedGenres.Key),
                    Confidence = Math.Min(1.0, acceptedGenres.Count() / 5.0),
                    DetectedAt = _clock.Now,
                    Emoji = "❤️"
                });
            }

            // Category preference
            var favCategory = decided
                .Where(p => p.Accepted.Value)
                .GroupBy(p => p.Category)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (favCategory != null)
            {
                insights.Add(new AutopilotInsight
                {
                    Type = "Discovery",
                    Message = string.Format("You love \"{0}\" suggestions — Autopilot will prioritize these.", favCategory.Key),
                    Confidence = 0.7,
                    DetectedAt = _clock.Now,
                    Emoji = "💡"
                });
            }

            if (history.Count >= 3)
            {
                insights.Add(new AutopilotInsight
                {
                    Type = "Milestone",
                    Message = string.Format("{0} weeks of Autopilot data — recommendations are getting smarter!", history.Count),
                    Confidence = 1.0,
                    DetectedAt = _clock.Now,
                    Emoji = "🏆"
                });
            }

            return insights;
        }

        public AutopilotViewModel GetDashboard(int customerId)
        {
            var customers = _customerRepository.GetAll().OrderBy(c => c.Name).ToList();
            var profile = GetOrCreateProfile(customerId);
            var insights = GetInsights(customerId);

            List<AutopilotWeeklyQueue> history;
            _queues.TryGetValue(customerId, out history);
            history = history ?? new List<AutopilotWeeklyQueue>();

            var currentQueue = history.LastOrDefault();
            var pastQueues = history.Count > 1
                ? history.Take(history.Count - 1).Reverse().ToList()
                : new List<AutopilotWeeklyQueue>();

            // Stats
            var allPicks = history.SelectMany(q => q.Picks).ToList();
            var decided = allPicks.Where(p => p.Accepted.HasValue).ToList();
            int totalAccepted = decided.Count(p => p.Accepted.Value);
            int totalSkipped = decided.Count(p => !p.Accepted.Value);
            double acceptanceRate = decided.Count > 0
                ? (double)totalAccepted / decided.Count : 0;

            // Acceptance streak
            int streak = 0;
            foreach (var p in allPicks.AsEnumerable().Reverse())
            {
                if (p.Accepted.HasValue && p.Accepted.Value) streak++;
                else break;
            }

            return new AutopilotViewModel
            {
                Profile = profile,
                CurrentQueue = currentQueue,
                PastQueues = pastQueues,
                Insights = insights,
                Customers = customers,
                SelectedCustomerId = customerId,
                TotalAccepted = totalAccepted,
                TotalSkipped = totalSkipped,
                Streak = streak,
                AcceptanceRate = Math.Round(acceptanceRate, 3),
                ErrorMessage = null
            };
        }

        private Dictionary<Genre, double> ComputeGenreWeights(
            IReadOnlyList<Rental> rentals, IReadOnlyList<Movie> allMovies)
        {
            var movieMap = allMovies.ToDictionary(m => m.Id);
            var weights = new Dictionary<Genre, double>();

            foreach (var rental in rentals)
            {
                Movie movie;
                if (!movieMap.TryGetValue(rental.MovieId, out movie)) continue;
                if (!movie.Genre.HasValue) continue;

                // Recent rentals weigh more
                double recency = 1.0;
                var age = (_clock.Today - rental.RentalDate).TotalDays;
                if (age > 365) recency = 0.3;
                else if (age > 180) recency = 0.5;
                else if (age > 90) recency = 0.7;

                if (weights.ContainsKey(movie.Genre.Value))
                    weights[movie.Genre.Value] += recency;
                else
                    weights[movie.Genre.Value] = recency;
            }

            return weights;
        }

        private AutopilotPick FindPick(int customerId, int movieId)
        {
            List<AutopilotWeeklyQueue> history;
            if (!_queues.TryGetValue(customerId, out history)) return null;

            var currentQueue = history.LastOrDefault();
            if (currentQueue == null) return null;

            return currentQueue.Picks.FirstOrDefault(p => p.Movie.Id == movieId);
        }

        private DateTime GetWeekStart(DateTime date)
        {
            int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
            return date.AddDays(-diff).Date;
        }
    }
}
