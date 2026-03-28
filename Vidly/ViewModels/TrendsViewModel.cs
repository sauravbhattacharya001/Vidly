using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class TrendsViewModel
    {
        public TrendsReport Report { get; set; }
        public int WindowDays { get; set; }
        public int TopCount { get; set; }

        public static readonly List<(int Days, string Label)> WindowPresets = new List<(int, string)>
        {
            (7, "Last 7 days"), (14, "Last 14 days"), (30, "Last 30 days"),
            (90, "Last 90 days"), (180, "Last 6 months"), (365, "Last year")
        };
    }
}
