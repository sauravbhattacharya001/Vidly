using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class SoundtrackViewModel
    {
        public IEnumerable<SoundtrackTrack> Tracks { get; set; }
        public IEnumerable<SoundtrackTrack> TopRated { get; set; }
        public IEnumerable<Movie> Movies { get; set; }
        public int? FilterMovieId { get; set; }
        public string SearchQuery { get; set; }
    }
}
