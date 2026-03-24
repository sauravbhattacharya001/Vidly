using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    public class InMemoryAnnouncementRepository : IAnnouncementRepository
    {
        private static readonly List<Announcement> _announcements = new List<Announcement>();
        private static readonly List<AnnouncementAcknowledgment> _acks = new List<AnnouncementAcknowledgment>();
        private static readonly List<AnnouncementPin> _pins = new List<AnnouncementPin>();
        private static int _nextId = 1;
        private static int _nextAckId = 1;
        private static bool _seeded;

        public InMemoryAnnouncementRepository()
        {
            if (!_seeded)
            {
                _seeded = true;
                SeedData();
            }
        }

        private void SeedData()
        {
            var items = new[]
            {
                new Announcement
                {
                    Title = "New Arrivals This Week!",
                    Body = "We've added 15 new titles to our collection including the latest blockbusters. Check out the New Arrivals section!",
                    Category = AnnouncementCategory.NewArrival,
                    Priority = AnnouncementPriority.Normal,
                    Status = AnnouncementStatus.Active,
                    AuthorStaffId = "staff-1",
                    CreatedAt = DateTime.Now.AddDays(-3),
                    PublishedAt = DateTime.Now.AddDays(-3),
                    ViewCount = 42
                },
                new Announcement
                {
                    Title = "Weekend Special: Rent 2 Get 1 Free",
                    Body = "This weekend only! Rent any two movies and get your third rental absolutely free. Valid Friday through Sunday.",
                    Category = AnnouncementCategory.Promotion,
                    Priority = AnnouncementPriority.High,
                    Status = AnnouncementStatus.Active,
                    AuthorStaffId = "staff-2",
                    ScheduledStart = DateTime.Now.AddDays(-1),
                    ExpiresAt = DateTime.Now.AddDays(3),
                    CreatedAt = DateTime.Now.AddDays(-2),
                    PublishedAt = DateTime.Now.AddDays(-1),
                    ViewCount = 78,
                    Tags = new List<string> { "weekend", "promotion", "bogo" }
                },
                new Announcement
                {
                    Title = "Updated Late Return Policy",
                    Body = "Starting next month, late fees will be calculated at a reduced rate of $0.50/day instead of $1.00/day. We appreciate your loyalty!",
                    Category = AnnouncementCategory.PolicyChange,
                    Priority = AnnouncementPriority.High,
                    Status = AnnouncementStatus.Active,
                    AuthorStaffId = "staff-1",
                    RequiresAcknowledgment = true,
                    CreatedAt = DateTime.Now.AddDays(-7),
                    PublishedAt = DateTime.Now.AddDays(-7),
                    ViewCount = 156
                },
                new Announcement
                {
                    Title = "Store Closed for Renovation - March 30",
                    Body = "We will be closed on March 30 for a one-day renovation. Online reservations will still be available.",
                    Category = AnnouncementCategory.StoreClosure,
                    Priority = AnnouncementPriority.Urgent,
                    Status = AnnouncementStatus.Active,
                    AuthorStaffId = "staff-1",
                    ExpiresAt = DateTime.Now.AddDays(10),
                    CreatedAt = DateTime.Now.AddDays(-1),
                    PublishedAt = DateTime.Now.AddDays(-1),
                    ViewCount = 23
                },
                new Announcement
                {
                    Title = "Classic Movie Marathon - April Event",
                    Body = "Join us for a classic movie marathon event! Details coming soon. Members get early access.",
                    Category = AnnouncementCategory.Event,
                    Priority = AnnouncementPriority.Normal,
                    Status = AnnouncementStatus.Draft,
                    AuthorStaffId = "staff-2",
                    CreatedAt = DateTime.Now,
                    Tags = new List<string> { "event", "marathon", "classics" }
                },
                new Announcement
                {
                    Title = "Holiday Season Hours",
                    Body = "Our holiday hours have ended. We are back to regular hours: Mon-Sat 10am-9pm, Sun 11am-7pm.",
                    Category = AnnouncementCategory.General,
                    Priority = AnnouncementPriority.Low,
                    Status = AnnouncementStatus.Expired,
                    AuthorStaffId = "staff-1",
                    ExpiresAt = DateTime.Now.AddDays(-14),
                    CreatedAt = DateTime.Now.AddDays(-45),
                    PublishedAt = DateTime.Now.AddDays(-45),
                    ViewCount = 312
                }
            };

            foreach (var a in items)
            {
                a.Id = _nextId++;
                _announcements.Add(a);
            }

            // Pin the urgent closure announcement
            _pins.Add(new AnnouncementPin
            {
                AnnouncementId = 4,
                PinnedAt = DateTime.Now.AddDays(-1),
                PinnedByStaffId = "staff-1"
            });
        }

        public Announcement GetById(int id) =>
            _announcements.FirstOrDefault(a => a.Id == id);

        public IReadOnlyList<Announcement> GetAll() =>
            _announcements.OrderByDescending(a => a.CreatedAt).ToList().AsReadOnly();

        public void Add(Announcement entity)
        {
            entity.Id = _nextId++;
            entity.CreatedAt = DateTime.Now;
            if (entity.Status == AnnouncementStatus.Active)
                entity.PublishedAt = DateTime.Now;
            _announcements.Add(entity);
        }

        public void Update(Announcement entity)
        {
            var idx = _announcements.FindIndex(a => a.Id == entity.Id);
            if (idx >= 0)
            {
                entity.LastEditedAt = DateTime.Now;
                _announcements[idx] = entity;
            }
        }

        public void Remove(int id)
        {
            _announcements.RemoveAll(a => a.Id == id);
            _acks.RemoveAll(a => a.AnnouncementId == id);
            _pins.RemoveAll(p => p.AnnouncementId == id);
        }

        public IReadOnlyList<Announcement> GetActive()
        {
            var now = DateTime.Now;
            return _announcements
                .Where(a => a.Status == AnnouncementStatus.Active
                    && (a.ScheduledStart == null || a.ScheduledStart <= now)
                    && (a.ExpiresAt == null || a.ExpiresAt > now))
                .OrderByDescending(a => a.Priority)
                .ThenByDescending(a => a.PublishedAt)
                .ToList().AsReadOnly();
        }

        public IReadOnlyList<Announcement> GetByCategory(AnnouncementCategory category) =>
            _announcements.Where(a => a.Category == category)
                .OrderByDescending(a => a.CreatedAt).ToList().AsReadOnly();

        public IReadOnlyList<Announcement> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return GetAll();
            var q = query.Trim().ToLowerInvariant();
            return _announcements
                .Where(a => (a.Title != null && a.Title.ToLowerInvariant().Contains(q))
                    || (a.Body != null && a.Body.ToLowerInvariant().Contains(q))
                    || a.Tags.Any(t => t.ToLowerInvariant().Contains(q)))
                .OrderByDescending(a => a.CreatedAt).ToList().AsReadOnly();
        }

        public IReadOnlyList<Announcement> GetPinned()
        {
            var pinnedIds = _pins.Select(p => p.AnnouncementId).ToHashSet();
            return _announcements.Where(a => pinnedIds.Contains(a.Id))
                .OrderByDescending(a => a.Priority).ToList().AsReadOnly();
        }

        public Announcement Publish(int id)
        {
            var a = GetById(id);
            if (a == null) return null;
            a.Status = AnnouncementStatus.Active;
            a.PublishedAt = DateTime.Now;
            return a;
        }

        public Announcement Archive(int id)
        {
            var a = GetById(id);
            if (a == null) return null;
            a.Status = AnnouncementStatus.Archived;
            return a;
        }

        public void TogglePin(int id, string staffId)
        {
            var existing = _pins.FirstOrDefault(p => p.AnnouncementId == id);
            if (existing != null)
                _pins.Remove(existing);
            else
                _pins.Add(new AnnouncementPin
                {
                    AnnouncementId = id,
                    PinnedAt = DateTime.Now,
                    PinnedByStaffId = staffId
                });
        }

        public void Acknowledge(int announcementId, int customerId)
        {
            if (HasAcknowledged(announcementId, customerId)) return;
            _acks.Add(new AnnouncementAcknowledgment
            {
                Id = _nextAckId++,
                AnnouncementId = announcementId,
                CustomerId = customerId,
                AcknowledgedAt = DateTime.Now
            });
        }

        public bool HasAcknowledged(int announcementId, int customerId) =>
            _acks.Any(a => a.AnnouncementId == announcementId && a.CustomerId == customerId);

        public AnnouncementAnalytics GetAnalytics()
        {
            var pinnedIds = _pins.Select(p => p.AnnouncementId).ToHashSet();
            var analytics = new AnnouncementAnalytics
            {
                TotalAnnouncements = _announcements.Count,
                ActiveCount = _announcements.Count(a => a.Status == AnnouncementStatus.Active),
                DraftCount = _announcements.Count(a => a.Status == AnnouncementStatus.Draft),
                ExpiredCount = _announcements.Count(a => a.Status == AnnouncementStatus.Expired),
                ArchivedCount = _announcements.Count(a => a.Status == AnnouncementStatus.Archived),
                PinnedCount = pinnedIds.Count
            };

            foreach (AnnouncementCategory cat in Enum.GetValues(typeof(AnnouncementCategory)))
            {
                var count = _announcements.Count(a => a.Category == cat);
                if (count > 0) analytics.ByCategory[cat] = count;
            }

            foreach (AnnouncementPriority pri in Enum.GetValues(typeof(AnnouncementPriority)))
            {
                var count = _announcements.Count(a => a.Priority == pri);
                if (count > 0) analytics.ByPriority[pri] = count;
            }

            var requiresAck = _announcements.Where(a => a.RequiresAcknowledgment).ToList();
            if (requiresAck.Any())
            {
                var rates = requiresAck.Select(a =>
                {
                    var ackCount = _acks.Count(ak => ak.AnnouncementId == a.Id);
                    return a.ViewCount > 0 ? (double)ackCount / a.ViewCount : 0;
                });
                analytics.AverageAcknowledgmentRate = rates.Average();
            }

            var mostViewed = _announcements.OrderByDescending(a => a.ViewCount).FirstOrDefault();
            analytics.MostViewedAnnouncement = mostViewed;

            return analytics;
        }
    }
}
