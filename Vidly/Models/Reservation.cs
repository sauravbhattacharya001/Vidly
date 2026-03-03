using System;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a reservation (hold) on a movie that is currently rented out.
    /// When the movie is returned, the next customer in the queue is notified
    /// and gets a limited-time window to pick it up.
    /// </summary>
    public class Reservation
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Customer is required.")]
        [Display(Name = "Customer")]
        public int CustomerId { get; set; }

        /// <summary>Resolved customer name (populated by repository).</summary>
        public string CustomerName { get; set; }

        [Required(ErrorMessage = "Movie is required.")]
        [Display(Name = "Movie")]
        public int MovieId { get; set; }

        /// <summary>Resolved movie name (populated by repository).</summary>
        public string MovieName { get; set; }

        /// <summary>When the reservation was placed.</summary>
        [Display(Name = "Reserved On")]
        [DataType(DataType.Date)]
        public DateTime ReservedDate { get; set; }

        /// <summary>
        /// When the hold expires. Null until the movie becomes available,
        /// at which point a pickup window is set.
        /// </summary>
        [Display(Name = "Expires")]
        [DataType(DataType.Date)]
        public DateTime? ExpiresDate { get; set; }

        /// <summary>Position in the queue (1-based). Lower = first in line.</summary>
        [Display(Name = "Queue Position")]
        public int QueuePosition { get; set; }

        /// <summary>Current status of the reservation.</summary>
        [Display(Name = "Status")]
        public ReservationStatus Status { get; set; }

        /// <summary>
        /// When the reservation was fulfilled (converted to a rental).
        /// </summary>
        [Display(Name = "Fulfilled On")]
        [DataType(DataType.Date)]
        public DateTime? FulfilledDate { get; set; }

        /// <summary>Whether the pickup window has expired.</summary>
        public bool IsExpired =>
            Status == ReservationStatus.Ready &&
            ExpiresDate.HasValue &&
            DateTime.Today > ExpiresDate.Value;

        /// <summary>Days waiting in the queue.</summary>
        public int DaysWaiting
        {
            get
            {
                var endDate = FulfilledDate ?? DateTime.Today;
                return Math.Max(0, (int)Math.Ceiling((endDate - ReservedDate).TotalDays));
            }
        }
    }

    /// <summary>
    /// Reservation lifecycle status.
    /// </summary>
    public enum ReservationStatus
    {
        /// <summary>Waiting in queue — movie is still rented out.</summary>
        [Display(Name = "Waiting")]
        Waiting = 1,

        /// <summary>Movie returned — customer has a pickup window.</summary>
        [Display(Name = "Ready for Pickup")]
        Ready = 2,

        /// <summary>Customer picked up the movie — reservation fulfilled.</summary>
        [Display(Name = "Fulfilled")]
        Fulfilled = 3,

        /// <summary>Customer cancelled the reservation.</summary>
        [Display(Name = "Cancelled")]
        Cancelled = 4,

        /// <summary>Pickup window expired — reservation auto-cancelled.</summary>
        [Display(Name = "Expired")]
        Expired = 5
    }
}
