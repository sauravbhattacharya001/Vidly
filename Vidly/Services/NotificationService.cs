using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Generates in-app notifications for customers: overdue rentals,
    /// upcoming due dates, new arrivals in preferred genres, watchlist
    /// availability, and membership anniversaries.
    /// </summary>
    public class NotificationService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IWatchlistRepository _watchlistRepository;

        public NotificationService()
            : this(new InMemoryRentalRepository(),
                   new InMemoryMovieRepository(),
                   new InMemoryCustomerRepository(),
                   new InMemoryWatchlistRepository())
        {
        }

        public NotificationService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IWatchlistRepository watchlistRepository)
        {
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _watchlistRepository = watchlistRepository ?? throw new ArgumentNullException(nameof(watchlistRepository));
        }

        /// <summary>
        /// Gets all notifications for a specific customer.
        /// </summary>
        public NotificationResult GetNotifications(int customerId)
        {
            var customer = _customerRepository.GetAll().FirstOrDefault(c => c.Id == customerId);
            if (customer == null)
                return new NotificationResult { CustomerId = customerId, CustomerName = "Unknown" };

            var notifications = new List<Notification>();

            notifications.AddRange(GetOverdueAlerts(customerId));
            notifications.AddRange(GetDueSoonAlerts(customerId));
            notifications.AddRange(GetNewArrivalAlerts(customerId));
            notifications.AddRange(GetWatchlistAlerts(customerId));
            notifications.AddRange(GetMembershipAlerts(customer));

            return new NotificationResult
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                Notifications = notifications.OrderByDescending(n => n.Priority)
                    .ThenByDescending(n => n.Timestamp)
                    .ToList(),
                UnreadCount = notifications.Count(n => n.Priority == NotificationPriority.Urgent),
                TotalCount = notifications.Count
            };
        }

        /// <summary>
        /// Gets a notification summary across all customers (admin view).
        /// </summary>
        public NotificationSummary GetSummary()
        {
            var customers = _customerRepository.GetAll();
            var allNotifications = new List<Notification>();

            foreach (var customer in customers)
            {
                var result = GetNotifications(customer.Id);
                foreach (var n in result.Notifications)
                {
                    n.CustomerName = customer.Name;
                    allNotifications.Add(n);
                }
            }

            var grouped = allNotifications.GroupBy(n => n.Type)
                .ToDictionary(g => g.Key, g => g.Count());

            return new NotificationSummary
            {
                TotalNotifications = allNotifications.Count,
                UrgentCount = allNotifications.Count(n => n.Priority == NotificationPriority.Urgent),
                ByType = grouped,
                TopNotifications = allNotifications
                    .OrderByDescending(n => n.Priority)
                    .ThenByDescending(n => n.Timestamp)
                    .Take(20)
                    .ToList(),
                CustomersWithAlerts = allNotifications
                    .Select(n => n.CustomerName)
                    .Where(name => name != null)
                    .Distinct()
                    .Count()
            };
        }

        private IEnumerable<Notification> GetOverdueAlerts(int customerId)
        {
            var rentals = _rentalRepository.GetAll()
                .Where(r => r.CustomerId == customerId && !r.ReturnDate.HasValue)
                .ToList();

            // Build movie lookup once instead of calling GetAll() per rental (N+1 fix)
            var movieLookup = _movieRepository.GetAll().ToDictionary(m => m.Id);

            foreach (var rental in rentals)
            {
                // Use the actual DueDate from the rental — it already accounts for
                // membership-tier extended rental periods (Silver +1, Gold +2, Platinum +3).
                // The previous hardcoded "7 days" logic was incorrect for non-Basic members.
                if (DateTime.Today > rental.DueDate)
                {
                    var daysOverdue = (int)(DateTime.Today - rental.DueDate).TotalDays;
                    movieLookup.TryGetValue(rental.MovieId, out var movie);
                    yield return new Notification
                    {
                        Type = NotificationType.OverdueRental,
                        Priority = NotificationPriority.Urgent,
                        Title = "Overdue Rental",
                        Message = $"\"{movie?.Name ?? "Unknown"}\" was due {daysOverdue} day(s) ago. Late fees may apply.",
                        Timestamp = DateTime.Now,
                        RelatedMovieId = rental.MovieId,
                        Icon = "⚠️"
                    };
                }
            }
        }

        private IEnumerable<Notification> GetDueSoonAlerts(int customerId)
        {
            var rentals = _rentalRepository.GetAll()
                .Where(r => r.CustomerId == customerId && !r.ReturnDate.HasValue)
                .ToList();

            // Build movie lookup once instead of calling GetAll() per rental (N+1 fix)
            var movieLookup = _movieRepository.GetAll().ToDictionary(m => m.Id);

            foreach (var rental in rentals)
            {
                // Use the actual DueDate — it already accounts for membership-tier
                // extended rental periods. Alert when due within 2 days.
                var daysUntilDue = (int)(rental.DueDate - DateTime.Today).TotalDays;
                if (daysUntilDue >= 0 && daysUntilDue <= 2)
                {
                    movieLookup.TryGetValue(rental.MovieId, out var movie);
                    yield return new Notification
                    {
                        Type = NotificationType.DueSoon,
                        Priority = NotificationPriority.High,
                        Title = "Rental Due Soon",
                        Message = $"\"{movie?.Name ?? "Unknown"}\" is due in {daysUntilDue} day(s). Return it to avoid late fees!",
                        Timestamp = DateTime.Now,
                        RelatedMovieId = rental.MovieId,
                        Icon = "⏰"
                    };
                }
            }
        }

        private IEnumerable<Notification> GetNewArrivalAlerts(int customerId)
        {
            // Find customer's preferred genres from rental history
            var customerRentals = _rentalRepository.GetAll()
                .Where(r => r.CustomerId == customerId)
                .ToList();

            var allMovies = _movieRepository.GetAll();
            var movieLookup = allMovies.ToDictionary(m => m.Id);

            var preferredGenres = customerRentals
                .Where(r => movieLookup.ContainsKey(r.MovieId) && movieLookup[r.MovieId].Genre.HasValue)
                .GroupBy(r => movieLookup[r.MovieId].Genre.Value)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => g.Key)
                .ToHashSet();

            if (preferredGenres.Count == 0)
                yield break;

            // Find new releases in preferred genres
            var newArrivals = allMovies
                .Where(m => m.IsNewRelease && m.Genre.HasValue && preferredGenres.Contains(m.Genre.Value))
                .Take(5);

            foreach (var movie in newArrivals)
            {
                yield return new Notification
                {
                    Type = NotificationType.NewArrival,
                    Priority = NotificationPriority.Normal,
                    Title = "New Arrival",
                    Message = $"\"{movie.Name}\" just arrived in {movie.Genre}! Based on your rental history, you might enjoy it.",
                    Timestamp = DateTime.Now,
                    RelatedMovieId = movie.Id,
                    Icon = "🎬"
                };
            }
        }

        private IEnumerable<Notification> GetWatchlistAlerts(int customerId)
        {
            var watchlistItems = _watchlistRepository.GetByCustomer(customerId);
            var rentals = _rentalRepository.GetAll();

            foreach (var item in watchlistItems.Where(w => w.Priority == WatchlistPriority.MustWatch))
            {
                // Check if the movie is currently available (not rented out by someone else)
                var activeRentals = rentals.Count(r => r.MovieId == item.MovieId && !r.ReturnDate.HasValue);
                if (activeRentals == 0)
                {
                    yield return new Notification
                    {
                        Type = NotificationType.WatchlistAvailable,
                        Priority = NotificationPriority.High,
                        Title = "Watchlist Movie Available",
                        Message = $"\"{item.MovieName}\" from your must-watch list is available to rent now!",
                        Timestamp = DateTime.Now,
                        RelatedMovieId = item.MovieId,
                        Icon = "🌟"
                    };
                }
            }
        }

        private IEnumerable<Notification> GetMembershipAlerts(Customer customer)
        {
            if (!customer.MemberSince.HasValue)
                yield break;

            var memberDays = (DateTime.Today - customer.MemberSince.Value).TotalDays;
            var years = (int)(memberDays / 365);

            // Anniversary within 7 days
            var dayOfYear = customer.MemberSince.Value.DayOfYear;
            var todayDayOfYear = DateTime.Today.DayOfYear;
            var daysUntilAnniversary = dayOfYear - todayDayOfYear;
            if (daysUntilAnniversary < 0) daysUntilAnniversary += 365;

            if (daysUntilAnniversary <= 7 && years > 0)
            {
                yield return new Notification
                {
                    Type = NotificationType.MembershipMilestone,
                    Priority = NotificationPriority.Normal,
                    Title = "Membership Anniversary",
                    Message = $"Your {years}-year membership anniversary is coming up! Thank you for being a loyal {customer.MembershipType} member.",
                    Timestamp = DateTime.Now,
                    Icon = "🎉"
                };
            }

            // Upgrade suggestion based on rental count
            if (customer.MembershipType < MembershipType.Platinum)
            {
                var rentalCount = _rentalRepository.GetAll().Count(r => r.CustomerId == customer.Id);
                var nextTier = (MembershipType)((int)customer.MembershipType + 1);
                int threshold = (int)customer.MembershipType * 10;

                if (rentalCount >= threshold)
                {
                    yield return new Notification
                    {
                        Type = NotificationType.UpgradeSuggestion,
                        Priority = NotificationPriority.Normal,
                        Title = "Upgrade Available",
                        Message = $"With {rentalCount} rentals, you qualify for {nextTier} membership! Enjoy better rates and perks.",
                        Timestamp = DateTime.Now,
                        Icon = "⬆️"
                    };
                }
            }
        }
    }

    public class Notification
    {
        public NotificationType Type { get; set; }
        public NotificationPriority Priority { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public int? RelatedMovieId { get; set; }
        public string CustomerName { get; set; }
        public string Icon { get; set; }
    }

    public enum NotificationType
    {
        OverdueRental,
        DueSoon,
        NewArrival,
        WatchlistAvailable,
        MembershipMilestone,
        UpgradeSuggestion
    }

    public enum NotificationPriority
    {
        Normal = 1,
        High = 2,
        Urgent = 3
    }

    public class NotificationResult
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public List<Notification> Notifications { get; set; } = new List<Notification>();
        public int UnreadCount { get; set; }
        public int TotalCount { get; set; }
    }

    public class NotificationSummary
    {
        public int TotalNotifications { get; set; }
        public int UrgentCount { get; set; }
        public Dictionary<NotificationType, int> ByType { get; set; } = new Dictionary<NotificationType, int>();
        public List<Notification> TopNotifications { get; set; } = new List<Notification>();
        public int CustomersWithAlerts { get; set; }
    }
}
