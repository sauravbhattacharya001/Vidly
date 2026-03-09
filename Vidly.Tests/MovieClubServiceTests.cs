using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class MovieClubServiceTests
    {
        private MovieClubService _service;

        [TestInitialize]
        public void Setup()
        {
            _service = new MovieClubService();
        }

        // ── Club Creation ───────────────────────────────────────────

        [TestMethod]
        public void CreateClub_ValidInput_ReturnsClub()
        {
            var club = _service.CreateClub("Horror Fans", "We love horror", 1);
            Assert.AreEqual("Horror Fans", club.Name);
            Assert.AreEqual(ClubStatus.Active, club.Status);
            Assert.AreEqual(1, club.FounderId);
        }

        [TestMethod]
        public void CreateClub_AddsFounderAsMember()
        {
            var club = _service.CreateClub("Test Club", "desc", 42);
            var members = _service.GetMembers(club.Id);
            Assert.AreEqual(1, members.Count);
            Assert.AreEqual(42, members[0].CustomerId);
            Assert.AreEqual(ClubRole.Founder, members[0].Role);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateClub_EmptyName_Throws()
        {
            _service.CreateClub("", "desc", 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateClub_MaxMembersTooLow_Throws()
        {
            _service.CreateClub("Test", "desc", 1, maxMembers: 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateClub_DiscountTooHigh_Throws()
        {
            _service.CreateClub("Test", "desc", 1, groupDiscountPercent: 60);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CreateClub_DuplicateName_Throws()
        {
            _service.CreateClub("Unique Club", "desc", 1);
            _service.CreateClub("Unique Club", "different desc", 2);
        }

        [TestMethod]
        public void CreateClub_SameNameAfterDisband_Works()
        {
            var club = _service.CreateClub("Reusable", "v1", 1);
            _service.DisbandClub(club.Id, 1);
            var club2 = _service.CreateClub("Reusable", "v2", 2);
            Assert.AreNotEqual(club.Id, club2.Id);
        }

        // ── Club Lifecycle ──────────────────────────────────────────

        [TestMethod]
        public void PauseClub_ByFounder_SetsStatusPaused()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.PauseClub(club.Id, 1);
            Assert.AreEqual(ClubStatus.Paused, _service.GetClub(club.Id).Status);
        }

        [TestMethod]
        public void ResumeClub_AfterPause_SetsStatusActive()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.PauseClub(club.Id, 1);
            _service.ResumeClub(club.Id, 1);
            Assert.AreEqual(ClubStatus.Active, _service.GetClub(club.Id).Status);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void PauseClub_AlreadyPaused_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.PauseClub(club.Id, 1);
            _service.PauseClub(club.Id, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ResumeClub_NotPaused_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.ResumeClub(club.Id, 1);
        }

        [TestMethod]
        public void DisbandClub_DeactivatesAllMembers()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.JoinClub(club.Id, 2);
            _service.DisbandClub(club.Id, 1);
            Assert.AreEqual(ClubStatus.Disbanded, _service.GetClub(club.Id).Status);
            Assert.AreEqual(0, _service.GetMembers(club.Id).Count);
        }

        [TestMethod]
        [ExpectedException(typeof(UnauthorizedAccessException))]
        public void DisbandClub_ByNonFounder_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.JoinClub(club.Id, 2);
            _service.DisbandClub(club.Id, 2);
        }

        // ── Membership ──────────────────────────────────────────────

        [TestMethod]
        public void JoinClub_ValidMember_AddsMembership()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var m = _service.JoinClub(club.Id, 2);
            Assert.AreEqual(ClubRole.Member, m.Role);
            Assert.IsTrue(m.IsActive);
            Assert.AreEqual(2, _service.GetMembers(club.Id).Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void JoinClub_AlreadyMember_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.JoinClub(club.Id, 2);
            _service.JoinClub(club.Id, 2);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void JoinClub_AtCapacity_Throws()
        {
            var club = _service.CreateClub("Tiny", "desc", 1, maxMembers: 2);
            _service.JoinClub(club.Id, 2);
            _service.JoinClub(club.Id, 3); // 3rd would exceed max of 2
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void JoinClub_NonActiveClub_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.PauseClub(club.Id, 1);
            _service.JoinClub(club.Id, 2);
        }

        [TestMethod]
        public void LeaveClub_Member_DeactivatesMembership()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.JoinClub(club.Id, 2);
            _service.LeaveClub(club.Id, 2);
            Assert.AreEqual(1, _service.GetMembers(club.Id).Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void LeaveClub_Founder_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.LeaveClub(club.Id, 1);
        }

        [TestMethod]
        public void PromoteToModerator_ByFounder_Works()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.JoinClub(club.Id, 2);
            _service.PromoteToModerator(club.Id, 2, 1);
            var members = _service.GetMembers(club.Id);
            var m2 = members.First(m => m.CustomerId == 2);
            Assert.AreEqual(ClubRole.Moderator, m2.Role);
        }

        [TestMethod]
        [ExpectedException(typeof(UnauthorizedAccessException))]
        public void PromoteToModerator_ByNonFounder_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.JoinClub(club.Id, 2);
            _service.JoinClub(club.Id, 3);
            _service.PromoteToModerator(club.Id, 3, 2);
        }

        [TestMethod]
        public void GetCustomerClubs_ReturnsOnlyActiveClubs()
        {
            var c1 = _service.CreateClub("Club A", "a", 1);
            var c2 = _service.CreateClub("Club B", "b", 1);
            var clubs = _service.GetCustomerClubs(1);
            Assert.AreEqual(2, clubs.Count);
        }

        // ── Shared Watchlist ────────────────────────────────────────

        [TestMethod]
        public void AddToWatchlist_ValidMovie_ReturnsItem()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var item = _service.AddToWatchlist(club.Id, 100, 1);
            Assert.AreEqual(100, item.MovieId);
            Assert.IsFalse(item.IsWatched);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void AddToWatchlist_DuplicateMovie_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.AddToWatchlist(club.Id, 100, 1);
            _service.AddToWatchlist(club.Id, 100, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(UnauthorizedAccessException))]
        public void AddToWatchlist_NonMember_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.AddToWatchlist(club.Id, 100, 99);
        }

        [TestMethod]
        public void MarkAsWatched_SetsWatchedFlag()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var item = _service.AddToWatchlist(club.Id, 100, 1);
            _service.MarkAsWatched(club.Id, item.Id, 4.5);
            var list = _service.GetWatchlist(club.Id, includeWatched: true);
            Assert.IsTrue(list[0].IsWatched);
            Assert.AreEqual(4.5, list[0].AverageRating.Value, 0.01);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void MarkAsWatched_InvalidRating_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var item = _service.AddToWatchlist(club.Id, 100, 1);
            _service.MarkAsWatched(club.Id, item.Id, 6.0);
        }

        [TestMethod]
        public void GetWatchlist_ExcludesWatchedByDefault()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var item1 = _service.AddToWatchlist(club.Id, 100, 1);
            _service.AddToWatchlist(club.Id, 200, 1);
            _service.MarkAsWatched(club.Id, item1.Id);
            var unwatched = _service.GetWatchlist(club.Id);
            Assert.AreEqual(1, unwatched.Count);
            Assert.AreEqual(200, unwatched[0].MovieId);
        }

        // ── Polls ───────────────────────────────────────────────────

        [TestMethod]
        public void CreatePoll_ValidInput_ReturnsPoll()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var options = new List<(int, string)> { (1, "Movie A"), (2, "Movie B") };
            var poll = _service.CreatePoll(club.Id, "What next?", options, 1);
            Assert.AreEqual("What next?", poll.Title);
            Assert.AreEqual(PollStatus.Open, poll.Status);
            Assert.AreEqual(2, poll.Options.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreatePoll_TooFewOptions_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var options = new List<(int, string)> { (1, "Only one") };
            _service.CreatePoll(club.Id, "Bad poll", options, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreatePoll_TooManyOptions_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var options = Enumerable.Range(1, 11).Select(i => (i, $"Movie {i}")).ToList();
            _service.CreatePoll(club.Id, "Too many", options, 1);
        }

        [TestMethod]
        public void CastVote_ValidVote_RegistersVote()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.JoinClub(club.Id, 2);
            var options = new List<(int, string)> { (1, "A"), (2, "B") };
            var poll = _service.CreatePoll(club.Id, "Vote!", options, 1);
            _service.CastVote(poll.Id, 1, 1);
            _service.CastVote(poll.Id, 2, 2);
            Assert.AreEqual(1, poll.Options[0].VoterIds.Count);
            Assert.AreEqual(1, poll.Options[1].VoterIds.Count);
        }

        [TestMethod]
        public void CastVote_ChangesVote_RemovesPrevious()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var options = new List<(int, string)> { (1, "A"), (2, "B") };
            var poll = _service.CreatePoll(club.Id, "Vote!", options, 1);
            _service.CastVote(poll.Id, 1, 1);
            _service.CastVote(poll.Id, 2, 1); // change vote
            Assert.AreEqual(0, poll.Options[0].VoterIds.Count);
            Assert.AreEqual(1, poll.Options[1].VoterIds.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CastVote_ClosedPoll_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var options = new List<(int, string)> { (1, "A"), (2, "B") };
            var poll = _service.CreatePoll(club.Id, "Vote!", options, 1);
            _service.ClosePoll(poll.Id, 1);
            _service.CastVote(poll.Id, 1, 1);
        }

        [TestMethod]
        public void ClosePoll_ReturnsWinner()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.JoinClub(club.Id, 2);
            _service.JoinClub(club.Id, 3);
            var options = new List<(int, string)> { (1, "A"), (2, "B") };
            var poll = _service.CreatePoll(club.Id, "Vote!", options, 1);
            _service.CastVote(poll.Id, 2, 1);
            _service.CastVote(poll.Id, 2, 2);
            _service.CastVote(poll.Id, 1, 3);
            var winner = _service.ClosePoll(poll.Id, 1);
            Assert.AreEqual(2, winner.MovieId);
            Assert.AreEqual("B", winner.MovieTitle);
        }

        [TestMethod]
        [ExpectedException(typeof(UnauthorizedAccessException))]
        public void ClosePoll_ByRegularMember_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.JoinClub(club.Id, 2);
            var options = new List<(int, string)> { (1, "A"), (2, "B") };
            var poll = _service.CreatePoll(club.Id, "Vote!", options, 1);
            _service.ClosePoll(poll.Id, 2);
        }

        [TestMethod]
        public void GetPolls_FiltersByStatus()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var options = new List<(int, string)> { (1, "A"), (2, "B") };
            _service.CreatePoll(club.Id, "Open", options, 1);
            var poll2 = _service.CreatePoll(club.Id, "Closed", options, 1);
            _service.ClosePoll(poll2.Id, 1);
            Assert.AreEqual(1, _service.GetPolls(club.Id, PollStatus.Open).Count);
            Assert.AreEqual(1, _service.GetPolls(club.Id, PollStatus.Closed).Count);
        }

        // ── Meetings ────────────────────────────────────────────────

        [TestMethod]
        public void ScheduleMeeting_ValidInput_ReturnsMeeting()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var meeting = _service.ScheduleMeeting(club.Id, "Friday Night",
                DateTime.Now.AddDays(7), "Store Room A", movieId: 42, requesterId: 1);
            Assert.AreEqual("Friday Night", meeting.Title);
            Assert.AreEqual(42, meeting.MovieId);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ScheduleMeeting_PastDate_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.ScheduleMeeting(club.Id, "Past", DateTime.Now.AddDays(-1), "Room");
        }

        [TestMethod]
        public void RsvpToMeeting_AddsMemberToAttendees()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.JoinClub(club.Id, 2);
            var meeting = _service.ScheduleMeeting(club.Id, "Meetup",
                DateTime.Now.AddDays(3), "Room");
            _service.RsvpToMeeting(meeting.Id, 1);
            _service.RsvpToMeeting(meeting.Id, 2);
            Assert.AreEqual(2, meeting.AttendeeIds.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void RsvpToMeeting_DuplicateRsvp_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var meeting = _service.ScheduleMeeting(club.Id, "Meetup",
                DateTime.Now.AddDays(3), "Room");
            _service.RsvpToMeeting(meeting.Id, 1);
            _service.RsvpToMeeting(meeting.Id, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(UnauthorizedAccessException))]
        public void RsvpToMeeting_NonMember_Throws()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var meeting = _service.ScheduleMeeting(club.Id, "Meetup",
                DateTime.Now.AddDays(3), "Room");
            _service.RsvpToMeeting(meeting.Id, 99);
        }

        // ── Group Discount ──────────────────────────────────────────

        [TestMethod]
        public void CalculateGroupDiscount_LessThan3Members_ReturnsZero()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.JoinClub(club.Id, 2);
            Assert.AreEqual(0m, _service.CalculateGroupDiscount(club.Id, 10m));
        }

        [TestMethod]
        public void CalculateGroupDiscount_3Members_ReturnsBaseDiscount()
        {
            var club = _service.CreateClub("Test", "desc", 1, groupDiscountPercent: 10);
            _service.JoinClub(club.Id, 2);
            _service.JoinClub(club.Id, 3);
            var discount = _service.CalculateGroupDiscount(club.Id, 10m);
            Assert.AreEqual(1m, discount); // 10% of $10
        }

        [TestMethod]
        public void CalculateGroupDiscount_ScalesWithMembers()
        {
            var club = _service.CreateClub("Test", "desc", 1, maxMembers: 10, groupDiscountPercent: 10);
            for (int i = 2; i <= 6; i++) _service.JoinClub(club.Id, i);
            // 6 members: base 10% + 3% bonus (3 beyond 3) = 13%
            var discount = _service.CalculateGroupDiscount(club.Id, 100m);
            Assert.AreEqual(13m, discount);
        }

        [TestMethod]
        public void CalculateGroupDiscount_CapsAt50Percent()
        {
            var club = _service.CreateClub("Test", "desc", 1, maxMembers: 50, groupDiscountPercent: 45);
            for (int i = 2; i <= 20; i++) _service.JoinClub(club.Id, i);
            // 20 members: base 45% + 17% bonus → capped at 50%
            var discount = _service.CalculateGroupDiscount(club.Id, 100m);
            Assert.AreEqual(50m, discount);
        }

        // ── Analytics ───────────────────────────────────────────────

        [TestMethod]
        public void GetClubStats_ReturnsCorrectCounts()
        {
            var club = _service.CreateClub("Test", "desc", 1, genre: "Horror");
            _service.JoinClub(club.Id, 2);
            _service.JoinClub(club.Id, 3);
            var item = _service.AddToWatchlist(club.Id, 100, 1);
            _service.MarkAsWatched(club.Id, item.Id, 4.0);
            _service.AddToWatchlist(club.Id, 200, 1);

            var stats = _service.GetClubStats(club.Id);
            Assert.AreEqual(3, stats.MemberCount);
            Assert.AreEqual(1, stats.MoviesWatched);
            Assert.AreEqual(1, stats.WatchlistSize);
            Assert.AreEqual(4.0, stats.AverageMovieRating, 0.01);
            Assert.AreEqual("Horror", stats.MostActiveGenre);
        }

        [TestMethod]
        public void GetClubStats_NoGenre_ReturnsMixed()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var stats = _service.GetClubStats(club.Id);
            Assert.AreEqual("Mixed", stats.MostActiveGenre);
        }

        [TestMethod]
        public void GetClubLeaderboard_OrdersByMoviesWatched()
        {
            var c1 = _service.CreateClub("Club A", "a", 1);
            var c2 = _service.CreateClub("Club B", "b", 2);
            var item1 = _service.AddToWatchlist(c1.Id, 100, 1);
            var item2 = _service.AddToWatchlist(c1.Id, 200, 1);
            _service.MarkAsWatched(c1.Id, item1.Id);
            _service.MarkAsWatched(c1.Id, item2.Id);
            var item3 = _service.AddToWatchlist(c2.Id, 300, 2);
            _service.MarkAsWatched(c2.Id, item3.Id);

            var board = _service.GetClubLeaderboard();
            Assert.AreEqual("Club A", board[0].ClubName);
            Assert.AreEqual(2, board[0].MoviesWatched);
        }

        // ── List & Filter ───────────────────────────────────────────

        [TestMethod]
        public void ListClubs_ReturnsOnlyActive()
        {
            _service.CreateClub("Active", "a", 1);
            var paused = _service.CreateClub("Paused", "b", 2);
            _service.PauseClub(paused.Id, 2);
            Assert.AreEqual(1, _service.ListClubs().Count);
        }

        [TestMethod]
        public void ListClubs_FiltersByGenre()
        {
            _service.CreateClub("Horror Club", "h", 1, genre: "Horror");
            _service.CreateClub("Comedy Club", "c", 2, genre: "Comedy");
            var horror = _service.ListClubs("Horror");
            Assert.AreEqual(1, horror.Count);
            Assert.AreEqual("Horror Club", horror[0].Name);
        }

        [TestMethod]
        public void ListClubs_GenreFilterCaseInsensitive()
        {
            _service.CreateClub("Sci-Fi Fans", "s", 1, genre: "Sci-Fi");
            Assert.AreEqual(1, _service.ListClubs("sci-fi").Count);
        }

        // ── Edge Cases ──────────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void GetClub_NonExistent_ReturnsNull()
        {
            // GetClubStats uses GetClubOrThrow internally
            _service.GetClubStats(999);
        }

        [TestMethod]
        public void ModeratorCanPauseClub()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.JoinClub(club.Id, 2);
            _service.PromoteToModerator(club.Id, 2, 1);
            _service.PauseClub(club.Id, 2);
            Assert.AreEqual(ClubStatus.Paused, _service.GetClub(club.Id).Status);
        }

        [TestMethod]
        public void ModeratorCanClosePoll()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.JoinClub(club.Id, 2);
            _service.PromoteToModerator(club.Id, 2, 1);
            var options = new List<(int, string)> { (1, "A"), (2, "B") };
            var poll = _service.CreatePoll(club.Id, "Vote!", options, 1);
            var winner = _service.ClosePoll(poll.Id, 2);
            Assert.IsNotNull(winner);
        }

        [TestMethod]
        public void MarkAsWatched_WithoutRating_SetsNull()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            var item = _service.AddToWatchlist(club.Id, 100, 1);
            _service.MarkAsWatched(club.Id, item.Id);
            var list = _service.GetWatchlist(club.Id, includeWatched: true);
            Assert.IsTrue(list[0].IsWatched);
            Assert.IsNull(list[0].AverageRating);
        }

        [TestMethod]
        public void GetUpcomingMeetings_OnlyFuture()
        {
            var club = _service.CreateClub("Test", "desc", 1);
            _service.ScheduleMeeting(club.Id, "Future",
                DateTime.Now.AddDays(5), "Room A");
            var upcoming = _service.GetUpcomingMeetings(club.Id);
            Assert.AreEqual(1, upcoming.Count);
        }
    }
}
