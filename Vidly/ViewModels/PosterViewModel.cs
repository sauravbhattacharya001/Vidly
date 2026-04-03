using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class PosterViewModel
    {
        public IEnumerable<Movie> Movies { get; set; }
    }
}
