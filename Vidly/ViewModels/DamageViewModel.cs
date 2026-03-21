using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class DamageViewModel
    {
        public IEnumerable<DamageReport> Reports { get; set; }
        public DamageSummary Summary { get; set; }
        public IEnumerable<Customer> Customers { get; set; }
        public IEnumerable<Movie> Movies { get; set; }
        public DamageStatus? FilterStatus { get; set; }
        public DamageSeverity? FilterSeverity { get; set; }
        public string StatusMessage { get; set; }
        public bool IsError { get; set; }
    }
}
