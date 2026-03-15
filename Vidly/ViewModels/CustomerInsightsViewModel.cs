using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class CustomerInsightsViewModel
    {
        public Customer Customer { get; set; }
        public LoyaltyResult Loyalty { get; set; }
        public IReadOnlyList<TimelineEvent> Timeline { get; set; }
        public IReadOnlyList<RentalHistoryEntry> RentalHistory { get; set; }
        public GenreBreakdown GenreStats { get; set; }
        public SpendingSummary Spending { get; set; }
        public RentalPatterns Patterns { get; set; }
        public IReadOnlyList<Customer> AllCustomers { get; set; }
        public string StatusMessage { get; set; }
    }

    public class GenreBreakdown
    {
        public Dictionary<string, int> GenreCounts { get; set; } = new Dictionary<string, int>();
        public string FavoriteGenre { get; set; }
        public int TotalRentals { get; set; }
        public int UniqueGenres { get; set; }
    }

    public class SpendingSummary
    {
        public decimal TotalSpent { get; set; }
        public decimal AveragePerRental { get; set; }
        public decimal TotalLateFees { get; set; }
        public decimal LateFeePct { get; set; }
        public int TotalRentals { get; set; }
    }

    public class RentalPatterns
    {
        public double AverageDurationDays { get; set; }
        public int LongestRentalDays { get; set; }
        public double OnTimeReturnRate { get; set; }
        public int TotalOverdue { get; set; }
        public string MostActiveDay { get; set; }
        public int CurrentStreak { get; set; }
    }
}
