using System;
using System.Web.Mvc;
using Vidly.Filters;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Staff tool to detect and merge duplicate customer accounts.
    /// Transfers rentals from the secondary customer to the primary,
    /// keeps the best data from each, and maintains an audit log.
    /// Rate-limited because merge is a destructive, irreversible operation —
    /// automated or accidental rapid merges could corrupt customer data (CWE-799).
    /// </summary>
    [RateLimit(MaxRequests = 5, WindowSeconds = 120,
        Message = "Too many merge operations. Please wait before merging again.")]
    public class CustomerMergeController : Controller
    {
        private readonly CustomerMergeService _mergeService;
        private readonly ICustomerRepository _customers;

        public CustomerMergeController()
            : this(
                new CustomerMergeService(
                    new InMemoryCustomerRepository(),
                    new InMemoryRentalRepository(),
                    new SystemClock()),
                new InMemoryCustomerRepository())
        {
        }

        public CustomerMergeController(
            CustomerMergeService mergeService,
            ICustomerRepository customers)
        {
            _mergeService = mergeService ?? throw new ArgumentNullException(nameof(mergeService));
            _customers = customers ?? throw new ArgumentNullException(nameof(customers));
        }

        /// <summary>
        /// GET: CustomerMerge — Shows detected duplicates, manual merge form, and audit log.
        /// </summary>
        public ActionResult Index()
        {
            var vm = new CustomerMergeViewModel
            {
                Duplicates = _mergeService.FindDuplicates(),
                AuditLog = _mergeService.GetAuditLog(),
                AllCustomers = _customers.GetAll()
            };
            return View(vm);
        }

        /// <summary>
        /// POST: CustomerMerge/Merge — Executes a merge operation.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Merge(MergeRequest request)
        {
            var result = _mergeService.Merge(request);

            var vm = new CustomerMergeViewModel
            {
                Duplicates = _mergeService.FindDuplicates(),
                AuditLog = _mergeService.GetAuditLog(),
                AllCustomers = _customers.GetAll(),
                LastResult = result
            };

            return View("Index", vm);
        }
    }
}
