using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Store-wide announcement bulletin board: create, schedule, target by membership tier,
    /// pin important notices, track acknowledgments, and generate engagement analytics.
    /// </summary>
    public class StoreAnnouncementService
    {
        private readonly List<Announcement> _announcements = new List<Announcement>();
        private readonly List<AnnouncementAcknowledgment> _acks = new List<AnnouncementAcknowledgment>();
        private readonly List<AnnouncementPin> _pins = new List<AnnouncementPin>();
        private readonly IClock _clock;
        private int _nextId = 1;
        private int _nextAckId = 1;

        public StoreAnnouncementService() : this(new SystemClock()) { }

        public StoreAnnouncementService(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        // ── CRUD ──────────────────────────────────────────────────

        /// <summary>Create a draft announcement.</summary>
        public Announcement Create(Announcement a)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (string.IsNullOrWhiteSpace(a.Title))
                throw new ArgumentException("Title is required.");
            if (string.IsNullOrWhiteSpace(a.Body))
                throw new ArgumentException("Body is required.");
            if (a.ExpiresAt.HasValue && a.ScheduledStart.HasValue && a.ExpiresAt <= a.ScheduledStart)
                throw new ArgumentException("Expiry must be after scheduled start.");

            a.Id = _nextId++;
            a.Status = AnnouncementStatus.Draft;
            a.CreatedAt = _clock.Now;
            a.ViewCount = 0;
            _announcements.Add(a);
            return a;
        }

        /// <summary>Update a draft or active announcement.</summary>
        public Announcement Update(int id, Action<Announcement> modifier)
        {
            var a = GetById(id);
            if (a.Status == AnnouncementStatus.Archived)
                throw new InvalidOperationException("Cannot edit archived announcements.");
            modifier(a);
            if (string.IsNullOrWhiteSpace(a.Title))
                throw new ArgumentException("Title cannot be empty.");
            a.LastEditedAt = _clock.Now;
            return a;
        }

        /// <summary>Get announcement by ID.</summary>
        public Announcement GetById(int id)
        {
            var a = _announcements.FirstOrDefault(x => x.Id == id);
            if (a == null)
                throw new KeyNotFoundException($"Announcement {id} not found.");
            return a;
        }

        // ── Lifecycle ─────────────────────────────────────────────

        /// <summary>Publish a draft immediately or schedule for later.</summary>
        public Announcement Publish(int id)
        {
            var a = GetById(id);
            if (a.Status != AnnouncementStatus.Draft)
                throw new InvalidOperationException($"Can only publish drafts, current status: {a.Status}.");

            if (a.ScheduledStart.HasValue && a.ScheduledStart.Value > _clock.Now)
            {
                a.Status = AnnouncementStatus.Scheduled;
            }
            else
            {
                a.Status = AnnouncementStatus.Active;
                a.PublishedAt = _clock.Now;
            }
            return a;
        }

        /// <summary>Activate scheduled announcements whose start time has arrived.</summary>
        public List<Announcement> ActivateScheduled()
        {
            var now = _clock.Now;
            var toActivate = _announcements
                .Where(a => a.Status == AnnouncementStatus.Scheduled
                         && a.ScheduledStart.HasValue
                         && a.ScheduledStart.Value <= now)
                .ToList();

            foreach (var a in toActivate)
            {
                a.Status = AnnouncementStatus.Active;
                a.PublishedAt = now;
            }
            return toActivate;
        }

        /// <summary>Expire announcements past their expiry date.</summary>
        public List<Announcement> ExpireStale()
        {
            var now = _clock.Now;
            var toExpire = _announcements
                .Where(a => a.Status == AnnouncementStatus.Active
                         && a.ExpiresAt.HasValue
                         && a.ExpiresAt.Value <= now)
                .ToList();

            foreach (var a in toExpire)
                a.Status = AnnouncementStatus.Expired;
            return toExpire;
        }

        /// <summary>Archive an announcement (removes from active board).</summary>
        public Announcement Archive(int id)
        {
            var a = GetById(id);
            if (a.Status == AnnouncementStatus.Draft)
                throw new InvalidOperationException("Cannot archive a draft; delete it instead.");
            a.Status = AnnouncementStatus.Archived;
            return a;
        }

        // ── Pinning ───────────────────────────────────────────────

        /// <summary>Pin an active announcement to the top of the board.</summary>
        public void Pin(int announcementId, string staffId)
        {
            var a = GetById(announcementId);
            if (a.Status != AnnouncementStatus.Active)
                throw new InvalidOperationException("Only active announcements can be pinned.");
            if (_pins.Any(p => p.AnnouncementId == announcementId))
                throw new InvalidOperationException("Announcement is already pinned.");

            _pins.Add(new AnnouncementPin
            {
                AnnouncementId = announcementId,
                PinnedAt = _clock.Now,
                PinnedByStaffId = staffId
            });
        }

        /// <summary>Unpin an announcement.</summary>
        public void Unpin(int announcementId)
        {
            var pin = _pins.FirstOrDefault(p => p.AnnouncementId == announcementId);
            if (pin == null)
                throw new InvalidOperationException("Announcement is not pinned.");
            _pins.Remove(pin);
        }

        public bool IsPinned(int announcementId) =>
            _pins.Any(p => p.AnnouncementId == announcementId);

        // ── Acknowledgment ────────────────────────────────────────

        /// <summary>Customer acknowledges/dismisses an announcement.</summary>
        public AnnouncementAcknowledgment Acknowledge(int announcementId, int customerId)
        {
            var a = GetById(announcementId);
            if (!a.RequiresAcknowledgment)
                throw new InvalidOperationException("This announcement does not require acknowledgment.");
            if (_acks.Any(x => x.AnnouncementId == announcementId && x.CustomerId == customerId))
                throw new InvalidOperationException("Already acknowledged.");

            var ack = new AnnouncementAcknowledgment
            {
                Id = _nextAckId++,
                AnnouncementId = announcementId,
                CustomerId = customerId,
                AcknowledgedAt = _clock.Now
            };
            _acks.Add(ack);
            return ack;
        }

        /// <summary>Check if a customer has acknowledged a specific announcement.</summary>
        public bool HasAcknowledged(int announcementId, int customerId) =>
            _acks.Any(x => x.AnnouncementId == announcementId && x.CustomerId == customerId);

        /// <summary>Get all customers who acknowledged a given announcement.</summary>
        public List<AnnouncementAcknowledgment> GetAcknowledgments(int announcementId) =>
            _acks.Where(x => x.AnnouncementId == announcementId).ToList();

        /// <summary>Get unacknowledged announcements for a customer.</summary>
        public List<Announcement> GetPendingAcknowledgments(int customerId) =>
            _announcements
                .Where(a => a.Status == AnnouncementStatus.Active
                         && a.RequiresAcknowledgment
                         && !_acks.Any(x => x.AnnouncementId == a.Id && x.CustomerId == customerId))
                .OrderByDescending(a => a.Priority)
                .ToList();

        // ── Viewing / Recording Views ─────────────────────────────

        /// <summary>Record that a customer viewed an announcement.</summary>
        public void RecordView(int announcementId)
        {
            var a = GetById(announcementId);
            a.ViewCount++;
        }

        // ── Board Queries ─────────────────────────────────────────

        /// <summary>
        /// Get the announcement board for a customer. Pinned items first, then by priority/date.
        /// Filters by tier targeting and applies optional filter criteria.
        /// </summary>
        public List<Announcement> GetBoard(string customerTier = null, AnnouncementFilter filter = null)
        {
            var query = _announcements.Where(a => a.Status == AnnouncementStatus.Active).AsEnumerable();

            // Tier targeting: show if no tier restriction, or customer tier matches
            if (!string.IsNullOrEmpty(customerTier))
            {
                query = query.Where(a =>
                    a.TargetTiers == null || a.TargetTiers.Count == 0
                    || a.TargetTiers.Contains(customerTier, StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                // No tier provided: only show untargeted announcements
                query = query.Where(a => a.TargetTiers == null || a.TargetTiers.Count == 0);
            }

            if (filter != null)
            {
                if (filter.Category.HasValue)
                    query = query.Where(a => a.Category == filter.Category.Value);
                if (filter.MinPriority.HasValue)
                    query = query.Where(a => a.Priority >= filter.MinPriority.Value);
                if (filter.Tag != null)
                    query = query.Where(a => a.Tags != null && a.Tags.Contains(filter.Tag, StringComparer.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(filter.SearchText))
                    query = query.Where(a =>
                        a.Title.IndexOf(filter.SearchText, StringComparison.OrdinalIgnoreCase) >= 0
                        || a.Body.IndexOf(filter.SearchText, StringComparison.OrdinalIgnoreCase) >= 0);
                if (filter.PinnedOnly == true)
                    query = query.Where(a => IsPinned(a.Id));
                if (filter.RequiresAcknowledgment == true)
                    query = query.Where(a => a.RequiresAcknowledgment);
            }

            // Sort: pinned first, then priority descending, then newest first
            return query
                .OrderByDescending(a => IsPinned(a.Id) ? 1 : 0)
                .ThenByDescending(a => a.Priority)
                .ThenByDescending(a => a.PublishedAt ?? a.CreatedAt)
                .ToList();
        }

        /// <summary>Get all announcements with optional status filter.</summary>
        public List<Announcement> GetAll(AnnouncementStatus? status = null)
        {
            var query = _announcements.AsEnumerable();
            if (status.HasValue)
                query = query.Where(a => a.Status == status.Value);
            return query.OrderByDescending(a => a.CreatedAt).ToList();
        }

        // ── Analytics ─────────────────────────────────────────────

        /// <summary>Generate engagement analytics for the announcement board.</summary>
        public AnnouncementAnalytics GetAnalytics()
        {
            var analytics = new AnnouncementAnalytics
            {
                TotalAnnouncements = _announcements.Count,
                ActiveCount = _announcements.Count(a => a.Status == AnnouncementStatus.Active),
                DraftCount = _announcements.Count(a => a.Status == AnnouncementStatus.Draft),
                ExpiredCount = _announcements.Count(a => a.Status == AnnouncementStatus.Expired),
                ArchivedCount = _announcements.Count(a => a.Status == AnnouncementStatus.Archived),
                PinnedCount = _pins.Count
            };

            // Category breakdown
            foreach (AnnouncementCategory cat in Enum.GetValues(typeof(AnnouncementCategory)))
            {
                var count = _announcements.Count(a => a.Category == cat);
                if (count > 0)
                    analytics.ByCategory[cat] = count;
            }

            // Priority breakdown
            foreach (AnnouncementPriority p in Enum.GetValues(typeof(AnnouncementPriority)))
            {
                var count = _announcements.Count(a => a.Priority == p);
                if (count > 0)
                    analytics.ByPriority[p] = count;
            }

            // Acknowledgment rate
            var ackRequired = _announcements.Where(a => a.RequiresAcknowledgment).ToList();
            if (ackRequired.Count > 0)
            {
                var rates = ackRequired.Select(a =>
                {
                    var ackCount = _acks.Count(x => x.AnnouncementId == a.Id);
                    return ackCount; // raw count (rate per-announcement)
                }).ToList();
                analytics.AverageAcknowledgmentRate = rates.Average();
            }

            // Most viewed
            analytics.MostViewedAnnouncement = _announcements
                .OrderByDescending(a => a.ViewCount)
                .FirstOrDefault();

            // Recent 7-day trends
            var last7Days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(-i)).ToList();
            analytics.RecentTrends = last7Days.Select(date => new AnnouncementTrend
            {
                Date = date,
                Published = _announcements.Count(a => a.PublishedAt.HasValue && a.PublishedAt.Value.Date == date),
                Expired = _announcements.Count(a => a.ExpiresAt.HasValue && a.ExpiresAt.Value.Date == date && a.Status == AnnouncementStatus.Expired),
                Acknowledged = _acks.Count(x => x.AcknowledgedAt.Date == date)
            }).OrderBy(t => t.Date).ToList();

            return analytics;
        }

        /// <summary>Get acknowledgment rate for a specific announcement.</summary>
        public double GetAcknowledgmentRate(int announcementId, int totalCustomers)
        {
            if (totalCustomers <= 0) throw new ArgumentException("Total customers must be positive.");
            var a = GetById(announcementId);
            if (!a.RequiresAcknowledgment) return 0;
            var ackCount = _acks.Count(x => x.AnnouncementId == announcementId);
            return (double)ackCount / totalCustomers * 100;
        }

        /// <summary>Search announcements across title, body, and tags.</summary>
        public List<Announcement> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Search query cannot be empty.");

            return _announcements
                .Where(a =>
                    a.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || a.Body.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || (a.Tags != null && a.Tags.Any(t => t.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)))
                .OrderByDescending(a => a.Priority)
                .ThenByDescending(a => a.CreatedAt)
                .ToList();
        }

        /// <summary>Duplicate an announcement as a new draft (for re-use).</summary>
        public Announcement Duplicate(int id)
        {
            var original = GetById(id);
            var copy = new Announcement
            {
                Title = original.Title + " (Copy)",
                Body = original.Body,
                Category = original.Category,
                Priority = original.Priority,
                AuthorStaffId = original.AuthorStaffId,
                ScheduledStart = null,
                ExpiresAt = null,
                TargetTiers = new List<string>(original.TargetTiers ?? new List<string>()),
                RequiresAcknowledgment = original.RequiresAcknowledgment,
                RelatedMovieId = original.RelatedMovieId,
                Tags = new List<string>(original.Tags ?? new List<string>())
            };
            return Create(copy);
        }
    }
}
