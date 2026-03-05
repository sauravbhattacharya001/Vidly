using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a customer waiting for a currently-unavailable movie.
    /// </summary>
    public class WaitlistEntry
    {
        public int Id { get; set; }

        [Required]
        public int CustomerId { get; set; }

        public string CustomerName { get; set; }

        [Required]
        public int MovieId { get; set; }

        public string MovieName { get; set; }

        /// <summary>When the customer joined the waitlist.</summary>
        public DateTime JoinedDate { get; set; } = DateTime.Now;

        /// <summary>When the customer was notified that the movie is available.</summary>
        public DateTime? NotifiedDate { get; set; }

        /// <summary>When the customer picked up (rented) the movie after notification.</summary>
        public DateTime? FulfilledDate { get; set; }

        /// <summary>When the customer cancelled or the entry expired.</summary>
        public DateTime? CancelledDate { get; set; }

        /// <summary>Current status of the waitlist entry.</summary>
        public WaitlistStatus Status { get; set; } = WaitlistStatus.Waiting;

        /// <summary>Optional note from the customer.</summary>
        [StringLength(500)]
        public string Note { get; set; }

        /// <summary>Preferred notification method.</summary>
        public NotificationMethod PreferredNotification { get; set; } = NotificationMethod.Email;

        /// <summary>Hours the customer has to pick up after notification before the spot expires.</summary>
        public int PickupWindowHours { get; set; } = 48;

        /// <summary>Whether the pickup window has expired.</summary>
        public bool IsExpired =>
            Status == WaitlistStatus.Notified &&
            NotifiedDate.HasValue &&
            DateTime.Now > NotifiedDate.Value.AddHours(PickupWindowHours);

        /// <summary>How long the customer has been waiting (or waited).</summary>
        public TimeSpan WaitDuration
        {
            get
            {
                var end = FulfilledDate ?? CancelledDate ?? DateTime.Now;
                return end - JoinedDate;
            }
        }
    }

    public enum WaitlistStatus
    {
        [Display(Name = "Waiting")]
        Waiting = 1,

        [Display(Name = "Notified")]
        Notified = 2,

        [Display(Name = "Fulfilled")]
        Fulfilled = 3,

        [Display(Name = "Cancelled")]
        Cancelled = 4,

        [Display(Name = "Expired")]
        Expired = 5
    }

    public enum NotificationMethod
    {
        Email = 1,
        Phone = 2,
        Both = 3
    }

    /// <summary>
    /// Analytics report for waitlist activity.
    /// </summary>
    public class WaitlistReport
    {
        public int TotalEntries { get; set; }
        public int ActivelyWaiting { get; set; }
        public int Notified { get; set; }
        public int Fulfilled { get; set; }
        public int Cancelled { get; set; }
        public int Expired { get; set; }
        public double AverageWaitHours { get; set; }
        public double FulfillmentRate { get; set; }
        public double ExpirationRate { get; set; }
        public IReadOnlyList<MovieWaitlistSummary> MostWaitlistedMovies { get; set; }
        public IReadOnlyList<CustomerWaitlistSummary> MostActiveCustomers { get; set; }
        public string TextSummary { get; set; }
    }

    public class MovieWaitlistSummary
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public int WaitingCount { get; set; }
        public double AverageWaitHours { get; set; }
    }

    public class CustomerWaitlistSummary
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int TotalRequests { get; set; }
        public int FulfilledCount { get; set; }
    }
}
