using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidly.Models
{
    /// <summary>
    /// Stock status for a single movie title.
    /// </summary>
    public class MovieStock
    {
        /// <summary>Movie ID.</summary>
        public int MovieId { get; set; }

        /// <summary>Movie name.</summary>
        public string MovieName { get; set; }

        /// <summary>Genre of the movie.</summary>
        public Genre? Genre { get; set; }

        /// <summary>Total copies owned by the store.</summary>
        public int TotalCopies { get; set; }

        /// <summary>Copies currently rented out (active or overdue).</summary>
        public int RentedCopies { get; set; }

        /// <summary>Copies available for rental.</summary>
        public int AvailableCopies => Math.Max(0, TotalCopies - RentedCopies);

        /// <summary>Whether any copies are available.</summary>
        public bool IsAvailable => AvailableCopies > 0;

        /// <summary>Utilization ratio (0.0–1.0). Higher means more copies are out.</summary>
        public double Utilization => TotalCopies > 0
            ? (double)RentedCopies / TotalCopies
            : 0.0;

        /// <summary>Stock level classification.</summary>
        public StockLevel Level
        {
            get
            {
                if (TotalCopies == 0) return StockLevel.OutOfStock;
                if (AvailableCopies == 0) return StockLevel.OutOfStock;
                if (Utilization >= 0.8) return StockLevel.Low;
                if (Utilization >= 0.5) return StockLevel.Medium;
                return StockLevel.High;
            }
        }

        /// <summary>Earliest expected return date among active rentals (null if none rented).</summary>
        public DateTime? EarliestReturn { get; set; }

        /// <summary>Number of overdue copies.</summary>
        public int OverdueCopies { get; set; }
    }

    /// <summary>
    /// Stock level classification.
    /// </summary>
    public enum StockLevel
    {
        High,
        Medium,
        Low,
        OutOfStock
    }

    /// <summary>
    /// Overall inventory health summary.
    /// </summary>
    public class InventorySummary
    {
        /// <summary>Total unique movie titles in catalog.</summary>
        public int TotalTitles { get; set; }

        /// <summary>Total copies across all titles.</summary>
        public int TotalCopies { get; set; }

        /// <summary>Total copies currently rented out.</summary>
        public int TotalRented { get; set; }

        /// <summary>Total copies available.</summary>
        public int TotalAvailable => TotalCopies - TotalRented;

        /// <summary>Overall utilization (0.0–1.0).</summary>
        public double OverallUtilization => TotalCopies > 0
            ? (double)TotalRented / TotalCopies
            : 0.0;

        /// <summary>Number of titles completely out of stock.</summary>
        public int OutOfStockTitles { get; set; }

        /// <summary>Number of titles with low stock.</summary>
        public int LowStockTitles { get; set; }

        /// <summary>Number of overdue rentals.</summary>
        public int TotalOverdue { get; set; }

        /// <summary>Revenue at risk from overdue rentals (potential late fees).</summary>
        public decimal OverdueRevenue { get; set; }

        /// <summary>Stock distribution by genre.</summary>
        public List<GenreStock> GenreBreakdown { get; set; } = new List<GenreStock>();
    }

    /// <summary>
    /// Stock breakdown for a single genre.
    /// </summary>
    public class GenreStock
    {
        /// <summary>Genre name.</summary>
        public string GenreName { get; set; }

        /// <summary>Number of titles in this genre.</summary>
        public int TitleCount { get; set; }

        /// <summary>Total copies in this genre.</summary>
        public int TotalCopies { get; set; }

        /// <summary>Copies currently rented.</summary>
        public int RentedCopies { get; set; }

        /// <summary>Utilization for this genre.</summary>
        public double Utilization => TotalCopies > 0
            ? (double)RentedCopies / TotalCopies
            : 0.0;
    }

    /// <summary>
    /// Predicted availability for a movie on a future date.
    /// </summary>
    public class AvailabilityForecast
    {
        /// <summary>The date of the forecast.</summary>
        public DateTime Date { get; set; }

        /// <summary>Predicted available copies on this date.</summary>
        public int PredictedAvailable { get; set; }

        /// <summary>Number of rentals expected to return by this date.</summary>
        public int ExpectedReturns { get; set; }
    }
}
