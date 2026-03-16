using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class TournamentViewModel
    {
        // Index page
        public IReadOnlyList<Tournament> ActiveTournaments { get; set; } = new List<Tournament>();
        public IReadOnlyList<TournamentResult> HallOfFame { get; set; } = new List<TournamentResult>();
        public IReadOnlyList<MovieTournamentRecord> MovieRecords { get; set; } = new List<MovieTournamentRecord>();
        public List<Customer> Customers { get; set; } = new List<Customer>();
        public List<Movie> Movies { get; set; } = new List<Movie>();

        // Bracket view
        public Tournament Tournament { get; set; }
        public IReadOnlyList<TournamentMatch> PendingMatches { get; set; } = new List<TournamentMatch>();
        public TournamentMatch CurrentMatch { get; set; }

        // Create form
        public int SelectedCustomerId { get; set; }
        public string TournamentName { get; set; }
        public int BracketSize { get; set; } = 8;
        public Genre? GenreFilter { get; set; }
    }
}
