using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    public class InMemoryPenaltyWaiverRepository : IPenaltyWaiverRepository
    {
        private static readonly List<PenaltyWaiver> _waivers = new List<PenaltyWaiver>();
        private static int _nextId = 1;
        private static readonly object _lock = new object();

        public IReadOnlyList<PenaltyWaiver> GetAll()
        {
            lock (_lock)
                return _waivers.OrderByDescending(w => w.GrantedDate).ToList().AsReadOnly();
        }

        public PenaltyWaiver GetById(int id)
        {
            lock (_lock)
                return _waivers.FirstOrDefault(w => w.Id == id);
        }

        public IReadOnlyList<PenaltyWaiver> GetByRental(int rentalId)
        {
            lock (_lock)
                return _waivers.Where(w => w.RentalId == rentalId)
                    .OrderByDescending(w => w.GrantedDate)
                    .ToList().AsReadOnly();
        }

        public PenaltyWaiver Add(PenaltyWaiver waiver)
        {
            if (waiver == null) throw new ArgumentNullException(nameof(waiver));

            lock (_lock)
            {
                waiver.Id = _nextId++;
                if (waiver.GrantedDate == default)
                    waiver.GrantedDate = DateTime.Today;
                _waivers.Add(waiver);
                return waiver;
            }
        }

        public decimal GetTotalWaivedForRental(int rentalId)
        {
            lock (_lock)
                return _waivers.Where(w => w.RentalId == rentalId).Sum(w => w.AmountWaived);
        }

        public WaiverStats GetStats()
        {
            lock (_lock)
            {
                return new WaiverStats
                {
                    TotalWaivers = _waivers.Count,
                    TotalAmountWaived = _waivers.Sum(w => w.AmountWaived),
                    FullWaivers = _waivers.Count(w => w.Type == WaiverType.Full),
                    PartialWaivers = _waivers.Count(w => w.Type != WaiverType.Full)
                };
            }
        }
    }
}
