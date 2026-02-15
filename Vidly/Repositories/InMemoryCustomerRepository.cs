using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Thread-safe in-memory customer repository.
    /// </summary>
    public class InMemoryCustomerRepository : ICustomerRepository
    {
        private static readonly List<Customer> _customers = new List<Customer>
        {
            new Customer
            {
                Id = 1,
                Name = "John Smith",
                Email = "john.smith@example.com",
                Phone = "555-0101",
                MemberSince = new DateTime(2024, 1, 15),
                MembershipType = MembershipType.Gold
            },
            new Customer
            {
                Id = 2,
                Name = "Jane Doe",
                Email = "jane.doe@example.com",
                Phone = "555-0102",
                MemberSince = new DateTime(2024, 6, 20),
                MembershipType = MembershipType.Silver
            },
            new Customer
            {
                Id = 3,
                Name = "Bob Wilson",
                Email = "bob.wilson@example.com",
                Phone = "555-0103",
                MemberSince = new DateTime(2025, 3, 10),
                MembershipType = MembershipType.Basic
            },
            new Customer
            {
                Id = 4,
                Name = "Alice Johnson",
                Email = "alice.j@example.com",
                Phone = "555-0104",
                MemberSince = new DateTime(2023, 11, 5),
                MembershipType = MembershipType.Platinum
            },
            new Customer
            {
                Id = 5,
                Name = "Charlie Brown",
                Email = "charlie.b@example.com",
                Phone = "555-0105",
                MemberSince = new DateTime(2025, 1, 1),
                MembershipType = MembershipType.Gold
            }
        };

        private static readonly object _lock = new object();

        public Customer GetById(int id)
        {
            lock (_lock)
            {
                var customer = _customers.SingleOrDefault(c => c.Id == id);
                return customer == null ? null : Clone(customer);
            }
        }

        public IReadOnlyList<Customer> GetAll()
        {
            lock (_lock)
            {
                return _customers.Select(Clone).ToList().AsReadOnly();
            }
        }

        public void Add(Customer customer)
        {
            if (customer == null)
                throw new ArgumentNullException(nameof(customer));

            lock (_lock)
            {
                customer.Id = _customers.Any() ? _customers.Max(c => c.Id) + 1 : 1;
                _customers.Add(customer);
            }
        }

        public void Update(Customer customer)
        {
            if (customer == null)
                throw new ArgumentNullException(nameof(customer));

            lock (_lock)
            {
                var existing = _customers.SingleOrDefault(c => c.Id == customer.Id);
                if (existing == null)
                    throw new KeyNotFoundException(
                        $"Customer with Id {customer.Id} not found.");

                existing.Name = customer.Name;
                existing.Email = customer.Email;
                existing.Phone = customer.Phone;
                existing.MemberSince = customer.MemberSince;
                existing.MembershipType = customer.MembershipType;
            }
        }

        public void Remove(int id)
        {
            lock (_lock)
            {
                var customer = _customers.SingleOrDefault(c => c.Id == id);
                if (customer == null)
                    throw new KeyNotFoundException(
                        $"Customer with Id {id} not found.");

                _customers.Remove(customer);
            }
        }

        public IReadOnlyList<Customer> Search(string query, MembershipType? membershipType)
        {
            lock (_lock)
            {
                IEnumerable<Customer> results = _customers;

                if (!string.IsNullOrWhiteSpace(query))
                {
                    results = results.Where(c =>
                        (c.Name != null &&
                         c.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (c.Email != null &&
                         c.Email.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
                }

                if (membershipType.HasValue)
                {
                    results = results.Where(c => c.MembershipType == membershipType.Value);
                }

                return results
                    .OrderBy(c => c.Name)
                    .Select(Clone)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<Customer> GetByMemberSince(int year, int month)
        {
            lock (_lock)
            {
                return _customers
                    .Where(c => c.MemberSince.HasValue
                             && c.MemberSince.Value.Year == year
                             && c.MemberSince.Value.Month == month)
                    .OrderBy(c => c.MemberSince)
                    .Select(Clone)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public CustomerStats GetStats()
        {
            lock (_lock)
            {
                return new CustomerStats
                {
                    TotalCustomers = _customers.Count,
                    BasicCount = _customers.Count(c => c.MembershipType == MembershipType.Basic),
                    SilverCount = _customers.Count(c => c.MembershipType == MembershipType.Silver),
                    GoldCount = _customers.Count(c => c.MembershipType == MembershipType.Gold),
                    PlatinumCount = _customers.Count(c => c.MembershipType == MembershipType.Platinum)
                };
            }
        }

        /// <summary>
        /// Creates a defensive copy to prevent callers from
        /// mutating the internal store outside the lock.
        /// </summary>
        private static Customer Clone(Customer source)
        {
            return new Customer
            {
                Id = source.Id,
                Name = source.Name,
                Email = source.Email,
                Phone = source.Phone,
                MemberSince = source.MemberSince,
                MembershipType = source.MembershipType
            };
        }
    }
}
