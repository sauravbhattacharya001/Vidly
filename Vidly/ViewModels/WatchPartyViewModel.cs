using System.Collections.Generic;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.ViewModels
{
    /// <summary>
    /// ViewModel for the Watch Party planner.
    /// </summary>
    public class WatchPartyViewModel
    {
        /// <summary>Available party themes to choose from.</summary>
        public IReadOnlyList<PartyTheme> Themes { get; set; }

        /// <summary>The generated party plan (null until a theme is picked).</summary>
        public WatchPartyPlan Plan { get; set; }

        /// <summary>Previously saved party plans.</summary>
        public IReadOnlyList<WatchPartyPlan> SavedParties { get; set; }

        /// <summary>Status/info message.</summary>
        public string Message { get; set; }

        /// <summary>Whether we're showing a generated plan.</summary>
        public bool HasPlan => Plan != null;
    }
}
