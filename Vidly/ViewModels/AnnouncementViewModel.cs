using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class AnnouncementBoardViewModel
    {
        public IReadOnlyList<Announcement> Announcements { get; set; }
        public IReadOnlyList<Announcement> PinnedAnnouncements { get; set; }
        public AnnouncementAnalytics Analytics { get; set; }

        // Filters
        public AnnouncementCategory? CategoryFilter { get; set; }
        public AnnouncementStatus? StatusFilter { get; set; }
        public string SearchQuery { get; set; }
        public string View { get; set; } // "board" or "manage"
    }

    public class AnnouncementCreateViewModel
    {
        public string Title { get; set; }
        public string Body { get; set; }
        public AnnouncementCategory Category { get; set; }
        public AnnouncementPriority Priority { get; set; }
        public bool RequiresAcknowledgment { get; set; }
        public bool HasExpiration { get; set; }
        public int ExpirationDays { get; set; } = 30;
        public string Tags { get; set; }
        public int? RelatedMovieId { get; set; }
    }

    public class AnnouncementDetailViewModel
    {
        public Announcement Announcement { get; set; }
        public bool IsPinned { get; set; }
        public bool IsAcknowledged { get; set; }
        public int AcknowledgmentCount { get; set; }
    }
}
