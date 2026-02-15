using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Customer-specific repository with query methods beyond basic CRUD.
    /// </summary>
    public interface ICustomerRepository : IRepository<Customer>
    {
        /// <summary>
        /// Searches customers by name or email (case-insensitive substring match),
        /// optionally filtered by membership type.
        /// Results are ordered by name.
        /// </summary>
        IReadOnlyList<Customer> Search(string query, MembershipType? membershipType);

        /// <summary>
        /// Returns customers who joined in the given year and month, ordered by join date.
        /// </summary>
        IReadOnlyList<Customer> GetByMemberSince(int year, int month);

        /// <summary>
        /// Returns statistics about the customer base.
        /// </summary>
        CustomerStats GetStats();
    }

    /// <summary>
    /// Summary statistics for the customer base.
    /// </summary>
    public class CustomerStats
    {
        public int TotalCustomers { get; set; }
        public int BasicCount { get; set; }
        public int SilverCount { get; set; }
        public int GoldCount { get; set; }
        public int PlatinumCount { get; set; }
    }
}
