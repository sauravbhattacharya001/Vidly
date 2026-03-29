using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Service for managing movie rental challenges.
    /// </summary>
    public class ChallengeService
    {
        private static readonly List<MovieChallenge> _challenges;
        private readonly ICustomerRepository _customerRepository;

        static ChallengeService()
        {
            var now = DateTime.Now;
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var weekStart = now.AddDays(-(int)now.DayOfWeek);
            var weekEnd = weekStart.AddDays(6);

            _challenges = new List<MovieChallenge>
            {
                new MovieChallenge
                {
                    Id = 1,
                    Title = "Sci-Fi Sprint",
                    Description = "Rent 3 science fiction movies this month.",
                    Icon = "🚀",
                    Difficulty = ChallengeDifficulty.Easy,
                    Type = ChallengeType.GenreExplorer,
                    Target = 3,
                    RewardPoints = 50,
                    RequiredGenre = "Sci-Fi",
                    StartDate = monthStart,
                    EndDate = monthEnd
                },
                new MovieChallenge
                {
                    Id = 2,
                    Title = "Genre Hopper",
                    Description = "Rent movies from 5 different genres this month.",
                    Icon = "🎭",
                    Difficulty = ChallengeDifficulty.Medium,
                    Type = ChallengeType.GenreVariety,
                    Target = 5,
                    RewardPoints = 100,
                    StartDate = monthStart,
                    EndDate = monthEnd
                },
                new MovieChallenge
                {
                    Id = 3,
                    Title = "Weekend Warrior",
                    Description = "Rent 5 movies this week.",
                    Icon = "⚔️",
                    Difficulty = ChallengeDifficulty.Medium,
                    Type = ChallengeType.RentalStreak,
                    Target = 5,
                    RewardPoints = 75,
                    StartDate = weekStart,
                    EndDate = weekEnd
                },
                new MovieChallenge
                {
                    Id = 4,
                    Title = "Time Traveler",
                    Description = "Rent movies from 4 different decades.",
                    Icon = "⏰",
                    Difficulty = ChallengeDifficulty.Hard,
                    Type = ChallengeType.DecadeHopper,
                    Target = 4,
                    RewardPoints = 150,
                    StartDate = monthStart,
                    EndDate = monthEnd
                },
                new MovieChallenge
                {
                    Id = 5,
                    Title = "Horror Month",
                    Description = "Rent 4 horror movies this month.",
                    Icon = "👻",
                    Difficulty = ChallengeDifficulty.Easy,
                    Type = ChallengeType.GenreExplorer,
                    Target = 4,
                    RewardPoints = 60,
                    RequiredGenre = "Horror",
                    StartDate = monthStart,
                    EndDate = monthEnd
                },
                new MovieChallenge
                {
                    Id = 6,
                    Title = "Director's Cut",
                    Description = "Rent 3 movies by the same director.",
                    Icon = "🎬",
                    Difficulty = ChallengeDifficulty.Hard,
                    Type = ChallengeType.DirectorDeepDive,
                    Target = 3,
                    RewardPoints = 120,
                    StartDate = monthStart,
                    EndDate = monthEnd
                },
                new MovieChallenge
                {
                    Id = 7,
                    Title = "Movie Marathon",
                    Description = "Rent 10 movies this month.",
                    Icon = "🏃",
                    Difficulty = ChallengeDifficulty.Epic,
                    Type = ChallengeType.RentalStreak,
                    Target = 10,
                    RewardPoints = 250,
                    StartDate = monthStart,
                    EndDate = monthEnd
                },
                new MovieChallenge
                {
                    Id = 8,
                    Title = "Comedy Night",
                    Description = "Rent 3 comedies this month.",
                    Icon = "😂",
                    Difficulty = ChallengeDifficulty.Easy,
                    Type = ChallengeType.GenreExplorer,
                    Target = 3,
                    RewardPoints = 50,
                    RequiredGenre = "Comedy",
                    StartDate = monthStart,
                    EndDate = monthEnd
                }
            };

            // Seed some participants
            var rng = new Random(42);
            string[] names = { "Alice", "Bob", "Charlie", "Diana", "Eve", "Frank", "Grace", "Hank" };
            foreach (var challenge in _challenges)
            {
                int numParticipants = rng.Next(2, 7);
                for (int i = 0; i < numParticipants; i++)
                {
                    int progress = rng.Next(0, challenge.Target + 2);
                    bool completed = progress >= challenge.Target;
                    challenge.Participants.Add(new ChallengeParticipant
                    {
                        CustomerId = i + 1,
                        CustomerName = names[i % names.Length],
                        Progress = Math.Min(progress, challenge.Target),
                        IsCompleted = completed,
                        JoinedDate = challenge.StartDate.AddDays(rng.Next(0, 5)),
                        CompletedDate = completed ? (DateTime?)challenge.StartDate.AddDays(rng.Next(5, 20)) : null
                    });
                }
            }
        }

        public ChallengeService(ICustomerRepository customerRepository = null)
        {
            _customerRepository = customerRepository;
        }

        public List<ChallengeSummary> GetActiveChallenges(int? customerId = null)
        {
            return _challenges
                .Where(c => c.IsActive)
                .Select(c => ToSummary(c, customerId))
                .ToList();
        }

        public List<ChallengeSummary> GetAllChallenges(int? customerId = null)
        {
            return _challenges
                .Select(c => ToSummary(c, customerId))
                .OrderByDescending(s => s.Challenge.IsActive)
                .ThenBy(s => s.Challenge.EndDate)
                .ToList();
        }

        public MovieChallenge GetChallenge(int id)
        {
            return _challenges.FirstOrDefault(c => c.Id == id);
        }

        public ChallengeParticipant JoinChallenge(int challengeId, int customerId, string customerName)
        {
            var challenge = GetChallenge(challengeId);
            if (challenge == null || !challenge.IsActive)
                return null;

            var existing = challenge.Participants.FirstOrDefault(p => p.CustomerId == customerId);
            if (existing != null)
                return existing;

            var participant = new ChallengeParticipant
            {
                CustomerId = customerId,
                CustomerName = customerName,
                Progress = 0,
                IsCompleted = false,
                JoinedDate = DateTime.Now
            };
            challenge.Participants.Add(participant);
            return participant;
        }

        public List<ChallengeSummary> GetChallengesByDifficulty(ChallengeDifficulty difficulty)
        {
            return _challenges
                .Where(c => c.Difficulty == difficulty && c.IsActive)
                .Select(c => ToSummary(c, null))
                .ToList();
        }

        public Dictionary<string, int> GetLeaderboard(int top = 10)
        {
            return _challenges
                .SelectMany(c => c.Participants.Where(p => p.IsCompleted))
                .GroupBy(p => p.CustomerName)
                .ToDictionary(g => g.Key, g => g.Count())
                .OrderByDescending(kv => kv.Value)
                .Take(top)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private ChallengeSummary ToSummary(MovieChallenge challenge, int? customerId)
        {
            return new ChallengeSummary
            {
                Challenge = challenge,
                TotalParticipants = challenge.Participants.Count,
                TotalCompleted = challenge.Participants.Count(p => p.IsCompleted),
                CurrentUserProgress = customerId.HasValue
                    ? challenge.Participants.FirstOrDefault(p => p.CustomerId == customerId.Value)
                    : null
            };
        }
    }
}
