using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    public interface IWaitlistRepository
    {
        IEnumerable<WaitlistEntry> GetAll();
        WaitlistEntry GetById(int id);
        IEnumerable<WaitlistEntry> GetByCustomer(int customerId);
        IEnumerable<WaitlistEntry> GetByMovie(int movieId);
        IEnumerable<WaitlistEntry> GetActiveByMovie(int movieId);
        WaitlistEntry FindExisting(int customerId, int movieId);
        void Add(WaitlistEntry entry);
        void Update(WaitlistEntry entry);
        void Remove(int id);
        WaitlistStats GetStats();
    }
}
