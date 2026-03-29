using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a rental challenge that customers can participate in.
    /// </summary>
    public class MovieChallenge
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Title { get; set; }

        [StringLength(500)]
        public string Description { get; set; }

        public string Icon { get; set; }

        public ChallengeDifficulty Difficulty { get; set; }

        public ChallengeType Type { get; set; }

        /// <summary>
        /// Number of rentals/actions required to complete the challenge.
        /// </summary>
        public int Target { get; set; }

        /// <summary>
        /// Loyalty points awarded on completion.
        /// </summary>
        public int RewardPoints { get; set; }

        /// <summary>
        /// Genre filter (null = any genre).
        /// </summary>
        public string RequiredGenre { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsActive => DateTime.Now >= StartDate && DateTime.Now <= EndDate;
        public bool IsExpired => DateTime.Now > EndDate;

        public List<ChallengeParticipant> Participants { get; set; } = new List<ChallengeParticipant>();
    }

    /// <summary>
    /// A customer's progress in a challenge.
    /// </summary>
    public class ChallengeParticipant
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int Progress { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime JoinedDate { get; set; }
        public DateTime? CompletedDate { get; set; }

        public double PercentComplete(int target)
        {
            if (target <= 0) return 100;
            return Math.Min(100.0, (double)Progress / target * 100);
        }
    }

    public enum ChallengeDifficulty
    {
        Easy,
        Medium,
        Hard,
        Epic
    }

    public enum ChallengeType
    {
        /// <summary>Rent N movies of a specific genre.</summary>
        GenreExplorer,
        /// <summary>Rent movies from N different genres.</summary>
        GenreVariety,
        /// <summary>Rent N movies total in the time period.</summary>
        RentalStreak,
        /// <summary>Rent movies from N different decades.</summary>
        DecadeHopper,
        /// <summary>Rent N movies directed by the same director.</summary>
        DirectorDeepDive
    }

    /// <summary>
    /// Summary of a challenge for display in lists.
    /// </summary>
    public class ChallengeSummary
    {
        public MovieChallenge Challenge { get; set; }
        public int TotalParticipants { get; set; }
        public int TotalCompleted { get; set; }
        public ChallengeParticipant CurrentUserProgress { get; set; }
    }
}
