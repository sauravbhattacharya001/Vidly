using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Repository for managing store announcements.
    /// </summary>
    public interface IAnnouncementRepository : IRepository<Announcement>
    {
        /// <summary>Returns announcements visible to customers (Active, not expired).</summary>
        IReadOnlyList<Announcement> GetActive();

        /// <summary>Returns announcements filtered by category.</summary>
        IReadOnlyList<Announcement> GetByCategory(AnnouncementCategory category);

        /// <summary>Returns announcements matching a search query (title/body).</summary>
        IReadOnlyList<Announcement> Search(string query);

        /// <summary>Returns pinned announcements.</summary>
        IReadOnlyList<Announcement> GetPinned();

        /// <summary>Publishes a draft announcement (sets status to Active).</summary>
        Announcement Publish(int id);

        /// <summary>Archives an announcement.</summary>
        Announcement Archive(int id);

        /// <summary>Pins or unpins an announcement.</summary>
        void TogglePin(int id, string staffId);

        /// <summary>Records that a customer acknowledged an announcement.</summary>
        void Acknowledge(int announcementId, int customerId);

        /// <summary>Checks if a customer has acknowledged an announcement.</summary>
        bool HasAcknowledged(int announcementId, int customerId);

        /// <summary>Returns analytics/statistics about announcements.</summary>
        AnnouncementAnalytics GetAnalytics();
    }
}
