using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// A seasonal or holiday promotion with date-bounded discounts
    /// and optional featured movies.
    /// </summary>
    public class Promotion
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Promotion name is required.")]
        [StringLength(100)]
        [Display(Name = "Promotion Name")]
        public string Name { get; set; }

        [StringLength(500)]
        public string Description { get; set; }

        [Required]
        [Display(Name = "Start Date")]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [Required]
        [Display(Name = "End Date")]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; }

        [Required]
        [Display(Name = "Discount %")]
        [Range(1, 90, ErrorMessage = "Discount must be between 1% and 90%.")]
        public int DiscountPercent { get; set; }

        [Display(Name = "Banner Color")]
        [StringLength(7)]
        public string BannerColor { get; set; } = "#e74c3c";

        [Display(Name = "Season")]
        public PromotionSeason Season { get; set; }

        /// <summary>Comma-separated movie IDs featured in this promo.</summary>
        [Display(Name = "Featured Movie IDs")]
        [StringLength(500)]
        public string FeaturedMovieIds { get; set; }

        public bool IsActive =>
            DateTime.Today >= StartDate.Date && DateTime.Today <= EndDate.Date;

        public string StatusDisplay
        {
            get
            {
                if (DateTime.Today < StartDate.Date) return "Upcoming";
                if (DateTime.Today > EndDate.Date) return "Expired";
                return "Active";
            }
        }

        public int DaysRemaining =>
            IsActive ? (int)(EndDate.Date - DateTime.Today).TotalDays : 0;

        public List<int> GetFeaturedMovieIdList()
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(FeaturedMovieIds)) return result;
            foreach (var part in FeaturedMovieIds.Split(','))
            {
                if (int.TryParse(part.Trim(), out var id))
                    result.Add(id);
            }
            return result;
        }
    }

    public enum PromotionSeason
    {
        [Display(Name = "Spring")]
        Spring = 1,
        [Display(Name = "Summer")]
        Summer = 2,
        [Display(Name = "Fall")]
        Fall = 3,
        [Display(Name = "Winter / Holiday")]
        Winter = 4,
        [Display(Name = "Special Event")]
        SpecialEvent = 5
    }
}
