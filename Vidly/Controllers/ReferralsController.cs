using System;
using System.Web.Mvc;
using Vidly.Filters;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class ReferralsController : Controller
    {
        private readonly ReferralService _referralService;
        private readonly ICustomerRepository _customerRepository;

        public ReferralsController()
        {
            _customerRepository = new InMemoryCustomerRepository();
            _referralService = new ReferralService(_customerRepository);
        }

        public ReferralsController(ReferralService referralService,
                                    ICustomerRepository customerRepository)
        {
            _referralService = referralService
                ?? throw new ArgumentNullException(nameof(referralService));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        // GET: Referrals
        public ActionResult Index(int? customerId = null)
        {
            var vm = new ReferralViewModel
            {
                ProgramStats = _referralService.GetProgramStats(),
                Customers = _customerRepository.GetAll(),
                SelectedCustomerId = customerId
            };

            if (customerId.HasValue)
            {
                try
                {
                    vm.CustomerSummary = _referralService.GetCustomerSummary(customerId.Value);
                    vm.Referrals = _referralService.GetReferralsByCustomer(customerId.Value);
                }
                catch (ArgumentException)
                {
                    vm.Message = "Customer not found.";
                    vm.MessageType = "danger";
                }
            }
            else
            {
                vm.Referrals = _referralService.GetAll();
            }

            return View(vm);
        }

        // POST: Referrals/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RateLimit(MaxRequests = 5, WindowSeconds = 60,
            Message = "Too many referral requests. Please try again later.")]
        public ActionResult Create(int referrerId, string referredName, string referredEmail)
        {
            try
            {
                _referralService.CreateReferral(referrerId, referredName, referredEmail);
                TempData["Message"] = $"Referral sent to {referredName}!";
                TempData["MessageType"] = "success";
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                TempData["Message"] = ex.Message;
            }
            catch (Exception)
            {
                TempData["Message"] = "An unexpected error occurred. Please try again.";
                TempData["MessageType"] = "danger";
            }

            return RedirectToAction("Index", new { customerId = referrerId });
        }

        // POST: Referrals/Convert
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RateLimit(MaxRequests = 5, WindowSeconds = 60,
            Message = "Too many conversion attempts. Please try again later.")]
        public ActionResult Convert(string referralCode, int newCustomerId)
        {
            try
            {
                _referralService.ConvertReferral(referralCode, newCustomerId);
                TempData["Message"] = "Referral converted successfully! Points awarded.";
                TempData["MessageType"] = "success";
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                TempData["Message"] = ex.Message;
            }
            catch (Exception)
            {
                TempData["Message"] = "An unexpected error occurred. Please try again.";
                TempData["MessageType"] = "danger";
            }

            return RedirectToAction("Index");
        }
    }
}
