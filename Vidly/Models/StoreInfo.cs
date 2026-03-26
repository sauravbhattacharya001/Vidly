using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a store location with hours of operation.
    /// </summary>
    public class StoreInfo
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Store Name")]
        public string Name { get; set; }

        [Required]
        [StringLength(200)]
        public string Address { get; set; }

        [StringLength(50)]
        public string City { get; set; }

        [StringLength(2)]
        public string State { get; set; }

        [StringLength(10)]
        [Display(Name = "ZIP Code")]
        public string ZipCode { get; set; }

        [Phone]
        [Display(Name = "Phone Number")]
        public string Phone { get; set; }

        [EmailAddress]
        public string Email { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        /// <summary>
        /// Hours for each day of the week (0=Sunday through 6=Saturday).
        /// </summary>
        public List<StoreHours> Hours { get; set; } = new List<StoreHours>();

        /// <summary>
        /// Special closures or modified hours.
        /// </summary>
        public List<SpecialHours> SpecialDays { get; set; } = new List<SpecialHours>();

        /// <summary>
        /// Whether the store is currently open.
        /// </summary>
        public bool IsCurrentlyOpen
        {
            get
            {
                var now = DateTime.Now;
                // Check special days first
                var special = SpecialDays?.FirstOrDefault(s => s.Date.Date == now.Date);
                if (special != null)
                    return !special.IsClosed && now.TimeOfDay >= special.OpenTime && now.TimeOfDay <= special.CloseTime;

                var todayHours = Hours?.FirstOrDefault(h => h.DayOfWeek == now.DayOfWeek);
                if (todayHours == null || todayHours.IsClosed)
                    return false;

                return now.TimeOfDay >= todayHours.OpenTime && now.TimeOfDay <= todayHours.CloseTime;
            }
        }

        /// <summary>
        /// Full formatted address.
        /// </summary>
        public string FullAddress => $"{Address}, {City}, {State} {ZipCode}";
    }

    /// <summary>
    /// Regular operating hours for a specific day of the week.
    /// </summary>
    public class StoreHours
    {
        public DayOfWeek DayOfWeek { get; set; }
        public TimeSpan OpenTime { get; set; }
        public TimeSpan CloseTime { get; set; }
        public bool IsClosed { get; set; }

        public string FormattedOpen => DateTime.Today.Add(OpenTime).ToString("h:mm tt");
        public string FormattedClose => DateTime.Today.Add(CloseTime).ToString("h:mm tt");
    }

    /// <summary>
    /// Special/holiday hours override for a specific date.
    /// </summary>
    public class SpecialHours
    {
        public DateTime Date { get; set; }
        public string Label { get; set; }
        public bool IsClosed { get; set; }
        public TimeSpan OpenTime { get; set; }
        public TimeSpan CloseTime { get; set; }
    }
}
