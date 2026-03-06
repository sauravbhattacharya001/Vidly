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
        public ActionResult Create([Bind(Exclude = "Id")] Customer customer)
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
