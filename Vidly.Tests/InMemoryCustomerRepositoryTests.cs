using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Tests
{
    [TestClass]
    public class InMemoryCustomerRepositoryTests
    {
        private InMemoryCustomerRepository _repo;

        [TestInitialize]
        public void Setup()
        {
            _repo = new InMemoryCustomerRepository();
        }

        [TestMethod]
        public void GetAll_ReturnsAllSeededCustomers()
        {
            var customers = _repo.GetAll();
            Assert.IsTrue(customers.Count >= 5,
                "Should return at least 5 seeded customers.");
        }

        [TestMethod]
        public void GetById_ValidId_ReturnsCustomer()
        {
            var customer = _repo.GetById(1);
            Assert.IsNotNull(customer);
            Assert.AreEqual("John Smith", customer.Name);
            Assert.AreEqual("john.smith@example.com", customer.Email);
        }

        [TestMethod]
        public void GetById_InvalidId_ReturnsNull()
        {
            var customer = _repo.GetById(99999);
            Assert.IsNull(customer);
        }

        [TestMethod]
        public void GetById_ReturnsDefensiveCopy()
        {
            var customer = _repo.GetById(1);
            customer.Name = "MODIFIED";

            var original = _repo.GetById(1);
            Assert.AreEqual("John Smith", original.Name,
                "Modifying the returned object should not affect the store.");
        }

        [TestMethod]
        public void Add_IncreasesCount()
        {
            var before = _repo.GetAll().Count;
            _repo.Add(new Customer
            {
                Name = "New Customer",
                Email = "new@example.com",
                MembershipType = MembershipType.Basic
            });
            var after = _repo.GetAll().Count;
            Assert.AreEqual(before + 1, after);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Add_NullCustomer_Throws()
        {
            _repo.Add(null);
        }

        [TestMethod]
        public void Update_ChangesCustomerData()
        {
            var customer = _repo.GetById(1);
            customer.Name = "Updated Name";
            customer.Email = "updated@example.com";
            _repo.Update(customer);

            var updated = _repo.GetById(1);
            Assert.AreEqual("Updated Name", updated.Name);
            Assert.AreEqual("updated@example.com", updated.Email);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void Update_NonExistent_Throws()
        {
            _repo.Update(new Customer { Id = 99999, Name = "Ghost" });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Update_NullCustomer_Throws()
        {
            _repo.Update(null);
        }

        [TestMethod]
        public void Remove_DecreasesCount()
        {
            var before = _repo.GetAll().Count;
            _repo.Remove(1);
            var after = _repo.GetAll().Count;
            Assert.AreEqual(before - 1, after);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void Remove_NonExistent_Throws()
        {
            _repo.Remove(99999);
        }

        [TestMethod]
        public void Search_ByName_ReturnsMatches()
        {
            var results = _repo.Search("john", null);
            Assert.IsTrue(results.Count >= 1);
            Assert.IsTrue(results.Any(c =>
                c.Name.IndexOf("john", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [TestMethod]
        public void Search_ByEmail_ReturnsMatches()
        {
            var results = _repo.Search("alice.j@", null);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Alice Johnson", results[0].Name);
        }

        [TestMethod]
        public void Search_ByMembership_ReturnsMatches()
        {
            var results = _repo.Search(null, MembershipType.Platinum);
            Assert.IsTrue(results.Count >= 1);
            Assert.IsTrue(results.All(c => c.MembershipType == MembershipType.Platinum));
        }

        [TestMethod]
        public void Search_NoMatch_ReturnsEmpty()
        {
            var results = _repo.Search("zzzznotfound", null);
            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public void GetByMemberSince_ReturnsMatches()
        {
            var results = _repo.GetByMemberSince(2024, 1);
            Assert.IsTrue(results.Count >= 1);
            Assert.IsTrue(results.All(c =>
                c.MemberSince.HasValue &&
                c.MemberSince.Value.Year == 2024 &&
                c.MemberSince.Value.Month == 1));
        }

        [TestMethod]
        public void GetByMemberSince_NoMatch_ReturnsEmpty()
        {
            var results = _repo.GetByMemberSince(1900, 1);
            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public void GetStats_ReturnsAccurateStats()
        {
            var stats = _repo.GetStats();
            Assert.IsTrue(stats.TotalCustomers >= 5);
            Assert.AreEqual(
                stats.BasicCount + stats.SilverCount + stats.GoldCount + stats.PlatinumCount,
                stats.TotalCustomers,
                "Sum of membership counts should equal total.");
        }
    }
}
