using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// "Wrapped"-style rental summary for a customer — think Spotify Wrapped
    /// but for a video rental store. Captures lifetime or yearly highlights.
    /// </summary>
    public class CustomerWrapped
    {
        /// <summary>Customer identifier.</summary>
        public int CustomerId { get; set; }

        /// <summary>Customer display name.</summary>
        public string CustomerName { get; set; }

        /// <summary>Period start (null = all-time).</summary>
        public DateTime? PeriodStart { get; set; }

        /// <summary>Period end (null = all-time).</summary>
        public DateTime? PeriodEnd { get; set; }

        /// <summary>Whether this is an all-time summary.</summary>
        public bool IsAllTime => !PeriodStart.HasValue;

        // ── Volume ──

        /// <summary>Total number of rentals in the period.</summary>
        public int TotalRentals { get; set; }

        /// <summary>Total unique movies rented.</summary>
        public int UniqueMovies { get; set; }

        /// <summary>Movies rented more than once.</summary>
        public int RepeatRentals { get; set; }

        // ── Spending ──

        /// <summary>Total amount spent on rentals (cost + late fees).</summary>
        public decimal TotalSpent { get; set; }

        /// <summary>Average cost per rental.</summary>
        public decimal AverageCostPerRental { get; set; }

        /// <summary>Total late fees paid.</summary>
        public decimal TotalLateFees { get; set; }

        // ── Timing ──

        /// <summary>Average rental duration in days.</summary>
        public double AverageRentalDays { get; set; }

        /// <summary>Shortest rental duration in days.</summary>
        public int ShortestRentalDays { get; set; }

        /// <summary>Longest rental duration in days.</summary>
        public int LongestRentalDays { get; set; }

        // ── Genre Insights ──

        /// <summary>Top genre by rental count (null if no rentals).</summary>
        public Genre? FavoriteGenre { get; set; }

        /// <summary>Breakdown of rentals per genre, descending.</summary>
        public List<GenreBreakdownEntry> GenreBreakdown { get; set; } = new List<GenreBreakdownEntry>();

        /// <summary>
        /// Genre diversity score 0-1 (Shannon evenness). 0 = one genre only,
        /// 1 = perfectly even spread across all rented genres.
        /// </summary>
        public double GenreDiversity { get; set; }

        // ── Streaks ──

        /// <summary>Longest consecutive-day rental streak.</summary>
        public int LongestRentalStreak { get; set; }

        /// <summary>Start date of longest streak (null if no rentals).</summary>
        public DateTime? StreakStartDate { get; set; }

        // ── Day-of-week ──

        /// <summary>Favourite day of week for renting.</summary>
        public DayOfWeek? FavoriteRentalDay { get; set; }

        /// <summary>Rental count per day of week.</summary>
        public Dictionary<DayOfWeek, int> RentalsByDayOfWeek { get; set; } = new Dictionary<DayOfWeek, int>();

        // ── Highlights ──

        /// <summary>Highest-rated movie rented (by movie rating).</summary>
        public string TopRatedMovieRented { get; set; }

        /// <summary>Most expensive single rental.</summary>
        public decimal MostExpensiveRental { get; set; }

        /// <summary>Fun "personality" label derived from genre preferences.</summary>
        public string RentalPersonality { get; set; }
    }

    /// <summary>Per-genre rental count entry.</summary>
    public class GenreBreakdownEntry
    {
        public Genre Genre { get; set; }
        public int Count { get; set; }
        public double Percentage { get; set; }
    }
}
