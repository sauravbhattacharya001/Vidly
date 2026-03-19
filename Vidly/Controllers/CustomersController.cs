using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Utilities;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class CustomersController : Controller
    {
        private readonly ICustomerRepository _customerRepository;

        private static readonly SortHelper<Customer> _sorter = new SortHelper<Customer>(
            "name",
            new Dictionary<string, SortColumn<Customer>>
            {
                ["name"]        = new SortColumn<Customer>(c => c.Name ?? ""),
                ["membership"]  = new SortColumn<Customer>(c => c.MembershipType, thenBy: c => c.Name ?? ""),
                ["membersince"] = new SortColumn<Customer>(c => c.MemberSince ?? DateTime.MinValue, descending: true, thenBy: c => c.Name ?? ""),
                ["email"]       = new SortColumn<Customer>(c => c.Email ?? "", thenBy: c => c.Name ?? ""),
                ["id"]          = new SortColumn<Customer>(c => c.Id),
            });

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

        /// <summary>
        /// GET: Customers — Lists all customers with optional search, membership
        /// type filter, and configurable sort order.
        /// </summary>
        /// <param name="query">Case-insensitive substring search on name or email.</param>
        /// <param name="membershipType">Optional membership tier filter.</param>
        /// <param name="sortBy">Sort column key (name, membership, membersince, email, id).</param>
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

            // Apply sorting via declarative SortHelper (replaces switch block)
            var sort = _sorter.ResolveKey(sortBy);

            var viewModel = new CustomerSearchViewModel
            {
                Customers = _sorter.Apply(customers, sort),
                Query = query,
                MembershipType = membershipType,
                SortBy = sort,
                TotalCount = totalCount,
                Stats = _customerRepository.GetStats()
            };

            return View(viewModel);
        }

        /// <summary>
        /// GET: Customers/Details/{id} — Shows full details for a single customer.
        /// Returns 404 if the customer does not exist.
        /// </summary>
        /// <param name="id">The customer identifier.</param>
        public ActionResult Details(int id)
        {
            var customer = _customerRepository.GetById(id);

            if (customer == null)
                return HttpNotFound();

            return View(customer);
        }

        /// <summary>
        /// GET: Customers/Create — Renders the customer editor form with sensible defaults
        /// (today's date, Basic membership).
        /// </summary>
        public ActionResult Create()
        {
            var customer = new Customer
            {
                MemberSince = DateTime.Today,
                MembershipType = MembershipType.Basic
            };
            return View("Edit", customer);
        }

        /// <summary>
        /// POST: Customers/Create — Validates and persists a new customer record.
        /// Re-renders the edit form on validation failure.
        /// </summary>
        /// <param name="customer">Form-bound customer data (Id excluded via Bind).</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Exclude = "Id")] Customer customer)
        {
            if (!ModelState.IsValid)
                return View("Edit", customer);

            _customerRepository.Add(customer);

            return RedirectToAction("Index");
        }

        /// <summary>
        /// GET: Customers/Edit/{id} — Renders the customer editor form for an existing customer.
        /// Returns 404 if the customer does not exist.
        /// </summary>
        /// <param name="id">The customer identifier.</param>
        public ActionResult Edit(int id)
        {
            var customer = _customerRepository.GetById(id);

            if (customer == null)
                return HttpNotFound();

            return View(customer);
        }

        /// <summary>
        /// POST: Customers/Edit/{id} — Updates an existing customer record.
        /// Uses the route ID as authoritative to prevent over-posting attacks
        /// where an attacker modifies the hidden Id field to update a different record.
        /// </summary>
        /// <param name="id">Route-based customer identifier (authoritative).</param>
        /// <param name="customer">Form-bound customer data (Id excluded via Bind).</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, [Bind(Include = "Name,Email,Phone,MemberSince,MembershipType")] Customer customer)
        {
            // Security: Use route ID as authoritative — prevents over-posting via
            // manipulated hidden Id field that could update a different customer record.
            customer.Id = id;

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

        /// <summary>
        /// POST: Customers/Delete/{id} — Permanently removes a customer record.
        /// Returns 404 if the customer does not exist.
        /// </summary>
        /// <param name="id">The customer identifier.</param>
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
