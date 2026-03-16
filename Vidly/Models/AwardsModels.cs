using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Represents an award category in the annual Vidly Awards ceremony.
    /// </summary>
    public class AwardCategory
    {
        public string Name { get; set; }
        public string Icon { get; set; }
        public string Description { get; set; }
        public AwardWinner Winner { get; set; }
        public List<AwardNominee> Nominees { get; set; } = new List<AwardNominee>();
    }

    /// <summary>
    /// The winner of an award category.
    /// </summary>
    public class AwardWinner
    {
        public string Name { get; set; }
        public string Subtitle { get; set; }
        public string StatLabel { get; set; }
        public string StatValue { get; set; }
    }

    /// <summary>
    /// A runner-up nominee in an award category.
    /// </summary>
    public class AwardNominee
    {
        public string Name { get; set; }
        public string StatValue { get; set; }
        public int Rank { get; set; }
    }

    /// <summary>
    /// Summary statistics for the awards year.
    /// </summary>
    public class AwardsYearSummary
    {
        public int Year { get; set; }
        public int TotalRentals { get; set; }
        public int TotalMovies { get; set; }
        public int TotalCustomers { get; set; }
        public int TotalReviews { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    /// <summary>
    /// Complete awards ceremony data for a given year.
    /// </summary>
    public class AwardsCeremony
    {
        public int Year { get; set; }
        public AwardsYearSummary Summary { get; set; }
        public List<AwardCategory> Categories { get; set; } = new List<AwardCategory>();
        public List<int> AvailableYears { get; set; } = new List<int>();
    }
}
