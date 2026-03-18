using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Priority level for waitlist entries.
    /// </summary>
    public enum WaitlistPriority
    {
        [Display(Name = "Normal")]
        Normal = 0,

        [Display(Name = "High (Loyalty Member)")]
        High = 1,

        [Display(Name = "Urgent (Pre-order)")]
        Urgent = 2
    }

    /// <summary>
    /// Status of a waitlist entry.
    /// </summary>
    public enum WaitlistStatus
    {
        [Display(Name = "Waiting")]
        Waiting = 0,

        [Display(Name = "Notified")]
        Notified = 1,

        [Display(Name = "Fulfilled")]
        Fulfilled = 2,

        [Display(Name = "Expired")]
        Expired = 3,

        [Display(Name = "Cancelled")]
        Cancelled = 4
    }

    /// <summary>
    /// Represents a customer waiting for a movie to become available.
    /// </summary>
    public class WaitlistEntry
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Customer")]
        public int CustomerId { get; set; }

        /// <summary>Resolved customer name.</summary>
        public string CustomerName { get; set; }

        [Required]
        [Display(Name = "Movie")]
        public int MovieId { get; set; }

        /// <summary>Resolved movie name.</summary>
        public string MovieName { get; set; }

        [Display(Name = "Joined")]
        [DataType(DataType.DateTime)]
        public DateTime JoinedAt { get; set; }

        [Display(Name = "Notified")]
        [DataType(DataType.DateTime)]
        public DateTime? NotifiedAt { get; set; }

        [Display(Name = "Expires")]
        [DataType(DataType.DateTime)]
        public DateTime? ExpiresAt { get; set; }

        [Display(Name = "Position")]
        public int Position { get; set; }

        [Display(Name = "Priority")]
        public WaitlistPriority Priority { get; set; }

        [Display(Name = "Status")]
        public WaitlistStatus Status { get; set; }

        /// <summary>Optional note from the customer.</summary>
        [StringLength(500)]
        public string Note { get; set; }

        /// <summary>
        /// How long the customer has been waiting (from join to now or fulfillment).
        /// </summary>
        public TimeSpan WaitDuration
        {
            get
            {
                var end = (Status == WaitlistStatus.Fulfilled || Status == WaitlistStatus.Cancelled)
                    ? (NotifiedAt ?? DateTime.Now)
                    : DateTime.Now;
                return end - JoinedAt;
            }
        }

        /// <summary>Whether the entry has expired past its notification window.</summary>
        public bool IsExpired => ExpiresAt.HasValue && DateTime.Now > ExpiresAt.Value
                                 && Status == WaitlistStatus.Notified;
    }

    /// <summary>
    /// Summary statistics for the waitlist system.
    /// </summary>
    public class WaitlistStats
    {
        public int TotalWaiting { get; set; }
        public int TotalNotified { get; set; }
        public int TotalFulfilled { get; set; }
        public int TotalExpired { get; set; }
        public int TotalCancelled { get; set; }
        public double AverageWaitDays { get; set; }
        public string MostWaitlistedMovie { get; set; }
        public int MostWaitlistedCount { get; set; }
        public Dictionary<string, int> WaitlistByMovie { get; set; } = new Dictionary<string, int>();
    }
}
