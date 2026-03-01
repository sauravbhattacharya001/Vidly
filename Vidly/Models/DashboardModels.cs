using System.Collections.Generic;
using Vidly.Repositories;

namespace Vidly.Models
{
    /// <summary>
    /// Complete dashboard data model.
    /// </summary>
    public class DashboardData
    {
        public RentalStats Stats { get; set; }
        public int CustomerCount { get; set; }
        public int MovieCount { get; set; }
        public decimal AverageRevenuePerRental { get; set; }
        public decimal RealizedRevenue { get; set; }
        public decimal ProjectedRevenue { get; set; }
        public List<MovieRankEntry> TopMovies { get; set; }
        public List<CustomerRankEntry> TopCustomers { get; set; }
        public List<GenreRevenueEntry> RevenueByGenre { get; set; }
        public List<MembershipRevenueEntry> MembershipBreakdown { get; set; }
        public List<Rental> RecentRentals { get; set; }
        public List<MonthlyRevenueEntry> MonthlyRevenue { get; set; }
    }

    /// <summary>
    /// A movie's rank in the top-movies list: rental count, total revenue, and metadata.
    /// </summary>
    public class MovieRankEntry
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? Genre { get; set; }
        public int? Rating { get; set; }
        public int RentalCount { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    /// <summary>
    /// A customer's rank in the top-customers list: rental count, spending, and late fees.
    /// </summary>
    public class CustomerRankEntry
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public MembershipType MembershipType { get; set; }
        public int RentalCount { get; set; }
        public decimal TotalSpent { get; set; }
        public decimal LateFees { get; set; }
    }

    /// <summary>
    /// Revenue breakdown for a single genre: rental count, total revenue, and late fees.
    /// </summary>
    public class GenreRevenueEntry
    {
        public string GenreName { get; set; }
        public int RentalCount { get; set; }
        public decimal Revenue { get; set; }
        public decimal LateFees { get; set; }
    }

    /// <summary>
    /// Revenue breakdown by customer membership tier.
    /// </summary>
    public class MembershipRevenueEntry
    {
        public MembershipType Tier { get; set; }
        public int UniqueCustomers { get; set; }
        public int RentalCount { get; set; }
        public decimal Revenue { get; set; }

        /// <summary>
        /// Internal tracking — customer IDs for unique count.
        /// </summary>
        internal HashSet<int> CustomerIds { get; set; } = new HashSet<int>();
    }

    /// <summary>
    /// Monthly revenue data point: revenue, rental count, and late fees for one month.
    /// </summary>
    public class MonthlyRevenueEntry
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string Label { get; set; }
        public decimal Revenue { get; set; }
        public int RentalCount { get; set; }
        public decimal LateFees { get; set; }
    }
}
