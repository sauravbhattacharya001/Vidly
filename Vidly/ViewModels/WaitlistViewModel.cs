using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class WaitlistViewModel
    {
        public IEnumerable<WaitlistEntry> Entries { get; set; } = new List<WaitlistEntry>();
        public IEnumerable<Customer> Customers { get; set; } = new List<Customer>();
        public IEnumerable<Movie> Movies { get; set; } = new List<Movie>();
        public WaitlistStats Stats { get; set; }
        public int? SelectedCustomerId { get; set; }
        public int? SelectedMovieId { get; set; }
        public string StatusMessage { get; set; }
        public bool IsError { get; set; }
    }
}
