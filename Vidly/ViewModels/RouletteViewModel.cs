using Vidly.Models;
using Vidly.Services;
using System.Collections.Generic;

namespace Vidly.ViewModels
{
    public class RouletteViewModel
    {
        public RouletteResult Result { get; set; }
        public IReadOnlyList<Movie> WheelMovies { get; set; }
        public Genre? SelectedGenre { get; set; }
        public int? SelectedMinRating { get; set; }
        public bool HasSpun { get; set; }
    }
}
