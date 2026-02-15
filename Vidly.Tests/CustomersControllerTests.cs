using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class CustomersControllerTests
    {
        /// <summary>
        /// Verifies that Index returns a ViewResult with a CustomerSearchViewModel.
        /// </summary>
        [TestMethod]
        public void Index_ReturnsViewWithCustomerSearchViewModel()
        {
            var controller = new CustomersController();

            var result = controller.Index(null, null, null) as ViewResult;

            Assert.IsNotNull(result, "Index should return a ViewResult.");
            var vm = result.Model as CustomerSearchViewModel;
            Assert.IsNotNull(vm, "Model should be a CustomerSearchViewModel.");
            Assert.IsTrue(vm.Customers.Count >= 5,
                "Should contain at least the 5 pre-seeded customers.");
        }

        /// <summary>
        /// Verifies that Index sorts by Name by default.
        /// </summary>
        [TestMethod]
        public void Index_DefaultSort_ByName()
        {
            var controller = new CustomersController();

            var result = controller.Index(null, null, null) as ViewResult;
            var vm = result?.Model as CustomerSearchViewModel;

            Assert.IsNotNull(vm);
            for (int i = 1; i < vm.Customers.Count; i++)
            {
                Assert.IsTrue(
                    string.Compare(vm.Customers[i - 1].Name, vm.Customers[i].Name, StringComparison.Ordinal) <= 0,
                    $"Customers should be sorted by name: '{vm.Customers[i - 1].Name}' should come before '{vm.Customers[i].Name}'.");
            }
        }

        /// <summary>
        /// Verifies that Index can sort by membership type.
        /// </summary>
        [TestMethod]
        public void Index_SortByMembership_OrdersCorrectly()
        {
            var controller = new CustomersController();

            var result = controller.Index(null, null, "Membership") as ViewResult;
            var vm = result?.Model as CustomerSearchViewModel;

            Assert.IsNotNull(vm);
            for (int i = 1; i < vm.Customers.Count; i++)
            {
                Assert.IsTrue(
                    vm.Customers[i - 1].MembershipType <= vm.Customers[i].MembershipType,
                    "Customers should be sorted by membership type ascending.");
            }
        }

        /// <summary>
        /// Verifies that Index filters by membership type.
        /// </summary>
        [TestMethod]
        public void Index_FilterByMembership_ReturnsMatchingCustomers()
        {
            var controller = new CustomersController();

            var result = controller.Index(null, MembershipType.Gold, null) as ViewResult;
            var vm = result?.Model as CustomerSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.Customers.Count >= 2,
                "Should find at least 2 Gold members (John Smith and Charlie Brown).");
            Assert.IsTrue(vm.Customers.All(c => c.MembershipType == MembershipType.Gold),
                "All returned customers should be Gold members.");
        }

        /// <summary>
        /// Verifies that Index searches by name substring.
        /// </summary>
        [TestMethod]
        public void Index_SearchByName_ReturnsMatchingCustomers()
        {
            var controller = new CustomersController();

            var result = controller.Index("john", null, null) as ViewResult;
            var vm = result?.Model as CustomerSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.Customers.Count >= 1, "Should find at least 1 customer matching 'john'.");
            Assert.IsTrue(vm.Customers.Any(c => c.Name.IndexOf("john", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 (c.Email != null && c.Email.IndexOf("john", StringComparison.OrdinalIgnoreCase) >= 0)),
                "Matched customers should contain 'john' in name or email.");
        }

        /// <summary>
        /// Verifies that Index searches by email substring.
        /// </summary>
        [TestMethod]
        public void Index_SearchByEmail_ReturnsMatchingCustomers()
        {
            var controller = new CustomersController();

            var result = controller.Index("alice.j@", null, null) as ViewResult;
            var vm = result?.Model as CustomerSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.AreEqual(1, vm.Customers.Count, "Should find exactly 1 customer matching 'alice.j@'.");
            Assert.AreEqual("Alice Johnson", vm.Customers[0].Name);
        }

        /// <summary>
        /// Verifies that Index shows TotalCount reflecting unfiltered count.
        /// </summary>
        [TestMethod]
        public void Index_WithFilter_ShowsTotalCount()
        {
            var controller = new CustomersController();

            var result = controller.Index("john", null, null) as ViewResult;
            var vm = result?.Model as CustomerSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.IsTrue(vm.TotalCount >= 5,
                "TotalCount should reflect all customers, not just filtered.");
        }

        /// <summary>
        /// Verifies that Index includes customer stats.
        /// </summary>
        [TestMethod]
        public void Index_IncludesStats()
        {
            var controller = new CustomersController();

            var result = controller.Index(null, null, null) as ViewResult;
            var vm = result?.Model as CustomerSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.IsNotNull(vm.Stats, "Stats should be populated.");
            Assert.IsTrue(vm.Stats.TotalCustomers >= 5);
            Assert.IsTrue(vm.Stats.GoldCount >= 2);
            Assert.IsTrue(vm.Stats.SilverCount >= 1);
            Assert.IsTrue(vm.Stats.PlatinumCount >= 1);
            Assert.IsTrue(vm.Stats.BasicCount >= 1);
        }

        /// <summary>
        /// Verifies that Details returns the correct customer for a valid ID.
        /// </summary>
        [TestMethod]
        public void Details_ValidId_ReturnsCustomerView()
        {
            var controller = new CustomersController();

            var result = controller.Details(1) as ViewResult;

            Assert.IsNotNull(result, "Details with valid ID should return a ViewResult.");
            var customer = result.Model as Customer;
            Assert.IsNotNull(customer, "Model should be a Customer.");
            Assert.AreEqual(1, customer.Id);
            Assert.AreEqual("John Smith", customer.Name);
            Assert.AreEqual("john.smith@example.com", customer.Email);
            Assert.AreEqual(MembershipType.Gold, customer.MembershipType);
        }

        /// <summary>
        /// Verifies that Details returns HttpNotFound for a non-existent customer ID.
        /// </summary>
        [TestMethod]
        public void Details_InvalidId_ReturnsHttpNotFound()
        {
            var controller = new CustomersController();

            var result = controller.Details(99999);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult),
                "Details with invalid ID should return HttpNotFoundResult.");
        }

        /// <summary>
        /// Verifies that Create GET returns an Edit view with defaults.
        /// </summary>
        [TestMethod]
        public void Create_Get_ReturnsEditViewWithDefaults()
        {
            var controller = new CustomersController();

            var result = controller.Create() as ViewResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Edit", result.ViewName,
                "Create should render the Edit view.");
            var customer = result.Model as Customer;
            Assert.IsNotNull(customer);
            Assert.AreEqual(0, customer.Id,
                "New customer should have default Id of 0.");
            Assert.AreEqual(MembershipType.Basic, customer.MembershipType,
                "New customer should default to Basic membership.");
        }

        /// <summary>
        /// Verifies that Edit returns the correct customer for a valid ID.
        /// </summary>
        [TestMethod]
        public void Edit_ValidId_ReturnsCustomerView()
        {
            var controller = new CustomersController();

            var result = controller.Edit(1) as ViewResult;

            Assert.IsNotNull(result, "Edit with valid ID should return a ViewResult.");
            var customer = result.Model as Customer;
            Assert.IsNotNull(customer, "Model should be a Customer.");
            Assert.AreEqual(1, customer.Id);
        }

        /// <summary>
        /// Verifies that Edit returns HttpNotFound for a non-existent ID.
        /// </summary>
        [TestMethod]
        public void Edit_InvalidId_ReturnsHttpNotFound()
        {
            var controller = new CustomersController();

            var result = controller.Edit(99999);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult),
                "Edit with invalid ID should return HttpNotFoundResult.");
        }

        /// <summary>
        /// Verifies combined query and membership filter.
        /// </summary>
        [TestMethod]
        public void Index_CombinedFilters_NarrowsResults()
        {
            var controller = new CustomersController();

            var result = controller.Index("smith", MembershipType.Gold, null) as ViewResult;
            var vm = result?.Model as CustomerSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.AreEqual(1, vm.Customers.Count,
                "Should find exactly 1 Gold member named Smith.");
            Assert.AreEqual("John Smith", vm.Customers[0].Name);
        }

        /// <summary>
        /// Verifies Index preserves filter values in ViewModel.
        /// </summary>
        [TestMethod]
        public void Index_PreservesFilterValues()
        {
            var controller = new CustomersController();

            var result = controller.Index("test", MembershipType.Silver, "Email") as ViewResult;
            var vm = result?.Model as CustomerSearchViewModel;

            Assert.IsNotNull(vm);
            Assert.AreEqual("test", vm.Query);
            Assert.AreEqual(MembershipType.Silver, vm.MembershipType);
            Assert.AreEqual("Email", vm.SortBy);
        }

        /// <summary>
        /// Verifies sort by member since (descending).
        /// </summary>
        [TestMethod]
        public void Index_SortByMemberSince_DescendingOrder()
        {
            var controller = new CustomersController();

            var result = controller.Index(null, null, "MemberSince") as ViewResult;
            var vm = result?.Model as CustomerSearchViewModel;

            Assert.IsNotNull(vm);
            for (int i = 1; i < vm.Customers.Count; i++)
            {
                var prev = vm.Customers[i - 1].MemberSince ?? DateTime.MinValue;
                var curr = vm.Customers[i].MemberSince ?? DateTime.MinValue;
                Assert.IsTrue(prev >= curr,
                    $"Customers should be sorted by member since descending: {prev:d} should be >= {curr:d}.");
            }
        }
    }
}
