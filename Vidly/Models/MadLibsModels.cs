using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// A Mad Libs template derived from a movie plot.
    /// </summary>
    public class MadLibsTemplate
    {
        public int Id { get; set; }
        public string MovieName { get; set; }
        public int Year { get; set; }
        public string Genre { get; set; }

        /// <summary>
        /// Template text with placeholders like {noun}, {verb}, {adjective}, etc.
        /// </summary>
        public string TemplateText { get; set; }

        /// <summary>
        /// The original un-blanked plot summary.
        /// </summary>
        public string OriginalText { get; set; }

        /// <summary>
        /// Ordered list of blank types the player must fill in.
        /// </summary>
        public List<MadLibsBlank> Blanks { get; set; } = new List<MadLibsBlank>();
    }

    /// <summary>
    /// A single blank in a Mad Libs template.
    /// </summary>
    public class MadLibsBlank
    {
        public int Index { get; set; }
        public string WordType { get; set; } // e.g. "Noun", "Verb (past tense)", "Adjective"
        public string Placeholder { get; set; } // e.g. "{noun1}"
    }

    /// <summary>
    /// A completed Mad Libs story.
    /// </summary>
    public class MadLibsResult
    {
        public MadLibsTemplate Template { get; set; }
        public Dictionary<string, string> FilledWords { get; set; } = new Dictionary<string, string>();
        public string GeneratedStory { get; set; }
    }
}
