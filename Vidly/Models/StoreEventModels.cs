using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Types of in-store events the rental store can host.
    /// </summary>
    public enum StoreEventType
    {
        /// <summary>Public or private screening of a film.</summary>
        MovieScreening = 1,
        /// <summary>Movie trivia competition with prizes.</summary>
        TriviaNight = 2,
        /// <summary>Celebration for a new release or catalog addition.</summary>
        ReleaseParty = 3,
        /// <summary>Live or virtual Q&amp;A session with a director.</summary>
        DirectorQA = 4,
        /// <summary>Extended viewing marathon of a genre or franchise.</summary>
        GenreMarathon = 5,
        /// <summary>Family-friendly daytime screening for children.</summary>
        KidsMatinee = 6,
        /// <summary>Event restricted to members of a certain tier or above.</summary>
        MemberExclusive = 7
    }

    /// <summary>
    /// Lifecycle status of a store event.
    /// </summary>
    public enum StoreEventStatus
    {
        /// <summary>Event is scheduled but has not started.</summary>
        Upcoming = 1,
        /// <summary>Event is currently taking place.</summary>
        InProgress = 2,
        /// <summary>Event has finished.</summary>
        Completed = 3,
        /// <summary>Event was cancelled before or during execution.</summary>
        Cancelled = 4,
        /// <summary>Event is at full capacity; no more RSVPs accepted.</summary>
        Soldout = 5
    }

    /// <summary>
    /// An in-store event with scheduling, capacity, pricing, and optional
    /// membership requirements. Can be linked to a featured movie.
    /// </summary>
    public class StoreEvent
    {
        public int Id { get; set; }

        /// <summary>Display title for the event (e.g. "Sci-Fi Friday Marathon").</summary>
        public string Title { get; set; }

        /// <summary>Detailed description shown to customers.</summary>
        public string Description { get; set; }

        public StoreEventType EventType { get; set; }

        /// <summary>Scheduled start time of the event.</summary>
        public DateTime StartTime { get; set; }

        /// <summary>Scheduled end time of the event.</summary>
        public DateTime EndTime { get; set; }

        /// <summary>Maximum number of attendees (including guests).</summary>
        public int Capacity { get; set; }

        /// <summary>Ticket price, or null for free events.</summary>
        public decimal? TicketPrice { get; set; }

        /// <summary>Optional movie ID featured at this event (screenings, release parties).</summary>
        public int? FeaturedMovieId { get; set; }

        /// <summary>Optional genre filter for genre-focused events.</summary>
        public Genre? Genre { get; set; }

        /// <summary>Minimum membership tier required to RSVP, or null for open events.</summary>
        public MembershipType? MinimumMembership { get; set; }

        public StoreEventStatus Status { get; set; } = StoreEventStatus.Upcoming;

        /// <summary>When the event was created in the system.</summary>
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// A customer's RSVP to a store event, tracking guest count and attendance.
    /// </summary>
    public class EventRsvp
    {
        public int Id { get; set; }

        /// <summary>ID of the event being RSVP'd to.</summary>
        public int EventId { get; set; }

        /// <summary>Customer making the reservation.</summary>
        public int CustomerId { get; set; }

        /// <summary>When the RSVP was submitted.</summary>
        public DateTime RsvpDate { get; set; } = DateTime.Now;

        /// <summary>Total attendees including the customer. Default: 1 (just the customer).</summary>
        public int GuestCount { get; set; } = 1;

        /// <summary>Whether the customer actually attended the event.</summary>
        public bool Attended { get; set; }

        /// <summary>Whether the customer cancelled their RSVP.</summary>
        public bool Cancelled { get; set; }
    }

    /// <summary>
    /// A personalized event suggestion with a relevance score and explanation,
    /// generated based on a customer's rental history and preferences.
    /// </summary>
    public class EventSuggestion
    {
        /// <summary>The suggested event.</summary>
        public StoreEvent Event { get; set; }

        /// <summary>Relevance score (0.0–1.0) based on customer preference matching.</summary>
        public double RelevanceScore { get; set; }

        /// <summary>Human-readable explanation (e.g. "You've rented 12 sci-fi movies").</summary>
        public string Reason { get; set; }
    }

    /// <summary>
    /// Aggregate statistics across all store events, including attendance rates,
    /// revenue, and per-type breakdowns.
    /// </summary>
    public class EventStats
    {
        /// <summary>Total number of events ever created.</summary>
        public int TotalEvents { get; set; }

        /// <summary>Number of events with Upcoming status.</summary>
        public int UpcomingEvents { get; set; }

        /// <summary>Total RSVP count across all events.</summary>
        public int TotalRsvps { get; set; }

        /// <summary>Total confirmed attendees across all events.</summary>
        public int TotalAttendees { get; set; }

        /// <summary>Average attendance rate (attendees / RSVPs) as a fraction.</summary>
        public double AverageAttendanceRate { get; set; }

        /// <summary>Total ticket revenue across all paid events.</summary>
        public decimal TotalRevenue { get; set; }

        /// <summary>Event type with the highest total RSVPs.</summary>
        public StoreEventType MostPopularType { get; set; }

        /// <summary>Title of the single most-attended event.</summary>
        public string MostPopularEvent { get; set; }

        /// <summary>Per-type statistics breakdown.</summary>
        public List<EventTypeBreakdown> TypeBreakdown { get; set; } = new List<EventTypeBreakdown>();
    }

    /// <summary>
    /// Statistics for a single event type within the overall event stats.
    /// </summary>
    public class EventTypeBreakdown
    {
        /// <summary>The event type these stats apply to.</summary>
        public StoreEventType EventType { get; set; }

        /// <summary>Number of events of this type.</summary>
        public int Count { get; set; }

        /// <summary>Total RSVPs across events of this type.</summary>
        public int TotalRsvps { get; set; }

        /// <summary>Average attendance per event of this type.</summary>
        public double AverageAttendance { get; set; }
    }
}
