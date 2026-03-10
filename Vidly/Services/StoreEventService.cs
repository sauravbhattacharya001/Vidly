using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Manages in-store events (screenings, trivia nights, release parties),
    /// RSVPs with capacity tracking, attendance logging, and personalized
    /// event recommendations based on customer rental history.
    /// </summary>
    public class StoreEventService
    {
        private readonly List<StoreEvent> _events = new List<StoreEvent>();
        private readonly List<EventRsvp> _rsvps = new List<EventRsvp>();
        private int _nextEventId = 1;
        private int _nextRsvpId = 1;

        // ── Event CRUD ────────────────────────────────────────────

        public StoreEvent CreateEvent(StoreEvent ev)
        {
            if (ev == null) throw new ArgumentNullException(nameof(ev));
            if (string.IsNullOrWhiteSpace(ev.Title))
                throw new ArgumentException("Event title is required.");
            if (ev.EndTime <= ev.StartTime)
                throw new ArgumentException("End time must be after start time.");
            if (ev.Capacity <= 0)
                throw new ArgumentException("Capacity must be positive.");
            if (ev.TicketPrice.HasValue && ev.TicketPrice.Value < 0)
                throw new ArgumentException("Ticket price cannot be negative.");

            ev.Id = _nextEventId++;
            ev.Status = StoreEventStatus.Upcoming;
            ev.CreatedDate = DateTime.Now;
            _events.Add(ev);
            return ev;
        }

        public IReadOnlyList<StoreEvent> GetEvents(StoreEventStatus? status = null)
        {
            if (status.HasValue)
                return _events.Where(e => e.Status == status.Value).ToList();
            return _events.ToList();
        }

        public StoreEvent GetById(int eventId)
        {
            return _events.FirstOrDefault(e => e.Id == eventId);
        }

        public IReadOnlyList<StoreEvent> GetUpcoming()
        {
            return _events
                .Where(e => e.Status == StoreEventStatus.Upcoming && e.StartTime > DateTime.Now)
                .OrderBy(e => e.StartTime)
                .ToList();
        }

        public void CancelEvent(int eventId)
        {
            var ev = _events.FirstOrDefault(e => e.Id == eventId);
            if (ev == null) throw new ArgumentException("Event not found.");
            if (ev.Status == StoreEventStatus.Completed)
                throw new InvalidOperationException("Cannot cancel a completed event.");
            ev.Status = StoreEventStatus.Cancelled;

            foreach (var rsvp in _rsvps.Where(r => r.EventId == eventId && !r.Cancelled))
                rsvp.Cancelled = true;
        }

        public void CompleteEvent(int eventId)
        {
            var ev = _events.FirstOrDefault(e => e.Id == eventId);
            if (ev == null) throw new ArgumentException("Event not found.");
            ev.Status = StoreEventStatus.Completed;
        }

        // ── RSVP Management ──────────────────────────────────────

        public EventRsvp Rsvp(int eventId, int customerId, int guestCount = 1,
                              MembershipType customerMembership = MembershipType.Basic)
        {
            var ev = _events.FirstOrDefault(e => e.Id == eventId);
            if (ev == null) throw new ArgumentException("Event not found.");
            if (ev.Status == StoreEventStatus.Cancelled)
                throw new InvalidOperationException("Cannot RSVP to a cancelled event.");
            if (ev.Status == StoreEventStatus.Completed)
                throw new InvalidOperationException("Cannot RSVP to a completed event.");
            if (ev.Status == StoreEventStatus.Soldout)
                throw new InvalidOperationException("Event is sold out.");
            if (guestCount <= 0)
                throw new ArgumentException("Guest count must be positive.");

            if (ev.MinimumMembership.HasValue && customerMembership < ev.MinimumMembership.Value)
                throw new InvalidOperationException(
                    $"This event requires {ev.MinimumMembership.Value} membership or higher.");

            var existing = _rsvps.FirstOrDefault(
                r => r.EventId == eventId && r.CustomerId == customerId && !r.Cancelled);
            if (existing != null)
                throw new InvalidOperationException("Customer already has an active RSVP for this event.");

            int currentCount = GetActiveRsvpCount(eventId);
            if (currentCount + guestCount > ev.Capacity)
                throw new InvalidOperationException(
                    $"Not enough capacity. Available: {ev.Capacity - currentCount}, requested: {guestCount}.");

            var rsvp = new EventRsvp
            {
                Id = _nextRsvpId++,
                EventId = eventId,
                CustomerId = customerId,
                GuestCount = guestCount,
                RsvpDate = DateTime.Now
            };
            _rsvps.Add(rsvp);

            if (currentCount + guestCount >= ev.Capacity)
                ev.Status = StoreEventStatus.Soldout;

            return rsvp;
        }

        public void CancelRsvp(int eventId, int customerId)
        {
            var rsvp = _rsvps.FirstOrDefault(
                r => r.EventId == eventId && r.CustomerId == customerId && !r.Cancelled);
            if (rsvp == null)
                throw new ArgumentException("No active RSVP found for this customer.");

            rsvp.Cancelled = true;

            var ev = _events.FirstOrDefault(e => e.Id == eventId);
            if (ev != null && ev.Status == StoreEventStatus.Soldout)
                ev.Status = StoreEventStatus.Upcoming;
        }

        public void RecordAttendance(int eventId, int customerId)
        {
            var rsvp = _rsvps.FirstOrDefault(
                r => r.EventId == eventId && r.CustomerId == customerId && !r.Cancelled);
            if (rsvp == null)
                throw new ArgumentException("No active RSVP found for this customer.");
            rsvp.Attended = true;
        }

        public IReadOnlyList<EventRsvp> GetEventRsvps(int eventId)
        {
            return _rsvps.Where(r => r.EventId == eventId && !r.Cancelled).ToList();
        }

        public IReadOnlyList<StoreEvent> GetCustomerEvents(int customerId)
        {
            var eventIds = _rsvps
                .Where(r => r.CustomerId == customerId && !r.Cancelled)
                .Select(r => r.EventId)
                .ToHashSet();
            return _events.Where(e => eventIds.Contains(e.Id)).ToList();
        }

        public int GetRemainingCapacity(int eventId)
        {
            var ev = _events.FirstOrDefault(e => e.Id == eventId);
            if (ev == null) throw new ArgumentException("Event not found.");
            return ev.Capacity - GetActiveRsvpCount(eventId);
        }

        // ── Recommendations ──────────────────────────────────────

        /// <summary>
        /// Suggests upcoming events for a customer based on their rental
        /// history and genre preferences. Uses rental MovieIds joined
        /// against the movies list to determine genre affinity.
        /// </summary>
        public IReadOnlyList<EventSuggestion> GetRecommendations(
            int customerId, List<Rental> rentals, List<Movie> movies, int limit = 5)
        {
            if (rentals == null) throw new ArgumentNullException(nameof(rentals));
            if (movies == null) throw new ArgumentNullException(nameof(movies));

            var movieLookup = new Dictionary<int, Movie>();
            foreach (var m in movies)
                if (!movieLookup.ContainsKey(m.Id)) movieLookup[m.Id] = m;

            var customerRentals = rentals.Where(r => r.CustomerId == customerId).ToList();

            var genreCounts = new Dictionary<Genre, int>();
            var rentedMovieIds = new HashSet<int>();
            foreach (var r in customerRentals)
            {
                rentedMovieIds.Add(r.MovieId);
                Movie m;
                if (movieLookup.TryGetValue(r.MovieId, out m) && m.Genre.HasValue)
                {
                    genreCounts.TryGetValue(m.Genre.Value, out var _c1);
                    genreCounts[m.Genre.Value] = _c1 + 1;
                }
            }

            var upcoming = _events
                .Where(e => e.Status == StoreEventStatus.Upcoming && e.StartTime > DateTime.Now)
                .ToList();

            var suggestions = new List<EventSuggestion>();
            foreach (var ev in upcoming)
            {
                double score = 0;
                var reasons = new List<string>();

                if (ev.Genre.HasValue && genreCounts.ContainsKey(ev.Genre.Value))
                {
                    int count = genreCounts[ev.Genre.Value];
                    score += Math.Min(count * 10, 50);
                    reasons.Add($"You've rented {count} {ev.Genre.Value} movies");
                }

                if (ev.FeaturedMovieId.HasValue && rentedMovieIds.Contains(ev.FeaturedMovieId.Value))
                {
                    score += 30;
                    reasons.Add("You've rented the featured movie");
                }

                if (!ev.TicketPrice.HasValue || ev.TicketPrice.Value == 0)
                {
                    score += 10;
                    reasons.Add("Free event");
                }

                int rsvpCount = GetActiveRsvpCount(ev.Id);
                if (rsvpCount > 0)
                {
                    score += Math.Min(rsvpCount * 2, 20);
                    reasons.Add($"{rsvpCount} people already signed up");
                }

                double fillRate = (double)rsvpCount / ev.Capacity;
                if (fillRate > 0.8)
                {
                    score += 15;
                    reasons.Add("Almost sold out!");
                }

                if (score > 0 || reasons.Count > 0)
                {
                    if (reasons.Count == 0) reasons.Add("Upcoming event");
                    suggestions.Add(new EventSuggestion
                    {
                        Event = ev,
                        RelevanceScore = Math.Round(score, 1),
                        Reason = string.Join("; ", reasons)
                    });
                }
            }

            return suggestions
                .OrderByDescending(s => s.RelevanceScore)
                .Take(limit)
                .ToList();
        }

        // ── Analytics ────────────────────────────────────────────

        public EventStats GetStats()
        {
            var stats = new EventStats
            {
                TotalEvents = _events.Count,
                UpcomingEvents = _events.Count(e => e.Status == StoreEventStatus.Upcoming),
                TotalRsvps = _rsvps.Count(r => !r.Cancelled),
                TotalAttendees = _rsvps.Where(r => r.Attended && !r.Cancelled).Sum(r => r.GuestCount),
                TotalRevenue = CalculateTotalRevenue()
            };

            var completedEvents = _events.Where(e => e.Status == StoreEventStatus.Completed).ToList();
            if (completedEvents.Count > 0)
            {
                double totalRate = 0;
                foreach (var ev in completedEvents)
                {
                    int rsvps = _rsvps.Count(r => r.EventId == ev.Id && !r.Cancelled);
                    int attended = _rsvps.Count(r => r.EventId == ev.Id && r.Attended && !r.Cancelled);
                    if (rsvps > 0) totalRate += (double)attended / rsvps;
                }
                stats.AverageAttendanceRate = Math.Round(totalRate / completedEvents.Count * 100, 1);
            }

            var typeGroups = _rsvps
                .Where(r => !r.Cancelled)
                .GroupBy(r => _events.FirstOrDefault(e => e.Id == r.EventId)?.EventType)
                .Where(g => g.Key.HasValue)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            if (typeGroups != null)
                stats.MostPopularType = typeGroups.Key.Value;

            var popularEvent = _rsvps
                .Where(r => !r.Cancelled)
                .GroupBy(r => r.EventId)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            if (popularEvent != null)
            {
                var ev = _events.FirstOrDefault(e => e.Id == popularEvent.Key);
                stats.MostPopularEvent = ev?.Title ?? "Unknown";
            }

            foreach (StoreEventType type in Enum.GetValues(typeof(StoreEventType)))
            {
                var typeEvents = _events.Where(e => e.EventType == type).ToList();
                if (typeEvents.Count == 0) continue;

                var typeRsvps = _rsvps.Where(r =>
                    !r.Cancelled && typeEvents.Any(e => e.Id == r.EventId)).ToList();

                var completedOfType = typeEvents
                    .Where(e => e.Status == StoreEventStatus.Completed).ToList();
                double avgAttendance = 0;
                if (completedOfType.Count > 0)
                {
                    int totalAttended = _rsvps
                        .Count(r => r.Attended && !r.Cancelled &&
                               completedOfType.Any(e => e.Id == r.EventId));
                    avgAttendance = Math.Round((double)totalAttended / completedOfType.Count, 1);
                }

                stats.TypeBreakdown.Add(new EventTypeBreakdown
                {
                    EventType = type,
                    Count = typeEvents.Count,
                    TotalRsvps = typeRsvps.Count,
                    AverageAttendance = avgAttendance
                });
            }

            return stats;
        }

        public IReadOnlyList<StoreEvent> GetEventsInRange(int days)
        {
            if (days <= 0) throw new ArgumentException("Days must be positive.");
            var cutoff = DateTime.Now.AddDays(days);
            return _events
                .Where(e => e.Status == StoreEventStatus.Upcoming &&
                            e.StartTime > DateTime.Now && e.StartTime <= cutoff)
                .OrderBy(e => e.StartTime)
                .ToList();
        }

        // ── Helpers ──────────────────────────────────────────────

        private int GetActiveRsvpCount(int eventId)
        {
            return _rsvps
                .Where(r => r.EventId == eventId && !r.Cancelled)
                .Sum(r => r.GuestCount);
        }

        private decimal CalculateTotalRevenue()
        {
            decimal total = 0;
            foreach (var rsvp in _rsvps.Where(r => !r.Cancelled))
            {
                var ev = _events.FirstOrDefault(e => e.Id == rsvp.EventId);
                if (ev?.TicketPrice != null)
                    total += ev.TicketPrice.Value * rsvp.GuestCount;
            }
            return total;
        }
    }
}
