using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class DirectorListViewModel
    {
        public IEnumerable<Director> Directors { get; set; }
        public string SearchQuery { get; set; }
    }

    public class DirectorSpotlightViewModel
    {
        public Director Director { get; set; }
        public IEnumerable<Movie> Filmography { get; set; }
        public int TotalMoviesInStore { get; set; }
        public double? AverageRating { get; set; }
    }
}
