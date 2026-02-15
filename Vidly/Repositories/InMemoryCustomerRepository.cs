using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Thread-safe in-memory customer repository.
    /// Uses Dictionary for O(1) lookups by ID, counter-based ID generation,
    /// and single-pass statistics computation.
    /// </summary>
    public class InMemoryCustomerRepository : ICustomerRepository
    {
        private static readonly Dictionary<int, Customer> _customers = new Dictionary<int, Customer>
        {
            [1] = new Customer
            {
                Id = 1,
                Name = "John Smith",
                Email = "john.smith@example.com",
                Phone = "555-0101",
                MemberSince = new DateTime(2024, 1, 15),
                MembershipType = MembershipType.Gold
            },
            [2] = new Customer
            {
                Id = 2,
                Name = "Jane Doe",
                Email = "jane.doe@example.com",
                Phone = "555-0102",
                MemberSince = new DateTime(2024, 6, 20),
                MembershipType = MembershipType.Silver
            },
            [3] = new Customer
            {
                Id = 3,
                Name = "Bob Wilson",
                Email = "bob.wilson@example.com",
                Phone = "555-0103",
                MemberSince = new DateTime(2025, 3, 10),
                MembershipType = MembershipType.Basic
            },
            [4] = new Customer
            {
                Id = 4,
                Name = "Alice Johnson",
                Email = "alice.j@example.com",
                Phone = "555-0104",
                MemberSince = new DateTime(2023, 11, 5),
                MembershipType = MembershipType.Platinum
            },
            [5] = new Customer
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
        private static int _nextId = 6;

        public Customer GetById(int id)
        {
            lock (_lock)
            {
                return _customers.TryGetValue(id, out var customer) ? Clone(customer) : null;
            }
        }

        public IReadOnlyList<Customer> GetAll()
        {
            lock (_lock)
            {
                return _customers.Values.Select(Clone).ToList().AsReadOnly();
            }
        }

        public void Add(Customer customer)
        {
            if (customer == null)
                throw new ArgumentNullException(nameof(customer));

            lock (_lock)
            {
                customer.Id = _nextId++;
                _customers[customer.Id] = customer;
            }
        }

        public void Update(Customer customer)
        {
            if (customer == null)
                throw new ArgumentNullException(nameof(customer));

            lock (_lock)
            {
                if (!_customers.TryGetValue(customer.Id, out var existing))
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
                if (!_customers.Remove(id))
                    throw new KeyNotFoundException(
                        $"Customer with Id {id} not found.");
            }
        }

        public IReadOnlyList<Customer> Search(string query, MembershipType? membershipType)
        {
            lock (_lock)
            {
                IEnumerable<Customer> results = _customers.Values;

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
                return _customers.Values
                    .Where(c => c.MemberSince.HasValue
                             && c.MemberSince.Value.Year == year
                             && c.MemberSince.Value.Month == month)
                    .OrderBy(c => c.MemberSince)
                    .Select(Clone)
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <summary>
        /// Computes membership statistics in a single pass over the collection,
        /// avoiding multiple separate Count() enumerations.
        /// </summary>
        public CustomerStats GetStats()
        {
            lock (_lock)
            {
                int basic = 0, silver = 0, gold = 0, platinum = 0;

                foreach (var c in _customers.Values)
                {
                    switch (c.MembershipType)
                    {
                        case MembershipType.Basic:    basic++;    break;
                        case MembershipType.Silver:   silver++;   break;
                        case MembershipType.Gold:     gold++;     break;
                        case MembershipType.Platinum: platinum++; break;
                    }
                }

                return new CustomerStats
                {
                    TotalCustomers = _customers.Count,
                    BasicCount = basic,
                    SilverCount = silver,
                    GoldCount = gold,
                    PlatinumCount = platinum
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
