using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Repository contract for dispute persistence.
    /// </summary>
    public interface IDisputeRepository
    {
        Dispute GetById(int id);
        IEnumerable<Dispute> GetAll();
        IEnumerable<Dispute> GetByCustomer(int customerId);
        IEnumerable<Dispute> GetByRental(int rentalId);
        IEnumerable<Dispute> GetByStatus(DisputeStatus status);
        void Add(Dispute dispute);
        void Update(Dispute dispute);
    }
}
