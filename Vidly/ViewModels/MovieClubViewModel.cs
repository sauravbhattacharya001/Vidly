using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the Movie Clubs index page.
    /// </summary>
    public class MovieClubIndexViewModel
    {
        public IEnumerable<MovieClub> Clubs { get; set; }
        public IEnumerable<Customer> Customers { get; set; }
        public Dictionary<int, int> MemberCounts { get; set; } = new Dictionary<int, int>();
    }

    /// <summary>
    /// View model for club detail page.
    /// </summary>
    public class MovieClubDetailViewModel
    {
        public MovieClub Club { get; set; }
        public IEnumerable<ClubMembership> Members { get; set; }
        public IEnumerable<ClubWatchlistItem> Watchlist { get; set; }
        public IEnumerable<ClubPoll> Polls { get; set; }
        public ClubStats Stats { get; set; }
        public IEnumerable<Customer> AllCustomers { get; set; }
        public IEnumerable<Movie> AllMovies { get; set; }
        public Dictionary<int, string> CustomerNames { get; set; } = new Dictionary<int, string>();
    }
}
