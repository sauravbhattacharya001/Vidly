using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages single-elimination movie tournaments with bracket seeding,
    /// round-by-round voting, and hall-of-fame tracking.
    /// </summary>
    public class MovieTournamentService
    {
        private readonly List<Movie> _movies;
        private readonly List<Tournament> _tournaments = new List<Tournament>();
        private readonly IClock _clock;
        private int _nextTournamentId = 1;
        private int _nextMatchId = 1;
        private readonly Random _rng;

        /// <summary>Valid bracket sizes.</summary>
        public static readonly int[] ValidSizes = { 4, 8, 16 };

        public MovieTournamentService(IEnumerable<Movie> movies, IClock clock = null, int? seed = null)
        {
            _movies = (movies ?? throw new ArgumentNullException(nameof(movies))).ToList();
            _clock = clock ?? new SystemClock();
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        // ══════════════════════════════════════════════════════
        //  Tournament Creation
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Create a new tournament. Optionally filter movies by genre.
        /// If movieIds is null/empty, movies are randomly selected.
        /// </summary>
        public Tournament CreateTournament(
            string name,
            int createdByCustomerId,
            string createdByCustomerName,
            int size = 8,
            Genre? genreFilter = null,
            List<int> movieIds = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Tournament name is required.", nameof(name));
            if (!ValidSizes.Contains(size))
                throw new ArgumentException($"Size must be one of: {string.Join(", ", ValidSizes)}.", nameof(size));

            var candidates = _movies.AsEnumerable();
            if (genreFilter.HasValue)
                candidates = candidates.Where(m => m.Genre == genreFilter.Value);

            List<Movie> selected;
            if (movieIds != null && movieIds.Count > 0)
            {
                if (movieIds.Count != size)
                    throw new ArgumentException($"Must provide exactly {size} movie IDs.", nameof(movieIds));
                if (movieIds.Distinct().Count() != movieIds.Count)
                    throw new ArgumentException("Duplicate movie IDs are not allowed.", nameof(movieIds));

                selected = new List<Movie>();
                foreach (var id in movieIds)
                {
                    var movie = _movies.FirstOrDefault(m => m.Id == id);
                    if (movie == null)
                        throw new ArgumentException($"Movie {id} not found.", nameof(movieIds));
                    selected.Add(movie);
                }
            }
            else
            {
                var pool = candidates.ToList();
                if (pool.Count < size)
                    throw new InvalidOperationException(
                        $"Not enough movies ({pool.Count}) for a {size}-movie bracket." +
                        (genreFilter.HasValue ? $" Genre filter: {genreFilter.Value}." : ""));

                selected = pool.OrderBy(_ => _rng.Next()).Take(size).ToList();
            }

            // Seed by rating (higher = better seed), then alphabetically
            var seeded = selected
                .OrderByDescending(m => m.Rating ?? 0)
                .ThenBy(m => m.Name)
                .ToList();

            var tournament = new Tournament
            {
                Id = _nextTournamentId++,
                Name = name.Trim(),
                CreatedByCustomerId = createdByCustomerId,
                CreatedByCustomerName = createdByCustomerName,
                CreatedAt = _clock.Now,
                Status = TournamentStatus.InProgress,
                Size = size,
                TotalRounds = (int)Math.Log(size, 2),
                CurrentRound = 1,
                GenreFilter = genreFilter
            };

            // Create seeds
            for (int i = 0; i < seeded.Count; i++)
            {
                tournament.Seeds.Add(new TournamentSeed
                {
                    SeedNumber = i + 1,
                    MovieId = seeded[i].Id,
                    MovieName = seeded[i].Name,
                    Genre = seeded[i].Genre,
                    Rating = seeded[i].Rating
                });
            }

            // Generate first round matches (classic bracket seeding: 1v8, 2v7, etc.)
            GenerateRoundMatches(tournament, 1);

            _tournaments.Add(tournament);
            return tournament;
        }

        // ══════════════════════════════════════════════════════
        //  Voting
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Vote for a winner in a match.
        /// </summary>
        public TournamentMatch Vote(int tournamentId, int matchId, int winnerMovieId, string reason = null)
        {
            var tournament = GetTournament(tournamentId);
            if (tournament == null)
                throw new ArgumentException($"Tournament {tournamentId} not found.");
            if (tournament.Status != TournamentStatus.InProgress)
                throw new InvalidOperationException("Tournament is not in progress.");

            var match = tournament.Matches.FirstOrDefault(m => m.Id == matchId);
            if (match == null)
                throw new ArgumentException($"Match {matchId} not found.");
            if (match.IsComplete)
                throw new InvalidOperationException("This match has already been decided.");
            if (match.Round != tournament.CurrentRound)
                throw new InvalidOperationException("This match is not in the current round.");
            if (winnerMovieId != match.Movie1Id && winnerMovieId != match.Movie2Id)
                throw new ArgumentException("Winner must be one of the two competing movies.");

            // Record vote
            match.WinnerMovieId = winnerMovieId;
            match.WinnerMovieName = winnerMovieId == match.Movie1Id ? match.Movie1Name : match.Movie2Name;
            match.VoteReason = reason?.Trim();
            match.VotedAt = _clock.Now;

            // Mark loser as eliminated
            var loserId = winnerMovieId == match.Movie1Id ? match.Movie2Id : match.Movie1Id;
            var loserSeed = tournament.Seeds.FirstOrDefault(s => s.MovieId == loserId);
            if (loserSeed != null) loserSeed.Eliminated = true;

            var winnerSeed = tournament.Seeds.FirstOrDefault(s => s.MovieId == winnerMovieId);
            if (winnerSeed != null) winnerSeed.Wins++;

            // Check if round is complete
            var roundMatches = tournament.Matches.Where(m => m.Round == tournament.CurrentRound).ToList();
            if (roundMatches.All(m => m.IsComplete))
            {
                if (tournament.CurrentRound >= tournament.TotalRounds)
                {
                    // Tournament over!
                    tournament.Status = TournamentStatus.Completed;
                    tournament.ChampionMovieId = winnerMovieId;
                    tournament.ChampionMovieName = match.WinnerMovieName;
                }
                else
                {
                    // Advance to next round
                    tournament.CurrentRound++;
                    GenerateRoundMatches(tournament, tournament.CurrentRound);
                }
            }

            return match;
        }

        // ══════════════════════════════════════════════════════
        //  Queries
        // ══════════════════════════════════════════════════════

        public Tournament GetTournament(int id)
        {
            return _tournaments.FirstOrDefault(t => t.Id == id);
        }

        public IReadOnlyList<Tournament> ListTournaments(TournamentStatus? status = null)
        {
            var query = _tournaments.AsEnumerable();
            if (status.HasValue)
                query = query.Where(t => t.Status == status.Value);
            return query.OrderByDescending(t => t.CreatedAt).ToList();
        }

        /// <summary>
        /// Get all matches for a specific round.
        /// </summary>
        public IReadOnlyList<TournamentMatch> GetRoundMatches(int tournamentId, int round)
        {
            var tournament = GetTournament(tournamentId);
            if (tournament == null) return new List<TournamentMatch>();
            return tournament.Matches.Where(m => m.Round == round).OrderBy(m => m.MatchNumber).ToList();
        }

        /// <summary>
        /// Get the current pending (unvoted) matches.
        /// </summary>
        public IReadOnlyList<TournamentMatch> GetPendingMatches(int tournamentId)
        {
            var tournament = GetTournament(tournamentId);
            if (tournament == null) return new List<TournamentMatch>();
            return tournament.Matches
                .Where(m => m.Round == tournament.CurrentRound && !m.IsComplete)
                .OrderBy(m => m.MatchNumber)
                .ToList();
        }

        /// <summary>
        /// Get hall of fame — all completed tournaments with champions.
        /// </summary>
        public IReadOnlyList<TournamentResult> GetHallOfFame()
        {
            return _tournaments
                .Where(t => t.Status == TournamentStatus.Completed)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new TournamentResult
                {
                    TournamentId = t.Id,
                    TournamentName = t.Name,
                    Size = t.Size,
                    ChampionName = t.ChampionMovieName,
                    ChampionMovieId = t.ChampionMovieId ?? 0,
                    CreatedBy = t.CreatedByCustomerName,
                    CompletedAt = t.Matches.Where(m => m.IsComplete).Max(m => m.VotedAt ?? t.CreatedAt),
                    TotalMatches = t.Matches.Count
                })
                .ToList();
        }

        /// <summary>
        /// Get aggregate win/loss records for all movies across all tournaments.
        /// </summary>
        public IReadOnlyList<MovieTournamentRecord> GetMovieRecords()
        {
            var records = new Dictionary<int, MovieTournamentRecord>();

            foreach (var t in _tournaments.Where(t => t.Status == TournamentStatus.Completed))
            {
                foreach (var seed in t.Seeds)
                {
                    if (!records.ContainsKey(seed.MovieId))
                    {
                        records[seed.MovieId] = new MovieTournamentRecord
                        {
                            MovieId = seed.MovieId,
                            MovieName = seed.MovieName
                        };
                    }
                    var rec = records[seed.MovieId];
                    rec.TournamentsEntered++;
                    if (t.ChampionMovieId == seed.MovieId)
                        rec.TournamentsWon++;
                }

                foreach (var match in t.Matches.Where(m => m.IsComplete))
                {
                    if (records.ContainsKey(match.WinnerMovieId ?? 0))
                        records[match.WinnerMovieId.Value].MatchesWon++;

                    var loserId = match.WinnerMovieId == match.Movie1Id ? match.Movie2Id : match.Movie1Id;
                    if (records.ContainsKey(loserId))
                        records[loserId].MatchesLost++;
                }
            }

            foreach (var rec in records.Values)
            {
                var total = rec.MatchesWon + rec.MatchesLost;
                rec.WinRate = total > 0 ? Math.Round((double)rec.MatchesWon / total * 100, 1) : 0;
            }

            return records.Values.OrderByDescending(r => r.TournamentsWon)
                .ThenByDescending(r => r.WinRate).ToList();
        }

        /// <summary>
        /// Cancel a tournament.
        /// </summary>
        public bool CancelTournament(int tournamentId)
        {
            var tournament = GetTournament(tournamentId);
            if (tournament == null || tournament.Status == TournamentStatus.Completed)
                return false;
            tournament.Status = TournamentStatus.Cancelled;
            return true;
        }

        // ══════════════════════════════════════════════════════
        //  Private Helpers
        // ══════════════════════════════════════════════════════

        private void GenerateRoundMatches(Tournament tournament, int round)
        {
            List<TournamentSeed> competitors;

            if (round == 1)
            {
                // Classic bracket seeding: 1v8, 4v5, 2v7, 3v6 (for 8-bracket)
                var seeds = tournament.Seeds.ToList();
                competitors = ArrangeBracketSeeding(seeds);
            }
            else
            {
                // Winners from previous round, in match order
                var prevMatches = tournament.Matches
                    .Where(m => m.Round == round - 1)
                    .OrderBy(m => m.MatchNumber)
                    .ToList();

                competitors = new List<TournamentSeed>();
                foreach (var pm in prevMatches)
                {
                    var winnerSeed = tournament.Seeds.FirstOrDefault(s => s.MovieId == pm.WinnerMovieId);
                    if (winnerSeed != null) competitors.Add(winnerSeed);
                }
            }

            var roundLabel = GetRoundLabel(round, tournament.TotalRounds);
            var matchesInRound = competitors.Count / 2;

            for (int i = 0; i < matchesInRound; i++)
            {
                var m1 = competitors[i * 2];
                var m2 = competitors[i * 2 + 1];

                tournament.Matches.Add(new TournamentMatch
                {
                    Id = _nextMatchId++,
                    Round = round,
                    MatchNumber = i + 1,
                    Movie1Id = m1.MovieId,
                    Movie1Name = m1.MovieName,
                    Movie1Seed = m1.SeedNumber,
                    Movie2Id = m2.MovieId,
                    Movie2Name = m2.MovieName,
                    Movie2Seed = m2.SeedNumber,
                    RoundLabel = roundLabel
                });
            }
        }

        /// <summary>
        /// Arrange seeds in classic bracket order so top seeds don't meet early.
        /// For 8: [1,8,4,5,2,7,3,6] → matches: 1v8, 4v5, 2v7, 3v6
        /// </summary>
        private List<TournamentSeed> ArrangeBracketSeeding(List<TournamentSeed> seeds)
        {
            int n = seeds.Count;
            if (n <= 2) return seeds;

            // Build bracket positions recursively
            var positions = new List<int> { 0 };
            while (positions.Count < n)
            {
                var next = new List<int>();
                int count = positions.Count;
                for (int i = 0; i < count; i++)
                {
                    next.Add(positions[i]);
                    next.Add(2 * count - 1 - positions[i]);
                }
                positions = next;
            }

            return positions.Select(p => seeds[p]).ToList();
        }

        private static string GetRoundLabel(int round, int totalRounds)
        {
            int remaining = totalRounds - round;
            if (remaining == 0) return "Finals";
            if (remaining == 1) return "Semifinals";
            if (remaining == 2) return "Quarterfinals";
            return $"Round {round}";
        }
    }
}
