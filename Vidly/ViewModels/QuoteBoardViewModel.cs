using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.ViewModels
{
    public class QuoteBoardViewModel
    {
        public IEnumerable<MovieQuote> Quotes { get; set; }
        public IEnumerable<Movie> Movies { get; set; }
        public int? FilterMovieId { get; set; }
        public MovieQuote RandomQuote { get; set; }
    }
}
