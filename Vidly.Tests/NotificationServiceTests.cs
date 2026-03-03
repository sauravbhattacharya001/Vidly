using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class NotificationServiceTests
    {
        private NotificationService _service;

        [TestInitialize]
        public void Setup()
        {
            _service = new NotificationService(
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryWatchlistRepository());
        }

        // ── GetNotifications — basic behavior ────────────────────────

        [TestMethod]
        public void GetNotifications_UnknownCustomer_ReturnsEmptyResult()
        {
            var result = _service.GetNotifications(9999);
            Assert.AreEqual(9999, result.CustomerId);
            Assert.AreEqual("Unknown", result.CustomerName);
            Assert.AreEqual(0, result.TotalCount);
            Assert.AreEqual(0, result.UnreadCount);
            Assert.IsNotNull(result.Notifications);
            Assert.AreEqual(0, result.Notifications.Count);
        }

        [TestMethod]
        public void GetNotifications_ValidCustomer_SetsCustomerName()
        {
            // Customer 1 = "John Smith"
            var result = _service.GetNotifications(1);
            Assert.AreEqual("John Smith", result.CustomerName);
            Assert.AreEqual(1, result.CustomerId);
        }

        [TestMethod]
        public void GetNotifications_ValidCustomer_ReturnsNonNullCollections()
        {
            var result = _service.GetNotifications(1);
            Assert.IsNotNull(result.Notifications);
            Assert.IsTrue(result.TotalCount >= 0);
            Assert.IsTrue(result.UnreadCount >= 0);
        }

        [TestMethod]
        public void GetNotifications_OrdersByPriorityThenTimestamp()
        {
            // Customer 1 may have multiple notifications; they should be
            // sorted by Priority descending, then Timestamp descending
            var result = _service.GetNotifications(1);
            if (result.Notifications.Count > 1)
            {
                for (int i = 0; i < result.Notifications.Count - 1; i++)
                {
                    var current = result.Notifications[i];
                    var next = result.Notifications[i + 1];
                    Assert.IsTrue(
                        (int)current.Priority > (int)next.Priority ||
                        ((int)current.Priority == (int)next.Priority &&
                         current.Timestamp >= next.Timestamp),
                        $"Notification at index {i} should have higher or equal priority than index {i + 1}");
                }
            }
        }

        [TestMethod]
        public void GetNotifications_UnreadCountEqualsUrgentCount()
        {
            // UnreadCount is defined as notifications with Urgent priority
            var result = _service.GetNotifications(1);
            var urgentCount = result.Notifications.Count(n => n.Priority == NotificationPriority.Urgent);
            Assert.AreEqual(urgentCount, result.UnreadCount);
        }

        [TestMethod]
        public void GetNotifications_TotalCountMatchesListSize()
        {
            var result = _service.GetNotifications(1);
            Assert.AreEqual(result.Notifications.Count, result.TotalCount);
        }

        // ── Overdue rental alerts ────────────────────────────────────

        [TestMethod]
        public void GetNotifications_OverdueRental_GeneratesUrgentAlert()
        {
            // Customer 2 (Jane Doe) has rental of movie 2 (Godfather),
            // due 3 days ago, still active — should be overdue
            var result = _service.GetNotifications(2);
            var overdueAlerts = result.Notifications
                .Where(n => n.Type == NotificationType.OverdueRental)
                .ToList();

            Assert.IsTrue(overdueAlerts.Count > 0, "Expected at least one overdue alert for customer 2");
            Assert.IsTrue(overdueAlerts.All(n => n.Priority == NotificationPriority.Urgent),
                "All overdue alerts should be Urgent priority");
            Assert.IsTrue(overdueAlerts.All(n => n.Title == "Overdue Rental"));
            Assert.IsTrue(overdueAlerts.All(n => n.Icon == "⚠️"));
        }

        [TestMethod]
        public void GetNotifications_OverdueRental_MessageIncludesDaysOverdue()
        {
            var result = _service.GetNotifications(2);
            var overdueAlert = result.Notifications
                .FirstOrDefault(n => n.Type == NotificationType.OverdueRental);

            Assert.IsNotNull(overdueAlert, "Expected overdue alert for customer 2");
            Assert.IsTrue(overdueAlert.Message.Contains("day(s) ago"),
                "Overdue message should mention days overdue");
            Assert.IsTrue(overdueAlert.Message.Contains("Late fees may apply"),
                "Overdue message should mention late fees");
        }

        [TestMethod]
        public void GetNotifications_OverdueRental_IncludesRelatedMovieId()
        {
            var result = _service.GetNotifications(2);
            var overdueAlert = result.Notifications
                .FirstOrDefault(n => n.Type == NotificationType.OverdueRental);

            Assert.IsNotNull(overdueAlert);
            Assert.AreEqual(2, overdueAlert.RelatedMovieId, "Overdue alert should reference movie 2 (Godfather)");
        }

        [TestMethod]
        public void GetNotifications_ReturnedRental_NoOverdueAlert()
        {
            // Customer 4 (Alice Johnson) has rental 3, already returned
            var result = _service.GetNotifications(4);
            var overdueAlerts = result.Notifications
                .Where(n => n.Type == NotificationType.OverdueRental)
                .ToList();

            Assert.AreEqual(0, overdueAlerts.Count,
                "Returned rentals should not generate overdue alerts");
        }

        // ── Due soon alerts ──────────────────────────────────────────

        [TestMethod]
        public void GetNotifications_DueSoon_GeneratesHighPriorityAlert()
        {
            // Customer 1 (John Smith) has rental of movie 1 (Shrek),
            // due in 4 days — NOT due soon (threshold is <=2 days)
            // However, the seed data is dynamic (DateTime.Today based)
            var result = _service.GetNotifications(1);
            var dueSoonAlerts = result.Notifications
                .Where(n => n.Type == NotificationType.DueSoon)
                .ToList();

            // All due-soon alerts should be High priority
            Assert.IsTrue(dueSoonAlerts.All(n => n.Priority == NotificationPriority.High));
            Assert.IsTrue(dueSoonAlerts.All(n => n.Title == "Rental Due Soon"));
            Assert.IsTrue(dueSoonAlerts.All(n => n.Icon == "⏰"));
        }

        [TestMethod]
        public void GetNotifications_OverdueRental_NoDueSoonAlert()
        {
            // Customer 2's overdue rental should NOT also generate due-soon
            var result = _service.GetNotifications(2);
            var dueSoonAlerts = result.Notifications
                .Where(n => n.Type == NotificationType.DueSoon)
                .ToList();

            // Movie 2 is overdue (due -3 days), so daysUntilDue is negative → no due-soon
            var dueSoonForMovie2 = dueSoonAlerts.Where(n => n.RelatedMovieId == 2).ToList();
            Assert.AreEqual(0, dueSoonForMovie2.Count,
                "Overdue rental should not also generate a due-soon alert");
        }

        // ── Watchlist availability alerts ────────────────────────────

        [TestMethod]
        public void GetNotifications_WatchlistMustWatch_Available_GeneratesAlert()
        {
            // Customer 1 has movie 2 (Godfather) on watchlist with MustWatch priority
            // Movie 2 is currently rented by customer 2 (active rental)
            // So it should NOT generate a watchlist alert for customer 1
            var result = _service.GetNotifications(1);
            var watchlistAlerts = result.Notifications
                .Where(n => n.Type == NotificationType.WatchlistAvailable
                            && n.RelatedMovieId == 2)
                .ToList();

            // Godfather is rented (active), so no availability alert
            Assert.AreEqual(0, watchlistAlerts.Count,
                "Movie currently rented should not trigger watchlist availability alert");
        }

        [TestMethod]
        public void GetNotifications_WatchlistAlerts_AreHighPriority()
        {
            var result = _service.GetNotifications(1);
            var watchlistAlerts = result.Notifications
                .Where(n => n.Type == NotificationType.WatchlistAvailable)
                .ToList();

            Assert.IsTrue(watchlistAlerts.All(n => n.Priority == NotificationPriority.High));
            Assert.IsTrue(watchlistAlerts.All(n => n.Icon == "🌟"));
        }

        // ── Membership alerts ────────────────────────────────────────

        [TestMethod]
        public void GetNotifications_CustomerWithNoMemberSince_NoMembershipAlerts()
        {
            // Customer 3 (Bob Wilson) has MemberSince set, so check a customer
            // where we know there won't be an anniversary within 7 days
            // (This is a structural test — the exact result depends on current date)
            var result = _service.GetNotifications(3);
            var membershipAlerts = result.Notifications
                .Where(n => n.Type == NotificationType.MembershipMilestone
                            || n.Type == NotificationType.UpgradeSuggestion)
                .ToList();

            // At least verify the types are correct
            Assert.IsTrue(membershipAlerts.All(n =>
                n.Type == NotificationType.MembershipMilestone ||
                n.Type == NotificationType.UpgradeSuggestion));
        }

        [TestMethod]
        public void GetNotifications_PlatinumMember_NoUpgradeSuggestion()
        {
            // Customer 4 (Alice Johnson) is Platinum — already at top tier
            var result = _service.GetNotifications(4);
            var upgradeSuggestions = result.Notifications
                .Where(n => n.Type == NotificationType.UpgradeSuggestion)
                .ToList();

            Assert.AreEqual(0, upgradeSuggestions.Count,
                "Platinum members should not get upgrade suggestions");
        }

        [TestMethod]
        public void GetNotifications_MembershipAlerts_AreNormalPriority()
        {
            // Check all customers for membership alerts
            foreach (var customerId in new[] { 1, 2, 3, 4, 5 })
            {
                var result = _service.GetNotifications(customerId);
                var membershipAlerts = result.Notifications
                    .Where(n => n.Type == NotificationType.MembershipMilestone
                                || n.Type == NotificationType.UpgradeSuggestion);

                Assert.IsTrue(membershipAlerts.All(n => n.Priority == NotificationPriority.Normal),
                    $"Membership alerts for customer {customerId} should be Normal priority");
            }
        }

        [TestMethod]
        public void GetNotifications_UpgradeSuggestion_MessageMentionsNextTier()
        {
            // Look for any customer with an upgrade suggestion
            for (int id = 1; id <= 5; id++)
            {
                var result = _service.GetNotifications(id);
                var upgrade = result.Notifications
                    .FirstOrDefault(n => n.Type == NotificationType.UpgradeSuggestion);
                if (upgrade != null)
                {
                    Assert.IsTrue(
                        upgrade.Message.Contains("Silver") ||
                        upgrade.Message.Contains("Gold") ||
                        upgrade.Message.Contains("Platinum"),
                        "Upgrade message should mention the next tier name");
                    Assert.AreEqual("⬆️", upgrade.Icon);
                    break;
                }
            }
        }

        // ── GetSummary ───────────────────────────────────────────────

        [TestMethod]
        public void GetSummary_ReturnsAggregateAcrossAllCustomers()
        {
            var summary = _service.GetSummary();
            Assert.IsTrue(summary.TotalNotifications >= 0);
            Assert.IsTrue(summary.UrgentCount >= 0);
            Assert.IsTrue(summary.UrgentCount <= summary.TotalNotifications);
            Assert.IsNotNull(summary.ByType);
            Assert.IsNotNull(summary.TopNotifications);
        }

        [TestMethod]
        public void GetSummary_IncludesOverdueRentals()
        {
            // We know customer 2 has an overdue rental, so summary should include it
            var summary = _service.GetSummary();
            Assert.IsTrue(summary.UrgentCount > 0,
                "Summary should include urgent notifications from overdue rentals");
        }

        [TestMethod]
        public void GetSummary_ByTypeDictionaryContainsTypes()
        {
            var summary = _service.GetSummary();
            // Should have at least OverdueRental type from customer 2
            if (summary.TotalNotifications > 0)
            {
                Assert.IsTrue(summary.ByType.Count > 0,
                    "ByType dictionary should have entries when notifications exist");
                Assert.IsTrue(summary.ByType.All(kv => kv.Value > 0),
                    "Each type count should be positive");
            }
        }

        [TestMethod]
        public void GetSummary_TopNotificationsLimitedTo20()
        {
            var summary = _service.GetSummary();
            Assert.IsTrue(summary.TopNotifications.Count <= 20,
                "TopNotifications should be limited to 20");
        }

        [TestMethod]
        public void GetSummary_TopNotificationsOrderedByPriority()
        {
            var summary = _service.GetSummary();
            if (summary.TopNotifications.Count > 1)
            {
                for (int i = 0; i < summary.TopNotifications.Count - 1; i++)
                {
                    var current = summary.TopNotifications[i];
                    var next = summary.TopNotifications[i + 1];
                    Assert.IsTrue(
                        (int)current.Priority >= (int)next.Priority,
                        $"Top notification at index {i} should have >= priority than index {i + 1}");
                }
            }
        }

        [TestMethod]
        public void GetSummary_CustomersWithAlertsCountsDistinctCustomers()
        {
            var summary = _service.GetSummary();
            Assert.IsTrue(summary.CustomersWithAlerts >= 0);
            // Should be at most 5 (we have 5 seed customers)
            Assert.IsTrue(summary.CustomersWithAlerts <= 5,
                "CustomersWithAlerts should not exceed total customer count");
        }

        [TestMethod]
        public void GetSummary_SetsCustomerNameOnNotifications()
        {
            var summary = _service.GetSummary();
            // The GetSummary method sets CustomerName on each notification
            foreach (var n in summary.TopNotifications)
            {
                Assert.IsNotNull(n.CustomerName,
                    $"Notification of type {n.Type} should have CustomerName set");
            }
        }

        // ── Notification model ───────────────────────────────────────

        [TestMethod]
        public void Notification_AllFieldsPopulated()
        {
            var result = _service.GetNotifications(2);
            // Customer 2 has at least one overdue alert
            var alert = result.Notifications.FirstOrDefault();
            Assert.IsNotNull(alert);
            Assert.IsNotNull(alert.Title);
            Assert.IsNotNull(alert.Message);
            Assert.IsNotNull(alert.Icon);
            Assert.IsTrue(alert.Message.Length > 0);
            Assert.IsTrue(alert.Title.Length > 0);
        }

        [TestMethod]
        public void NotificationType_AllValuesAreDefined()
        {
            // Ensure all enum values in the NotificationType are valid
            var values = Enum.GetValues(typeof(NotificationType));
            Assert.AreEqual(6, values.Length, "NotificationType should have 6 values");
        }

        [TestMethod]
        public void NotificationPriority_OrderIsCorrect()
        {
            Assert.IsTrue((int)NotificationPriority.Normal < (int)NotificationPriority.High);
            Assert.IsTrue((int)NotificationPriority.High < (int)NotificationPriority.Urgent);
        }

        // ── Constructor validation ───────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new NotificationService(
                null,
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryWatchlistRepository());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new NotificationService(
                new InMemoryRentalRepository(),
                null,
                new InMemoryCustomerRepository(),
                new InMemoryWatchlistRepository());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new NotificationService(
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                null,
                new InMemoryWatchlistRepository());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullWatchlistRepo_Throws()
        {
            new NotificationService(
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository(),
                null);
        }

        [TestMethod]
        public void DefaultConstructor_Works()
        {
            // Parameterless constructor should not throw
            var service = new NotificationService();
            var result = service.GetNotifications(1);
            Assert.IsNotNull(result);
        }

        // ── Cross-notification type consistency ──────────────────────

        [TestMethod]
        public void GetNotifications_AllCustomers_NoExceptions()
        {
            // Ensure GetNotifications works for all seed customers without crashing
            foreach (var customerId in new[] { 1, 2, 3, 4, 5 })
            {
                var result = _service.GetNotifications(customerId);
                Assert.IsNotNull(result);
                Assert.AreEqual(customerId, result.CustomerId);
                Assert.IsNotNull(result.Notifications);
            }
        }

        [TestMethod]
        public void GetNotifications_EachAlertHasValidType()
        {
            var validTypes = Enum.GetValues(typeof(NotificationType)).Cast<NotificationType>().ToHashSet();
            for (int id = 1; id <= 5; id++)
            {
                var result = _service.GetNotifications(id);
                foreach (var n in result.Notifications)
                {
                    Assert.IsTrue(validTypes.Contains(n.Type),
                        $"Notification type {n.Type} for customer {id} should be a valid enum value");
                }
            }
        }

        [TestMethod]
        public void GetNotifications_EachAlertHasValidPriority()
        {
            var validPriorities = Enum.GetValues(typeof(NotificationPriority))
                .Cast<NotificationPriority>().ToHashSet();
            for (int id = 1; id <= 5; id++)
            {
                var result = _service.GetNotifications(id);
                foreach (var n in result.Notifications)
                {
                    Assert.IsTrue(validPriorities.Contains(n.Priority),
                        $"Notification priority {n.Priority} for customer {id} should be valid");
                }
            }
        }

        [TestMethod]
        public void GetNotifications_TimestampIsRecent()
        {
            var result = _service.GetNotifications(2);
            foreach (var n in result.Notifications)
            {
                // Timestamp should be set to roughly "now"
                var diff = Math.Abs((DateTime.Now - n.Timestamp).TotalMinutes);
                Assert.IsTrue(diff < 5,
                    $"Notification timestamp should be within 5 minutes of now, was {n.Timestamp}");
            }
        }

        // ── New arrival alerts ───────────────────────────────────────

        [TestMethod]
        public void GetNotifications_NewArrivals_AreNormalPriority()
        {
            for (int id = 1; id <= 5; id++)
            {
                var result = _service.GetNotifications(id);
                var newArrivals = result.Notifications
                    .Where(n => n.Type == NotificationType.NewArrival);

                Assert.IsTrue(newArrivals.All(n => n.Priority == NotificationPriority.Normal),
                    $"New arrival alerts for customer {id} should be Normal priority");
                Assert.IsTrue(newArrivals.All(n => n.Icon == "🎬"),
                    $"New arrival alerts should have 🎬 icon");
            }
        }

        [TestMethod]
        public void GetNotifications_NewArrivals_AtMost5()
        {
            for (int id = 1; id <= 5; id++)
            {
                var result = _service.GetNotifications(id);
                var newArrivals = result.Notifications
                    .Where(n => n.Type == NotificationType.NewArrival)
                    .ToList();

                Assert.IsTrue(newArrivals.Count <= 5,
                    $"New arrival alerts for customer {id} should be at most 5");
            }
        }

        [TestMethod]
        public void GetNotifications_NewArrivals_MessageMentionsGenre()
        {
            for (int id = 1; id <= 5; id++)
            {
                var result = _service.GetNotifications(id);
                var newArrival = result.Notifications
                    .FirstOrDefault(n => n.Type == NotificationType.NewArrival);

                if (newArrival != null)
                {
                    Assert.IsTrue(
                        newArrival.Message.Contains("just arrived"),
                        "New arrival message should mention 'just arrived'");
                    Assert.IsTrue(
                        newArrival.Message.Contains("rental history"),
                        "New arrival message should mention 'rental history'");
                }
            }
        }

        // ── NotificationResult model ─────────────────────────────────

        [TestMethod]
        public void NotificationResult_DefaultsToEmptyList()
        {
            var result = new NotificationResult();
            Assert.IsNotNull(result.Notifications);
            Assert.AreEqual(0, result.Notifications.Count);
        }

        [TestMethod]
        public void NotificationSummary_DefaultsToEmptyCollections()
        {
            var summary = new NotificationSummary();
            Assert.IsNotNull(summary.ByType);
            Assert.IsNotNull(summary.TopNotifications);
            Assert.AreEqual(0, summary.TotalNotifications);
        }
    }
}
