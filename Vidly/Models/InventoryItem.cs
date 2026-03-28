namespace Vidly.Models
{
    /// <summary>
    /// Represents inventory stock information for a single movie title.
    /// </summary>
    public class InventoryItem
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public string Genre { get; set; }

        /// <summary>Total copies the store owns.</summary>
        public int TotalCopies { get; set; }

        /// <summary>Copies currently rented out.</summary>
        public int RentedOut { get; set; }

        /// <summary>Copies available on shelf.</summary>
        public int Available => TotalCopies - RentedOut;

        /// <summary>Low-stock threshold (configurable per title).</summary>
        public int Threshold { get; set; }

        /// <summary>True when available copies are at or below threshold.</summary>
        public bool IsLowStock => Available <= Threshold;

        /// <summary>Utilization percentage (0-100).</summary>
        public int Utilization => TotalCopies > 0
            ? (int)((double)RentedOut / TotalCopies * 100)
            : 0;
    }
}
