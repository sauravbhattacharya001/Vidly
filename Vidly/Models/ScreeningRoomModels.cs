using System;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// A private screening room available for booking.
    /// </summary>
    public class ScreeningRoom
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Name { get; set; }

        [Range(2, 50)]
        public int Capacity { get; set; }

        /// <summary>Hourly rate in dollars.</summary>
        [Range(0.01, 999.99)]
        public decimal HourlyRate { get; set; }

        public bool HasSurroundSound { get; set; }
        public bool Has4KProjector { get; set; }

        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// A booking for a screening room by a customer.
    /// </summary>
    public class ScreeningBooking
    {
        public int Id { get; set; }

        public int RoomId { get; set; }
        public string RoomName { get; set; }

        public int CustomerId { get; set; }
        public string CustomerName { get; set; }

        /// <summary>Movie to screen (optional — customer may bring their own).</summary>
        public int? MovieId { get; set; }
        public string MovieName { get; set; }

        [Required]
        public DateTime Date { get; set; }

        /// <summary>Start hour (0-23).</summary>
        [Range(0, 23)]
        public int StartHour { get; set; }

        /// <summary>Duration in hours (1-4).</summary>
        [Range(1, 4)]
        public int DurationHours { get; set; } = 2;

        public int GuestCount { get; set; }

        public decimal TotalCost { get; set; }

        public BookingStatus Status { get; set; } = BookingStatus.Confirmed;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string Notes { get; set; }

        public int EndHour => StartHour + DurationHours;
    }

    public enum BookingStatus
    {
        Confirmed,
        Cancelled,
        Completed
    }
}
