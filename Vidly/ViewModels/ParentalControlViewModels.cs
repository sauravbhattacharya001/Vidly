using System;
using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class ParentalControlViewModel
    {
        public List<FamilyProfile> Profiles { get; set; } = new List<FamilyProfile>();
        public FamilyProfile ActiveProfile { get; set; }
        public List<ParentalControlLog> RecentLogs { get; set; } = new List<ParentalControlLog>();
        public Dictionary<string, int> BlockedAttemptsByRating { get; set; } = new Dictionary<string, int>();
        public int TotalBlockedThisWeek { get; set; }
    }

    public class ProfileFormViewModel
    {
        public FamilyProfile Profile { get; set; }
        public bool IsEdit { get; set; }
        public List<string> AvailableGenres { get; set; } = new List<string>();
        public List<string> AvatarOptions { get; set; } = new List<string>
        {
            "user", "baby-formula", "education", "sunglasses", "star", "heart", "film", "music"
        };
    }
}
