using System.Collections.Generic;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the rental swap feature.
    /// </summary>
    public class SwapViewModel
    {
        /// <summary>The rental being swapped.</summary>
        public Rental CurrentRental { get; set; }

        /// <summary>Available movies to swap to.</summary>
        public IReadOnlyList<Movie> AvailableMovies { get; set; } = new List<Movie>();

        /// <summary>Quote for a specific swap (null if no movie selected yet).</summary>
        public SwapQuote Quote { get; set; }

        /// <summary>Selected new movie ID.</summary>
        public int? NewMovieId { get; set; }

        /// <summary>Result message after swap execution.</summary>
        public string Message { get; set; }

        /// <summary>Whether the message is an error.</summary>
        public bool IsError { get; set; }

        /// <summary>Whether this rental has already been swapped.</summary>
        public bool AlreadySwapped { get; set; }

        /// <summary>Swap history for the customer.</summary>
        public IReadOnlyList<SwapRecord> SwapHistory { get; set; } = new List<SwapRecord>();

        /// <summary>Swap statistics (admin).</summary>
        public SwapStats Stats { get; set; }
    }
}
