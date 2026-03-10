using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the movie availability calendar page.
    /// </summary>
    public class AvailabilityViewModel
    {
        /// <summary>All movie availability records (filtered).</summary>
        public List<MovieAvailability> Movies { get; set; } = new List<MovieAvailability>();

        /// <summary>Calendar view of upcoming returns.</summary>
        public List<CalendarDay> Calendar { get; set; } = new List<CalendarDay>();

        /// <summary>Overall availability summary.</summary>
        public AvailabilitySummary Summary { get; set; }

        /// <summary>Current genre filter.</summary>
        public Genre? SelectedGenre { get; set; }

        /// <summary>Whether showing only available movies.</summary>
        public bool AvailableOnly { get; set; }

        /// <summary>Current search query.</summary>
        public string Query { get; set; }

        /// <summary>Number of calendar days shown.</summary>
        public int CalendarDays { get; set; } = 14;

        /// <summary>Active view tab (list or calendar).</summary>
        public string ActiveTab { get; set; } = "list";
    }
}
