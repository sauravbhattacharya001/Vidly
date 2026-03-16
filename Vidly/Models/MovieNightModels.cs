using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Curation theme for a movie night planning session.
    /// Determines the algorithm used to select and order films.
    /// </summary>
    public enum MovieNightTheme
    {
        /// <summary>All movies from a single genre.</summary>
        GenreFocus,
        /// <summary>A curated mix across multiple genres.</summary>
        GenreMix,
        /// <summary>Movies from a specific decade.</summary>
        DecadeFocus,
        /// <summary>Highly rated films chosen by critic scores.</summary>
        CriticsChoice,
        /// <summary>Popular titles based on rental frequency and ratings.</summary>
        FanFavorites,
        /// <summary>Underappreciated films with high quality but low rental counts.</summary>
        HiddenGems,
        /// <summary>Recently added titles to the catalog.</summary>
        NewReleases,
        /// <summary>Random curated selection for an element of surprise.</summary>
        SurpriseMe
    }

    /// <summary>
    /// Input parameters for generating a movie night plan.
    /// Specifies theme, constraints, and scheduling preferences.
    /// </summary>
    public class MovieNightRequest
    {
        /// <summary>Curation theme that guides film selection. Default: SurpriseMe.</summary>
        public MovieNightTheme Theme { get; set; } = MovieNightTheme.SurpriseMe;

        /// <summary>Optional genre filter (used with GenreFocus theme).</summary>
        public Genre? Genre { get; set; }

        /// <summary>Optional decade filter (e.g. 1990 for '90s films). Used with DecadeFocus.</summary>
        public int? Decade { get; set; }

        /// <summary>Number of movies to include in the plan. Default: 3.</summary>
        public int MovieCount { get; set; } = 3;

        /// <summary>Target total runtime in minutes (including breaks). Default: 120.</summary>
        public int EstimatedRuntimeMinutes { get; set; } = 120;

        /// <summary>Optional customer ID for personalized recommendations based on history.</summary>
        public int? CustomerId { get; set; }

        /// <summary>Optional start time for scheduling. Affects slot times and end estimate.</summary>
        public DateTime? StartTime { get; set; }

        /// <summary>Minutes of break between movies. Default: 15.</summary>
        public int BreakMinutes { get; set; } = 15;
    }

    /// <summary>
    /// A complete movie night plan with an ordered schedule of films,
    /// timing information, snack suggestions, and availability status.
    /// </summary>
    public class MovieNightPlan
    {
        /// <summary>Generated title for the movie night (e.g. "90s Classics Night").</summary>
        public string Title { get; set; }

        /// <summary>Theme used to generate this plan.</summary>
        public MovieNightTheme Theme { get; set; }

        /// <summary>Ordered list of movie time slots.</summary>
        public List<MovieNightSlot> Slots { get; set; } = new List<MovieNightSlot>();

        /// <summary>Total duration in minutes including movies and breaks.</summary>
        public int TotalMinutes { get; set; }

        /// <summary>Human-readable total duration (e.g. "3h 45m").</summary>
        public string TotalDuration { get; set; }

        /// <summary>Estimated end time based on start time and total duration.</summary>
        public DateTime EstimatedEndTime { get; set; }

        /// <summary>Theme-appropriate snack recommendations.</summary>
        public List<string> SnackSuggestions { get; set; } = new List<string>();

        /// <summary>Human-readable description of the selected theme.</summary>
        public string ThemeDescription { get; set; }

        /// <summary>Number of movies included in the plan.</summary>
        public int MovieCount { get; set; }

        /// <summary>Number of selected movies currently available for rental.</summary>
        public int AvailableCount { get; set; }

        /// <summary>Note about availability if some movies are currently rented out.</summary>
        public string AvailabilityNote { get; set; }
    }

    /// <summary>
    /// A single time slot within a movie night plan, representing one film
    /// with its scheduled start/end times and availability.
    /// </summary>
    public class MovieNightSlot
    {
        /// <summary>1-based position in the viewing order.</summary>
        public int Order { get; set; }

        /// <summary>The movie scheduled for this slot.</summary>
        public Movie Movie { get; set; }

        /// <summary>Scheduled start time for this movie.</summary>
        public DateTime StartTime { get; set; }

        /// <summary>Scheduled end time (start + runtime).</summary>
        public DateTime EndTime { get; set; }

        /// <summary>Runtime of this movie in minutes.</summary>
        public int RuntimeMinutes { get; set; }

        /// <summary>Whether this movie is currently available for rental.</summary>
        public bool IsAvailable { get; set; }

        /// <summary>Optional note about this slot (e.g. "Director's cut available").</summary>
        public string SlotNote { get; set; }

        /// <summary>Suggested break activity before the next movie.</summary>
        public string BreakSuggestion { get; set; }
    }
}
