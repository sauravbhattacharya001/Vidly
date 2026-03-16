using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// A staff-curated pick — one movie recommendation from a staff member
    /// with a personal note explaining why it's worth watching.
    /// </summary>
    public class StaffPick
    {
        public int Id { get; set; }
        public int MovieId { get; set; }
        public string StaffName { get; set; }
        public string Note { get; set; }
        public string Theme { get; set; }
        public DateTime PickedDate { get; set; }
        public bool IsFeatured { get; set; }
    }

    /// <summary>
    /// A themed staff picks list grouping multiple picks under a heading.
    /// </summary>
    public class StaffPicksList
    {
        public string Theme { get; set; }
        public string Description { get; set; }
        public List<StaffPickViewModel> Picks { get; set; } = new List<StaffPickViewModel>();
    }

    /// <summary>
    /// View model combining a staff pick with its movie details.
    /// </summary>
    public class StaffPickViewModel
    {
        public StaffPick Pick { get; set; }
        public Movie Movie { get; set; }
    }

    /// <summary>
    /// Page-level view model for the Staff Picks index.
    /// </summary>
    public class StaffPicksPageViewModel
    {
        public StaffPickViewModel FeaturedPick { get; set; }
        public List<StaffPicksList> ThemedLists { get; set; } = new List<StaffPicksList>();
        public List<string> AllStaff { get; set; } = new List<string>();
        public List<string> AllThemes { get; set; } = new List<string>();
        public int TotalPicks { get; set; }
    }
}
