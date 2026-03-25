using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class TriviaBoardViewModel
    {
        public IEnumerable<TriviaFact> Facts { get; set; }
        public IEnumerable<Movie> Movies { get; set; }
        public IReadOnlyList<string> Categories { get; set; }
        public int? FilterMovieId { get; set; }
        public string FilterCategory { get; set; }
        public TriviaFact RandomFact { get; set; }
    }
}
