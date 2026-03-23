using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Subscription management — browse plans, subscribe, pause/resume,
    /// upgrade/downgrade, cancel, and view usage &amp; revenue stats.
    /// </summary>
    public class SubscriptionController : Controller
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly SubscriptionService _subscriptionService;

        public SubscriptionController()
            : this(new InMemoryCustomerRepository(),
                   new InMemorySubscriptionRepository()) { }

        public SubscriptionController(
            ICustomerRepository customerRepository,
            ISubscriptionRepository subscriptionRepository)
        {
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _subscriptionService = new SubscriptionService(
                subscriptionRepository, customerRepository);
        }

        /// <summary>
        /// GET /Subscription?customerId=1
        /// Main page: plan comparison, subscription status, usage dashboard.
        /// </summary>
        public ActionResult Index(int? customerId)
        {
            var customers = _customerRepository.GetAll().ToList();
            var revenue = _subscriptionService.GetRevenueBreakdown();

            var vm = new SubscriptionViewModel
            {
                Customers = customers,
                SelectedCustomerId = customerId,
                Revenue = revenue
            };

            if (customerId.HasValue)
            {
                var customer = _customerRepository.GetById(customerId.Value);
                if (customer == null)
                {
                    vm.StatusMessage = "Customer not found.";
                    return View(vm);
                }

                vm.SelectedCustomer = customer;
                var sub = _subscriptionService.GetByCustomerId(customerId.Value);
                if (sub != null)
                {
                    vm.Subscription = sub;
                    vm.Usage = _subscriptionService.GetUsage(sub.Id);
                }
            }

            return View(vm);
        }

        /// <summary>
        /// POST /Subscription/Subscribe
        /// Subscribe a customer to a plan.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Subscribe(int customerId, int planType)
        {
            try
            {
                var plan = (SubscriptionPlanType)planType;
                _subscriptionService.Subscribe(customerId, plan);
                TempData["Success"] = "Successfully subscribed to the " + SubscriptionService.GetPlan(plan).Name + " plan!";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index", new { customerId });
        }

        /// <summary>
        /// POST /Subscription/ChangePlan
        /// Upgrade or downgrade the subscription plan.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ChangePlan(int customerId, int subscriptionId, int newPlanType)
        {
            try
            {
                var plan = (SubscriptionPlanType)newPlanType;
                _subscriptionService.ChangePlan(subscriptionId, plan);
                TempData["Success"] = "Plan changed to " + SubscriptionService.GetPlan(plan).Name + "!";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index", new { customerId });
        }

        /// <summary>
        /// POST /Subscription/Pause
        /// Pause the subscription.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Pause(int customerId, int subscriptionId)
        {
            try
            {
                _subscriptionService.Pause(subscriptionId);
                TempData["Success"] = "Subscription paused. You won't be billed while paused (max 30 days).";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index", new { customerId });
        }

        /// <summary>
        /// POST /Subscription/Resume
        /// Resume a paused subscription.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Resume(int customerId, int subscriptionId)
        {
            try
            {
                _subscriptionService.Resume(subscriptionId);
                TempData["Success"] = "Subscription resumed! Your billing period has been extended.";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index", new { customerId });
        }

        /// <summary>
        /// POST /Subscription/Cancel
        /// Cancel the subscription.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Cancel(int customerId, int subscriptionId, string reason)
        {
            try
            {
                _subscriptionService.Cancel(subscriptionId, reason);
                TempData["Success"] = "Subscription cancelled. You can still use it until the end of the current billing period.";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index", new { customerId });
        }
    }
}
