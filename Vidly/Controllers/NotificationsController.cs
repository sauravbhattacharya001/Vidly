using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Notification center — shows alerts for overdue rentals, due-soon items,
    /// new arrivals, watchlist availability, and membership milestones.
    /// Supports per-customer view and admin summary.
    /// </summary>
    public class NotificationsController : Controller
    {
        private readonly NotificationService _notificationService;
        private readonly ICustomerRepository _customerRepository;

        public NotificationsController()
            : this(new NotificationService(),
                   new InMemoryCustomerRepository())
        {
        }

        public NotificationsController(
            NotificationService notificationService,
            ICustomerRepository customerRepository)
        {
            _notificationService = notificationService
                ?? throw new ArgumentNullException(nameof(notificationService));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        // GET: Notifications (admin summary)
        public ActionResult Index()
        {
            var summary = _notificationService.GetSummary();
            var customers = _customerRepository.GetAll()
                .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
                .OrderBy(s => s.Text)
                .ToList();

            ViewBag.Customers = customers;
            return View(summary);
        }

        // GET: Notifications/Customer/5
        public ActionResult Customer(int id)
        {
            var result = _notificationService.GetNotifications(id);
            return View(result);
        }
    }
}
