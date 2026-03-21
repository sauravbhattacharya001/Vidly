using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// MPAA-style content rating for movies.
    /// </summary>
    public enum ContentRating
    {
        [Display(Name = "G — General Audiences")]
        G = 1,

        [Display(Name = "PG — Parental Guidance")]
        PG = 2,

        [Display(Name = "PG-13 — Parents Strongly Cautioned")]
        PG13 = 3,

        [Display(Name = "R — Restricted")]
        R = 4,

        [Display(Name = "NC-17 — Adults Only")]
        NC17 = 5
    }

    /// <summary>
    /// A family profile with age-based rental restrictions.
    /// </summary>
    public class FamilyProfile
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Name { get; set; }

        /// <summary>
        /// Maximum content rating this profile is allowed to rent.
        /// Movies above this rating are blocked.
        /// </summary>
        [Display(Name = "Max Allowed Rating")]
        public ContentRating MaxRating { get; set; }

        /// <summary>
        /// 4-digit PIN required to switch to or modify this profile.
        /// Null means no PIN protection (typically for child profiles managed by parents).
        /// </summary>
        [StringLength(4, MinimumLength = 4)]
        [RegularExpression(@"^\d{4}$", ErrorMessage = "PIN must be exactly 4 digits.")]
        public string Pin { get; set; }

        /// <summary>
        /// Whether this is the account owner / parent profile.
        /// </summary>
        [Display(Name = "Parent Profile")]
        public bool IsParent { get; set; }

        /// <summary>
        /// Optional avatar icon name (Bootstrap glyphicon suffix).
        /// </summary>
        public string AvatarIcon { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Optional linked customer ID.
        /// </summary>
        public int? CustomerId { get; set; }

        /// <summary>
        /// Daily rental hour window start (e.g., 8 = 8 AM). Null = no time restriction.
        /// </summary>
        [Range(0, 23)]
        [Display(Name = "Allowed From (Hour)")]
        public int? AllowedFromHour { get; set; }

        /// <summary>
        /// Daily rental hour window end (e.g., 21 = 9 PM). Null = no time restriction.
        /// </summary>
        [Range(0, 23)]
        [Display(Name = "Allowed Until (Hour)")]
        public int? AllowedUntilHour { get; set; }

        /// <summary>
        /// Maximum rentals allowed per week. Null = unlimited.
        /// </summary>
        [Range(1, 50)]
        [Display(Name = "Weekly Rental Limit")]
        public int? WeeklyRentalLimit { get; set; }

        /// <summary>
        /// Blocked genre list (comma-separated genre names).
        /// </summary>
        [Display(Name = "Blocked Genres")]
        public string BlockedGenres { get; set; }

        /// <summary>
        /// Check if a movie's content rating is allowed under this profile.
        /// </summary>
        public bool IsRatingAllowed(ContentRating movieRating)
        {
            return movieRating <= MaxRating;
        }

        /// <summary>
        /// Check if the current time falls within the allowed rental window.
        /// </summary>
        public bool IsWithinAllowedHours()
        {
            if (!AllowedFromHour.HasValue || !AllowedUntilHour.HasValue)
                return true;

            int currentHour = DateTime.Now.Hour;
            if (AllowedFromHour.Value <= AllowedUntilHour.Value)
                return currentHour >= AllowedFromHour.Value && currentHour < AllowedUntilHour.Value;
            // Wraps midnight (e.g., 22 to 6)
            return currentHour >= AllowedFromHour.Value || currentHour < AllowedUntilHour.Value;
        }

        /// <summary>
        /// Check if a genre name is blocked.
        /// </summary>
        public bool IsGenreBlocked(string genreName)
        {
            if (string.IsNullOrWhiteSpace(BlockedGenres) || string.IsNullOrWhiteSpace(genreName))
                return false;

            var blocked = BlockedGenres.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var g in blocked)
            {
                if (g.Trim().Equals(genreName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Log entry for parental control actions (profile switches, blocked attempts, setting changes).
    /// </summary>
    public class ParentalControlLog
    {
        public int Id { get; set; }
        public int ProfileId { get; set; }
        public string ProfileName { get; set; }
        public string Action { get; set; } // "Switch", "Blocked", "Created", "Updated", "Deleted"
        public string Details { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
