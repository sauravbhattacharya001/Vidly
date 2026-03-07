using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    public enum StoreEventType
    {
        MovieScreening = 1,
        TriviaNight = 2,
        ReleaseParty = 3,
        DirectorQA = 4,
        GenreMarathon = 5,
        KidsMatinee = 6,
        MemberExclusive = 7
    }

    public enum StoreEventStatus
    {
        Upcoming = 1,
        InProgress = 2,
        Completed = 3,
        Cancelled = 4,
        Soldout = 5
    }

    public class StoreEvent
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public StoreEventType EventType { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int Capacity { get; set; }
        public decimal? TicketPrice { get; set; }
        public int? FeaturedMovieId { get; set; }
        public Genre? Genre { get; set; }
        public MembershipType? MinimumMembership { get; set; }
        public StoreEventStatus Status { get; set; } = StoreEventStatus.Upcoming;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }

    public class EventRsvp
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public int CustomerId { get; set; }
        public DateTime RsvpDate { get; set; } = DateTime.Now;
        public int GuestCount { get; set; } = 1;
        public bool Attended { get; set; }
        public bool Cancelled { get; set; }
    }

    public class EventSuggestion
    {
        public StoreEvent Event { get; set; }
        public double RelevanceScore { get; set; }
        public string Reason { get; set; }
    }

    public class EventStats
    {
        public int TotalEvents { get; set; }
        public int UpcomingEvents { get; set; }
        public int TotalRsvps { get; set; }
        public int TotalAttendees { get; set; }
        public double AverageAttendanceRate { get; set; }
        public decimal TotalRevenue { get; set; }
        public StoreEventType MostPopularType { get; set; }
        public string MostPopularEvent { get; set; }
        public List<EventTypeBreakdown> TypeBreakdown { get; set; } = new List<EventTypeBreakdown>();
    }

    public class EventTypeBreakdown
    {
        public StoreEventType EventType { get; set; }
        public int Count { get; set; }
        public int TotalRsvps { get; set; }
        public double AverageAttendance { get; set; }
    }
}
