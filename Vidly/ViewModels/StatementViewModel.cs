using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.ViewModels
{
    public class StatementViewModel
    {
        /// <summary>
        /// All customers for the dropdown selector.
        /// </summary>
        public IReadOnlyList<Vidly.Models.Customer> AllCustomers { get; set; }

        /// <summary>
        /// Selected customer ID.
        /// </summary>
        [Display(Name = "Customer")]
        public int? CustomerId { get; set; }

        /// <summary>
        /// Start of the statement period.
        /// </summary>
        [Display(Name = "From")]
        [DataType(DataType.Date)]
        public DateTime? PeriodStart { get; set; }

        /// <summary>
        /// End of the statement period.
        /// </summary>
        [Display(Name = "To")]
        [DataType(DataType.Date)]
        public DateTime? PeriodEnd { get; set; }

        /// <summary>
        /// The generated statement (null until form is submitted).
        /// </summary>
        public Vidly.Models.CustomerStatement Statement { get; set; }

        /// <summary>
        /// Status/error message.
        /// </summary>
        public string StatusMessage { get; set; }
    }
}
