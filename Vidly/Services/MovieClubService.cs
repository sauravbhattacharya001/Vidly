using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Manages movie clubs — groups of customers who watch, discuss, and vote
    /// on movies together. Features: club CRUD, membership management,
    /// shared watchlists, democratic polls, meeting scheduling, group discounts,
    /// and club analytics.
    /// </summary>
    public class MovieClubService
    {
        private readonly List<MovieClub> _clubs = new List<MovieClub>();
        private readonly List<ClubMembership> _memberships = new List<ClubMembership>();
        private readonly List<ClubWatchlistItem> _watchlist = new List<ClubWatchlistItem>();
        private readonly List<ClubPoll> _polls = new List<ClubPoll>();
        private readonly List<ClubMeeting> _meetings = new List<ClubMeeting>();
        private readonly IClock _clock;

        private int _nextClubId = 1;
        private int _nextMembershipId = 1;
        private int _nextWatchlistId = 1;
        private int _nextPollId = 1;
        private int _nextMeetingId = 1;

        public MovieClubService() : this(new SystemClock()) { }

        public MovieClubService(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        // ── Club CRUD ───────────────────────────────────────────────

        /// <summary>Creates a new movie club and adds the founder as first member.</summary>
        public MovieClub CreateClub(string name, string description, int founderId,
            string genre = null, int maxMembers = 20, decimal groupDiscountPercent = 10m)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Club name is required.", nameof(name));
            if (maxMembers < 2)
                throw new ArgumentException("Club must allow at least 2 members.", nameof(maxMembers));
            if (groupDiscountPercent < 0 || groupDiscountPercent > 50)
                throw new ArgumentException("Discount must be between 0% and 50%.", nameof(groupDiscountPercent));
            if (_clubs.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && c.Status == ClubStatus.Active))
                throw new InvalidOperationException($"An active club named '{name}' already exists.");

            var club = new MovieClub
            {
                Id = _nextClubId++,
                Name = name,
                Description = description,
                Genre = genre,
                MaxMembers = maxMembers,
                GroupDiscountPercent = groupDiscountPercent,
                Status = ClubStatus.Active,
                CreatedDate = _clock.Now,
                FounderId = founderId
            };
            _clubs.Add(club);

            // Auto-add founder
            _memberships.Add(new ClubMembership
            {
                Id = _nextMembershipId++,
                ClubId = club.Id,
                CustomerId = founderId,
                Role = ClubRole.Founder,
                JoinedDate = _clock.Now,
                IsActive = true
            });

            return club;
        }

        /// <summary>Gets a club by ID.</summary>
        public MovieClub GetClub(int clubId)
        {
            return _clubs.FirstOrDefault(c => c.Id == clubId);
        }

        /// <summary>Lists all active clubs, optionally filtered by genre.</summary>
        public IReadOnlyList<MovieClub> ListClubs(string genre = null)
        {
            var query = _clubs.Where(c => c.Status == ClubStatus.Active);
            if (!string.IsNullOrEmpty(genre))
                query = query.Where(c => c.Genre != null &&
                    c.Genre.Equals(genre, StringComparison.OrdinalIgnoreCase));
            return query.OrderBy(c => c.Name).ToList();
        }

        /// <summary>Pauses a club (only founder/moderator).</summary>
        public void PauseClub(int clubId, int requesterId)
        {
            var club = GetClubOrThrow(clubId);
            RequireModeratorOrFounder(clubId, requesterId);
            if (club.Status != ClubStatus.Active)
                throw new InvalidOperationException("Only active clubs can be paused.");
            club.Status = ClubStatus.Paused;
        }

        /// <summary>Resumes a paused club.</summary>
        public void ResumeClub(int clubId, int requesterId)
        {
            var club = GetClubOrThrow(clubId);
            RequireModeratorOrFounder(clubId, requesterId);
            if (club.Status != ClubStatus.Paused)
                throw new InvalidOperationException("Only paused clubs can be resumed.");
            club.Status = ClubStatus.Active;
        }

        /// <summary>Permanently disbands a club (founder only).</summary>
        public void DisbandClub(int clubId, int requesterId)
        {
            var club = GetClubOrThrow(clubId);
            if (club.FounderId != requesterId)
                throw new UnauthorizedAccessException("Only the founder can disband a club.");
            club.Status = ClubStatus.Disbanded;
            foreach (var m in _memberships.Where(m => m.ClubId == clubId))
                m.IsActive = false;
        }

        // ── Membership ──────────────────────────────────────────────

        /// <summary>Adds a customer to a club.</summary>
        public ClubMembership JoinClub(int clubId, int customerId)
        {
            var club = GetClubOrThrow(clubId);
            if (club.Status != ClubStatus.Active)
                throw new InvalidOperationException("Cannot join a non-active club.");

            var existing = _memberships.FirstOrDefault(m =>
                m.ClubId == clubId && m.CustomerId == customerId && m.IsActive);
            if (existing != null)
                throw new InvalidOperationException("Customer is already a member of this club.");

            var memberCount = _memberships.Count(m => m.ClubId == clubId && m.IsActive);
            if (memberCount >= club.MaxMembers)
                throw new InvalidOperationException("Club has reached maximum capacity.");

            var membership = new ClubMembership
            {
                Id = _nextMembershipId++,
                ClubId = clubId,
                CustomerId = customerId,
                Role = ClubRole.Member,
                JoinedDate = _clock.Now,
                IsActive = true
            };
            _memberships.Add(membership);
            return membership;
        }

        /// <summary>Removes a member from a club.</summary>
        public void LeaveClub(int clubId, int customerId)
        {
            var membership = _memberships.FirstOrDefault(m =>
                m.ClubId == clubId && m.CustomerId == customerId && m.IsActive);
            if (membership == null)
                throw new InvalidOperationException("Customer is not a member of this club.");
            if (membership.Role == ClubRole.Founder)
                throw new InvalidOperationException("Founder cannot leave — disband the club instead.");
            membership.IsActive = false;
        }

        /// <summary>Promotes a member to moderator (founder only).</summary>
        public void PromoteToModerator(int clubId, int customerId, int requesterId)
        {
            var club = GetClubOrThrow(clubId);
            if (club.FounderId != requesterId)
                throw new UnauthorizedAccessException("Only the founder can promote members.");

            var membership = _memberships.FirstOrDefault(m =>
                m.ClubId == clubId && m.CustomerId == customerId && m.IsActive);
            if (membership == null)
                throw new InvalidOperationException("Customer is not a member of this club.");
            if (membership.Role == ClubRole.Founder)
                throw new InvalidOperationException("Cannot change founder's role.");
            membership.Role = ClubRole.Moderator;
        }

        /// <summary>Gets all active members of a club.</summary>
        public IReadOnlyList<ClubMembership> GetMembers(int clubId)
        {
            return _memberships.Where(m => m.ClubId == clubId && m.IsActive)
                .OrderByDescending(m => m.Role).ThenBy(m => m.JoinedDate).ToList();
        }

        /// <summary>Gets clubs a customer belongs to.</summary>
        public IReadOnlyList<MovieClub> GetCustomerClubs(int customerId)
        {
            var clubIds = _memberships
                .Where(m => m.CustomerId == customerId && m.IsActive)
                .Select(m => m.ClubId).ToHashSet();
            return _clubs.Where(c => clubIds.Contains(c.Id)).ToList();
        }

        // ── Shared Watchlist ────────────────────────────────────────

        /// <summary>Adds a movie to the club's shared watchlist.</summary>
        public ClubWatchlistItem AddToWatchlist(int clubId, int movieId, int customerId,
            string movieTitle = null)
        {
            GetClubOrThrow(clubId);
            RequireMembership(clubId, customerId);

            if (_watchlist.Any(w => w.ClubId == clubId && w.MovieId == movieId && !w.IsWatched))
                throw new InvalidOperationException("Movie is already on the watchlist.");

            var item = new ClubWatchlistItem
            {
                Id = _nextWatchlistId++,
                ClubId = clubId,
                MovieId = movieId,
                AddedByCustomerId = customerId,
                AddedDate = _clock.Now,
                IsWatched = false
            };
            _watchlist.Add(item);
            return item;
        }

        /// <summary>Marks a watchlist movie as watched with an optional group rating.</summary>
        public void MarkAsWatched(int clubId, int watchlistItemId, double? rating = null)
        {
            var item = _watchlist.FirstOrDefault(w => w.ClubId == clubId && w.Id == watchlistItemId);
            if (item == null)
                throw new InvalidOperationException("Watchlist item not found.");
            item.IsWatched = true;
            item.WatchedDate = _clock.Now;
            if (rating.HasValue)
            {
                if (rating.Value < 1 || rating.Value > 5)
                    throw new ArgumentException("Rating must be between 1 and 5.");
                item.AverageRating = rating.Value;
            }
        }

        /// <summary>Gets the club's watchlist (unwatched or all).</summary>
        public IReadOnlyList<ClubWatchlistItem> GetWatchlist(int clubId, bool includeWatched = false)
        {
            var query = _watchlist.Where(w => w.ClubId == clubId);
            if (!includeWatched)
                query = query.Where(w => !w.IsWatched);
            return query.OrderByDescending(w => w.AddedDate).ToList();
        }

        // ── Polls (Democratic Voting) ───────────────────────────────

        /// <summary>Creates a poll for club members to vote on the next movie.</summary>
        public ClubPoll CreatePoll(int clubId, string title, List<(int movieId, string movieTitle)> options,
            int createdByCustomerId)
        {
            GetClubOrThrow(clubId);
            RequireMembership(clubId, createdByCustomerId);

            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Poll title is required.", nameof(title));
            if (options == null || options.Count < 2)
                throw new ArgumentException("Poll must have at least 2 options.", nameof(options));
            if (options.Count > 10)
                throw new ArgumentException("Poll cannot have more than 10 options.", nameof(options));

            var poll = new ClubPoll
            {
                Id = _nextPollId++,
                ClubId = clubId,
                Title = title,
                Status = PollStatus.Open,
                CreatedDate = _clock.Now,
                CreatedByCustomerId = createdByCustomerId,
                Options = options.Select((o, i) => new ClubPollOption
                {
                    OptionId = i + 1,
                    MovieId = o.movieId,
                    MovieTitle = o.movieTitle,
                    VoterIds = new List<int>()
                }).ToList()
            };
            _polls.Add(poll);
            return poll;
        }

        /// <summary>Casts a vote in a poll (one vote per member).</summary>
        public void CastVote(int pollId, int optionId, int customerId)
        {
            var poll = _polls.FirstOrDefault(p => p.Id == pollId);
            if (poll == null)
                throw new InvalidOperationException("Poll not found.");
            if (poll.Status != PollStatus.Open)
                throw new InvalidOperationException("Poll is not open for voting.");

            RequireMembership(poll.ClubId, customerId);

            // Remove any previous vote by this customer
            foreach (var opt in poll.Options)
                opt.VoterIds.Remove(customerId);

            var option = poll.Options.FirstOrDefault(o => o.OptionId == optionId);
            if (option == null)
                throw new ArgumentException("Invalid poll option.", nameof(optionId));
            option.VoterIds.Add(customerId);
        }

        /// <summary>Closes a poll and returns the winning option.</summary>
        public ClubPollOption ClosePoll(int pollId, int requesterId)
        {
            var poll = _polls.FirstOrDefault(p => p.Id == pollId);
            if (poll == null)
                throw new InvalidOperationException("Poll not found.");
            if (poll.Status != PollStatus.Open)
                throw new InvalidOperationException("Poll is already closed.");

            RequireModeratorOrFounder(poll.ClubId, requesterId);

            poll.Status = PollStatus.Closed;
            poll.ClosedDate = _clock.Now;

            return poll.Options.OrderByDescending(o => o.VoterIds.Count).FirstOrDefault();
        }

        /// <summary>Gets polls for a club.</summary>
        public IReadOnlyList<ClubPoll> GetPolls(int clubId, PollStatus? status = null)
        {
            var query = _polls.Where(p => p.ClubId == clubId);
            if (status.HasValue)
                query = query.Where(p => p.Status == status.Value);
            return query.OrderByDescending(p => p.CreatedDate).ToList();
        }

        // ── Meetings ────────────────────────────────────────────────

        /// <summary>Schedules a club meeting/watch session.</summary>
        public ClubMeeting ScheduleMeeting(int clubId, string title, DateTime scheduledDate,
            string location, int? movieId = null, int requesterId = 0)
        {
            GetClubOrThrow(clubId);
            if (requesterId > 0)
                RequireMembership(clubId, requesterId);

            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Meeting title is required.", nameof(title));
            if (scheduledDate <= _clock.Now)
                throw new ArgumentException("Meeting must be scheduled in the future.", nameof(scheduledDate));

            var meeting = new ClubMeeting
            {
                Id = _nextMeetingId++,
                ClubId = clubId,
                MovieId = movieId,
                Title = title,
                ScheduledDate = scheduledDate,
                Location = location,
                AttendeeIds = new List<int>(),
                Notes = null
            };
            _meetings.Add(meeting);
            return meeting;
        }

        /// <summary>RSVPs a member to a meeting.</summary>
        public void RsvpToMeeting(int meetingId, int customerId)
        {
            var meeting = _meetings.FirstOrDefault(m => m.Id == meetingId);
            if (meeting == null)
                throw new InvalidOperationException("Meeting not found.");
            RequireMembership(meeting.ClubId, customerId);

            if (meeting.AttendeeIds.Contains(customerId))
                throw new InvalidOperationException("Already RSVPed to this meeting.");
            meeting.AttendeeIds.Add(customerId);
        }

        /// <summary>Gets upcoming meetings for a club.</summary>
        public IReadOnlyList<ClubMeeting> GetUpcomingMeetings(int clubId)
        {
            return _meetings
                .Where(m => m.ClubId == clubId && m.ScheduledDate > _clock.Now)
                .OrderBy(m => m.ScheduledDate).ToList();
        }

        // ── Group Discount ──────────────────────────────────────────

        /// <summary>Calculates the group discount for a rental price.</summary>
        public decimal CalculateGroupDiscount(int clubId, decimal originalPrice)
        {
            var club = GetClubOrThrow(clubId);
            var memberCount = _memberships.Count(m => m.ClubId == clubId && m.IsActive);
            if (memberCount < 3)
                return 0m; // Need at least 3 members for group discount

            // Scale discount by member count: base discount + 1% per member beyond 3 (capped at club max)
            var bonusPercent = Math.Min((memberCount - 3) * 1m, 10m);
            var effectivePercent = Math.Min(club.GroupDiscountPercent + bonusPercent, 50m);
            return Math.Round(originalPrice * effectivePercent / 100m, 2);
        }

        // ── Analytics ───────────────────────────────────────────────

        /// <summary>Computes comprehensive club statistics.</summary>
        public ClubStats GetClubStats(int clubId)
        {
            var club = GetClubOrThrow(clubId);
            var members = _memberships.Where(m => m.ClubId == clubId && m.IsActive).ToList();
            var watched = _watchlist.Where(w => w.ClubId == clubId && w.IsWatched).ToList();
            var unwatched = _watchlist.Where(w => w.ClubId == clubId && !w.IsWatched).ToList();
            var closedPolls = _polls.Count(p => p.ClubId == clubId && p.Status == PollStatus.Closed);
            var pastMeetings = _meetings.Count(m => m.ClubId == clubId && m.ScheduledDate <= _clock.Now);

            var avgRating = watched.Where(w => w.AverageRating.HasValue)
                .Select(w => w.AverageRating.Value).DefaultIfEmpty(0).Average();

            // Calculate total savings from group discounts
            // Estimate: each watched movie saved the group discount on an average $4 rental
            var totalSavings = watched.Count * 4m * club.GroupDiscountPercent / 100m;

            return new ClubStats
            {
                ClubId = clubId,
                ClubName = club.Name,
                MemberCount = members.Count,
                MoviesWatched = watched.Count,
                WatchlistSize = unwatched.Count,
                MeetingsHeld = pastMeetings,
                PollsCompleted = closedPolls,
                AverageMovieRating = Math.Round(avgRating, 2),
                MostActiveGenre = club.Genre ?? "Mixed",
                TotalSavings = totalSavings
            };
        }

        /// <summary>Returns a leaderboard of most active clubs by movies watched.</summary>
        public IReadOnlyList<ClubStats> GetClubLeaderboard(int top = 10)
        {
            return _clubs.Where(c => c.Status == ClubStatus.Active)
                .Select(c => GetClubStats(c.Id))
                .OrderByDescending(s => s.MoviesWatched)
                .ThenByDescending(s => s.MemberCount)
                .Take(top).ToList();
        }

        // ── Helpers ─────────────────────────────────────────────────

        private MovieClub GetClubOrThrow(int clubId)
        {
            var club = _clubs.FirstOrDefault(c => c.Id == clubId);
            if (club == null)
                throw new InvalidOperationException($"Club {clubId} not found.");
            return club;
        }

        private void RequireMembership(int clubId, int customerId)
        {
            var member = _memberships.FirstOrDefault(m =>
                m.ClubId == clubId && m.CustomerId == customerId && m.IsActive);
            if (member == null)
                throw new UnauthorizedAccessException("Customer is not a member of this club.");
        }

        private void RequireModeratorOrFounder(int clubId, int requesterId)
        {
            var member = _memberships.FirstOrDefault(m =>
                m.ClubId == clubId && m.CustomerId == requesterId && m.IsActive);
            if (member == null || (member.Role != ClubRole.Founder && member.Role != ClubRole.Moderator))
                throw new UnauthorizedAccessException("Only the founder or a moderator can perform this action.");
        }
    }
}
