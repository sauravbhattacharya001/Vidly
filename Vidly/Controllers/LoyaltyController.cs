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
    /// Loyalty points dashboard — view balances, transaction history,
    /// redeem rewards, and see the store-wide leaderboard.
    /// </summary>
    public class LoyaltyController : Controller
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly LoyaltyPointsService _loyaltyService;

        public LoyaltyController()
            : this(new InMemoryCustomerRepository(),
                   new InMemoryRentalRepository(),
                   new InMemoryMovieRepository()) { }

        public LoyaltyController(
            ICustomerRepository customerRepository,
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository)
        {
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _loyaltyService = new LoyaltyPointsService(
                _customerRepository, _rentalRepository);
        }

        /// <summary>
        /// GET /Loyalty?customerId=1
        /// Main dashboard: points balance, tier info, rewards, history, leaderboard.
        /// </summary>
        public ActionResult Index(int? customerId)
        {
            var allCustomers = _customerRepository.GetAll();
            var leaderboard = _loyaltyService.GetLeaderboard(10);

            if (!customerId.HasValue)
            {
                return View(new LoyaltyViewModel
                {
                    AllCustomers = allCustomers,
                    Leaderboard = leaderboard,
                    StatusMessage = "Select a customer to view their loyalty dashboard."
                });
            }

            var customer = _customerRepository.GetById(customerId.Value);
            if (customer == null)
            {
                return View(new LoyaltyViewModel
                {
                    AllCustomers = allCustomers,
                    Leaderboard = leaderboard,
                    StatusMessage = "Customer not found."
                });
            }

            var summary = _loyaltyService.GetSummary(customerId.Value);
            var transactions = _loyaltyService.GetHistory(customerId.Value);

            // Calculate progress toward next reward
            var nextReward = summary.AvailableRewards
                .Where(r => !r.CanAfford)
                .OrderBy(r => r.PointsCost)
                .FirstOrDefault();

            int nextRewardProgress = 100;
            string nextRewardName = null;
            int pointsToNext = 0;

            if (nextReward != null)
            {
                pointsToNext = nextReward.PointsNeeded;
                nextRewardName = nextReward.Description;
                nextRewardProgress = nextReward.PointsCost > 0
                    ? (int)Math.Floor((double)summary.CurrentBalance / nextReward.PointsCost * 100)
                    : 100;
                nextRewardProgress = Math.Min(nextRewardProgress, 100);
            }

            return View(new LoyaltyViewModel
            {
                AllCustomers = allCustomers,
                SelectedCustomer = customer,
                Summary = summary,
                Transactions = transactions,
                Leaderboard = leaderboard,
                NextRewardProgress = nextRewardProgress,
                NextRewardName = nextRewardName,
                PointsToNextReward = pointsToNext
            });
        }

        /// <summary>
        /// POST /Loyalty/Redeem
        /// Redeem a reward for the selected customer.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Redeem(int customerId, int rewardType)
        {
            try
            {
                var reward = (RewardType)rewardType;
                var tx = _loyaltyService.RedeemPoints(customerId, reward);

                TempData["RedemptionSuccess"] = true;
                TempData["RedeemedReward"] = LoyaltyPointsService.GetRewardDescription(reward);
            }
            catch (InvalidOperationException ex)
            {
                TempData["RedemptionError"] = ex.Message;
            }
            catch (ArgumentException ex)
            {
                TempData["RedemptionError"] = ex.Message;
            }

            return RedirectToAction("Index", new { customerId });
        }

        /// <summary>
        /// POST /Loyalty/EarnPoints
        /// Award points for a completed rental (admin action).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EarnPoints(int customerId, int rentalId)
        {
            try
            {
                _loyaltyService.EarnPointsForRental(rentalId);
                TempData["EarnSuccess"] = true;
            }
            catch (Exception ex)
            {
                TempData["EarnError"] = ex.Message;
            }

            return RedirectToAction("Index", new { customerId });
        }

        /// <summary>
        /// GET /Loyalty/Leaderboard
        /// Standalone leaderboard view.
        /// </summary>
        public ActionResult Leaderboard()
        {
            var leaderboard = _loyaltyService.GetLeaderboard(25);
            return View(new LoyaltyViewModel
            {
                Leaderboard = leaderboard,
                AllCustomers = _customerRepository.GetAll()
            });
        }
    }
}
