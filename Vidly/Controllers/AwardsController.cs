using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Annual Vidly Awards ceremony — celebrates the best movies,
    /// customers, and genres of the year.
    /// </summary>
    public class AwardsController : Controller
    {
        private readonly AwardsService _awardsService;

        public AwardsController()
            : this(new AwardsService(
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryReviewRepository()))
        {
        }

        public AwardsController(AwardsService awardsService)
        {
            _awardsService = awardsService
                ?? throw new ArgumentNullException(nameof(awardsService));
        }

        // GET: Awards
        // GET: Awards?year=2025
        public ActionResult Index(int? year)
        {
            var selectedYear = year ?? DateTime.Now.Year;
            var ceremony = _awardsService.GetCeremony(selectedYear);
            return View(ceremony);
        }
    }
}
