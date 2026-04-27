using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// In-memory parental control service managing family profiles and activity logs.
    /// </summary>
    public class ParentalControlService
    {
        private static readonly List<FamilyProfile> _profiles = new List<FamilyProfile>();
        private static readonly List<ParentalControlLog> _logs = new List<ParentalControlLog>();
        private static int _nextProfileId = 1;
        private static int _nextLogId = 1;
        private static int? _activeProfileId;
        private static readonly Dictionary<int, FailedPinAttempts> _pinAttempts =
            new Dictionary<int, FailedPinAttempts>();

        /// <summary>Maximum consecutive failed PIN attempts before lockout.</summary>
        private const int MaxPinAttempts = 5;
        /// <summary>Lockout duration after too many failed PIN attempts.</summary>
        private static readonly TimeSpan PinLockoutDuration = TimeSpan.FromMinutes(15);

        static ParentalControlService()
        {
            // Seed with default profiles
            var parent = new FamilyProfile
            {
                Id = _nextProfileId++,
                Name = "Parent",
                MaxRating = ContentRating.NC17,
                Pin = "0000",
                IsParent = true,
                AvatarIcon = "user",
                CreatedAt = DateTime.Now.AddDays(-30)
            };
            var teen = new FamilyProfile
            {
                Id = _nextProfileId++,
                Name = "Teen",
                MaxRating = ContentRating.PG13,
                Pin = null,
                IsParent = false,
                AvatarIcon = "education",
                WeeklyRentalLimit = 5,
                CreatedAt = DateTime.Now.AddDays(-30)
            };
            var child = new FamilyProfile
            {
                Id = _nextProfileId++,
                Name = "Kids",
                MaxRating = ContentRating.G,
                Pin = null,
                IsParent = false,
                AvatarIcon = "baby-formula",
                AllowedFromHour = 8,
                AllowedUntilHour = 20,
                WeeklyRentalLimit = 3,
                BlockedGenres = "Horror,Thriller",
                CreatedAt = DateTime.Now.AddDays(-30)
            };

            _profiles.AddRange(new[] { parent, teen, child });
            _activeProfileId = parent.Id;

            // Seed some log entries
            _logs.Add(new ParentalControlLog { Id = _nextLogId++, ProfileId = child.Id, ProfileName = "Kids", Action = "Blocked", Details = "Attempted to rent 'The Matrix' (R) — exceeds G rating limit", Timestamp = DateTime.Now.AddHours(-2) });
            _logs.Add(new ParentalControlLog { Id = _nextLogId++, ProfileId = teen.Id, ProfileName = "Teen", Action = "Switch", Details = "Switched to Teen profile", Timestamp = DateTime.Now.AddHours(-5) });
            _logs.Add(new ParentalControlLog { Id = _nextLogId++, ProfileId = child.Id, ProfileName = "Kids", Action = "Blocked", Details = "Attempted to rent 'Saw' (R) — exceeds G rating limit", Timestamp = DateTime.Now.AddDays(-1) });
            _logs.Add(new ParentalControlLog { Id = _nextLogId++, ProfileId = teen.Id, ProfileName = "Teen", Action = "Blocked", Details = "Attempted to rent 'Deadpool' (R) — exceeds PG-13 rating limit", Timestamp = DateTime.Now.AddDays(-2) });
        }

        public List<FamilyProfile> GetAllProfiles() => _profiles.OrderBy(p => p.IsParent ? 0 : 1).ThenBy(p => p.Name).ToList();

        public FamilyProfile GetProfile(int id) => _profiles.FirstOrDefault(p => p.Id == id);

        public FamilyProfile GetActiveProfile() => _activeProfileId.HasValue
            ? _profiles.FirstOrDefault(p => p.Id == _activeProfileId.Value)
            : _profiles.FirstOrDefault(p => p.IsParent);

        public bool SwitchProfile(int profileId, string pin)
        {
            var profile = GetProfile(profileId);
            if (profile == null) return false;

            // PIN check: parent profiles always need PIN, others only if PIN is set
            if (profile.Pin != null)
            {
                // CWE-307: enforce rate-limiting on PIN attempts to prevent brute-force
                if (IsLockedOut(profileId))
                {
                    AddLog(profileId, profile.Name, "Locked",
                        $"PIN attempt rejected — profile locked after {MaxPinAttempts} failed attempts");
                    return false;
                }

                // CWE-208: use fixed-time comparison to prevent timing side-channel
                if (!FixedTimeEquals(profile.Pin, pin ?? ""))
                {
                    RecordFailedAttempt(profileId);
                    AddLog(profileId, profile.Name, "PIN-Failed",
                        $"Incorrect PIN attempt for {profile.Name} profile");
                    return false;
                }

                // Correct PIN — clear any accumulated failures
                ClearFailedAttempts(profileId);
            }

            _activeProfileId = profileId;
            AddLog(profileId, profile.Name, "Switch", $"Switched to {profile.Name} profile");
            return true;
        }

        /// <summary>
        /// Creates a new family profile. Only parent profiles are authorized
        /// to manage profiles — otherwise a child/teen could create an
        /// unrestricted parent profile and bypass all parental controls
        /// (CWE-862: Missing Authorization).
        /// </summary>
        public FamilyProfile CreateProfile(FamilyProfile profile)
        {
            RequireParentAuthorization("create profiles");

            profile.Id = _nextProfileId++;
            profile.CreatedAt = DateTime.Now;
            _profiles.Add(profile);
            AddLog(profile.Id, profile.Name, "Created", $"Profile '{profile.Name}' created (max rating: {profile.MaxRating})");
            return profile;
        }

        /// <summary>
        /// Updates an existing family profile. Requires parent authorization
        /// to prevent restricted profiles from escalating their own
        /// privileges (e.g. setting IsParent=true, raising MaxRating,
        /// removing BlockedGenres, or changing a parent's PIN)
        /// — CWE-862: Missing Authorization.
        /// </summary>
        public bool UpdateProfile(FamilyProfile updated)
        {
            RequireParentAuthorization("edit profiles");

            var existing = GetProfile(updated.Id);
            if (existing == null) return false;

            existing.Name = updated.Name;
            existing.MaxRating = updated.MaxRating;
            existing.IsParent = updated.IsParent;
            existing.AvatarIcon = updated.AvatarIcon;
            existing.AllowedFromHour = updated.AllowedFromHour;
            existing.AllowedUntilHour = updated.AllowedUntilHour;
            existing.WeeklyRentalLimit = updated.WeeklyRentalLimit;
            existing.BlockedGenres = updated.BlockedGenres;

            if (!string.IsNullOrEmpty(updated.Pin))
                existing.Pin = updated.Pin;

            AddLog(existing.Id, existing.Name, "Updated", $"Profile '{existing.Name}' settings updated");
            return true;
        }

        /// <summary>
        /// Deletes a non-parent profile. Requires parent authorization
        /// to prevent restricted profiles from removing sibling profiles
        /// or disrupting the control hierarchy (CWE-862).
        /// </summary>
        public bool DeleteProfile(int id)
        {
            RequireParentAuthorization("delete profiles");

            var profile = GetProfile(id);
            if (profile == null || profile.IsParent) return false; // Can't delete parent profile

            _profiles.Remove(profile);
            AddLog(id, profile.Name, "Deleted", $"Profile '{profile.Name}' deleted");

            if (_activeProfileId == id)
                _activeProfileId = _profiles.FirstOrDefault(p => p.IsParent)?.Id;

            return true;
        }

        /// <summary>
        /// Check whether a movie can be rented under the active profile.
        /// Returns null if allowed, or a reason string if blocked.
        /// </summary>
        public string CheckRentalPermission(ContentRating movieRating, string genreName)
        {
            var active = GetActiveProfile();
            if (active == null || active.IsParent) return null; // Parent can rent anything

            if (!active.IsRatingAllowed(movieRating))
            {
                var reason = $"Movie rated {movieRating} exceeds {active.Name}'s limit of {active.MaxRating}";
                AddLog(active.Id, active.Name, "Blocked", reason);
                return reason;
            }

            if (active.IsGenreBlocked(genreName))
            {
                var reason = $"Genre '{genreName}' is blocked for {active.Name} profile";
                AddLog(active.Id, active.Name, "Blocked", reason);
                return reason;
            }

            if (!active.IsWithinAllowedHours())
            {
                var reason = $"Rentals not allowed at this hour for {active.Name} (allowed {active.AllowedFromHour}:00–{active.AllowedUntilHour}:00)";
                AddLog(active.Id, active.Name, "Blocked", reason);
                return reason;
            }

            return null;
        }

        public List<ParentalControlLog> GetRecentLogs(int count = 20)
            => _logs.OrderByDescending(l => l.Timestamp).Take(count).ToList();

        public List<ParentalControlLog> GetLogsByProfile(int profileId)
            => _logs.Where(l => l.ProfileId == profileId).OrderByDescending(l => l.Timestamp).ToList();

        public Dictionary<string, int> GetBlockedAttemptsByRating()
        {
            return _logs
                .Where(l => l.Action == "Blocked")
                .GroupBy(l =>
                {
                    // Extract rating from details if possible
                    if (l.Details.Contains("(R)") || l.Details.Contains(" R ")) return "R";
                    if (l.Details.Contains("NC-17") || l.Details.Contains("NC17")) return "NC-17";
                    if (l.Details.Contains("PG-13") || l.Details.Contains("PG13")) return "PG-13";
                    if (l.Details.Contains("(PG)") || l.Details.Contains(" PG ")) return "PG";
                    return "Other";
                })
                .ToDictionary(g => g.Key, g => g.Count());
        }

        public int GetBlockedCountThisWeek()
        {
            var weekAgo = DateTime.Now.AddDays(-7);
            return _logs.Count(l => l.Action == "Blocked" && l.Timestamp >= weekAgo);
        }

        /// <summary>
        /// Verifies the active profile is a parent profile. Throws
        /// <see cref="UnauthorizedAccessException"/> when a restricted
        /// profile attempts a management action. This is the central
        /// authorization gate for all profile CRUD operations, preventing
        /// privilege escalation (CWE-862) where a child/teen could
        /// otherwise create an unrestricted parent profile, elevate their
        /// own MaxRating/IsParent flags, change the parent PIN, or remove
        /// their blocked-genre and time-window restrictions.
        /// </summary>
        private void RequireParentAuthorization(string action)
        {
            var active = GetActiveProfile();
            if (active == null || !active.IsParent)
            {
                var profileName = active?.Name ?? "Unknown";
                AddLog(active?.Id ?? 0, profileName, "Unauthorized",
                    $"Non-parent profile '{profileName}' attempted to {action}");
                throw new UnauthorizedAccessException(
                    $"Only parent profiles can {action}. Current profile: {profileName}");
            }
        }

        /// <summary>
        /// Constant-time string comparison to prevent timing side-channel
        /// attacks on PIN verification (CWE-208). Compares every byte pair
        /// regardless of mismatch position — no early-exit.
        /// (.NET Framework 4.5 lacks CryptographicOperations, so this is
        /// implemented manually.)
        /// </summary>
        private static bool FixedTimeEquals(string expected, string actual)
        {
            var a = System.Text.Encoding.UTF8.GetBytes(expected ?? "");
            var b = System.Text.Encoding.UTF8.GetBytes(actual ?? "");

            // Length difference leaks 1 bit (same-length vs not); accumulate
            // into the diff mask so we still compare all bytes of the longer.
            int diff = a.Length ^ b.Length;
            int len = Math.Max(a.Length, b.Length);
            for (int i = 0; i < len; i++)
            {
                byte x = i < a.Length ? a[i] : (byte)0;
                byte y = i < b.Length ? b[i] : (byte)0;
                diff |= x ^ y;
            }
            return diff == 0;
        }

        private bool IsLockedOut(int profileId)
        {
            if (!_pinAttempts.TryGetValue(profileId, out var attempts))
                return false;
            if (attempts.Count < MaxPinAttempts)
                return false;
            return (DateTime.Now - attempts.LastAttempt) < PinLockoutDuration;
        }

        private void RecordFailedAttempt(int profileId)
        {
            if (!_pinAttempts.TryGetValue(profileId, out var attempts))
            {
                attempts = new FailedPinAttempts();
                _pinAttempts[profileId] = attempts;
            }
            attempts.Count++;
            attempts.LastAttempt = DateTime.Now;
        }

        private void ClearFailedAttempts(int profileId)
        {
            _pinAttempts.Remove(profileId);
        }

        private class FailedPinAttempts
        {
            public int Count;
            public DateTime LastAttempt;
        }

        private void AddLog(int profileId, string profileName, string action, string details)
        {
            _logs.Add(new ParentalControlLog
            {
                Id = _nextLogId++,
                ProfileId = profileId,
                ProfileName = profileName,
                Action = action,
                Details = details,
                Timestamp = DateTime.Now
            });
        }
    }
}
