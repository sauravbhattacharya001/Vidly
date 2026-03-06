using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// In-memory implementation of the dispute repository.
    /// Thread-safe via locking. Uses static storage for shared state across tests.
    /// </summary>
    public class InMemoryDisputeRepository : IDisputeRepository
    {
        private static readonly List<Dispute> _disputes = new List<Dispute>();
        private static readonly object _lock = new object();
        private static int _nextId = 1;

        /// <summary>
        /// Resets the repository to empty state for test isolation.
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _disputes.Clear();
                _nextId = 1;
            }
        }

        public Dispute GetById(int id)
        {
            lock (_lock)
            {
                return _disputes.FirstOrDefault(d => d.Id == id);
            }
        }

        public IEnumerable<Dispute> GetAll()
        {
            lock (_lock)
            {
                return _disputes.ToList();
            }
        }

        public IEnumerable<Dispute> GetByCustomer(int customerId)
        {
            lock (_lock)
            {
                return _disputes.Where(d => d.CustomerId == customerId).ToList();
            }
        }

        public IEnumerable<Dispute> GetByRental(int rentalId)
        {
            lock (_lock)
            {
                return _disputes.Where(d => d.RentalId == rentalId).ToList();
            }
        }

        public IEnumerable<Dispute> GetByStatus(DisputeStatus status)
        {
            lock (_lock)
            {
                return _disputes.Where(d => d.Status == status).ToList();
            }
        }

        public void Add(Dispute dispute)
        {
            if (dispute == null)
                throw new ArgumentNullException(nameof(dispute));

            lock (_lock)
            {
                dispute.Id = _nextId++;
                _disputes.Add(dispute);
            }
        }

        public void Update(Dispute dispute)
        {
            if (dispute == null)
                throw new ArgumentNullException(nameof(dispute));

            lock (_lock)
            {
                var index = _disputes.FindIndex(d => d.Id == dispute.Id);
                if (index >= 0)
                    _disputes[index] = dispute;
            }
        }
    }
}
