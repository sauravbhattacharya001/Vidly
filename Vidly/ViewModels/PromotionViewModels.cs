using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class PromotionIndexViewModel
    {
        public IReadOnlyList<Promotion> Promotions { get; set; }
        public string StatusFilter { get; set; }
        public int TotalCount { get; set; }
        public int ActiveCount { get; set; }
        public int UpcomingCount { get; set; }
        public int ExpiredCount { get; set; }
    }

    public class PromotionDetailsViewModel
    {
        public Promotion Promotion { get; set; }
        public IReadOnlyList<Movie> FeaturedMovies { get; set; }
    }
}
