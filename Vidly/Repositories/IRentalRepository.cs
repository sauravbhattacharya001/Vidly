using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Rental-specific repository with business logic queries.
    /// </summary>
    public interface IRentalRepository : IRepository<Rental>
    {
        /// <summary>
        /// Returns all active (non-returned) rentals for a customer.
        /// </summary>
        IReadOnlyList<Rental> GetActiveByCustomer(int customerId);

        /// <summary>
        /// Returns all rentals for a specific movie.
        /// </summary>
        IReadOnlyList<Rental> GetByMovie(int movieId);

        /// <summary>
        /// Returns all overdue rentals (due date passed, not returned).
        /// </summary>
        IReadOnlyList<Rental> GetOverdue();

        /// <summary>
        /// Searches rentals by customer name or movie name,
        /// optionally filtered by status.
        /// </summary>
        IReadOnlyList<Rental> Search(string query, RentalStatus? status);

        /// <summary>
        /// Marks a rental as returned, calculates late fees.
        /// Returns the updated rental.
        /// </summary>
        Rental ReturnRental(int rentalId);

        /// <summary>
        /// Checks whether a movie is currently rented out (active, not returned).
        /// </summary>
        bool IsMovieRentedOut(int movieId);

        /// <summary>
        /// Atomically checks movie availability and creates the rental in a single
        /// operation, preventing TOCTOU race conditions where two concurrent requests
        /// could both pass the availability check and create duplicate rentals.
        /// </summary>
        /// <param name="rental">The rental to create.</param>
        /// <returns>The created rental with assigned Id and defaults.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the movie is already rented out.
        /// </exception>
        Rental Checkout(Rental rental);

        /// <summary>
        /// Returns dashboard statistics about rentals.
        /// </summary>
        RentalStats GetStats();
    }

    /// <summary>
    /// Summary statistics for the rental system.
    /// </summary>
    public class RentalStats
    {
        public int TotalRentals { get; set; }
        public int ActiveRentals { get; set; }
        public int OverdueRentals { get; set; }
        public int ReturnedRentals { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalLateFees { get; set; }
    }
}
