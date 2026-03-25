using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// A trivia fact about a movie — behind-the-scenes info, easter eggs,
    /// production trivia, or fun facts that enrich the viewing experience.
    /// </summary>
    public class TriviaFact
    {
        public int Id { get; set; }
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public string Fact { get; set; }
        public string Category { get; set; } // e.g. "Behind the Scenes", "Easter Egg", "Cast", "Production", "Box Office"
        public string Source { get; set; } // optional attribution
        public int SubmittedByCustomerId { get; set; }
        public string SubmittedByName { get; set; }
        public DateTime SubmittedAt { get; set; } = DateTime.Now;
        public int Likes { get; set; }
        public bool IsVerified { get; set; }
    }

    public static class TriviaCategories
    {
        public static readonly IReadOnlyList<string> All = new[]
        {
            "Behind the Scenes",
            "Easter Egg",
            "Cast & Crew",
            "Production",
            "Box Office",
            "Deleted Scenes",
            "Soundtrack",
            "Cultural Impact",
            "Fun Fact",
            "Mistakes & Goofs"
        };
    }
}
