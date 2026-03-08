using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Customer achievements: badge profiles, leaderboard, and store-wide stats.
    /// </summary>
    public class AchievementsController : Controller
    {
        private readonly AchievementService _achievementService;
        private readonly ICustomerRepository _customerRepository;

        public AchievementsController()
            : this(
                new InMemoryCustomerRepository(),
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new InMemoryReviewRepository())
        {
        }

        public AchievementsController(
            ICustomerRepository customerRepository,
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            IReviewRepository reviewRepository)
        {
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _achievementService = new AchievementService(
                customerRepository, rentalRepository, movieRepository, reviewRepository);
        }

        // GET: Achievements
        public ActionResult Index()
        {
            var stats = _achievementService.GetStats();
            var leaderboard = _achievementService.GetLeaderboard(10);
            var customers = _customerRepository.GetAll();

            ViewBag.Stats = stats;
            ViewBag.Leaderboard = leaderboard;
            ViewBag.Customers = customers;

            return View();
        }

        // GET: Achievements/Profile/5
        public ActionResult Profile(int id)
        {
            try
            {
                var profile = _achievementService.GetProfile(id);
                return View(profile);
            }
            catch (KeyNotFoundException)
            {
                return HttpNotFound("Customer not found.");
            }
        }

        // GET: Achievements/Leaderboard
        public ActionResult Leaderboard(int top = 20)
        {
            var leaderboard = _achievementService.GetLeaderboard(Math.Min(top, 50));
            return View(leaderboard);
        }

        // GET: Achievements/Badge/first_rental
        public ActionResult Badge(string id)
        {
            BadgeDefinition badge = null;
            foreach (var b in AchievementService.AllBadges)
            {
                if (b.Id == id)
                {
                    badge = b;
                    break;
                }
            }

            if (badge == null)
                return HttpNotFound("Badge not found.");

            var stats = _achievementService.GetStats();
            BadgeDistributionEntry distribution = null;
            foreach (var entry in stats.BadgeDistribution)
            {
                if (entry.Badge.Id == id)
                {
                    distribution = entry;
                    break;
                }
            }

            ViewBag.Badge = badge;
            ViewBag.Distribution = distribution;

            return View();
        }
    }
}
