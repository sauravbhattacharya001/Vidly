using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// MPAA-style content rating for movies.
    /// </summary>
    public enum ContentRating
    {
        /// <summary>General audiences — all ages admitted.</summary>
        G = 0,
        /// <summary>Parental guidance suggested — some material may not be suitable for children.</summary>
        PG = 1,
        /// <summary>Parents strongly cautioned — some material may be inappropriate for children under 13.</summary>
        PG13 = 2,
        /// <summary>Restricted — under 17 requires accompanying parent or adult guardian.</summary>
        R = 3,
        /// <summary>Adults only — no one 17 and under admitted.</summary>
        NC17 = 4,
        /// <summary>Not yet rated.</summary>
        Unrated = 5
    }

    /// <summary>
    /// Content advisory flags for detailed content information.
    /// </summary>
    [Flags]
    public enum ContentAdvisory
    {
        None = 0,
        Violence = 1,
        Language = 2,
        NudityOrSexualContent = 4,
        DrugUse = 8,
        ScaryScenes = 16,
        ThematicElements = 32,
        SmokingOrAlcohol = 64,
        GamblingDepictions = 128
    }

    /// <summary>
    /// Assigns a content rating and advisories to a movie.
    /// </summary>
    public class MovieContentProfile
    {
        public int MovieId { get; set; }
        public ContentRating Rating { get; set; } = ContentRating.Unrated;
        public ContentAdvisory Advisories { get; set; } = ContentAdvisory.None;
        public string CustomNote { get; set; }

        /// <summary>
        /// Display-friendly rating label.
        /// </summary>
        public string RatingLabel
        {
            get
            {
                return Rating switch
                {
                    ContentRating.G => "G",
                    ContentRating.PG => "PG",
                    ContentRating.PG13 => "PG-13",
                    ContentRating.R => "R",
                    ContentRating.NC17 => "NC-17",
                    ContentRating.Unrated => "NR",
                    _ => "NR"
                };
            }
        }

        /// <summary>
        /// List of active advisory labels.
        /// </summary>
        public IReadOnlyList<string> AdvisoryLabels
        {
            get
            {
                var labels = new List<string>();
                if (Advisories.HasFlag(ContentAdvisory.Violence)) labels.Add("Violence");
                if (Advisories.HasFlag(ContentAdvisory.Language)) labels.Add("Strong Language");
                if (Advisories.HasFlag(ContentAdvisory.NudityOrSexualContent)) labels.Add("Nudity/Sexual Content");
                if (Advisories.HasFlag(ContentAdvisory.DrugUse)) labels.Add("Drug Use");
                if (Advisories.HasFlag(ContentAdvisory.ScaryScenes)) labels.Add("Scary Scenes");
                if (Advisories.HasFlag(ContentAdvisory.ThematicElements)) labels.Add("Thematic Elements");
                if (Advisories.HasFlag(ContentAdvisory.SmokingOrAlcohol)) labels.Add("Smoking/Alcohol");
                if (Advisories.HasFlag(ContentAdvisory.GamblingDepictions)) labels.Add("Gambling");
                return labels;
            }
        }
    }

    /// <summary>
    /// Parental control profile for a customer account.
    /// </summary>
    public class ParentalControlProfile
    {
        public int CustomerId { get; set; }

        /// <summary>
        /// Maximum content rating allowed for this account.
        /// Movies rated above this are blocked.
        /// </summary>
        public ContentRating MaxAllowedRating { get; set; } = ContentRating.NC17;

        /// <summary>
        /// Content advisories that should trigger a warning (not a block).
        /// </summary>
        public ContentAdvisory WarnAdvisories { get; set; } = ContentAdvisory.None;

        /// <summary>
        /// Content advisories that should block rental entirely.
        /// </summary>
        public ContentAdvisory BlockAdvisories { get; set; } = ContentAdvisory.None;

        /// <summary>
        /// Whether this profile is active.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// SHA-256 hash of (salt + PIN). Never stores the raw PIN.
        /// </summary>
        internal string PinHash { get; set; }

        /// <summary>
        /// Cryptographic salt for PIN hashing (hex-encoded, 16 bytes).
        /// </summary>
        internal string PinSalt { get; set; }

        /// <summary>
        /// Number of consecutive failed PIN attempts.
        /// </summary>
        public int FailedPinAttempts { get; set; }

        /// <summary>
        /// UTC time when the last failed PIN attempt occurred.
        /// Used for lockout window calculation.
        /// </summary>
        public DateTime? LastFailedAttempt { get; set; }

        /// <summary>
        /// Whether a valid PIN has been set (checks for hash, not raw value).
        /// </summary>
        public bool HasPin => !string.IsNullOrWhiteSpace(PinHash)
                              && !string.IsNullOrWhiteSpace(PinSalt);
    }

    /// <summary>
    /// Result of checking whether a customer can rent a movie.
    /// </summary>
    public class ContentAccessResult
    {
        public bool IsAllowed { get; set; }
        public bool HasWarnings { get; set; }
        public string BlockReason { get; set; }
        public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
        public bool CanOverrideWithPin { get; set; }

        public static ContentAccessResult Allowed() => new()
        {
            IsAllowed = true, HasWarnings = false
        };

        public static ContentAccessResult AllowedWithWarnings(List<string> warnings) => new()
        {
            IsAllowed = true, HasWarnings = true, Warnings = warnings
        };

        public static ContentAccessResult Blocked(string reason, bool canOverride = true) => new()
        {
            IsAllowed = false, BlockReason = reason, CanOverrideWithPin = canOverride
        };
    }

    /// <summary>
    /// Service for managing MPAA-style content ratings and parental controls.
    ///
    /// Features:
    /// <list type="bullet">
    ///   <item>Assign content ratings (G through NC-17) and advisories to movies</item>
    ///   <item>Per-customer parental control profiles with max rating + advisory blocking</item>
    ///   <item>Access checking with warnings vs blocks</item>
    ///   <item>PIN-based override for parental locks</item>
    ///   <item>Genre-based auto-rating suggestions</item>
    ///   <item>Family-friendly movie filtering</item>
    ///   <item>Content statistics and analytics</item>
    /// </list>
    /// </summary>
    public class ParentalControlService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;

        private readonly Dictionary<int, MovieContentProfile> _movieProfiles = new();
        private readonly Dictionary<int, ParentalControlProfile> _controlProfiles = new();

        /// <summary>Maximum consecutive failed PIN attempts before lockout.</summary>
        public const int MaxPinAttempts = 5;

        /// <summary>Lockout duration in minutes after max failed attempts.</summary>
        public const int LockoutMinutes = 15;

        public ParentalControlService(
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        // ── PIN Security Helpers ─────────────────────────────────────

        /// <summary>
        /// Generate a cryptographic salt (16 bytes, hex-encoded).
        /// </summary>
        private static string GenerateSalt()
        {
            var bytes = new byte[16];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Hash a PIN with SHA-256 using the provided salt.
        /// Returns hex-encoded hash string.
        /// </summary>
        private static string HashPin(string pin, string salt)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var data = System.Text.Encoding.UTF8.GetBytes(salt + pin);
                var hash = sha.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Constant-time comparison to prevent timing attacks.
        /// </summary>
        private static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>
        /// Set PIN on a profile (hashes with fresh salt).
        /// </summary>
        private static void SetPinOnProfile(ParentalControlProfile profile, string pin)
        {
            if (pin == null)
            {
                profile.PinHash = null;
                profile.PinSalt = null;
            }
            else
            {
                profile.PinSalt = GenerateSalt();
                profile.PinHash = HashPin(pin, profile.PinSalt);
            }
            profile.FailedPinAttempts = 0;
            profile.LastFailedAttempt = null;
        }

        /// <summary>
        /// Verify a PIN against a profile's stored hash.
        /// Includes rate limiting: locks out after MaxPinAttempts
        /// failures for LockoutMinutes.
        /// </summary>
        private bool VerifyPin(ParentalControlProfile profile, string pin)
        {
            if (!profile.HasPin) return false;

            // Check lockout
            if (profile.FailedPinAttempts >= MaxPinAttempts
                && profile.LastFailedAttempt.HasValue)
            {
                var elapsed = DateTime.UtcNow - profile.LastFailedAttempt.Value;
                if (elapsed.TotalMinutes < LockoutMinutes)
                    return false; // Still locked out

                // Lockout expired — reset attempts
                profile.FailedPinAttempts = 0;
                profile.LastFailedAttempt = null;
            }

            var candidateHash = HashPin(pin, profile.PinSalt);
            if (ConstantTimeEquals(candidateHash, profile.PinHash))
            {
                // Correct PIN — reset failed attempts
                profile.FailedPinAttempts = 0;
                profile.LastFailedAttempt = null;
                return true;
            }

            // Wrong PIN — increment failure counter
            profile.FailedPinAttempts++;
            profile.LastFailedAttempt = DateTime.UtcNow;
            return false;
        }

        // ── Movie Content Rating ─────────────────────────────────────

        /// <summary>
        /// Assign or update the content profile for a movie.
        /// </summary>
        public MovieContentProfile SetMovieRating(
            int movieId,
            ContentRating rating,
            ContentAdvisory advisories = ContentAdvisory.None,
            string customNote = null)
        {
            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                throw new ArgumentException("Movie not found.", nameof(movieId));

            var profile = new MovieContentProfile
            {
                MovieId = movieId,
                Rating = rating,
                Advisories = advisories,
                CustomNote = customNote
            };
            _movieProfiles[movieId] = profile;
            return profile;
        }

        /// <summary>
        /// Get the content profile for a movie. Returns null if not rated.
        /// </summary>
        public MovieContentProfile GetMovieProfile(int movieId)
        {
            return _movieProfiles.TryGetValue(movieId, out var profile)
                ? profile : null;
        }

        /// <summary>
        /// Suggest a content rating based on genre.
        /// This is a starting point — manual review is still recommended.
        /// </summary>
        public ContentRating SuggestRating(Genre genre)
        {
            return genre switch
            {
                Genre.Animation => ContentRating.G,
                Genre.Comedy => ContentRating.PG,
                Genre.Romance => ContentRating.PG13,
                Genre.Adventure => ContentRating.PG,
                Genre.Documentary => ContentRating.PG,
                Genre.Drama => ContentRating.PG13,
                Genre.SciFi => ContentRating.PG13,
                Genre.Action => ContentRating.PG13,
                Genre.Thriller => ContentRating.R,
                Genre.Horror => ContentRating.R,
                _ => ContentRating.Unrated
            };
        }

        /// <summary>
        /// Suggest content advisories based on genre.
        /// </summary>
        public ContentAdvisory SuggestAdvisories(Genre genre)
        {
            return genre switch
            {
                Genre.Horror => ContentAdvisory.Violence | ContentAdvisory.ScaryScenes,
                Genre.Thriller => ContentAdvisory.Violence | ContentAdvisory.ThematicElements,
                Genre.Action => ContentAdvisory.Violence,
                Genre.Romance => ContentAdvisory.ThematicElements,
                Genre.Drama => ContentAdvisory.ThematicElements | ContentAdvisory.Language,
                Genre.Comedy => ContentAdvisory.Language,
                _ => ContentAdvisory.None
            };
        }

        /// <summary>
        /// Auto-rate all movies that don't have a content profile yet,
        /// based on their genre. Returns the number of movies auto-rated.
        /// </summary>
        public int AutoRateUnratedMovies()
        {
            var allMovies = _movieRepository.GetAll();
            int count = 0;
            foreach (var movie in allMovies)
            {
                if (_movieProfiles.ContainsKey(movie.Id)) continue;
                if (!movie.Genre.HasValue) continue;

                var rating = SuggestRating(movie.Genre.Value);
                var advisories = SuggestAdvisories(movie.Genre.Value);
                SetMovieRating(movie.Id, rating, advisories,
                    "Auto-rated based on genre");
                count++;
            }
            return count;
        }

        // ── Parental Control Profiles ────────────────────────────────

        /// <summary>
        /// Enable parental controls for a customer account.
        /// </summary>
        public ParentalControlProfile EnableControls(
            int customerId,
            ContentRating maxRating,
            string pin = null,
            ContentAdvisory warnAdvisories = ContentAdvisory.None,
            ContentAdvisory blockAdvisories = ContentAdvisory.None)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException("Customer not found.", nameof(customerId));

            if (pin != null && (pin.Length != 4 || !pin.All(char.IsDigit)))
                throw new ArgumentException(
                    "PIN must be exactly 4 digits.", nameof(pin));

            var profile = new ParentalControlProfile
            {
                CustomerId = customerId,
                MaxAllowedRating = maxRating,
                WarnAdvisories = warnAdvisories,
                BlockAdvisories = blockAdvisories,
                IsEnabled = true
            };
            SetPinOnProfile(profile, pin);
            _controlProfiles[customerId] = profile;
            return profile;
        }

        /// <summary>
        /// Disable parental controls for a customer (requires PIN if set).
        /// </summary>
        public bool DisableControls(int customerId, string pin = null)
        {
            if (!_controlProfiles.TryGetValue(customerId, out var profile))
                return false;

            if (profile.HasPin && !VerifyPin(profile, pin))
                return false;

            profile.IsEnabled = false;
            return true;
        }

        /// <summary>
        /// Get the parental control profile for a customer.
        /// Returns null if no profile exists.
        /// </summary>
        public ParentalControlProfile GetControlProfile(int customerId)
        {
            return _controlProfiles.TryGetValue(customerId, out var profile)
                ? profile : null;
        }

        /// <summary>
        /// Update the PIN on a parental control profile.
        /// Requires the old PIN if one was previously set.
        /// </summary>
        public bool UpdatePin(int customerId, string oldPin, string newPin)
        {
            if (!_controlProfiles.TryGetValue(customerId, out var profile))
                return false;

            if (profile.HasPin && !VerifyPin(profile, oldPin))
                return false;

            if (newPin != null && (newPin.Length != 4 || !newPin.All(char.IsDigit)))
                return false;

            SetPinOnProfile(profile, newPin);
            return true;
        }

        // ── Access Checking ──────────────────────────────────────────

        /// <summary>
        /// Check whether a customer is allowed to rent a specific movie
        /// based on their parental control settings.
        /// </summary>
        public ContentAccessResult CheckAccess(int customerId, int movieId)
        {
            // No parental controls → always allowed
            if (!_controlProfiles.TryGetValue(customerId, out var control)
                || !control.IsEnabled)
                return ContentAccessResult.Allowed();

            // No content profile → treat as unrated
            if (!_movieProfiles.TryGetValue(movieId, out var content))
            {
                // Unrated movies are blocked if max rating is PG or below
                // (conservative: unrated content could be anything)
                if (control.MaxAllowedRating <= ContentRating.PG)
                    return ContentAccessResult.Blocked(
                        "This movie has not been rated. Unrated content is blocked under your parental controls.",
                        control.HasPin);
                return ContentAccessResult.Allowed();
            }

            // Check rating level
            if (content.Rating != ContentRating.Unrated
                && content.Rating > control.MaxAllowedRating)
            {
                return ContentAccessResult.Blocked(
                    $"This movie is rated {content.RatingLabel}, which exceeds your maximum allowed rating.",
                    control.HasPin);
            }

            // Check blocked advisories
            var blockedMatch = content.Advisories & control.BlockAdvisories;
            if (blockedMatch != ContentAdvisory.None)
            {
                var blockedLabels = GetAdvisoryLabels(blockedMatch);
                return ContentAccessResult.Blocked(
                    $"This movie contains blocked content: {string.Join(", ", blockedLabels)}.",
                    control.HasPin);
            }

            // Check warning advisories
            var warnMatch = content.Advisories & control.WarnAdvisories;
            if (warnMatch != ContentAdvisory.None)
            {
                var warnLabels = GetAdvisoryLabels(warnMatch);
                var warnings = warnLabels
                    .Select(l => $"Content advisory: {l}")
                    .ToList();
                return ContentAccessResult.AllowedWithWarnings(warnings);
            }

            return ContentAccessResult.Allowed();
        }

        /// <summary>
        /// Attempt to override a block using the parental control PIN.
        /// Returns true if the PIN is correct and override is allowed.
        /// Subject to rate limiting (locked out after MaxPinAttempts failures).
        /// </summary>
        public bool TryOverrideWithPin(int customerId, string pin)
        {
            if (!_controlProfiles.TryGetValue(customerId, out var profile))
                return false;

            return VerifyPin(profile, pin);
        }

        // ── Filtering ────────────────────────────────────────────────

        /// <summary>
        /// Get all movies that are safe for a given customer's parental controls.
        /// </summary>
        public IReadOnlyList<Movie> GetAllowedMovies(int customerId)
        {
            var allMovies = _movieRepository.GetAll().ToList();

            if (!_controlProfiles.TryGetValue(customerId, out var control)
                || !control.IsEnabled)
                return allMovies;

            return allMovies
                .Where(m => CheckAccess(customerId, m.Id).IsAllowed)
                .ToList();
        }

        /// <summary>
        /// Get all movies suitable for family viewing (rated G or PG).
        /// </summary>
        public IReadOnlyList<Movie> GetFamilyFriendlyMovies()
        {
            var allMovies = _movieRepository.GetAll().ToList();
            return allMovies
                .Where(m =>
                {
                    if (!_movieProfiles.TryGetValue(m.Id, out var profile))
                        return false; // Unrated movies excluded from family list
                    return profile.Rating <= ContentRating.PG;
                })
                .ToList();
        }

        /// <summary>
        /// Get movies by content rating.
        /// </summary>
        public IReadOnlyList<Movie> GetMoviesByRating(ContentRating rating)
        {
            var allMovies = _movieRepository.GetAll().ToList();
            return allMovies
                .Where(m =>
                    _movieProfiles.TryGetValue(m.Id, out var profile)
                    && profile.Rating == rating)
                .ToList();
        }

        /// <summary>
        /// Get movies that have a specific content advisory.
        /// </summary>
        public IReadOnlyList<Movie> GetMoviesByAdvisory(ContentAdvisory advisory)
        {
            var allMovies = _movieRepository.GetAll().ToList();
            return allMovies
                .Where(m =>
                    _movieProfiles.TryGetValue(m.Id, out var profile)
                    && profile.Advisories.HasFlag(advisory))
                .ToList();
        }

        // ── Statistics ───────────────────────────────────────────────

        /// <summary>
        /// Get content rating distribution across the catalog.
        /// </summary>
        public Dictionary<ContentRating, int> GetRatingDistribution()
        {
            var dist = new Dictionary<ContentRating, int>();
            foreach (ContentRating r in Enum.GetValues(typeof(ContentRating)))
                dist[r] = 0;

            foreach (var profile in _movieProfiles.Values)
                dist[profile.Rating]++;

            // Count unrated movies
            var allMovies = _movieRepository.GetAll().ToList();
            dist[ContentRating.Unrated] += allMovies.Count(m =>
                !_movieProfiles.ContainsKey(m.Id));

            return dist;
        }

        /// <summary>
        /// Get the most common content advisories in the catalog.
        /// </summary>
        public Dictionary<ContentAdvisory, int> GetAdvisoryDistribution()
        {
            var dist = new Dictionary<ContentAdvisory, int>();
            foreach (ContentAdvisory a in Enum.GetValues(typeof(ContentAdvisory)))
            {
                if (a == ContentAdvisory.None) continue;
                dist[a] = _movieProfiles.Values
                    .Count(p => p.Advisories.HasFlag(a));
            }
            return dist;
        }

        /// <summary>
        /// Get the percentage of the catalog that is family-friendly (G or PG).
        /// </summary>
        public double GetFamilyFriendlyPercent()
        {
            var allMovies = _movieRepository.GetAll().ToList();
            if (allMovies.Count == 0) return 0.0;

            var familyCount = allMovies.Count(m =>
                _movieProfiles.TryGetValue(m.Id, out var p)
                && p.Rating <= ContentRating.PG);

            return (double)familyCount / allMovies.Count * 100;
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static List<string> GetAdvisoryLabels(ContentAdvisory advisories)
        {
            var labels = new List<string>();
            if (advisories.HasFlag(ContentAdvisory.Violence)) labels.Add("Violence");
            if (advisories.HasFlag(ContentAdvisory.Language)) labels.Add("Strong Language");
            if (advisories.HasFlag(ContentAdvisory.NudityOrSexualContent)) labels.Add("Nudity/Sexual Content");
            if (advisories.HasFlag(ContentAdvisory.DrugUse)) labels.Add("Drug Use");
            if (advisories.HasFlag(ContentAdvisory.ScaryScenes)) labels.Add("Scary Scenes");
            if (advisories.HasFlag(ContentAdvisory.ThematicElements)) labels.Add("Thematic Elements");
            if (advisories.HasFlag(ContentAdvisory.SmokingOrAlcohol)) labels.Add("Smoking/Alcohol");
            if (advisories.HasFlag(ContentAdvisory.GamblingDepictions)) labels.Add("Gambling");
            return labels;
        }
    }
}
