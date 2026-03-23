using System;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a memorable quote from a movie, submitted by a customer.
    /// </summary>
    public class MovieQuote
    {
        public int Id { get; set; }

        /// <summary>The movie this quote is from.</summary>
        public int MovieId { get; set; }

        /// <summary>The movie name (denormalized for display).</summary>
        public string MovieName { get; set; }

        /// <summary>The actual quote text.</summary>
        public string Text { get; set; }

        /// <summary>Character who said the line (optional).</summary>
        public string Character { get; set; }

        /// <summary>Customer who submitted the quote.</summary>
        public int SubmittedByCustomerId { get; set; }

        /// <summary>Customer name (denormalized for display).</summary>
        public string SubmittedByName { get; set; }

        /// <summary>When the quote was submitted.</summary>
        public DateTime SubmittedAt { get; set; }

        /// <summary>Number of upvotes.</summary>
        public int Votes { get; set; }
    }
}
