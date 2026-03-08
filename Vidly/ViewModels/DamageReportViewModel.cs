using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class DamageReportViewModel
    {
        public DamageAnalytics Analytics { get; set; }
        public IReadOnlyList<DamageReport> RecentReports { get; set; }
        public IReadOnlyList<MovieDamageSummary> MoviesNeedingReplacement { get; set; }
        public IReadOnlyList<CustomerDamageProfile> FlaggedCustomers { get; set; }
        public string ActiveTab { get; set; }
    }
}
