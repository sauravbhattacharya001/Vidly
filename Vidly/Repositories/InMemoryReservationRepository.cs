using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// In-memory implementation of the reservation repository.
    /// Thread-safe for concurrent access via locking.
    /// </summary>
    public class InMemoryReservationRepository : IReservationRepository
    {
        private readonly List<Reservation> _reservations = new List<Reservation>();
        private readonly object _lock = new object();
        private int _nextId = 1;

        public Reservation GetById(int id)
        {
            lock (_lock)
            {
                return _reservations.FirstOrDefault(r => r.Id == id);
            }
        }

        public IReadOnlyList<Reservation> GetAll()
        {
            lock (_lock)
            {
                return _reservations.ToList().AsReadOnly();
            }
        }

        public void Add(Reservation entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            lock (_lock)
            {
                entity.Id = _nextId++;
                _reservations.Add(entity);
            }
        }

        public void Update(Reservation entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            lock (_lock)
            {
                var index = _reservations.FindIndex(r => r.Id == entity.Id);
                if (index < 0)
                    throw new InvalidOperationException(
                        $"Reservation {entity.Id} not found.");
                _reservations[index] = entity;
            }
        }

        public void Remove(int id)
        {
            lock (_lock)
            {
                var index = _reservations.FindIndex(r => r.Id == id);
                if (index >= 0)
                    _reservations.RemoveAt(index);
            }
        }

        public IReadOnlyList<Reservation> GetByCustomer(int customerId)
        {
            lock (_lock)
            {
                return _reservations
                    .Where(r => r.CustomerId == customerId)
                    .OrderByDescending(r => r.ReservedDate)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<Reservation> GetByMovie(int movieId)
        {
            lock (_lock)
            {
                return _reservations
                    .Where(r => r.MovieId == movieId)
                    .OrderBy(r => r.QueuePosition)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<Reservation> GetActiveByMovie(int movieId)
        {
            lock (_lock)
            {
                return _reservations
                    .Where(r => r.MovieId == movieId &&
                                (r.Status == ReservationStatus.Waiting ||
                                 r.Status == ReservationStatus.Ready))
                    .OrderBy(r => r.QueuePosition)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public Reservation GetNextInQueue(int movieId)
        {
            lock (_lock)
            {
                return _reservations
                    .Where(r => r.MovieId == movieId &&
                                r.Status == ReservationStatus.Waiting)
                    .OrderBy(r => r.QueuePosition)
                    .FirstOrDefault();
            }
        }

        public bool HasActiveReservation(int customerId, int movieId)
        {
            lock (_lock)
            {
                return _reservations.Any(r =>
                    r.CustomerId == customerId &&
                    r.MovieId == movieId &&
                    (r.Status == ReservationStatus.Waiting ||
                     r.Status == ReservationStatus.Ready));
            }
        }

        public IReadOnlyList<Reservation> GetExpired()
        {
            lock (_lock)
            {
                return _reservations
                    .Where(r => r.Status == ReservationStatus.Ready &&
                                r.ExpiresDate.HasValue &&
                                DateTime.Today > r.ExpiresDate.Value)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<Reservation> Search(string query, ReservationStatus? status)
        {
            lock (_lock)
            {
                var results = _reservations.AsEnumerable();

                if (status.HasValue)
                    results = results.Where(r => r.Status == status.Value);

                if (!string.IsNullOrWhiteSpace(query))
                {
                    var q = query.Trim();
                    results = results.Where(r =>
                        (r.CustomerName != null &&
                         r.CustomerName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (r.MovieName != null &&
                         r.MovieName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
                }

                return results
                    .OrderByDescending(r => r.ReservedDate)
                    .ToList()
                    .AsReadOnly();
            }
        }
    }
}
