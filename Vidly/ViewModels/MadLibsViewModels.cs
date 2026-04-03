using System.Collections.Generic;

namespace Vidly.ViewModels
{
    public class MadLibsPlayViewModel
    {
        public int TemplateId { get; set; }
        public string MovieName { get; set; }
        public string Genre { get; set; }
        public List<Models.MadLibsBlank> Blanks { get; set; } = new List<Models.MadLibsBlank>();
    }

    public class MadLibsResultViewModel
    {
        public string MovieName { get; set; }
        public int Year { get; set; }
        public string Genre { get; set; }
        public string GeneratedStory { get; set; }
        public string OriginalText { get; set; }
        public Dictionary<string, string> FilledWords { get; set; }
        public int TemplateId { get; set; }
    }

    public class MadLibsIndexViewModel
    {
        public List<MadLibsTemplateInfo> Templates { get; set; } = new List<MadLibsTemplateInfo>();
        public int TotalPlays { get; set; }
    }

    public class MadLibsTemplateInfo
    {
        public int Id { get; set; }
        public string MovieName { get; set; }
        public int Year { get; set; }
        public string Genre { get; set; }
        public int BlankCount { get; set; }
    }
}
