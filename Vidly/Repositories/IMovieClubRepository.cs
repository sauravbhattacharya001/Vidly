using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    public interface IMovieClubRepository
    {
        IEnumerable<MovieClub> GetAll();
        MovieClub GetById(int id);
        IEnumerable<MovieClub> GetByStatus(ClubStatus status);
        void Add(MovieClub club);
        void Update(MovieClub club);

        IEnumerable<ClubMembership> GetMembers(int clubId);
        ClubMembership GetMembership(int clubId, int customerId);
        void AddMember(ClubMembership membership);
        void RemoveMember(int clubId, int customerId);

        IEnumerable<ClubWatchlistItem> GetWatchlist(int clubId);
        void AddToWatchlist(ClubWatchlistItem item);
        void MarkWatched(int itemId, double rating);

        IEnumerable<ClubPoll> GetPolls(int clubId);
        ClubPoll GetPollById(int pollId);
        void AddPoll(ClubPoll poll);
        void Vote(int pollId, int optionId, int customerId);
        void ClosePoll(int pollId);

        ClubStats GetStats(int clubId);
    }
}
