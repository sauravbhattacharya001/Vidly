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
            if (profile.Pin != null && profile.Pin != pin)
                return false;

            _activeProfileId = profileId;
            AddLog(profileId, profile.Name, "Switch", $"Switched to {profile.Name} profile");
            return true;
        }

        public FamilyProfile CreateProfile(FamilyProfile profile)
        {
            profile.Id = _nextProfileId++;
            profile.CreatedAt = DateTime.Now;
            _profiles.Add(profile);
            AddLog(profile.Id, profile.Name, "Created", $"Profile '{profile.Name}' created (max rating: {profile.MaxRating})");
            return profile;
        }

        public bool UpdateProfile(FamilyProfile updated)
        {
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

        public bool DeleteProfile(int id)
        {
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
