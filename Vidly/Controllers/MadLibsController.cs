using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Mad Libs — fill in the blanks to create hilarious movie plot remixes.
    /// </summary>
    public class MadLibsController : Controller
    {
        private static readonly List<MadLibsTemplate> Templates = new List<MadLibsTemplate>
        {
            new MadLibsTemplate
            {
                Id = 1,
                MovieName = "The Godfather",
                Year = 1972,
                Genre = "Crime",
                OriginalText = "The aging patriarch of an organized crime dynasty transfers control of his clandestine empire to his reluctant youngest son.",
                TemplateText = "The aging {noun1} of an organized {noun2} dynasty transfers control of his {adjective1} empire to his {adjective2} youngest {noun3}.",
                Blanks = new List<MadLibsBlank>
                {
                    new MadLibsBlank { Index = 0, WordType = "Noun", Placeholder = "{noun1}" },
                    new MadLibsBlank { Index = 1, WordType = "Noun", Placeholder = "{noun2}" },
                    new MadLibsBlank { Index = 2, WordType = "Adjective", Placeholder = "{adjective1}" },
                    new MadLibsBlank { Index = 3, WordType = "Adjective", Placeholder = "{adjective2}" },
                    new MadLibsBlank { Index = 4, WordType = "Noun", Placeholder = "{noun3}" }
                }
            },
            new MadLibsTemplate
            {
                Id = 2,
                MovieName = "Jurassic Park",
                Year = 1993,
                Genre = "Sci-Fi",
                OriginalText = "A wealthy entrepreneur secretly creates a theme park featuring living dinosaurs drawn from prehistoric DNA. When the park's security system breaks down, the visitors must fight to survive.",
                TemplateText = "A {adjective1} entrepreneur secretly creates a {noun1} featuring living {plural_noun1} drawn from prehistoric {noun2}. When the park's {noun3} breaks down, the visitors must {verb1} to survive.",
                Blanks = new List<MadLibsBlank>
                {
                    new MadLibsBlank { Index = 0, WordType = "Adjective", Placeholder = "{adjective1}" },
                    new MadLibsBlank { Index = 1, WordType = "Noun (place)", Placeholder = "{noun1}" },
                    new MadLibsBlank { Index = 2, WordType = "Plural Noun (creatures)", Placeholder = "{plural_noun1}" },
                    new MadLibsBlank { Index = 3, WordType = "Noun", Placeholder = "{noun2}" },
                    new MadLibsBlank { Index = 4, WordType = "Noun", Placeholder = "{noun3}" },
                    new MadLibsBlank { Index = 5, WordType = "Verb", Placeholder = "{verb1}" }
                }
            },
            new MadLibsTemplate
            {
                Id = 3,
                MovieName = "Titanic",
                Year = 1997,
                Genre = "Romance",
                OriginalText = "A seventeen-year-old aristocrat falls in love with a kind but poor artist aboard the luxurious, ill-fated R.M.S. Titanic.",
                TemplateText = "A {adjective1} aristocrat falls in {noun1} with a kind but {adjective2} {occupation1} aboard the {adjective3}, ill-fated R.M.S. {noun2}.",
                Blanks = new List<MadLibsBlank>
                {
                    new MadLibsBlank { Index = 0, WordType = "Adjective", Placeholder = "{adjective1}" },
                    new MadLibsBlank { Index = 1, WordType = "Noun (emotion)", Placeholder = "{noun1}" },
                    new MadLibsBlank { Index = 2, WordType = "Adjective", Placeholder = "{adjective2}" },
                    new MadLibsBlank { Index = 3, WordType = "Occupation", Placeholder = "{occupation1}" },
                    new MadLibsBlank { Index = 4, WordType = "Adjective", Placeholder = "{adjective3}" },
                    new MadLibsBlank { Index = 5, WordType = "Noun (vehicle)", Placeholder = "{noun2}" }
                }
            },
            new MadLibsTemplate
            {
                Id = 4,
                MovieName = "The Matrix",
                Year = 1999,
                Genre = "Sci-Fi",
                OriginalText = "A computer hacker learns from mysterious rebels about the true nature of his reality and his role in the war against its controllers.",
                TemplateText = "A {noun1} {occupation1} learns from {adjective1} rebels about the true nature of his {noun2} and his role in the {noun3} against its {plural_noun1}.",
                Blanks = new List<MadLibsBlank>
                {
                    new MadLibsBlank { Index = 0, WordType = "Noun (tool)", Placeholder = "{noun1}" },
                    new MadLibsBlank { Index = 1, WordType = "Occupation", Placeholder = "{occupation1}" },
                    new MadLibsBlank { Index = 2, WordType = "Adjective", Placeholder = "{adjective1}" },
                    new MadLibsBlank { Index = 3, WordType = "Noun (abstract)", Placeholder = "{noun2}" },
                    new MadLibsBlank { Index = 4, WordType = "Noun (event)", Placeholder = "{noun3}" },
                    new MadLibsBlank { Index = 5, WordType = "Plural Noun (people)", Placeholder = "{plural_noun1}" }
                }
            },
            new MadLibsTemplate
            {
                Id = 5,
                MovieName = "Star Wars",
                Year = 1977,
                Genre = "Sci-Fi",
                OriginalText = "Luke Skywalker joins forces with a Jedi Knight, a cocky pilot, a Wookiee, and two droids to save the galaxy from the Empire's world-destroying battle station.",
                TemplateText = "{name1} joins forces with a {adjective1} Knight, a {adjective2} pilot, a {noun1}, and two {plural_noun1} to save the {noun2} from the Empire's {noun3}-destroying battle station.",
                Blanks = new List<MadLibsBlank>
                {
                    new MadLibsBlank { Index = 0, WordType = "Silly Name", Placeholder = "{name1}" },
                    new MadLibsBlank { Index = 1, WordType = "Adjective", Placeholder = "{adjective1}" },
                    new MadLibsBlank { Index = 2, WordType = "Adjective", Placeholder = "{adjective2}" },
                    new MadLibsBlank { Index = 3, WordType = "Noun (animal)", Placeholder = "{noun1}" },
                    new MadLibsBlank { Index = 4, WordType = "Plural Noun", Placeholder = "{plural_noun1}" },
                    new MadLibsBlank { Index = 5, WordType = "Noun (place)", Placeholder = "{noun2}" },
                    new MadLibsBlank { Index = 6, WordType = "Noun", Placeholder = "{noun3}" }
                }
            },
            new MadLibsTemplate
            {
                Id = 6,
                MovieName = "Finding Nemo",
                Year = 2003,
                Genre = "Animation",
                OriginalText = "After his son is captured in the Great Barrier Reef and taken to Sydney, a timid clownfish sets out on a journey to bring him back.",
                TemplateText = "After his {noun1} is captured in the Great {noun2} and taken to {place1}, a {adjective1} {noun3} sets out on a {noun4} to bring him back.",
                Blanks = new List<MadLibsBlank>
                {
                    new MadLibsBlank { Index = 0, WordType = "Noun (family member)", Placeholder = "{noun1}" },
                    new MadLibsBlank { Index = 1, WordType = "Noun (landmark)", Placeholder = "{noun2}" },
                    new MadLibsBlank { Index = 2, WordType = "Place", Placeholder = "{place1}" },
                    new MadLibsBlank { Index = 3, WordType = "Adjective", Placeholder = "{adjective1}" },
                    new MadLibsBlank { Index = 4, WordType = "Noun (animal)", Placeholder = "{noun3}" },
                    new MadLibsBlank { Index = 5, WordType = "Noun (type of journey)", Placeholder = "{noun4}" }
                }
            },
            new MadLibsTemplate
            {
                Id = 7,
                MovieName = "The Shawshank Redemption",
                Year = 1994,
                Genre = "Drama",
                OriginalText = "Two imprisoned men bond over a number of years, finding solace and eventual redemption through acts of common decency.",
                TemplateText = "Two {adjective1} men bond over a number of {plural_noun1}, finding {noun1} and eventual {noun2} through acts of common {noun3}.",
                Blanks = new List<MadLibsBlank>
                {
                    new MadLibsBlank { Index = 0, WordType = "Adjective", Placeholder = "{adjective1}" },
                    new MadLibsBlank { Index = 1, WordType = "Plural Noun (time periods)", Placeholder = "{plural_noun1}" },
                    new MadLibsBlank { Index = 2, WordType = "Noun (emotion)", Placeholder = "{noun1}" },
                    new MadLibsBlank { Index = 3, WordType = "Noun (abstract)", Placeholder = "{noun2}" },
                    new MadLibsBlank { Index = 4, WordType = "Noun", Placeholder = "{noun3}" }
                }
            },
            new MadLibsTemplate
            {
                Id = 8,
                MovieName = "The Wizard of Oz",
                Year = 1939,
                Genre = "Fantasy",
                OriginalText = "Dorothy Gale is swept away from a farm in Kansas to a magical land of Oz in a tornado and embarks on a quest to see the Wizard who can help her return home.",
                TemplateText = "{name1} is swept away from a {noun1} in Kansas to a {adjective1} land of {place1} in a {noun2} and embarks on a quest to see the {occupation1} who can help her return {noun3}.",
                Blanks = new List<MadLibsBlank>
                {
                    new MadLibsBlank { Index = 0, WordType = "Girl's Name", Placeholder = "{name1}" },
                    new MadLibsBlank { Index = 1, WordType = "Noun (building)", Placeholder = "{noun1}" },
                    new MadLibsBlank { Index = 2, WordType = "Adjective", Placeholder = "{adjective1}" },
                    new MadLibsBlank { Index = 3, WordType = "Silly Place Name", Placeholder = "{place1}" },
                    new MadLibsBlank { Index = 4, WordType = "Noun (weather event)", Placeholder = "{noun2}" },
                    new MadLibsBlank { Index = 5, WordType = "Occupation", Placeholder = "{occupation1}" },
                    new MadLibsBlank { Index = 6, WordType = "Noun (place)", Placeholder = "{noun3}" }
                }
            },
            new MadLibsTemplate
            {
                Id = 9,
                MovieName = "Jaws",
                Year = 1975,
                Genre = "Thriller",
                OriginalText = "A giant great white shark arrives on the shores of a small island community, and a local sheriff, a marine biologist, and an old seafarer set out to stop it.",
                TemplateText = "A {adjective1} great white {noun1} arrives on the shores of a {adjective2} island community, and a local {occupation1}, a marine {occupation2}, and an old {noun2} set out to {verb1} it.",
                Blanks = new List<MadLibsBlank>
                {
                    new MadLibsBlank { Index = 0, WordType = "Adjective (size)", Placeholder = "{adjective1}" },
                    new MadLibsBlank { Index = 1, WordType = "Noun (animal)", Placeholder = "{noun1}" },
                    new MadLibsBlank { Index = 2, WordType = "Adjective", Placeholder = "{adjective2}" },
                    new MadLibsBlank { Index = 3, WordType = "Occupation", Placeholder = "{occupation1}" },
                    new MadLibsBlank { Index = 4, WordType = "Occupation", Placeholder = "{occupation2}" },
                    new MadLibsBlank { Index = 5, WordType = "Noun (person)", Placeholder = "{noun2}" },
                    new MadLibsBlank { Index = 6, WordType = "Verb", Placeholder = "{verb1}" }
                }
            },
            new MadLibsTemplate
            {
                Id = 10,
                MovieName = "Forrest Gump",
                Year = 1994,
                Genre = "Drama",
                OriginalText = "The presidencies of Kennedy and Johnson, the Vietnam War, and other historical events unfold from the perspective of an Alabama man with a low IQ who has good intentions.",
                TemplateText = "The {plural_noun1} of Kennedy and Johnson, the {noun1} War, and other {adjective1} events unfold from the perspective of an Alabama {noun2} with a low {noun3} who has {adjective2} intentions.",
                Blanks = new List<MadLibsBlank>
                {
                    new MadLibsBlank { Index = 0, WordType = "Plural Noun", Placeholder = "{plural_noun1}" },
                    new MadLibsBlank { Index = 1, WordType = "Noun (country)", Placeholder = "{noun1}" },
                    new MadLibsBlank { Index = 2, WordType = "Adjective", Placeholder = "{adjective1}" },
                    new MadLibsBlank { Index = 3, WordType = "Noun (animal)", Placeholder = "{noun2}" },
                    new MadLibsBlank { Index = 4, WordType = "Noun (body part)", Placeholder = "{noun3}" },
                    new MadLibsBlank { Index = 5, WordType = "Adjective", Placeholder = "{adjective2}" }
                }
            }
        };

        private static int _totalPlays = 0;
        private static readonly Random _random = new Random();

        /// <summary>
        /// GET /MadLibs — landing page with template list.
        /// </summary>
        public ActionResult Index()
        {
            var vm = new MadLibsIndexViewModel
            {
                TotalPlays = _totalPlays,
                Templates = Templates.Select(t => new MadLibsTemplateInfo
                {
                    Id = t.Id,
                    MovieName = t.MovieName,
                    Year = t.Year,
                    Genre = t.Genre,
                    BlankCount = t.Blanks.Count
                }).ToList()
            };
            return View(vm);
        }

        /// <summary>
        /// GET /MadLibs/Play/5 — show the fill-in form for a specific template.
        /// GET /MadLibs/Play — random template.
        /// </summary>
        public ActionResult Play(int? id)
        {
            var template = id.HasValue
                ? Templates.FirstOrDefault(t => t.Id == id.Value)
                : Templates[_random.Next(Templates.Count)];

            if (template == null)
                return HttpNotFound();

            var vm = new MadLibsPlayViewModel
            {
                TemplateId = template.Id,
                MovieName = template.MovieName,
                Genre = template.Genre,
                Blanks = template.Blanks
            };
            return View(vm);
        }

        /// <summary>
        /// POST /MadLibs/Generate — fill in the template and show the result.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Generate(int templateId, FormCollection form)
        {
            var template = Templates.FirstOrDefault(t => t.Id == templateId);
            if (template == null)
                return HttpNotFound();

            var filled = new Dictionary<string, string>();
            var story = template.TemplateText;

            foreach (var blank in template.Blanks)
            {
                var value = form[blank.Placeholder] ?? "(blank)";
                if (string.IsNullOrWhiteSpace(value))
                    value = "(blank)";

                value = value.Trim();
                if (value.Length > 50)
                    value = value.Substring(0, 50);

                filled[blank.Placeholder] = value;
                story = story.Replace(blank.Placeholder, "<strong>" + System.Web.HttpUtility.HtmlEncode(value) + "</strong>");
            }

            _totalPlays++;

            var vm = new MadLibsResultViewModel
            {
                MovieName = template.MovieName,
                Year = template.Year,
                Genre = template.Genre,
                GeneratedStory = story,
                OriginalText = template.OriginalText,
                FilledWords = filled,
                TemplateId = template.Id
            };
            return View(vm);
        }

        /// <summary>
        /// GET /MadLibs/Random — redirect to a random template.
        /// </summary>
        public ActionResult Random()
        {
            var template = Templates[_random.Next(Templates.Count)];
            return RedirectToAction("Play", new { id = template.Id });
        }
    }
}
