using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Gamification system that awards badges and tracks milestones based on
    /// customer rental behavior. Badges are evaluated dynamically from rental
    /// history — no persistent badge state needed.
    /// </summary>
    public class AchievementService
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly IReviewRepository _reviewRepository;
        private readonly IClock _clock;

        public AchievementService(
            ICustomerRepository customerRepository,
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            IReviewRepository reviewRepository = null,
            IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _reviewRepository = reviewRepository;
        }

        // ── Badge definitions ────────────────────────────────────

        /// <summary>
        /// All available badges with their evaluation criteria.
        /// </summary>
        public static IReadOnlyList<BadgeDefinition> AllBadges { get; } = new List<BadgeDefinition>
        {
            // ── Rental count milestones ──
            new BadgeDefinition
            {
                Id = "first_rental",
                Name = "First Timer",
                Description = "Rented your first movie",
                Emoji = "🎬",
                Category = BadgeCategory.Milestone,
                Tier = BadgeTier.Bronze,
                RequiredCount = 1
            },
            new BadgeDefinition
            {
                Id = "regular_10",
                Name = "Regular",
                Description = "Rented 10 movies",
                Emoji = "📀",
                Category = BadgeCategory.Milestone,
                Tier = BadgeTier.Bronze,
                RequiredCount = 10
            },
            new BadgeDefinition
            {
                Id = "enthusiast_25",
                Name = "Movie Enthusiast",
                Description = "Rented 25 movies",
                Emoji = "🎞️",
                Category = BadgeCategory.Milestone,
                Tier = BadgeTier.Silver,
                RequiredCount = 25
            },
            new BadgeDefinition
            {
                Id = "buff_50",
                Name = "Movie Buff",
                Description = "Rented 50 movies",
                Emoji = "🍿",
                Category = BadgeCategory.Milestone,
                Tier = BadgeTier.Gold,
                RequiredCount = 50
            },
            new BadgeDefinition
            {
                Id = "legend_100",
                Name = "Loyalty Legend",
                Description = "Rented 100 movies — you're a legend!",
                Emoji = "👑",
                Category = BadgeCategory.Milestone,
                Tier = BadgeTier.Platinum,
                RequiredCount = 100
            },

            // ── Genre exploration ──
            new BadgeDefinition
            {
                Id = "genre_explorer_3",
                Name = "Genre Explorer",
                Description = "Rented movies from 3 different genres",
                Emoji = "🧭",
                Category = BadgeCategory.Exploration,
                Tier = BadgeTier.Bronze,
                RequiredCount = 3
            },
            new BadgeDefinition
            {
                Id = "genre_connoisseur_5",
                Name = "Genre Connoisseur",
                Description = "Rented movies from 5 different genres",
                Emoji = "🎭",
                Category = BadgeCategory.Exploration,
                Tier = BadgeTier.Silver,
                RequiredCount = 5
            },
            new BadgeDefinition
            {
                Id = "genre_master_all",
                Name = "Genre Master",
                Description = "Rented movies from every available genre",
                Emoji = "🌟",
                Category = BadgeCategory.Exploration,
                Tier = BadgeTier.Gold,
                RequiredCount = -1 // special: all genres
            },

            // ── Behavioral badges ──
            new BadgeDefinition
            {
                Id = "binge_watcher",
                Name = "Binge Watcher",
                Description = "Rented 5+ movies in a single week",
                Emoji = "📺",
                Category = BadgeCategory.Behavior,
                Tier = BadgeTier.Silver,
                RequiredCount = 5
            },
            new BadgeDefinition
            {
                Id = "on_time_streak_5",
                Name = "Punctual",
                Description = "Returned 5 movies on time in a row",
                Emoji = "⏰",
                Category = BadgeCategory.Behavior,
                Tier = BadgeTier.Bronze,
                RequiredCount = 5
            },
            new BadgeDefinition
            {
                Id = "on_time_streak_15",
                Name = "Always On Time",
                Description = "Returned 15 movies on time in a row",
                Emoji = "🏆",
                Category = BadgeCategory.Behavior,
                Tier = BadgeTier.Gold,
                RequiredCount = 15
            },
            new BadgeDefinition
            {
                Id = "early_bird",
                Name = "Early Bird",
                Description = "Returned a movie 3+ days before the due date",
                Emoji = "🐦",
                Category = BadgeCategory.Behavior,
                Tier = BadgeTier.Bronze,
                RequiredCount = 1
            },

            // ── Social / community badges ──
            new BadgeDefinition
            {
                Id = "reviewer",
                Name = "Critic",
                Description = "Left your first review",
                Emoji = "✍️",
                Category = BadgeCategory.Social,
                Tier = BadgeTier.Bronze,
                RequiredCount = 1
            },
            new BadgeDefinition
            {
                Id = "reviewer_10",
                Name = "Film Critic",
                Description = "Left 10 reviews",
                Emoji = "📝",
                Category = BadgeCategory.Social,
                Tier = BadgeTier.Silver,
                RequiredCount = 10
            },

            // ── Membership badges ──
            new BadgeDefinition
            {
                Id = "member_1_year",
                Name = "One Year Club",
                Description = "Member for over 1 year",
                Emoji = "🎂",
                Category = BadgeCategory.Loyalty,
                Tier = BadgeTier.Bronze,
                RequiredCount = 365
            },
            new BadgeDefinition
            {
                Id = "member_3_years",
                Name = "Veteran Member",
                Description = "Member for over 3 years",
                Emoji = "⭐",
                Category = BadgeCategory.Loyalty,
                Tier = BadgeTier.Silver,
                RequiredCount = 1095
            },
            new BadgeDefinition
            {
                Id = "member_5_years",
                Name = "Founding Member",
                Description = "Member for over 5 years",
                Emoji = "💎",
                Category = BadgeCategory.Loyalty,
                Tier = BadgeTier.Platinum,
                RequiredCount = 1825
            },

            // ── Rating-based ──
            new BadgeDefinition
            {
                Id = "five_star_fan",
                Name = "Five Star Fan",
                Description = "Rented 5 movies rated 5 stars",
                Emoji = "⭐",
                Category = BadgeCategory.Taste,
                Tier = BadgeTier.Silver,
                RequiredCount = 5
            },
            new BadgeDefinition
            {
                Id = "hidden_gem_hunter",
                Name = "Hidden Gem Hunter",
                Description = "Rented 3 movies rated 2 stars or below",
                Emoji = "💎",
                Category = BadgeCategory.Taste,
                Tier = BadgeTier.Bronze,
                RequiredCount = 3
            },
        }.AsReadOnly();

        // ── Core evaluation ──────────────────────────────────────

        /// <summary>
        /// Evaluates all badges for a customer, returning earned and locked badges.
        /// </summary>
        public AchievementProfile GetProfile(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new KeyNotFoundException($"Customer {customerId} not found.");

            var allRentals = _rentalRepository.GetAll();
            var customerRentals = allRentals.Where(r => r.CustomerId == customerId).ToList();
            var allMovies = _movieRepository.GetAll();
            var movieLookup = allMovies.ToDictionary(m => m.Id);

            var customerReviews = _reviewRepository?.GetByCustomer(customerId)
                ?? new List<Review>();

            var earned = new List<EarnedBadge>();
            var locked = new List<LockedBadge>();

            foreach (var badge in AllBadges)
            {
                var result = EvaluateBadge(badge, customer, customerRentals,
                    movieLookup, customerReviews);
                if (result.IsEarned)
                {
                    earned.Add(new EarnedBadge
                    {
                        Badge = badge,
                        EarnedDate = result.EarnedDate,
                    });
                }
                else
                {
                    locked.Add(new LockedBadge
                    {
                        Badge = badge,
                        Progress = result.Progress,
                        Remaining = result.Remaining,
                        Hint = result.Hint,
                    });
                }
            }

            // Calculate achievement score
            int score = earned.Sum(e => GetTierPoints(e.Badge.Tier));

            // Determine achievement level
            var level = GetLevel(score);

            return new AchievementProfile
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                EarnedBadges = earned.OrderByDescending(e => e.EarnedDate).ToList(),
                LockedBadges = locked.OrderBy(l => l.Remaining).ToList(),
                TotalScore = score,
                Level = level.Name,
                LevelNumber = level.Number,
                NextLevelAt = level.NextThreshold,
                BadgesEarned = earned.Count,
                BadgesTotal = AllBadges.Count,
            };
        }

        /// <summary>
        /// Gets a leaderboard of top customers by achievement score.
        /// </summary>
        public List<AchievementLeaderboardEntry> GetLeaderboard(int top = 10)
        {
            if (top < 1)
                throw new ArgumentOutOfRangeException(nameof(top));

            var customers = _customerRepository.GetAll();
            var allRentals = _rentalRepository.GetAll();
            var allMovies = _movieRepository.GetAll();
            var movieLookup = allMovies.ToDictionary(m => m.Id);

            var entries = new List<AchievementLeaderboardEntry>();

            foreach (var customer in customers)
            {
                var rentals = allRentals.Where(r => r.CustomerId == customer.Id).ToList();
                var reviews = _reviewRepository?.GetByCustomer(customer.Id)
                    ?? new List<Review>();

                int score = 0;
                int badgeCount = 0;

                foreach (var badge in AllBadges)
                {
                    var result = EvaluateBadge(badge, customer, rentals,
                        movieLookup, reviews);
                    if (result.IsEarned)
                    {
                        score += GetTierPoints(badge.Tier);
                        badgeCount++;
                    }
                }

                entries.Add(new AchievementLeaderboardEntry
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    Score = score,
                    BadgeCount = badgeCount,
                    Level = GetLevel(score).Name,
                });
            }

            return entries
                .OrderByDescending(e => e.Score)
                .ThenByDescending(e => e.BadgeCount)
                .Take(top)
                .ToList();
        }

        /// <summary>
        /// Gets aggregate stats across all customers.
        /// </summary>
        public AchievementStats GetStats()
        {
            var customers = _customerRepository.GetAll();
            var allRentals = _rentalRepository.GetAll();
            var allMovies = _movieRepository.GetAll();
            var movieLookup = allMovies.ToDictionary(m => m.Id);

            var badgeCounts = new Dictionary<string, int>();
            foreach (var badge in AllBadges)
                badgeCounts[badge.Id] = 0;

            int totalEarned = 0;

            foreach (var customer in customers)
            {
                var rentals = allRentals.Where(r => r.CustomerId == customer.Id).ToList();
                var reviews = _reviewRepository?.GetByCustomer(customer.Id)
                    ?? new List<Review>();

                foreach (var badge in AllBadges)
                {
                    var result = EvaluateBadge(badge, customer, rentals,
                        movieLookup, reviews);
                    if (result.IsEarned)
                    {
                        badgeCounts[badge.Id]++;
                        totalEarned++;
                    }
                }
            }

            var rarestBadge = AllBadges
                .OrderBy(b => badgeCounts[b.Id])
                .First();
            var mostCommonBadge = AllBadges
                .OrderByDescending(b => badgeCounts[b.Id])
                .First();

            return new AchievementStats
            {
                TotalCustomers = customers.Count,
                TotalBadgesAwarded = totalEarned,
                AverageBadgesPerCustomer = customers.Count > 0
                    ? Math.Round((double)totalEarned / customers.Count, 1) : 0,
                BadgeDistribution = AllBadges.Select(b => new BadgeDistributionEntry
                {
                    Badge = b,
                    EarnedCount = badgeCounts[b.Id],
                    EarnedPercent = customers.Count > 0
                        ? Math.Round(100.0 * badgeCounts[b.Id] / customers.Count, 1) : 0,
                }).OrderByDescending(e => e.EarnedCount).ToList(),
                RarestBadge = rarestBadge,
                MostCommonBadge = mostCommonBadge,
            };
        }

        // ── Badge evaluators ─────────────────────────────────────

        internal BadgeEvalResult EvaluateBadge(
            BadgeDefinition badge,
            Customer customer,
            List<Rental> rentals,
            Dictionary<int, Movie> movieLookup,
            IReadOnlyList<Review> reviews)
        {
            switch (badge.Category)
            {
                case BadgeCategory.Milestone:
                    return EvaluateMilestone(badge, rentals);
                case BadgeCategory.Exploration:
                    return EvaluateExploration(badge, rentals, movieLookup);
                case BadgeCategory.Behavior:
                    return EvaluateBehavior(badge, rentals);
                case BadgeCategory.Social:
                    return EvaluateSocial(badge, reviews);
                case BadgeCategory.Loyalty:
                    return EvaluateLoyalty(badge, customer);
                case BadgeCategory.Taste:
                    return EvaluateTaste(badge, rentals, movieLookup);
                default:
                    return new BadgeEvalResult { IsEarned = false, Progress = 0, Remaining = badge.RequiredCount, Hint = "Unknown badge category" };
            }
        }

        private BadgeEvalResult EvaluateMilestone(BadgeDefinition badge, List<Rental> rentals)
        {
            int count = rentals.Count;
            bool earned = count >= badge.RequiredCount;
            DateTime? earnedDate = null;

            if (earned)
            {
                var sorted = rentals.OrderBy(r => r.RentalDate).ToList();
                if (badge.RequiredCount <= sorted.Count)
                    earnedDate = sorted[badge.RequiredCount - 1].RentalDate;
            }

            return new BadgeEvalResult
            {
                IsEarned = earned,
                EarnedDate = earnedDate,
                Progress = Math.Min(100.0, 100.0 * count / badge.RequiredCount),
                Remaining = Math.Max(0, badge.RequiredCount - count),
                Hint = earned ? null : $"Rent {badge.RequiredCount - count} more movies"
            };
        }

        private BadgeEvalResult EvaluateExploration(
            BadgeDefinition badge, List<Rental> rentals,
            Dictionary<int, Movie> movieLookup)
        {
            var genres = new HashSet<Genre>();
            foreach (var r in rentals)
            {
                Movie movie;
                if (movieLookup.TryGetValue(r.MovieId, out movie) && movie.Genre.HasValue)
                    genres.Add(movie.Genre.Value);
            }

            int genreCount = genres.Count;
            int totalGenres = Enum.GetValues(typeof(Genre)).Length;
            int required = badge.RequiredCount == -1 ? totalGenres : badge.RequiredCount;

            bool earned = genreCount >= required;

            return new BadgeEvalResult
            {
                IsEarned = earned,
                EarnedDate = earned ? rentals.Max(r => r.RentalDate) : (DateTime?)null,
                Progress = required > 0 ? Math.Min(100.0, 100.0 * genreCount / required) : 0,
                Remaining = Math.Max(0, required - genreCount),
                Hint = earned ? null : $"Try {required - genreCount} more genres"
            };
        }

        private BadgeEvalResult EvaluateBehavior(BadgeDefinition badge, List<Rental> rentals)
        {
            switch (badge.Id)
            {
                case "binge_watcher":
                    return EvaluateBingeWatcher(rentals, badge.RequiredCount);
                case "on_time_streak_5":
                case "on_time_streak_15":
                    return EvaluateOnTimeStreak(rentals, badge.RequiredCount);
                case "early_bird":
                    return EvaluateEarlyBird(rentals);
                default:
                    return new BadgeEvalResult { IsEarned = false };
            }
        }

        private BadgeEvalResult EvaluateBingeWatcher(List<Rental> rentals, int required)
        {
            if (rentals.Count < required)
                return new BadgeEvalResult
                {
                    IsEarned = false,
                    Progress = 100.0 * rentals.Count / required,
                    Remaining = required - rentals.Count,
                    Hint = $"Rent {required}+ movies in one week"
                };

            // Check each 7-day window
            var sorted = rentals.OrderBy(r => r.RentalDate).ToList();
            DateTime? bingeDate = null;
            int maxInWeek = 0;

            for (int i = 0; i < sorted.Count; i++)
            {
                var windowEnd = sorted[i].RentalDate.AddDays(7);
                int count = sorted.Count(r =>
                    r.RentalDate >= sorted[i].RentalDate && r.RentalDate < windowEnd);
                if (count > maxInWeek)
                    maxInWeek = count;
                if (count >= required && bingeDate == null)
                    bingeDate = windowEnd;
            }

            bool earned = maxInWeek >= required;
            return new BadgeEvalResult
            {
                IsEarned = earned,
                EarnedDate = bingeDate,
                Progress = Math.Min(100.0, 100.0 * maxInWeek / required),
                Remaining = Math.Max(0, required - maxInWeek),
                Hint = earned ? null : $"Best so far: {maxInWeek} in a week (need {required})"
            };
        }

        private BadgeEvalResult EvaluateOnTimeStreak(List<Rental> rentals, int required)
        {
            var returned = rentals
                .Where(r => r.ReturnDate.HasValue)
                .OrderBy(r => r.ReturnDate)
                .ToList();

            int currentStreak = 0;
            int maxStreak = 0;
            DateTime? streakDate = null;

            foreach (var r in returned)
            {
                bool onTime = r.ReturnDate.Value.Date <= r.DueDate.Date;
                if (onTime)
                {
                    currentStreak++;
                    if (currentStreak > maxStreak)
                    {
                        maxStreak = currentStreak;
                        if (maxStreak == required)
                            streakDate = r.ReturnDate;
                    }
                }
                else
                {
                    currentStreak = 0;
                }
            }

            bool earned = maxStreak >= required;
            return new BadgeEvalResult
            {
                IsEarned = earned,
                EarnedDate = streakDate,
                Progress = Math.Min(100.0, 100.0 * maxStreak / required),
                Remaining = Math.Max(0, required - maxStreak),
                Hint = earned ? null : $"Current best streak: {maxStreak} on-time returns"
            };
        }

        private BadgeEvalResult EvaluateEarlyBird(List<Rental> rentals)
        {
            var earlyReturn = rentals
                .Where(r => r.ReturnDate.HasValue &&
                       (r.DueDate.Date - r.ReturnDate.Value.Date).Days >= 3)
                .OrderBy(r => r.ReturnDate)
                .FirstOrDefault();

            return new BadgeEvalResult
            {
                IsEarned = earlyReturn != null,
                EarnedDate = earlyReturn?.ReturnDate,
                Progress = earlyReturn != null ? 100.0 : 0,
                Remaining = earlyReturn != null ? 0 : 1,
                Hint = earlyReturn != null ? null : "Return a movie 3+ days early"
            };
        }

        private BadgeEvalResult EvaluateSocial(BadgeDefinition badge,
            IReadOnlyList<Review> reviews)
        {
            int count = reviews?.Count ?? 0;
            bool earned = count >= badge.RequiredCount;

            return new BadgeEvalResult
            {
                IsEarned = earned,
                EarnedDate = earned && reviews != null
                    ? reviews.OrderBy(r => r.CreatedDate).Skip(badge.RequiredCount - 1).First().CreatedDate
                    : (DateTime?)null,
                Progress = badge.RequiredCount > 0
                    ? Math.Min(100.0, 100.0 * count / badge.RequiredCount) : 0,
                Remaining = Math.Max(0, badge.RequiredCount - count),
                Hint = earned ? null : $"Write {badge.RequiredCount - count} more reviews"
            };
        }

        private BadgeEvalResult EvaluateLoyalty(BadgeDefinition badge, Customer customer)
        {
            if (!customer.MemberSince.HasValue)
                return new BadgeEvalResult
                {
                    IsEarned = false,
                    Progress = 0,
                    Remaining = badge.RequiredCount,
                    Hint = "Membership start date not set"
                };

            var days = (_clock.Today - customer.MemberSince.Value).TotalDays;
            bool earned = days >= badge.RequiredCount;

            return new BadgeEvalResult
            {
                IsEarned = earned,
                EarnedDate = earned
                    ? customer.MemberSince.Value.AddDays(badge.RequiredCount)
                    : (DateTime?)null,
                Progress = Math.Min(100.0, 100.0 * days / badge.RequiredCount),
                Remaining = earned ? 0 : (int)(badge.RequiredCount - days),
                Hint = earned ? null : $"{(int)(badge.RequiredCount - days)} days to go"
            };
        }

        private BadgeEvalResult EvaluateTaste(
            BadgeDefinition badge, List<Rental> rentals,
            Dictionary<int, Movie> movieLookup)
        {
            int count;
            switch (badge.Id)
            {
                case "five_star_fan":
                    count = rentals.Count(r =>
                    {
                        Movie m;
                        return movieLookup.TryGetValue(r.MovieId, out m)
                            && m.Rating.HasValue && m.Rating.Value == 5;
                    });
                    break;
                case "hidden_gem_hunter":
                    count = rentals.Count(r =>
                    {
                        Movie m;
                        return movieLookup.TryGetValue(r.MovieId, out m)
                            && m.Rating.HasValue && m.Rating.Value <= 2;
                    });
                    break;
                default:
                    count = 0;
                    break;
            }

            bool earned = count >= badge.RequiredCount;
            return new BadgeEvalResult
            {
                IsEarned = earned,
                EarnedDate = earned ? rentals.Max(r => r.RentalDate) : (DateTime?)null,
                Progress = badge.RequiredCount > 0
                    ? Math.Min(100.0, 100.0 * count / badge.RequiredCount) : 0,
                Remaining = Math.Max(0, badge.RequiredCount - count),
                Hint = earned ? null : $"Rent {badge.RequiredCount - count} more qualifying movies"
            };
        }

        // ── Helpers ──────────────────────────────────────────────

        internal static int GetTierPoints(BadgeTier tier)
        {
            switch (tier)
            {
                case BadgeTier.Bronze: return 10;
                case BadgeTier.Silver: return 25;
                case BadgeTier.Gold: return 50;
                case BadgeTier.Platinum: return 100;
                default: return 0;
            }
        }

        internal static AchievementLevel GetLevel(int score)
        {
            if (score >= 500) return new AchievementLevel { Number = 5, Name = "Hall of Fame", NextThreshold = 0 };
            if (score >= 300) return new AchievementLevel { Number = 4, Name = "Cinephile", NextThreshold = 500 };
            if (score >= 150) return new AchievementLevel { Number = 3, Name = "Movie Maven", NextThreshold = 300 };
            if (score >= 50) return new AchievementLevel { Number = 2, Name = "Film Fan", NextThreshold = 150 };
            return new AchievementLevel { Number = 1, Name = "Newcomer", NextThreshold = 50 };
        }
    }

    // ── Models ───────────────────────────────────────────────────

    public enum BadgeCategory
    {
        Milestone,
        Exploration,
        Behavior,
        Social,
        Loyalty,
        Taste
    }

    public enum BadgeTier
    {
        Bronze,
        Silver,
        Gold,
        Platinum
    }

    public class BadgeDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Emoji { get; set; }
        public BadgeCategory Category { get; set; }
        public BadgeTier Tier { get; set; }
        public int RequiredCount { get; set; }
    }

    public class EarnedBadge
    {
        public BadgeDefinition Badge { get; set; }
        public DateTime? EarnedDate { get; set; }
    }

    public class LockedBadge
    {
        public BadgeDefinition Badge { get; set; }
        public double Progress { get; set; }
        public int Remaining { get; set; }
        public string Hint { get; set; }
    }

    public class AchievementProfile
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public List<EarnedBadge> EarnedBadges { get; set; } = new List<EarnedBadge>();
        public List<LockedBadge> LockedBadges { get; set; } = new List<LockedBadge>();
        public int TotalScore { get; set; }
        public string Level { get; set; }
        public int LevelNumber { get; set; }
        public int NextLevelAt { get; set; }
        public int BadgesEarned { get; set; }
        public int BadgesTotal { get; set; }
    }

    public class AchievementStats
    {
        public int TotalCustomers { get; set; }
        public int TotalBadgesAwarded { get; set; }
        public double AverageBadgesPerCustomer { get; set; }
        public List<BadgeDistributionEntry> BadgeDistribution { get; set; }
        public BadgeDefinition RarestBadge { get; set; }
        public BadgeDefinition MostCommonBadge { get; set; }
    }

    public class BadgeDistributionEntry
    {
        public BadgeDefinition Badge { get; set; }
        public int EarnedCount { get; set; }
        public double EarnedPercent { get; set; }
    }

    public class AchievementLeaderboardEntry
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int Score { get; set; }
        public int BadgeCount { get; set; }
        public string Level { get; set; }
    }

    internal class BadgeEvalResult
    {
        public bool IsEarned { get; set; }
        public DateTime? EarnedDate { get; set; }
        public double Progress { get; set; }
        public int Remaining { get; set; }
        public string Hint { get; set; }
    }

    internal class AchievementLevel
    {
        public int Number { get; set; }
        public string Name { get; set; }
        public int NextThreshold { get; set; }
    }

}
