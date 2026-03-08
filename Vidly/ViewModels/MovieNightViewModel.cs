using System.Collections.Generic;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.ViewModels
{
    public class MovieNightViewModel
    {
        public IReadOnlyList<Customer> Customers { get; set; } = new List<Customer>();
        public IReadOnlyList<ThemeOption> Themes { get; set; } = new List<ThemeOption>();
        public MovieNightRequest Request { get; set; } = new MovieNightRequest();
        public MovieNightPlan Plan { get; set; }
        public bool HasPlan => Plan != null && Plan.MovieCount > 0;
    }
}
