using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>Status of a movie club.</summary>
    public enum ClubStatus
    {
        Active = 1,
        Paused = 2,
        Disbanded = 3
    }

    /// <summary>Role within a movie club.</summary>
    public enum ClubRole
    {
        Member = 1,
        Moderator = 2,
        Founder = 3
    }

    /// <summary>Status of a club poll.</summary>
    public enum PollStatus
    {
        Open = 1,
        Closed = 2,
        Cancelled = 3
    }

    /// <summary>A movie club that customers can form to watch and discuss films together.</summary>
    public class MovieClub
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Genre { get; set; }
        public int MaxMembers { get; set; }
        public decimal GroupDiscountPercent { get; set; }
        public ClubStatus Status { get; set; }
        public DateTime CreatedDate { get; set; }
        public int FounderId { get; set; }
    }

    /// <summary>Club membership record.</summary>
    public class ClubMembership
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public int CustomerId { get; set; }
        public ClubRole Role { get; set; }
        public DateTime JoinedDate { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>An item on the club's shared watchlist.</summary>
    public class ClubWatchlistItem
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public int MovieId { get; set; }
        public int AddedByCustomerId { get; set; }
        public DateTime AddedDate { get; set; }
        public bool IsWatched { get; set; }
        public DateTime? WatchedDate { get; set; }
        public double? AverageRating { get; set; }
    }

    /// <summary>A poll for club members to vote on the next movie.</summary>
    public class ClubPoll
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public string Title { get; set; }
        public List<ClubPollOption> Options { get; set; } = new List<ClubPollOption>();
        public PollStatus Status { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ClosedDate { get; set; }
        public int CreatedByCustomerId { get; set; }
    }

    /// <summary>A single option (movie) in a club poll.</summary>
    public class ClubPollOption
    {
        public int OptionId { get; set; }
        public int MovieId { get; set; }
        public string MovieTitle { get; set; }
        public List<int> VoterIds { get; set; } = new List<int>();
    }

    /// <summary>A scheduled club meeting/watch session.</summary>
    public class ClubMeeting
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public int? MovieId { get; set; }
        public string Title { get; set; }
        public DateTime ScheduledDate { get; set; }
        public string Location { get; set; }
        public List<int> AttendeeIds { get; set; } = new List<int>();
        public string Notes { get; set; }
    }

    /// <summary>Summary statistics for a club.</summary>
    public class ClubStats
    {
        public int ClubId { get; set; }
        public string ClubName { get; set; }
        public int MemberCount { get; set; }
        public int MoviesWatched { get; set; }
        public int WatchlistSize { get; set; }
        public int MeetingsHeld { get; set; }
        public int PollsCompleted { get; set; }
        public double AverageMovieRating { get; set; }
        public string MostActiveGenre { get; set; }
        public decimal TotalSavings { get; set; }
    }
}
