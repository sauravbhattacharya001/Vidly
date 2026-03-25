using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    public class InMemoryMovieClubRepository : IMovieClubRepository
    {
        private static readonly List<MovieClub> _clubs = new List<MovieClub>();
        private static readonly List<ClubMembership> _memberships = new List<ClubMembership>();
        private static readonly List<ClubWatchlistItem> _watchlist = new List<ClubWatchlistItem>();
        private static readonly List<ClubPoll> _polls = new List<ClubPoll>();
        private static int _nextClubId = 1;
        private static int _nextMembershipId = 1;
        private static int _nextWatchlistId = 1;
        private static int _nextPollId = 1;
        private static bool _seeded;

        public InMemoryMovieClubRepository()
        {
            if (!_seeded)
            {
                _seeded = true;
                SeedData();
            }
        }

        private void SeedData()
        {
            var club1 = new MovieClub
            {
                Id = _nextClubId++,
                Name = "Sci-Fi Enthusiasts",
                Description = "For lovers of science fiction cinema — from classic to modern.",
                Genre = "Sci-Fi",
                MaxMembers = 20,
                GroupDiscountPercent = 10,
                Status = ClubStatus.Active,
                CreatedDate = DateTime.Now.AddMonths(-3),
                FounderId = 1
            };
            var club2 = new MovieClub
            {
                Id = _nextClubId++,
                Name = "Horror Nights",
                Description = "Weekly horror movie watch sessions. Not for the faint of heart!",
                Genre = "Horror",
                MaxMembers = 15,
                GroupDiscountPercent = 15,
                Status = ClubStatus.Active,
                CreatedDate = DateTime.Now.AddMonths(-1),
                FounderId = 2
            };
            var club3 = new MovieClub
            {
                Id = _nextClubId++,
                Name = "Classic Film Society",
                Description = "Appreciating the golden age of cinema together.",
                Genre = "Drama",
                MaxMembers = 25,
                GroupDiscountPercent = 5,
                Status = ClubStatus.Active,
                CreatedDate = DateTime.Now.AddMonths(-6),
                FounderId = 3
            };
            _clubs.AddRange(new[] { club1, club2, club3 });

            // Memberships
            var memberships = new[]
            {
                new ClubMembership { Id = _nextMembershipId++, ClubId = 1, CustomerId = 1, Role = ClubRole.Founder, JoinedDate = DateTime.Now.AddMonths(-3), IsActive = true },
                new ClubMembership { Id = _nextMembershipId++, ClubId = 1, CustomerId = 2, Role = ClubRole.Member, JoinedDate = DateTime.Now.AddMonths(-2), IsActive = true },
                new ClubMembership { Id = _nextMembershipId++, ClubId = 1, CustomerId = 3, Role = ClubRole.Member, JoinedDate = DateTime.Now.AddDays(-14), IsActive = true },
                new ClubMembership { Id = _nextMembershipId++, ClubId = 2, CustomerId = 2, Role = ClubRole.Founder, JoinedDate = DateTime.Now.AddMonths(-1), IsActive = true },
                new ClubMembership { Id = _nextMembershipId++, ClubId = 2, CustomerId = 1, Role = ClubRole.Member, JoinedDate = DateTime.Now.AddDays(-20), IsActive = true },
                new ClubMembership { Id = _nextMembershipId++, ClubId = 3, CustomerId = 3, Role = ClubRole.Founder, JoinedDate = DateTime.Now.AddMonths(-6), IsActive = true },
            };
            _memberships.AddRange(memberships);

            // Watchlist items
            var watchItems = new[]
            {
                new ClubWatchlistItem { Id = _nextWatchlistId++, ClubId = 1, MovieId = 3, AddedByCustomerId = 1, AddedDate = DateTime.Now.AddDays(-10), IsWatched = true, WatchedDate = DateTime.Now.AddDays(-3), AverageRating = 4.5 },
                new ClubWatchlistItem { Id = _nextWatchlistId++, ClubId = 1, MovieId = 5, AddedByCustomerId = 2, AddedDate = DateTime.Now.AddDays(-5), IsWatched = false },
                new ClubWatchlistItem { Id = _nextWatchlistId++, ClubId = 2, MovieId = 7, AddedByCustomerId = 2, AddedDate = DateTime.Now.AddDays(-7), IsWatched = false },
            };
            _watchlist.AddRange(watchItems);

            // A poll
            var poll = new ClubPoll
            {
                Id = _nextPollId++,
                ClubId = 1,
                Title = "Next Movie Night Pick",
                Options = new List<ClubPollOption>
                {
                    new ClubPollOption { OptionId = 1, MovieId = 5, MovieTitle = "Inception", VoterIds = new List<int> { 1 } },
                    new ClubPollOption { OptionId = 2, MovieId = 7, MovieTitle = "Interstellar", VoterIds = new List<int> { 2, 3 } },
                },
                Status = PollStatus.Open,
                CreatedDate = DateTime.Now.AddDays(-2),
                CreatedByCustomerId = 1
            };
            _polls.Add(poll);
        }

        public IEnumerable<MovieClub> GetAll() => _clubs.ToList();
        public MovieClub GetById(int id) => _clubs.FirstOrDefault(c => c.Id == id);
        public IEnumerable<MovieClub> GetByStatus(ClubStatus status) => _clubs.Where(c => c.Status == status).ToList();

        public void Add(MovieClub club)
        {
            club.Id = _nextClubId++;
            club.CreatedDate = DateTime.Now;
            club.Status = ClubStatus.Active;
            _clubs.Add(club);
        }

        public void Update(MovieClub club)
        {
            var idx = _clubs.FindIndex(c => c.Id == club.Id);
            if (idx >= 0) _clubs[idx] = club;
        }

        public IEnumerable<ClubMembership> GetMembers(int clubId) =>
            _memberships.Where(m => m.ClubId == clubId && m.IsActive).ToList();

        public ClubMembership GetMembership(int clubId, int customerId) =>
            _memberships.FirstOrDefault(m => m.ClubId == clubId && m.CustomerId == customerId && m.IsActive);

        public void AddMember(ClubMembership membership)
        {
            membership.Id = _nextMembershipId++;
            membership.JoinedDate = DateTime.Now;
            membership.IsActive = true;
            _memberships.Add(membership);
        }

        public void RemoveMember(int clubId, int customerId)
        {
            var m = _memberships.FirstOrDefault(x => x.ClubId == clubId && x.CustomerId == customerId && x.IsActive);
            if (m != null) m.IsActive = false;
        }

        public IEnumerable<ClubWatchlistItem> GetWatchlist(int clubId) =>
            _watchlist.Where(w => w.ClubId == clubId).OrderByDescending(w => w.AddedDate).ToList();

        public void AddToWatchlist(ClubWatchlistItem item)
        {
            item.Id = _nextWatchlistId++;
            item.AddedDate = DateTime.Now;
            item.IsWatched = false;
            _watchlist.Add(item);
        }

        public void MarkWatched(int itemId, double rating)
        {
            var item = _watchlist.FirstOrDefault(w => w.Id == itemId);
            if (item != null)
            {
                item.IsWatched = true;
                item.WatchedDate = DateTime.Now;
                item.AverageRating = rating;
            }
        }

        public IEnumerable<ClubPoll> GetPolls(int clubId) =>
            _polls.Where(p => p.ClubId == clubId).OrderByDescending(p => p.CreatedDate).ToList();

        public ClubPoll GetPollById(int pollId) => _polls.FirstOrDefault(p => p.Id == pollId);

        public void AddPoll(ClubPoll poll)
        {
            poll.Id = _nextPollId++;
            poll.CreatedDate = DateTime.Now;
            poll.Status = PollStatus.Open;
            // Assign option IDs
            for (int i = 0; i < poll.Options.Count; i++)
                poll.Options[i].OptionId = i + 1;
            _polls.Add(poll);
        }

        public void Vote(int pollId, int optionId, int customerId)
        {
            var poll = _polls.FirstOrDefault(p => p.Id == pollId);
            if (poll == null || poll.Status != PollStatus.Open) return;

            // Remove any existing vote by this customer
            foreach (var opt in poll.Options)
                opt.VoterIds.Remove(customerId);

            var option = poll.Options.FirstOrDefault(o => o.OptionId == optionId);
            option?.VoterIds.Add(customerId);
        }

        public void ClosePoll(int pollId)
        {
            var poll = _polls.FirstOrDefault(p => p.Id == pollId);
            if (poll != null)
            {
                poll.Status = PollStatus.Closed;
                poll.ClosedDate = DateTime.Now;
            }
        }

        public ClubStats GetStats(int clubId)
        {
            var club = GetById(clubId);
            if (club == null) return null;

            var members = GetMembers(clubId).ToList();
            var watchlist = GetWatchlist(clubId).ToList();
            var polls = GetPolls(clubId).ToList();

            return new ClubStats
            {
                ClubId = clubId,
                ClubName = club.Name,
                MemberCount = members.Count,
                MoviesWatched = watchlist.Count(w => w.IsWatched),
                WatchlistSize = watchlist.Count(w => !w.IsWatched),
                MeetingsHeld = 0,
                PollsCompleted = polls.Count(p => p.Status == PollStatus.Closed),
                AverageMovieRating = watchlist.Where(w => w.AverageRating.HasValue).Select(w => w.AverageRating.Value).DefaultIfEmpty(0).Average(),
                MostActiveGenre = club.Genre,
                TotalSavings = members.Count * club.GroupDiscountPercent
            };
        }
    }
}
