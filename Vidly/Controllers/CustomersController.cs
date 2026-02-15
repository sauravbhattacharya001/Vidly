using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class CustomersController : Controller
    {
        private readonly ICustomerRepository _customerRepository;

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public CustomersController()
            : this(new InMemoryCustomerRepository())
        {
        }

        /// <summary>
        /// Constructor injection for testability and future DI container use.
        /// </summary>
        public CustomersController(ICustomerRepository customerRepository)
        {
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        // GET: Customers
        public ActionResult Index(string query, MembershipType? membershipType, string sortBy)
        {
            var allCustomers = _customerRepository.GetAll();
            var totalCount = allCustomers.Count;

            IReadOnlyList<Customer> customers;
            if (!string.IsNullOrWhiteSpace(query) || membershipType.HasValue)
            {
                customers = _customerRepository.Search(query, membershipType);
            }
            else
            {
                customers = allCustomers;
            }

            // Apply sorting
            var sort = string.IsNullOrWhiteSpace(sortBy) ? "Name" : sortBy;
            IEnumerable<Customer> sorted;
            switch (sort.ToLowerInvariant())
            {
                case "membership":
                    sorted = customers.OrderBy(c => c.MembershipType).ThenBy(c => c.Name);
                    break;
                case "membersince":
                    sorted = customers.OrderByDescending(c => c.MemberSince ?? DateTime.MinValue).ThenBy(c => c.Name);
                    break;
                case "email":
                    sorted = customers.OrderBy(c => c.Email ?? "").ThenBy(c => c.Name);
                    break;
                case "id":
                    sorted = customers.OrderBy(c => c.Id);
                    break;
                default:
                    sorted = customers.OrderBy(c => c.Name);
                    break;
            }

            var viewModel = new CustomerSearchViewModel
            {
                Customers = sorted.ToList(),
                Query = query,
                MembershipType = membershipType,
                SortBy = sort,
                TotalCount = totalCount,
                Stats = _customerRepository.GetStats()
            };

            return View(viewModel);
        }

        // GET: Customers/Details/5
        public ActionResult Details(int id)
        {
            var customer = _customerRepository.GetById(id);

            if (customer == null)
                return HttpNotFound();

            return View(customer);
        }

        // GET: Customers/Create
        public ActionResult Create()
        {
            var customer = new Customer
            {
                MemberSince = DateTime.Today,
                MembershipType = MembershipType.Basic
            };
            return View("Edit", customer);
        }

        // POST: Customers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(Customer customer)
        {
            if (!ModelState.IsValid)
                return View("Edit", customer);

            _customerRepository.Add(customer);

            return RedirectToAction("Index");
        }

        // GET: Customers/Edit/5
        public ActionResult Edit(int id)
        {
            var customer = _customerRepository.GetById(id);

            if (customer == null)
                return HttpNotFound();

            return View(customer);
        }

        // POST: Customers/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, Customer customer)
        {
            if (customer.Id != id)
                return new HttpStatusCodeResult(400, "Customer ID mismatch.");

            if (!ModelState.IsValid)
                return View(customer);

            try
            {
                _customerRepository.Update(customer);
            }
            catch (KeyNotFoundException)
            {
                return HttpNotFound();
            }

            return RedirectToAction("Index");
        }

        // POST: Customers/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            try
            {
                _customerRepository.Remove(id);
            }
            catch (KeyNotFoundException)
            {
                return HttpNotFound();
            }

            return RedirectToAction("Index");
        }
    }
}
