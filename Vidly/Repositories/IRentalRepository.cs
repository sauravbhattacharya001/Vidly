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
        /// Returns all rentals for a customer (any status).
        /// </summary>
        IReadOnlyList<Rental> GetByCustomer(int customerId);

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
        /// Atomically checks out a rental with concurrent rental limit enforcement.
        /// Verifies the customer's active rental count does not exceed the given limit
        /// before creating the rental, preventing TOCTOU races.
        /// </summary>
        /// <param name="rental">The rental to create.</param>
        /// <param name="maxConcurrentRentals">Maximum active rentals allowed for this customer.</param>
        /// <returns>The created rental with assigned Id and defaults.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the movie is already rented out or the customer has reached their rental limit.
        /// </exception>
        Rental Checkout(Rental rental, int maxConcurrentRentals);

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
        /// <summary>Revenue from returned rentals only (actual collected revenue).</summary>
        public decimal RealizedRevenue { get; set; }
        /// <summary>Projected revenue from active/overdue rentals (not yet collected).</summary>
        public decimal ProjectedRevenue { get; set; }
        public decimal TotalLateFees { get; set; }
    }
}
