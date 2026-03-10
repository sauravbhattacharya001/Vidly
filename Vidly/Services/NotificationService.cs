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
        private readonly IDateTimeProvider _dateTime;

        public NotificationService()
            : this(new InMemoryRentalRepository(),
                   new InMemoryMovieRepository(),
                   new InMemoryCustomerRepository(),
                   new InMemoryWatchlistRepository(),
                   new SystemDateTimeProvider())
        {
        }

        public NotificationService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IWatchlistRepository watchlistRepository,
            IDateTimeProvider dateTimeProvider = null)
        {
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _watchlistRepository = watchlistRepository ?? throw new ArgumentNullException(nameof(watchlistRepository));
            _dateTime = dateTimeProvider ?? new SystemDateTimeProvider();
        }

        /// <summary>
        /// Gets all notifications for a specific customer.
        /// </summary>
        public NotificationResult GetNotifications(int customerId)
        {
            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                return new NotificationResult { CustomerId = customerId, CustomerName = "Unknown" };

            // Pre-fetch shared data once instead of each helper calling GetAll() independently.
            // Before this refactor, GetNotifications made 5 × GetAll() on rentals and
            // 3 × GetAll() on movies. Now it's 1 call each.
            var allRentals = _rentalRepository.GetAll();
            var allMovies = _movieRepository.GetAll();
            var movieLookup = allMovies.ToDictionary(m => m.Id);
            var customerRentals = allRentals.Where(r => r.CustomerId == customerId).ToList();

            var notifications = new List<Notification>();

            notifications.AddRange(GetOverdueAlerts(customerRentals, movieLookup));
            notifications.AddRange(GetDueSoonAlerts(customerRentals, movieLookup));
            notifications.AddRange(GetNewArrivalAlerts(customerRentals, allMovies, movieLookup));
            notifications.AddRange(GetWatchlistAlerts(customerId, allRentals));
            notifications.AddRange(GetMembershipAlerts(customer, customerRentals));

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
        /// Pre-fetches all data once and shares it across per-customer
        /// notification generation to avoid O(C × R) repository calls.
        /// </summary>
        public NotificationSummary GetSummary()
        {
            var customers = _customerRepository.GetAll();
            var allRentals = _rentalRepository.GetAll();
            var allMovies = _movieRepository.GetAll();
            var movieLookup = allMovies.ToDictionary(m => m.Id);
            var rentalsByCustomer = allRentals
                .GroupBy(r => r.CustomerId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var allNotifications = new List<Notification>();

            foreach (var customer in customers)
            {
                var customerRentals = rentalsByCustomer.TryGetValue(customer.Id, out var rlist)
                    ? rlist
                    : new List<Rental>();

                var notifications = new List<Notification>();
                notifications.AddRange(GetOverdueAlerts(customerRentals, movieLookup));
                notifications.AddRange(GetDueSoonAlerts(customerRentals, movieLookup));
                notifications.AddRange(GetNewArrivalAlerts(customerRentals, allMovies, movieLookup));
                notifications.AddRange(GetWatchlistAlerts(customer.Id, allRentals));
                notifications.AddRange(GetMembershipAlerts(customer, customerRentals));

                foreach (var n in notifications)
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

        private IEnumerable<Notification> GetOverdueAlerts(
            List<Rental> customerRentals, Dictionary<int, Movie> movieLookup)
        {
            var unreturned = customerRentals.Where(r => !r.ReturnDate.HasValue);

            foreach (var rental in unreturned)
            {
                // Use the actual DueDate from the rental — it already accounts for
                // membership-tier extended rental periods (Silver +1, Gold +2, Platinum +3).
                if (_dateTime.Today > rental.DueDate)
                {
                    var daysOverdue = (int)(_dateTime.Today - rental.DueDate).TotalDays;
                    movieLookup.TryGetValue(rental.MovieId, out var movie);
                    yield return new Notification
                    {
                        Type = NotificationType.OverdueRental,
                        Priority = NotificationPriority.Urgent,
                        Title = "Overdue Rental",
                        Message = $"\"{movie?.Name ?? "Unknown"}\" was due {daysOverdue} day(s) ago. Late fees may apply.",
                        Timestamp = _dateTime.Now,
                        RelatedMovieId = rental.MovieId,
                        Icon = "⚠️"
                    };
                }
            }
        }

        private IEnumerable<Notification> GetDueSoonAlerts(
            List<Rental> customerRentals, Dictionary<int, Movie> movieLookup)
        {
            var unreturned = customerRentals.Where(r => !r.ReturnDate.HasValue);

            foreach (var rental in unreturned)
            {
                // Use the actual DueDate — it already accounts for membership-tier
                // extended rental periods. Alert when due within 2 days.
                var daysUntilDue = (int)(rental.DueDate - _dateTime.Today).TotalDays;
                if (daysUntilDue >= 0 && daysUntilDue <= 2)
                {
                    movieLookup.TryGetValue(rental.MovieId, out var movie);
                    yield return new Notification
                    {
                        Type = NotificationType.DueSoon,
                        Priority = NotificationPriority.High,
                        Title = "Rental Due Soon",
                        Message = $"\"{movie?.Name ?? "Unknown"}\" is due in {daysUntilDue} day(s). Return it to avoid late fees!",
                        Timestamp = _dateTime.Now,
                        RelatedMovieId = rental.MovieId,
                        Icon = "⏰"
                    };
                }
            }
        }

        private IEnumerable<Notification> GetNewArrivalAlerts(
            List<Rental> customerRentals,
            IReadOnlyList<Movie> allMovies,
            Dictionary<int, Movie> movieLookup)
        {
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
                    Timestamp = _dateTime.Now,
                    RelatedMovieId = movie.Id,
                    Icon = "🎬"
                };
            }
        }

        private IEnumerable<Notification> GetWatchlistAlerts(
            int customerId, IReadOnlyList<Rental> allRentals)
        {
            var watchlistItems = _watchlistRepository.GetByCustomer(customerId);

            foreach (var item in watchlistItems.Where(w => w.Priority == WatchlistPriority.MustWatch))
            {
                // Check if the movie is currently available (not rented out by someone else)
                var activeRentals = allRentals.Count(r => r.MovieId == item.MovieId && !r.ReturnDate.HasValue);
                if (activeRentals == 0)
                {
                    yield return new Notification
                    {
                        Type = NotificationType.WatchlistAvailable,
                        Priority = NotificationPriority.High,
                        Title = "Watchlist Movie Available",
                        Message = $"\"{item.MovieName}\" from your must-watch list is available to rent now!",
                        Timestamp = _dateTime.Now,
                        RelatedMovieId = item.MovieId,
                        Icon = "🌟"
                    };
                }
            }
        }

        private IEnumerable<Notification> GetMembershipAlerts(
            Customer customer, List<Rental> customerRentals)
        {
            if (!customer.MemberSince.HasValue)
                yield break;

            var memberDays = (_dateTime.Today - customer.MemberSince.Value).TotalDays;
            var years = (int)(memberDays / 365);

            // Anniversary: compute by building the actual anniversary date this year,
            // avoiding DayOfYear which is unreliable across leap/non-leap year boundaries
            // (e.g., Mar 1 is day 60 in non-leap years but day 61 in leap years, and
            // Feb 29 anniversaries have no direct equivalent in non-leap years).
            var today = _dateTime.Today;
            var memberMonth = customer.MemberSince.Value.Month;
            var memberDay = customer.MemberSince.Value.Day;

            // Handle Feb 29 members in non-leap years: treat as Feb 28
            if (memberMonth == 2 && memberDay == 29 && !DateTime.IsLeapYear(today.Year))
                memberDay = 28;

            var anniversaryThisYear = new DateTime(today.Year, memberMonth, memberDay);
            var daysUntilAnniversary = (int)(anniversaryThisYear - today).TotalDays;

            // If anniversary already passed this year, check next year's date
            if (daysUntilAnniversary < 0)
            {
                var nextYear = today.Year + 1;
                var nextDay = (memberMonth == 2 && customer.MemberSince.Value.Day == 29 && !DateTime.IsLeapYear(nextYear)) ? 28 : customer.MemberSince.Value.Day;
                anniversaryThisYear = new DateTime(nextYear, memberMonth, nextDay);
                daysUntilAnniversary = (int)(anniversaryThisYear - today).TotalDays;
            }

            if (daysUntilAnniversary <= 7 && years > 0)
            {
                yield return new Notification
                {
                    Type = NotificationType.MembershipMilestone,
                    Priority = NotificationPriority.Normal,
                    Title = "Membership Anniversary",
                    Message = $"Your {years}-year membership anniversary is coming up! Thank you for being a loyal {customer.MembershipType} member.",
                    Timestamp = _dateTime.Now,
                    Icon = "🎉"
                };
            }

            // Upgrade suggestion based on rental count
            if (customer.MembershipType < MembershipType.Platinum)
            {
                var rentalCount = customerRentals.Count;
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
                        Timestamp = _dateTime.Now,
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
