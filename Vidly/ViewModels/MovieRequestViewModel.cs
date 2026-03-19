using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the Movie Request Board.
    /// </summary>
    public class MovieRequestViewModel
    {
        // ── Stats ──
        public MovieRequestStats Stats { get; set; }

        // ── Trending ──
        public IReadOnlyList<TrendingRequest> Trending { get; set; }

        // ── All requests (filtered) ──
        public IReadOnlyList<MovieRequest> Requests { get; set; }

        // ── Genre breakdown ──
        public IDictionary<string, int> GenreBreakdown { get; set; }

        // ── Filters ──
        public string StatusFilter { get; set; }
        public string GenreFilter { get; set; }
        public string SearchQuery { get; set; }

        // ── Feedback ──
        public string StatusMessage { get; set; }
        public bool IsError { get; set; }
    }
}
