using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class AwardIndexViewModel
    {
        public List<AwardNomination> Nominations { get; set; } = new List<AwardNomination>();
        public List<AwardSummary> Leaderboard { get; set; } = new List<AwardSummary>();
        public List<Movie> Movies { get; set; } = new List<Movie>();

        public int? FilterMovieId { get; set; }
        public AwardBody? FilterBody { get; set; }
        public AwardCategory? FilterCategory { get; set; }
        public int? FilterYear { get; set; }
        public bool? FilterWonOnly { get; set; }

        public string Message { get; set; }
        public bool IsError { get; set; }
    }
}
