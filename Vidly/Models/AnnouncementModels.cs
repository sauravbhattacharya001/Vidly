using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    public enum AnnouncementCategory
    {
        General,
        NewArrival,
        Promotion,
        PolicyChange,
        StoreClosure,
        Event,
        Maintenance,
        MemberExclusive
    }

    public enum AnnouncementPriority
    {
        Low,
        Normal,
        High,
        Urgent
    }

    public enum AnnouncementStatus
    {
        Draft,
        Scheduled,
        Active,
        Expired,
        Archived
    }

    public class Announcement
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public AnnouncementCategory Category { get; set; }
        public AnnouncementPriority Priority { get; set; }
        public AnnouncementStatus Status { get; set; }
        public string AuthorStaffId { get; set; }

        /// <summary>When the announcement becomes visible. Null = immediately on publish.</summary>
        public DateTime? ScheduledStart { get; set; }

        /// <summary>When the announcement auto-expires. Null = never.</summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>If non-empty, only customers in these tiers see it.</summary>
        public List<string> TargetTiers { get; set; } = new List<string>();

        /// <summary>If true, customers must acknowledge (dismiss) this announcement.</summary>
        public bool RequiresAcknowledgment { get; set; }

        /// <summary>Optional related movie ID (for new arrival announcements).</summary>
        public int? RelatedMovieId { get; set; }

        /// <summary>Tags for filtering/search.</summary>
        public List<string> Tags { get; set; } = new List<string>();

        public DateTime CreatedAt { get; set; }
        public DateTime? PublishedAt { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public int ViewCount { get; set; }
    }

    public class AnnouncementAcknowledgment
    {
        public int Id { get; set; }
        public int AnnouncementId { get; set; }
        public int CustomerId { get; set; }
        public DateTime AcknowledgedAt { get; set; }
    }

    public class AnnouncementPin
    {
        public int AnnouncementId { get; set; }
        public DateTime PinnedAt { get; set; }
        public string PinnedByStaffId { get; set; }
    }

    public class AnnouncementAnalytics
    {
        public int TotalAnnouncements { get; set; }
        public int ActiveCount { get; set; }
        public int DraftCount { get; set; }
        public int ExpiredCount { get; set; }
        public int ArchivedCount { get; set; }
        public Dictionary<AnnouncementCategory, int> ByCategory { get; set; }
            = new Dictionary<AnnouncementCategory, int>();
        public Dictionary<AnnouncementPriority, int> ByPriority { get; set; }
            = new Dictionary<AnnouncementPriority, int>();
        public double AverageAcknowledgmentRate { get; set; }
        public int PinnedCount { get; set; }
        public Announcement MostViewedAnnouncement { get; set; }
        public List<AnnouncementTrend> RecentTrends { get; set; }
            = new List<AnnouncementTrend>();
    }

    public class AnnouncementTrend
    {
        public DateTime Date { get; set; }
        public int Published { get; set; }
        public int Expired { get; set; }
        public int Acknowledged { get; set; }
    }

    public class AnnouncementFilter
    {
        public AnnouncementCategory? Category { get; set; }
        public AnnouncementPriority? MinPriority { get; set; }
        public AnnouncementStatus? Status { get; set; }
        public string Tag { get; set; }
        public string SearchText { get; set; }
        public string CustomerTier { get; set; }
        public bool? PinnedOnly { get; set; }
        public bool? RequiresAcknowledgment { get; set; }
    }
}
