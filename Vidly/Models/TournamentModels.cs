using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// A single-elimination movie tournament where movies compete head-to-head
    /// through bracket rounds until a champion is crowned.
    /// </summary>
    public class Tournament
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int CreatedByCustomerId { get; set; }
        public string CreatedByCustomerName { get; set; }
        public DateTime CreatedAt { get; set; }
        public TournamentStatus Status { get; set; }
        public int Size { get; set; } // 4, 8, or 16
        public int TotalRounds { get; set; }
        public int CurrentRound { get; set; }
        public List<TournamentSeed> Seeds { get; set; } = new List<TournamentSeed>();
        public List<TournamentMatch> Matches { get; set; } = new List<TournamentMatch>();
        public int? ChampionMovieId { get; set; }
        public string ChampionMovieName { get; set; }
        public Genre? GenreFilter { get; set; }
    }

    public enum TournamentStatus
    {
        Setup,
        InProgress,
        Completed,
        Cancelled
    }

    /// <summary>
    /// A seeded entry in the tournament bracket.
    /// </summary>
    public class TournamentSeed
    {
        public int SeedNumber { get; set; }
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? Genre { get; set; }
        public int? Rating { get; set; }
        public bool Eliminated { get; set; }
        public int Wins { get; set; }
    }

    /// <summary>
    /// A single match between two movies in a tournament round.
    /// </summary>
    public class TournamentMatch
    {
        public int Id { get; set; }
        public int Round { get; set; }
        public int MatchNumber { get; set; } // position within the round
        public int Movie1Id { get; set; }
        public string Movie1Name { get; set; }
        public int Movie1Seed { get; set; }
        public int Movie2Id { get; set; }
        public string Movie2Name { get; set; }
        public int Movie2Seed { get; set; }
        public int? WinnerMovieId { get; set; }
        public string WinnerMovieName { get; set; }
        public string VoteReason { get; set; }
        public DateTime? VotedAt { get; set; }
        public bool IsComplete => WinnerMovieId.HasValue;

        /// <summary>Display label for the round (Finals, Semis, etc.)</summary>
        public string RoundLabel { get; set; }
    }

    /// <summary>
    /// Tournament history entry for the hall of fame.
    /// </summary>
    public class TournamentResult
    {
        public int TournamentId { get; set; }
        public string TournamentName { get; set; }
        public int Size { get; set; }
        public string ChampionName { get; set; }
        public int ChampionMovieId { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CompletedAt { get; set; }
        public int TotalMatches { get; set; }
    }

    /// <summary>
    /// Tracks how many times a movie has won tournaments.
    /// </summary>
    public class MovieTournamentRecord
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public int TournamentsEntered { get; set; }
        public int TournamentsWon { get; set; }
        public int MatchesWon { get; set; }
        public int MatchesLost { get; set; }
        public double WinRate { get; set; }
    }
}
