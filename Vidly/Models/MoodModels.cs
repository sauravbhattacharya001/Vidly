using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a viewer mood that maps to recommended genres and movie traits.
    /// </summary>
    public enum Mood
    {
        [Display(Name = "😄 Happy & Upbeat")]
        Happy = 1,

        [Display(Name = "😢 Sad & Reflective")]
        Sad = 2,

        [Display(Name = "😰 Stressed & Anxious")]
        Stressed = 3,

        [Display(Name = "🤩 Adventurous & Bold")]
        Adventurous = 4,

        [Display(Name = "😴 Tired & Chill")]
        Tired = 5,

        [Display(Name = "🤔 Curious & Intellectual")]
        Curious = 6,

        [Display(Name = "💕 Romantic")]
        Romantic = 7,

        [Display(Name = "😱 Thrill-Seeking")]
        ThrillSeeking = 8,

        [Display(Name = "👨‍👩‍👧‍👦 Family Time")]
        FamilyTime = 9,

        [Display(Name = "😂 Need a Laugh")]
        NeedALaugh = 10
    }

    /// <summary>
    /// Describes a mood with its genre affinities and display properties.
    /// </summary>
    public class MoodProfile
    {
        public Mood Mood { get; set; }
        public string DisplayName { get; set; }
        public string Emoji { get; set; }
        public string Description { get; set; }
        public string ColorHex { get; set; }
        public List<Genre> PreferredGenres { get; set; } = new List<Genre>();
        public int? MinRating { get; set; }
    }

    /// <summary>
    /// A movie recommendation with a relevance score based on mood matching.
    /// </summary>
    public class MoodRecommendation
    {
        public Movie Movie { get; set; }
        public double RelevanceScore { get; set; }
        public string MatchReason { get; set; }
    }

    /// <summary>
    /// View model for the mood matcher results page.
    /// </summary>
    public class MoodMatchResult
    {
        public MoodProfile SelectedMood { get; set; }
        public List<MoodRecommendation> Recommendations { get; set; } = new List<MoodRecommendation>();
        public int TotalMoviesScanned { get; set; }
    }
}
