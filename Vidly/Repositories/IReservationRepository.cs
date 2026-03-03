using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Repository for movie reservations (holds).
    /// </summary>
    public interface IReservationRepository : IRepository<Reservation>
    {
        /// <summary>Returns all reservations for a customer.</summary>
        IReadOnlyList<Reservation> GetByCustomer(int customerId);

        /// <summary>Returns all reservations for a movie, ordered by queue position.</summary>
        IReadOnlyList<Reservation> GetByMovie(int movieId);

        /// <summary>Returns active (Waiting or Ready) reservations for a movie.</summary>
        IReadOnlyList<Reservation> GetActiveByMovie(int movieId);

        /// <summary>Returns the next waiting reservation in the queue for a movie.</summary>
        Reservation GetNextInQueue(int movieId);

        /// <summary>
        /// Checks if a customer already has an active reservation for a movie.
        /// </summary>
        bool HasActiveReservation(int customerId, int movieId);

        /// <summary>Returns all reservations with expired pickup windows.</summary>
        IReadOnlyList<Reservation> GetExpired();

        /// <summary>
        /// Searches reservations by customer name or movie name,
        /// optionally filtered by status.
        /// </summary>
        IReadOnlyList<Reservation> Search(string query, ReservationStatus? status);
    }
}
