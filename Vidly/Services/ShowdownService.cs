using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Service for the Movie Showdown feature — generates random matchups
    /// and tracks voting scores in memory.
    /// </summary>
    public class ShowdownService
    {
        private readonly IMovieRepository _movieRepo;
        private static readonly Random _random = new Random();
        private static readonly object _lock = new object();

        // In-memory vote tracking (static so it persists across requests)
        private static readonly Dictionary<int, ShowdownScore> _scores
            = new Dictionary<int, ShowdownScore>();

        private static int _totalRounds;

        public ShowdownService(IMovieRepository movieRepo)
        {
            _movieRepo = movieRepo ?? throw new ArgumentNullException(nameof(movieRepo));
        }

        /// <summary>
        /// Generates a random matchup between two different movies.
        /// </summary>
        public ShowdownMatchup GenerateMatchup()
        {
            var movies = _movieRepo.GetAll().ToList();
            if (movies.Count < 2)
                return null;

            int indexA = _random.Next(movies.Count);
            int indexB;
            do
            {
                indexB = _random.Next(movies.Count);
            } while (indexB == indexA);

            lock (_lock)
            {
                _totalRounds++;
                return new ShowdownMatchup
                {
                    MovieA = movies[indexA],
                    MovieB = movies[indexB],
                    RoundNumber = _totalRounds
                };
            }
        }

        /// <summary>
        /// Records a vote: winner wins, loser loses.
        /// </summary>
        public void RecordVote(int winnerId, int loserId)
        {
            lock (_lock)
            {
                EnsureScore(winnerId);
                EnsureScore(loserId);
                _scores[winnerId].Wins++;
                _scores[loserId].Losses++;
            }
        }

        /// <summary>
        /// Gets the current leaderboard sorted by win rate then wins.
        /// </summary>
        public List<ShowdownScore> GetLeaderboard()
        {
            lock (_lock)
            {
                return _scores.Values
                    .Where(s => s.Wins + s.Losses > 0)
                    .OrderByDescending(s => s.WinRate)
                    .ThenByDescending(s => s.Wins)
                    .ToList();
            }
        }

        public int GetTotalRounds()
        {
            lock (_lock) { return _totalRounds; }
        }

        private void EnsureScore(int movieId)
        {
            if (!_scores.ContainsKey(movieId))
            {
                var movie = _movieRepo.GetById(movieId);
                _scores[movieId] = new ShowdownScore
                {
                    MovieId = movieId,
                    MovieName = movie?.Name ?? $"Movie #{movieId}"
                };
            }
        }
    }
}
