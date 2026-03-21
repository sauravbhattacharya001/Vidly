using System;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a movie trade-in where a customer trades a physical copy
    /// of a movie they own in exchange for rental credits.
    /// </summary>
    public class TradeIn
    {
        public int Id { get; set; }

        [Required]
        public int CustomerId { get; set; }

        [Required]
        [StringLength(255, ErrorMessage = "Movie title cannot exceed 255 characters.")]
        [Display(Name = "Movie Title")]
        public string MovieTitle { get; set; }

        [Required]
        [Display(Name = "Format")]
        public TradeInFormat Format { get; set; }

        [Required]
        [Display(Name = "Condition")]
        public TradeInCondition Condition { get; set; }

        [Display(Name = "Credits Awarded")]
        [Range(0, 100, ErrorMessage = "Credits must be between 0 and 100.")]
        public decimal CreditsAwarded { get; set; }

        [Display(Name = "Trade-In Date")]
        [DataType(DataType.Date)]
        public DateTime TradeInDate { get; set; }

        [Display(Name = "Status")]
        public TradeInStatus Status { get; set; }

        [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
        public string Notes { get; set; }
    }

    public enum TradeInFormat
    {
        [Display(Name = "DVD")]
        DVD = 0,

        [Display(Name = "Blu-ray")]
        BluRay = 1,

        [Display(Name = "4K UHD")]
        UHD4K = 2,

        [Display(Name = "VHS")]
        VHS = 3
    }

    public enum TradeInCondition
    {
        [Display(Name = "Like New")]
        LikeNew = 0,

        [Display(Name = "Good")]
        Good = 1,

        [Display(Name = "Fair")]
        Fair = 2,

        [Display(Name = "Poor")]
        Poor = 3
    }

    public enum TradeInStatus
    {
        [Display(Name = "Pending Review")]
        Pending = 0,

        [Display(Name = "Accepted")]
        Accepted = 1,

        [Display(Name = "Rejected")]
        Rejected = 2
    }
}
