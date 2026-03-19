using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Generates movie bingo cards with genre-specific and universal tropes.
    /// </summary>
    public class BingoService
    {
        private static readonly Random _rng = new Random();

        private static readonly Dictionary<string, List<string>> _tropesByTheme =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["General"] = new List<string>
                {
                    "Character walks away from explosion",
                    "Dramatic slow-motion scene",
                    "Villain monologues too long",
                    "Phone call at the worst time",
                    "Car won't start when fleeing",
                    "Character trips while running",
                    "Montage with upbeat music",
                    "Dramatic reveal with music sting",
                    "\"We're not so different, you and I\"",
                    "Unnecessary love subplot",
                    "Character looks at old photo",
                    "Rain during emotional scene",
                    "Last-second rescue",
                    "\"I have a bad feeling about this\"",
                    "Character removes glasses dramatically",
                    "Slow clap starts",
                    "\"It's quiet... too quiet\"",
                    "Someone says the movie title",
                    "Post-credits scene",
                    "Character slides across car hood",
                    "Dramatic zoom on character's face",
                    "Flashback explains everything",
                    "Mentor dies to motivate hero",
                    "\"We've got company!\"",
                    "Training montage",
                    "Fake-out death",
                    "Character stares out rainy window",
                    "Ticking clock countdown",
                    "\"You just don't get it, do you?\"",
                    "Two characters bump into each other",
                    "Ominous foreshadowing",
                    "Someone hacks in under 30 seconds"
                },
                ["Action"] = new List<string>
                {
                    "Helicopter explosion",
                    "Diving away from fireball",
                    "Unlimited ammo clip",
                    "Hero outnumbered but wins",
                    "Car chase through market",
                    "Building collapses dramatically",
                    "\"Get down!\" shouted",
                    "Hero walks in slow-mo",
                    "Countdown defused at 1 second",
                    "Dramatic rooftop fight",
                    "Vehicle flips through the air",
                    "Hero catches falling person",
                    "Glass shatters cinematically",
                    "\"Go! I'll hold them off!\"",
                    "Improbable physics stunt",
                    "Hero doesn't look at explosion",
                    "Villain has foreign accent",
                    "Dual-wielding weapons",
                    "\"Is that all you got?\"",
                    "Mid-fight witty one-liner",
                    "Dramatic weapon reload",
                    "Someone gets thrown through a wall",
                    "Hero survives fatal fall",
                    "Final boss has multiple phases",
                    "Reinforcements arrive last second"
                },
                ["Comedy"] = new List<string>
                {
                    "Awkward silence moment",
                    "Food fight or food disaster",
                    "Character falls into water",
                    "Misunderstanding drives entire plot",
                    "Breaking the fourth wall",
                    "Spit-take reaction",
                    "\"That's what she said\" moment",
                    "Embarrassing public scene",
                    "Dance scene nobody asked for",
                    "Running gag pays off",
                    "Record scratch freeze frame",
                    "Character talks to camera",
                    "Cringe-worthy lie spirals",
                    "Accidental insult to authority",
                    "Pet causes chaos",
                    "Over-the-top disguise",
                    "Deadpan reaction to chaos",
                    "Someone reads situation wrong",
                    "Unexpected cameo",
                    "Plan that couldn't possibly work... works",
                    "Character oblivious to danger",
                    "Buddy cop dynamic",
                    "Slow-motion ridiculous moment",
                    "Callback to earlier joke",
                    "Credits blooper reel"
                },
                ["Horror"] = new List<string>
                {
                    "Jump scare with loud sound",
                    "\"Let's split up\"",
                    "Cell phone has no signal",
                    "Creepy child appears",
                    "Mirror scare",
                    "Character investigates strange noise",
                    "Power goes out",
                    "Car won't start",
                    "Running upstairs instead of outside",
                    "\"I'll be right back\"",
                    "False scare (it's just a cat)",
                    "Creepy doll or toy",
                    "Bathroom medicine cabinet scare",
                    "Character reads aloud from cursed book",
                    "Flashlight flickers",
                    "Someone falls in the forest",
                    "Basement door opens on its own",
                    "Ghost in background nobody notices",
                    "Final girl survives",
                    "\"Don't go in there!\"",
                    "Shower scene tension",
                    "Old newspaper reveals dark history",
                    "Skeptic gets killed first",
                    "Killer appears behind victim",
                    "Found footage camera glitch"
                },
                ["Romance"] = new List<string>
                {
                    "Meet cute in coffee shop",
                    "Airport chase scene",
                    "Rain kiss",
                    "\"You had me at...\"",
                    "Best friend gives love advice",
                    "Makeover scene",
                    "Dancing in an unlikely place",
                    "Misunderstanding breaks them up",
                    "Grand gesture at the end",
                    "Rival love interest is terrible",
                    "Gazing at each other across room",
                    "Sharing one earphone/umbrella",
                    "Accidental hand touch",
                    "Love letter or confession",
                    "Almost-kiss interrupted",
                    "\"I'm just a girl, standing...\"",
                    "Wedding gets interrupted",
                    "Road trip brings them together",
                    "Cooking together scene",
                    "Dramatic airport/train station farewell",
                    "Character dates wrong person first",
                    "Friends say \"just tell them!\"",
                    "Jealousy scene at party",
                    "Sunset or sunrise confession",
                    "Montage of falling in love"
                },
                ["Sci-Fi"] = new List<string>
                {
                    "\"That's impossible!\" (narrator: it wasn't)",
                    "AI becomes self-aware",
                    "Dramatic airlock scene",
                    "Time paradox explained badly",
                    "Alien speaks perfect English",
                    "\"We're not alone\"",
                    "Holographic display interface",
                    "Character wakes from cryo-sleep",
                    "Self-destruct sequence initiated",
                    "Ship shakes, everyone lurches",
                    "\"Enhance!\" on blurry image",
                    "FTL travel with cool visual",
                    "Robot develops feelings",
                    "Planet looks like Earth but isn't",
                    "\"The readings are off the charts!\"",
                    "Dramatic space walk",
                    "Last-second warp/jump escape",
                    "\"I'm reading something on sensors\"",
                    "Ancient alien technology discovered",
                    "Scientist ignores safety protocols",
                    "Someone does math to save everyone",
                    "Corporate villain wants to weaponize it",
                    "Parallel universe/timeline twist",
                    "\"We have to go back!\"",
                    "Final sacrifice to save crew"
                }
            };

        /// <summary>
        /// Returns the available bingo themes.
        /// </summary>
        public IReadOnlyList<string> GetThemes()
        {
            return _tropesByTheme.Keys.ToList().AsReadOnly();
        }

        /// <summary>
        /// Generate a bingo card for the given request.
        /// </summary>
        public BingoCard Generate(BingoRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var theme = string.IsNullOrWhiteSpace(request.Theme) ? "General" : request.Theme;
            if (!_tropesByTheme.ContainsKey(theme))
                theme = "General";

            // Combine theme-specific tropes with some general ones for variety
            var pool = new List<string>(_tropesByTheme[theme]);
            if (!theme.Equals("General", StringComparison.OrdinalIgnoreCase))
            {
                var general = _tropesByTheme["General"];
                var extras = general.OrderBy(_ => _rng.Next()).Take(10).ToList();
                pool.AddRange(extras);
            }

            // Need 24 unique cells (25 minus free space, or 25 if no free space)
            var needed = request.IncludeFreeSpace ? 24 : 25;
            var selected = pool.Distinct().OrderBy(_ => _rng.Next()).Take(needed).ToList();

            // Pad if we don't have enough
            while (selected.Count < needed)
                selected.Add($"Trope #{selected.Count + 1}");

            var card = new BingoCard
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                Title = $"{theme} Movie Bingo",
                Genre = request.Genre,
                Theme = theme,
                CreatedAt = DateTime.Now
            };

            int idx = 0;
            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 5; c++)
                {
                    if (request.IncludeFreeSpace && r == 2 && c == 2)
                    {
                        card.Cells.Add(new BingoCell
                        {
                            Row = r,
                            Col = c,
                            Text = "FREE",
                            IsFreeSpace = true,
                            IsMarked = true
                        });
                    }
                    else
                    {
                        card.Cells.Add(new BingoCell
                        {
                            Row = r,
                            Col = c,
                            Text = selected[idx++],
                            IsFreeSpace = false,
                            IsMarked = false
                        });
                    }
                }
            }

            return card;
        }

        /// <summary>
        /// Map a Genre enum to a bingo theme name.
        /// </summary>
        public string GenreToTheme(Genre? genre)
        {
            if (!genre.HasValue) return "General";
            switch (genre.Value)
            {
                case Genre.Action:
                case Genre.Adventure:
                    return "Action";
                case Genre.Comedy:
                case Genre.Animation:
                    return "Comedy";
                case Genre.Horror:
                case Genre.Thriller:
                    return "Horror";
                case Genre.Romance:
                    return "Romance";
                case Genre.SciFi:
                    return "Sci-Fi";
                default:
                    return "General";
            }
        }
    }
}
