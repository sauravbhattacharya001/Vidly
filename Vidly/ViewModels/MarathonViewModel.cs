using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class MarathonViewModel
    {
        public IEnumerable<Movie> AvailableMovies { get; set; }
        public MarathonRequest Request { get; set; }
        public MarathonPlan Plan { get; set; }
    }
}
