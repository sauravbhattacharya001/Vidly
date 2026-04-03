using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Service for movie soundtrack discovery — generates themed playlists,
    /// maps genres to iconic soundtrack styles, and provides mood-based
    /// music suggestions tied to the movie catalog.
    /// </summary>
    public class MovieSoundtrackService
    {
        private readonly IMovieRepository _movieRepo;
        private static readonly Random _random = new Random();

        public MovieSoundtrackService(IMovieRepository movieRepo)
        {
            _movieRepo = movieRepo ?? throw new ArgumentNullException(nameof(movieRepo));
        }

        // ── Data types ────────────────────────────────────────────────

        public enum Mood
        {
            Energetic,
            Melancholic,
            Romantic,
            Suspenseful,
            Uplifting,
            Nostalgic,
            Chill,
            Epic
        }

        public class SoundtrackProfile
        {
            public Genre Genre { get; set; }
            public string MusicStyle { get; set; }
            public string[] IconicComposers { get; set; }
            public string[] SignatureInstruments { get; set; }
            public int TypicalBpm { get; set; }
            public string Description { get; set; }
        }

        public class PlaylistEntry
        {
            public string TrackTitle { get; set; }
            public string Artist { get; set; }
            public string Album { get; set; }
            public int DurationSeconds { get; set; }
            public Mood PrimaryMood { get; set; }
            public Genre AssociatedGenre { get; set; }
        }

        public class MoodPlaylist
        {
            public Mood Mood { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public List<PlaylistEntry> Tracks { get; set; }
            public int TotalDurationSeconds => Tracks?.Sum(t => t.DurationSeconds) ?? 0;
            public string FormattedDuration
            {
                get
                {
                    var ts = TimeSpan.FromSeconds(TotalDurationSeconds);
                    return ts.Hours > 0
                        ? $"{ts.Hours}h {ts.Minutes}m"
                        : $"{ts.Minutes}m {ts.Seconds}s";
                }
            }
        }

        public class MovieSoundtrackSuggestion
        {
            public Movie Movie { get; set; }
            public SoundtrackProfile Profile { get; set; }
            public Mood SuggestedMood { get; set; }
            public List<PlaylistEntry> RecommendedTracks { get; set; }
        }

        public class SoundtrackQuizQuestion
        {
            public string Question { get; set; }
            public string[] Options { get; set; }
            public int CorrectIndex { get; set; }
            public string Explanation { get; set; }
        }

        // ── Genre → Soundtrack Profiles ───────────────────────────────

        private static readonly Dictionary<Genre, SoundtrackProfile> _profiles
            = new Dictionary<Genre, SoundtrackProfile>
        {
            [Genre.Action] = new SoundtrackProfile
            {
                Genre = Genre.Action,
                MusicStyle = "Orchestral with heavy percussion and brass",
                IconicComposers = new[] { "Hans Zimmer", "Junkie XL", "Brian Tyler", "Steve Jablonsky" },
                SignatureInstruments = new[] { "Timpani", "French Horn", "Electric Guitar", "Taiko Drums" },
                TypicalBpm = 140,
                Description = "High-energy scores with driving rhythms, soaring brass, and explosive crescendos"
            },
            [Genre.Comedy] = new SoundtrackProfile
            {
                Genre = Genre.Comedy,
                MusicStyle = "Light orchestral with quirky woodwinds",
                IconicComposers = new[] { "David Newman", "Theodore Shapiro", "Christophe Beck", "Rolfe Kent" },
                SignatureInstruments = new[] { "Ukulele", "Pizzicato Strings", "Xylophone", "Clarinet" },
                TypicalBpm = 120,
                Description = "Playful, bouncy melodies with comedic timing and whimsical instrumentation"
            },
            [Genre.Drama] = new SoundtrackProfile
            {
                Genre = Genre.Drama,
                MusicStyle = "String-heavy orchestral with piano",
                IconicComposers = new[] { "Thomas Newman", "Alexandre Desplat", "Max Richter", "Ryuichi Sakamoto" },
                SignatureInstruments = new[] { "Piano", "Cello", "Violin", "Oboe" },
                TypicalBpm = 72,
                Description = "Emotional, introspective scores with rich string arrangements and subtle dynamics"
            },
            [Genre.Horror] = new SoundtrackProfile
            {
                Genre = Genre.Horror,
                MusicStyle = "Dissonant and atmospheric with sound design",
                IconicComposers = new[] { "John Carpenter", "Marco Beltrami", "Christopher Young", "Colin Stetson" },
                SignatureInstruments = new[] { "Synthesizer", "Prepared Piano", "Waterphone", "Bowed Metal" },
                TypicalBpm = 60,
                Description = "Unsettling textures, sudden stingers, and tension-building drones"
            },
            [Genre.SciFi] = new SoundtrackProfile
            {
                Genre = Genre.SciFi,
                MusicStyle = "Electronic-orchestral hybrid",
                IconicComposers = new[] { "Vangelis", "Clint Mansell", "Johann Johannsson", "Ben Salisbury" },
                SignatureInstruments = new[] { "Synthesizer", "Theremin", "Modular Synth", "Glass Harmonica" },
                TypicalBpm = 90,
                Description = "Futuristic soundscapes blending analog synths with orchestral elements"
            },
            [Genre.Animation] = new SoundtrackProfile
            {
                Genre = Genre.Animation,
                MusicStyle = "Full orchestral with memorable themes",
                IconicComposers = new[] { "Alan Menken", "Michael Giacchino", "Patrick Doyle", "Mark Mothersbaugh" },
                SignatureInstruments = new[] { "Full Orchestra", "Celesta", "Harp", "Flute" },
                TypicalBpm = 110,
                Description = "Melodic, colorful orchestrations with sing-along themes and emotional depth"
            },
            [Genre.Thriller] = new SoundtrackProfile
            {
                Genre = Genre.Thriller,
                MusicStyle = "Minimalist tension with rhythmic pulse",
                IconicComposers = new[] { "Trent Reznor", "Atticus Ross", "Cliff Martinez", "Daniel Pemberton" },
                SignatureInstruments = new[] { "Bass Synth", "Muted Strings", "Prepared Piano", "Electronics" },
                TypicalBpm = 85,
                Description = "Pulsing, tense underscore with industrial textures and psychological unease"
            },
            [Genre.Romance] = new SoundtrackProfile
            {
                Genre = Genre.Romance,
                MusicStyle = "Lyrical strings with acoustic guitar",
                IconicComposers = new[] { "Rachel Portman", "James Horner", "Dario Marianelli", "Gabriel Yared" },
                SignatureInstruments = new[] { "Acoustic Guitar", "Piano", "Violin Solo", "Harp" },
                TypicalBpm = 76,
                Description = "Warm, sweeping melodies with intimate solo passages and lush harmonies"
            },
            [Genre.Documentary] = new SoundtrackProfile
            {
                Genre = Genre.Documentary,
                MusicStyle = "Ambient with world music influences",
                IconicComposers = new[] { "Philip Glass", "Ludovico Einaudi", "Lisa Gerrard", "Dustin O'Halloran" },
                SignatureInstruments = new[] { "Piano", "Ethnic Percussion", "Duduk", "Ambient Pads" },
                TypicalBpm = 68,
                Description = "Contemplative, atmospheric music that supports storytelling without overwhelming it"
            },
            [Genre.Adventure] = new SoundtrackProfile
            {
                Genre = Genre.Adventure,
                MusicStyle = "Grand orchestral with heroic themes",
                IconicComposers = new[] { "John Williams", "Alan Silvestri", "James Newton Howard", "Patrick Doyle" },
                SignatureInstruments = new[] { "French Horn", "Trumpet", "Snare Drum", "Full Orchestra" },
                TypicalBpm = 130,
                Description = "Bold, sweeping scores with iconic leitmotifs and triumphant fanfares"
            }
        };

        // ── Mood → Track Library ──────────────────────────────────────

        private static readonly Dictionary<Mood, PlaylistEntry[]> _trackLibrary
            = new Dictionary<Mood, PlaylistEntry[]>
        {
            [Mood.Energetic] = new[]
            {
                new PlaylistEntry { TrackTitle = "Thunderstruck Overture", Artist = "Hans Zimmer", Album = "Action Essentials", DurationSeconds = 245, PrimaryMood = Mood.Energetic, AssociatedGenre = Genre.Action },
                new PlaylistEntry { TrackTitle = "Chase Through the City", Artist = "Brian Tyler", Album = "Urban Pursuit", DurationSeconds = 198, PrimaryMood = Mood.Energetic, AssociatedGenre = Genre.Action },
                new PlaylistEntry { TrackTitle = "Nitro Rush", Artist = "Junkie XL", Album = "Speed Demons", DurationSeconds = 212, PrimaryMood = Mood.Energetic, AssociatedGenre = Genre.Action },
                new PlaylistEntry { TrackTitle = "Galactic Dash", Artist = "Michael Giacchino", Album = "Star Runners", DurationSeconds = 267, PrimaryMood = Mood.Energetic, AssociatedGenre = Genre.Adventure },
                new PlaylistEntry { TrackTitle = "Breakneck Tempo", Artist = "Steve Jablonsky", Album = "Mechanical Fury", DurationSeconds = 183, PrimaryMood = Mood.Energetic, AssociatedGenre = Genre.SciFi },
            },
            [Mood.Melancholic] = new[]
            {
                new PlaylistEntry { TrackTitle = "Autumn Leaves Fall", Artist = "Thomas Newman", Album = "Quiet Moments", DurationSeconds = 312, PrimaryMood = Mood.Melancholic, AssociatedGenre = Genre.Drama },
                new PlaylistEntry { TrackTitle = "The Empty Chair", Artist = "Max Richter", Album = "Solitude", DurationSeconds = 284, PrimaryMood = Mood.Melancholic, AssociatedGenre = Genre.Drama },
                new PlaylistEntry { TrackTitle = "Rain on the Window", Artist = "Ryuichi Sakamoto", Album = "Still Life", DurationSeconds = 256, PrimaryMood = Mood.Melancholic, AssociatedGenre = Genre.Drama },
                new PlaylistEntry { TrackTitle = "Memory Lane", Artist = "Ludovico Einaudi", Album = "Chapters", DurationSeconds = 298, PrimaryMood = Mood.Melancholic, AssociatedGenre = Genre.Documentary },
                new PlaylistEntry { TrackTitle = "Faded Photograph", Artist = "Dustin O'Halloran", Album = "Echoes", DurationSeconds = 271, PrimaryMood = Mood.Melancholic, AssociatedGenre = Genre.Romance },
            },
            [Mood.Romantic] = new[]
            {
                new PlaylistEntry { TrackTitle = "First Dance", Artist = "Rachel Portman", Album = "Love Letters", DurationSeconds = 234, PrimaryMood = Mood.Romantic, AssociatedGenre = Genre.Romance },
                new PlaylistEntry { TrackTitle = "Sunset Promenade", Artist = "Dario Marianelli", Album = "Golden Hour", DurationSeconds = 267, PrimaryMood = Mood.Romantic, AssociatedGenre = Genre.Romance },
                new PlaylistEntry { TrackTitle = "Your Eyes", Artist = "Gabriel Yared", Album = "Heartstrings", DurationSeconds = 289, PrimaryMood = Mood.Romantic, AssociatedGenre = Genre.Romance },
                new PlaylistEntry { TrackTitle = "Moonlit Garden", Artist = "Alexandre Desplat", Album = "Enchantment", DurationSeconds = 245, PrimaryMood = Mood.Romantic, AssociatedGenre = Genre.Drama },
                new PlaylistEntry { TrackTitle = "Together at Last", Artist = "James Horner", Album = "Eternal", DurationSeconds = 312, PrimaryMood = Mood.Romantic, AssociatedGenre = Genre.Romance },
            },
            [Mood.Suspenseful] = new[]
            {
                new PlaylistEntry { TrackTitle = "Shadows Approach", Artist = "Trent Reznor", Album = "Dark Web", DurationSeconds = 278, PrimaryMood = Mood.Suspenseful, AssociatedGenre = Genre.Thriller },
                new PlaylistEntry { TrackTitle = "The Watcher", Artist = "John Carpenter", Album = "Night Vision", DurationSeconds = 234, PrimaryMood = Mood.Suspenseful, AssociatedGenre = Genre.Horror },
                new PlaylistEntry { TrackTitle = "Ticking Clock", Artist = "Cliff Martinez", Album = "Deadline", DurationSeconds = 198, PrimaryMood = Mood.Suspenseful, AssociatedGenre = Genre.Thriller },
                new PlaylistEntry { TrackTitle = "Behind the Door", Artist = "Colin Stetson", Album = "Dread", DurationSeconds = 256, PrimaryMood = Mood.Suspenseful, AssociatedGenre = Genre.Horror },
                new PlaylistEntry { TrackTitle = "Pulse", Artist = "Atticus Ross", Album = "Signal", DurationSeconds = 223, PrimaryMood = Mood.Suspenseful, AssociatedGenre = Genre.Thriller },
            },
            [Mood.Uplifting] = new[]
            {
                new PlaylistEntry { TrackTitle = "New Horizons", Artist = "Alan Silvestri", Album = "Dawn", DurationSeconds = 287, PrimaryMood = Mood.Uplifting, AssociatedGenre = Genre.Adventure },
                new PlaylistEntry { TrackTitle = "Rise Up", Artist = "Michael Giacchino", Album = "Triumph", DurationSeconds = 234, PrimaryMood = Mood.Uplifting, AssociatedGenre = Genre.Animation },
                new PlaylistEntry { TrackTitle = "Bright Morning", Artist = "Christophe Beck", Album = "Fresh Start", DurationSeconds = 198, PrimaryMood = Mood.Uplifting, AssociatedGenre = Genre.Comedy },
                new PlaylistEntry { TrackTitle = "Victory March", Artist = "John Williams", Album = "Champions", DurationSeconds = 312, PrimaryMood = Mood.Uplifting, AssociatedGenre = Genre.Adventure },
                new PlaylistEntry { TrackTitle = "Cloud Nine", Artist = "Mark Mothersbaugh", Album = "Joy", DurationSeconds = 187, PrimaryMood = Mood.Uplifting, AssociatedGenre = Genre.Animation },
            },
            [Mood.Nostalgic] = new[]
            {
                new PlaylistEntry { TrackTitle = "Summer of '85", Artist = "Alan Menken", Album = "Golden Days", DurationSeconds = 267, PrimaryMood = Mood.Nostalgic, AssociatedGenre = Genre.Animation },
                new PlaylistEntry { TrackTitle = "Homecoming", Artist = "Thomas Newman", Album = "Return", DurationSeconds = 298, PrimaryMood = Mood.Nostalgic, AssociatedGenre = Genre.Drama },
                new PlaylistEntry { TrackTitle = "Old Photographs", Artist = "Philip Glass", Album = "Memoir", DurationSeconds = 312, PrimaryMood = Mood.Nostalgic, AssociatedGenre = Genre.Documentary },
                new PlaylistEntry { TrackTitle = "Childhood Theme", Artist = "Rachel Portman", Album = "Innocence", DurationSeconds = 234, PrimaryMood = Mood.Nostalgic, AssociatedGenre = Genre.Romance },
                new PlaylistEntry { TrackTitle = "The Way We Were", Artist = "James Horner", Album = "Reflections", DurationSeconds = 278, PrimaryMood = Mood.Nostalgic, AssociatedGenre = Genre.Drama },
            },
            [Mood.Chill] = new[]
            {
                new PlaylistEntry { TrackTitle = "Floating", Artist = "Vangelis", Album = "Cosmos", DurationSeconds = 345, PrimaryMood = Mood.Chill, AssociatedGenre = Genre.SciFi },
                new PlaylistEntry { TrackTitle = "Deep Blue", Artist = "Lisa Gerrard", Album = "Ocean", DurationSeconds = 312, PrimaryMood = Mood.Chill, AssociatedGenre = Genre.Documentary },
                new PlaylistEntry { TrackTitle = "Drift", Artist = "Johann Johannsson", Album = "Stillness", DurationSeconds = 267, PrimaryMood = Mood.Chill, AssociatedGenre = Genre.SciFi },
                new PlaylistEntry { TrackTitle = "Evening Glow", Artist = "Ludovico Einaudi", Album = "Dusk", DurationSeconds = 289, PrimaryMood = Mood.Chill, AssociatedGenre = Genre.Documentary },
                new PlaylistEntry { TrackTitle = "Weightless", Artist = "Dustin O'Halloran", Album = "Ambient", DurationSeconds = 334, PrimaryMood = Mood.Chill, AssociatedGenre = Genre.Drama },
            },
            [Mood.Epic] = new[]
            {
                new PlaylistEntry { TrackTitle = "Titans Rise", Artist = "John Williams", Album = "Legends", DurationSeconds = 378, PrimaryMood = Mood.Epic, AssociatedGenre = Genre.Adventure },
                new PlaylistEntry { TrackTitle = "The Final Stand", Artist = "Hans Zimmer", Album = "Valor", DurationSeconds = 345, PrimaryMood = Mood.Epic, AssociatedGenre = Genre.Action },
                new PlaylistEntry { TrackTitle = "Across the Stars", Artist = "James Newton Howard", Album = "Destiny", DurationSeconds = 312, PrimaryMood = Mood.Epic, AssociatedGenre = Genre.Adventure },
                new PlaylistEntry { TrackTitle = "Kingdom Come", Artist = "Alan Silvestri", Album = "Ascension", DurationSeconds = 289, PrimaryMood = Mood.Epic, AssociatedGenre = Genre.Adventure },
                new PlaylistEntry { TrackTitle = "Into the Abyss", Artist = "Clint Mansell", Album = "Depths", DurationSeconds = 334, PrimaryMood = Mood.Epic, AssociatedGenre = Genre.SciFi },
            }
        };

        // ── Mood descriptions ─────────────────────────────────────────

        private static readonly Dictionary<Mood, (string Title, string Desc)> _moodMeta
            = new Dictionary<Mood, (string, string)>
        {
            [Mood.Energetic] = ("Adrenaline Rush", "High-octane tracks to get your heart pumping"),
            [Mood.Melancholic] = ("Beautiful Sadness", "Hauntingly emotional pieces for quiet reflection"),
            [Mood.Romantic] = ("Love & Longing", "Sweeping melodies for matters of the heart"),
            [Mood.Suspenseful] = ("Edge of Your Seat", "Tension-building scores that keep you guessing"),
            [Mood.Uplifting] = ("Sunshine & Triumph", "Feel-good tracks that lift your spirits"),
            [Mood.Nostalgic] = ("Remember When", "Warm, bittersweet melodies from simpler times"),
            [Mood.Chill] = ("Floating Away", "Ambient soundscapes for total relaxation"),
            [Mood.Epic] = ("Legendary Moments", "Grand, sweeping scores for the biggest moments"),
        };

        // ── Public API ────────────────────────────────────────────────

        /// <summary>
        /// Gets the soundtrack profile for a genre.
        /// </summary>
        public SoundtrackProfile GetGenreProfile(Genre genre)
        {
            return _profiles.TryGetValue(genre, out var profile)
                ? profile
                : null;
        }

        /// <summary>
        /// Gets all genre soundtrack profiles.
        /// </summary>
        public IReadOnlyList<SoundtrackProfile> GetAllProfiles()
        {
            return _profiles.Values.ToList();
        }

        /// <summary>
        /// Generates a mood-based playlist with the given number of tracks.
        /// </summary>
        public MoodPlaylist GenerateMoodPlaylist(Mood mood, int trackCount = 5)
        {
            trackCount = Math.Max(1, Math.Min(trackCount, 20));

            var meta = _moodMeta[mood];
            var tracks = _trackLibrary[mood];

            // Shuffle and take requested count (repeat if needed)
            var selected = new List<PlaylistEntry>();
            var shuffled = tracks.OrderBy(_ => _random.Next()).ToList();
            while (selected.Count < trackCount)
            {
                foreach (var t in shuffled)
                {
                    if (selected.Count >= trackCount) break;
                    selected.Add(t);
                }
            }

            return new MoodPlaylist
            {
                Mood = mood,
                Title = meta.Title,
                Description = meta.Desc,
                Tracks = selected
            };
        }

        /// <summary>
        /// Returns all available moods.
        /// </summary>
        public IReadOnlyList<Mood> GetAllMoods()
        {
            return Enum.GetValues(typeof(Mood)).Cast<Mood>().ToList();
        }

        /// <summary>
        /// Suggests a soundtrack mood for a given movie based on its genre and rating.
        /// </summary>
        public Mood SuggestMoodForMovie(Movie movie)
        {
            if (movie == null) throw new ArgumentNullException(nameof(movie));

            // Map genre to primary mood
            var genreMoods = new Dictionary<Genre, Mood>
            {
                [Genre.Action] = Mood.Energetic,
                [Genre.Comedy] = Mood.Uplifting,
                [Genre.Drama] = Mood.Melancholic,
                [Genre.Horror] = Mood.Suspenseful,
                [Genre.SciFi] = Mood.Epic,
                [Genre.Animation] = Mood.Uplifting,
                [Genre.Thriller] = Mood.Suspenseful,
                [Genre.Romance] = Mood.Romantic,
                [Genre.Documentary] = Mood.Chill,
                [Genre.Adventure] = Mood.Epic,
            };

            if (movie.Genre.HasValue && genreMoods.TryGetValue(movie.Genre.Value, out var mood))
            {
                // High-rated movies lean epic, low-rated lean nostalgic
                if (movie.Rating.HasValue && movie.Rating.Value >= 5)
                    return Mood.Epic;

                return mood;
            }

            return Mood.Chill; // default
        }

        /// <summary>
        /// Gets a soundtrack suggestion for a specific movie.
        /// </summary>
        public MovieSoundtrackSuggestion GetSuggestionForMovie(int movieId)
        {
            var movie = _movieRepo.Get(movieId);
            if (movie == null) return null;

            var mood = SuggestMoodForMovie(movie);
            var profile = movie.Genre.HasValue ? GetGenreProfile(movie.Genre.Value) : null;
            var playlist = GenerateMoodPlaylist(mood, 3);

            return new MovieSoundtrackSuggestion
            {
                Movie = movie,
                Profile = profile,
                SuggestedMood = mood,
                RecommendedTracks = playlist.Tracks
            };
        }

        /// <summary>
        /// Generates a "Movie Night Mixtape" — a cross-genre playlist
        /// based on what's currently in the catalog.
        /// </summary>
        public MoodPlaylist GenerateMovieNightMixtape()
        {
            var allMovies = _movieRepo.GetAll();
            if (!allMovies.Any())
            {
                return new MoodPlaylist
                {
                    Mood = Mood.Chill,
                    Title = "Empty Theater",
                    Description = "No movies in catalog — here's some ambient vibes",
                    Tracks = _trackLibrary[Mood.Chill].Take(3).ToList()
                };
            }

            // Sample moods from the catalog's genre distribution
            var moodCounts = allMovies
                .Where(m => m.Genre.HasValue)
                .GroupBy(m => SuggestMoodForMovie(m))
                .OrderByDescending(g => g.Count())
                .Take(4)
                .ToList();

            var tracks = new List<PlaylistEntry>();
            foreach (var group in moodCounts)
            {
                var moodTracks = _trackLibrary[group.Key]
                    .OrderBy(_ => _random.Next())
                    .Take(2);
                tracks.AddRange(moodTracks);
            }

            return new MoodPlaylist
            {
                Mood = moodCounts.First().Key,
                Title = "Movie Night Mixtape",
                Description = $"A curated mix based on your {allMovies.Count()} movie catalog",
                Tracks = tracks
            };
        }

        /// <summary>
        /// Generates a soundtrack trivia quiz with the given number of questions.
        /// </summary>
        public IReadOnlyList<SoundtrackQuizQuestion> GenerateQuiz(int questionCount = 5)
        {
            questionCount = Math.Max(1, Math.Min(questionCount, 10));
            var questions = new List<SoundtrackQuizQuestion>();
            var profiles = _profiles.Values.OrderBy(_ => _random.Next()).ToList();

            // Q type 1: "Which genre uses this instrument?"
            foreach (var p in profiles.Take(questionCount))
            {
                var instrument = p.SignatureInstruments[_random.Next(p.SignatureInstruments.Length)];
                var wrong = profiles
                    .Where(x => x.Genre != p.Genre)
                    .OrderBy(_ => _random.Next())
                    .Take(3)
                    .Select(x => x.Genre.ToString())
                    .ToList();

                var options = wrong.Concat(new[] { p.Genre.ToString() })
                    .OrderBy(_ => _random.Next())
                    .ToArray();

                questions.Add(new SoundtrackQuizQuestion
                {
                    Question = $"Which movie genre typically features the {instrument}?",
                    Options = options,
                    CorrectIndex = Array.IndexOf(options, p.Genre.ToString()),
                    Explanation = $"{p.Genre} soundtracks are known for: {p.Description}"
                });
            }

            return questions.Take(questionCount).ToList();
        }

        /// <summary>
        /// Gets soundtrack stats for the entire catalog.
        /// </summary>
        public SoundtrackCatalogStats GetCatalogStats()
        {
            var movies = _movieRepo.GetAll();
            var genreGroups = movies
                .Where(m => m.Genre.HasValue)
                .GroupBy(m => m.Genre.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var dominantGenre = genreGroups.Any()
                ? genreGroups.OrderByDescending(kv => kv.Value).First().Key
                : (Genre?)null;

            var moodDistribution = movies
                .Where(m => m.Genre.HasValue)
                .GroupBy(m => SuggestMoodForMovie(m))
                .ToDictionary(g => g.Key, g => g.Count());

            return new SoundtrackCatalogStats
            {
                TotalMovies = movies.Count(),
                GenreDistribution = genreGroups,
                DominantGenre = dominantGenre,
                DominantProfile = dominantGenre.HasValue ? GetGenreProfile(dominantGenre.Value) : null,
                MoodDistribution = moodDistribution,
                SuggestedAmbience = dominantGenre.HasValue
                    ? $"Your catalog leans {dominantGenre.Value} — perfect for {_profiles[dominantGenre.Value].MusicStyle.ToLower()}"
                    : "Add some movies to discover your soundtrack personality!"
            };
        }

        public class SoundtrackCatalogStats
        {
            public int TotalMovies { get; set; }
            public Dictionary<Genre, int> GenreDistribution { get; set; }
            public Genre? DominantGenre { get; set; }
            public SoundtrackProfile DominantProfile { get; set; }
            public Dictionary<Mood, int> MoodDistribution { get; set; }
            public string SuggestedAmbience { get; set; }
        }
    }
}
