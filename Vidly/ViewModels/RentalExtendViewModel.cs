using System.ComponentModel.DataAnnotations;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the rental extension form.
    /// </summary>
    public class RentalExtendViewModel
    {
        /// <summary>
        /// The rental being extended.
        /// </summary>
        public Rental Rental { get; set; }

        /// <summary>
        /// Number of days to extend the rental (1-7).
        /// </summary>
        [Required]
        [Range(1, 7, ErrorMessage = "Extension must be between 1 and 7 days.")]
        [Display(Name = "Extension Days")]
        public int ExtensionDays { get; set; }

        /// <summary>
        /// Extension fee per day (half the daily rate), for display purposes.
        /// </summary>
        [Display(Name = "Fee Per Day")]
        [DataType(DataType.Currency)]
        public decimal ExtensionFeePerDay { get; set; }

        /// <summary>
        /// Whether this rental has already been extended.
        /// </summary>
        public bool IsAlreadyExtended { get; set; }

        /// <summary>
        /// Total extension fee = ExtensionFeePerDay * ExtensionDays.
        /// </summary>
        [Display(Name = "Total Extension Fee")]
        public decimal TotalExtensionFee => ExtensionFeePerDay * ExtensionDays;
    }
}
