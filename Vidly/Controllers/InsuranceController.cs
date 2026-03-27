using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Manages rental insurance — customers can purchase policies,
    /// file claims, and view their coverage. Staff can review analytics,
    /// deny claims, and monitor loss ratios.
    /// </summary>
    public class InsuranceController : Controller
    {
        private readonly RentalInsuranceService _insuranceService;
        private readonly IRentalRepository _rentalRepo;
        private readonly ICustomerRepository _customerRepo;

        public InsuranceController()
            : this(new InMemoryRentalRepository(), new InMemoryCustomerRepository())
        {
        }

        public InsuranceController(IRentalRepository rentalRepo, ICustomerRepository customerRepo)
        {
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _customerRepo = customerRepo ?? throw new ArgumentNullException(nameof(customerRepo));
            _insuranceService = new RentalInsuranceService(rentalRepo, customerRepo);
        }

        // GET: Insurance
        public ActionResult Index(string message, bool? error)
        {
            var analytics = _insuranceService.GetAnalytics();
            var viewModel = new InsuranceViewModel
            {
                Analytics = analytics,
                UptakeRate = _insuranceService.GetUptakeRate(),
                TopClaimers = _insuranceService.GetTopClaimers(5),
                Message = message,
                IsError = error ?? false
            };

            return View(viewModel);
        }

        // GET: Insurance/Purchase?rentalId=1
        public ActionResult Purchase(int? rentalId)
        {
            if (!rentalId.HasValue)
                return RedirectToAction("Index", "Rentals");

            var rental = _rentalRepo.GetById(rentalId.Value);
            if (rental == null)
                return HttpNotFound("Rental not found.");

            var quotes = _insuranceService.GetQuotes(rentalId.Value);

            var viewModel = new InsuranceViewModel
            {
                RentalId = rentalId.Value,
                CustomerId = rental.CustomerId,
                CustomerName = rental.CustomerName,
                MovieName = rental.MovieName,
                Quotes = quotes
            };

            return View(viewModel);
        }

        // POST: Insurance/Buy
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Buy(int rentalId, int customerId, InsuranceTier tier)
        {
            try
            {
                var policy = _insuranceService.Purchase(rentalId, customerId, tier);
                return RedirectToAction("Policy", new
                {
                    id = policy.Id,
                    message = $"Insurance policy #{policy.Id} purchased — {tier} tier for ${policy.Premium:F2}."
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                return RedirectToAction("Purchase", new { rentalId, message = ex.Message, error = true });
            }
        }

        // GET: Insurance/Policy/1
        public ActionResult Policy(int id, string message, bool? error)
        {
            var policy = _insuranceService.GetPolicy(id);
            if (policy == null)
                return HttpNotFound("Policy not found.");

            var claims = _insuranceService.GetClaimsForPolicy(id);

            var viewModel = new InsuranceViewModel
            {
                CurrentPolicy = policy,
                Claims = claims,
                Message = message,
                IsError = error ?? false
            };

            return View(viewModel);
        }

        // GET: Insurance/Customer/1
        public ActionResult Customer(int id)
        {
            var customer = _customerRepo.GetById(id);
            if (customer == null)
                return HttpNotFound("Customer not found.");

            var viewModel = new InsuranceViewModel
            {
                CustomerId = id,
                CustomerName = customer.Name,
                Policies = _insuranceService.GetCustomerPolicies(id),
                Claims = _insuranceService.GetCustomerClaims(id)
            };

            return View(viewModel);
        }

        // GET: Insurance/Claim?policyId=1
        public ActionResult Claim(int? policyId)
        {
            if (!policyId.HasValue)
                return RedirectToAction("Index");

            var policy = _insuranceService.GetPolicy(policyId.Value);
            if (policy == null)
                return HttpNotFound("Policy not found.");

            var viewModel = new InsuranceViewModel
            {
                CurrentPolicy = policy,
                PolicyId = policyId.Value
            };

            return View(viewModel);
        }

        // POST: Insurance/FileClaim
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult FileClaim(int policyId, ClaimType claimType, decimal amount)
        {
            try
            {
                var claim = _insuranceService.FileClaim(policyId, claimType, amount);
                return RedirectToAction("Policy", new
                {
                    id = claim.PolicyId,
                    message = $"Claim #{claim.Id} filed — ${claim.Amount:F2} for {claim.ClaimType}. Status: {claim.Status}."
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                return RedirectToAction("Claim", new { policyId, message = ex.Message, error = true });
            }
        }

        // POST: Insurance/DenyClaim
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DenyClaim(int claimId, string reason, int policyId)
        {
            try
            {
                var claim = _insuranceService.DenyClaim(claimId, reason);
                return RedirectToAction("Policy", new
                {
                    id = policyId,
                    message = $"Claim #{claim.Id} denied."
                });
            }
            catch (Exception ex)
            {
                return RedirectToAction("Policy", new
                {
                    id = policyId,
                    message = ex.Message,
                    error = true
                });
            }
        }

        // POST: Insurance/Cancel
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Cancel(int policyId)
        {
            try
            {
                var policy = _insuranceService.CancelPolicy(policyId);
                return RedirectToAction("Index", new
                {
                    message = $"Policy #{policy.Id} cancelled."
                });
            }
            catch (Exception ex)
            {
                return RedirectToAction("Policy", new
                {
                    id = policyId,
                    message = ex.Message,
                    error = true
                });
            }
        }
    }
}
