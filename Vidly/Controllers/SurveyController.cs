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
    public class SurveyController : Controller
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly RentalSurveyService _surveyService;

        public SurveyController()
            : this(new InMemoryCustomerRepository(),
                   new InMemoryRentalRepository(),
                   new InMemoryMovieRepository())
        {
        }

        public SurveyController(
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
            _surveyService = new RentalSurveyService(
                customerRepository, rentalRepository, movieRepository);
        }

        // GET: Survey
        public ActionResult Index()
        {
            // Seed sample surveys for demo
            SeedSampleData();

            var report = _surveyService.GenerateReport();
            var allSurveys = _surveyService.GetAll();

            var model = new SurveyViewModel
            {
                Report = report,
                RecentSurveys = allSurveys
                    .OrderByDescending(s => s.SubmittedAt)
                    .Take(10)
                    .ToList(),
                AtRiskCustomers = _surveyService.GetAtRiskCustomers(),
                Opportunities = _surveyService.GetImprovementOpportunities(),
                PendingInvitations = _surveyService.GetPendingInvitations(),
                Customers = _customerRepository.GetAll().ToList(),
                CompletedRentals = _rentalRepository.GetAll()
                    .Where(r => r.ReturnDate.HasValue)
                    .OrderByDescending(r => r.ReturnDate)
                    .Take(20)
                    .ToList()
            };

            return View(model);
        }

        // POST: Survey/Submit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Submit(int customerId, int rentalId, int npsScore,
            int overallSatisfaction, string comments, int wouldRentAgain,
            int? movieSelection, int? pricing, int? staffFriendliness,
            int? storeCleanliness, int? checkoutSpeed, int? returnProcess,
            int? discQuality, int? onlineExperience)
        {
            try
            {
                var categoryRatings = new Dictionary<SurveyCategory, int>();
                if (movieSelection.HasValue) categoryRatings[SurveyCategory.MovieSelection] = movieSelection.Value;
                if (pricing.HasValue) categoryRatings[SurveyCategory.Pricing] = pricing.Value;
                if (staffFriendliness.HasValue) categoryRatings[SurveyCategory.StaffFriendliness] = staffFriendliness.Value;
                if (storeCleanliness.HasValue) categoryRatings[SurveyCategory.StoreCleanliness] = storeCleanliness.Value;
                if (checkoutSpeed.HasValue) categoryRatings[SurveyCategory.CheckoutSpeed] = checkoutSpeed.Value;
                if (returnProcess.HasValue) categoryRatings[SurveyCategory.ReturnProcess] = returnProcess.Value;
                if (discQuality.HasValue) categoryRatings[SurveyCategory.DiscQuality] = discQuality.Value;
                if (onlineExperience.HasValue) categoryRatings[SurveyCategory.OnlineExperience] = onlineExperience.Value;

                _surveyService.Submit(customerId, rentalId, npsScore,
                    overallSatisfaction, categoryRatings, comments,
                    (RentAgainResponse)wouldRentAgain);

                TempData["Success"] = "Survey submitted successfully! Thank you for your feedback.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index");
        }

        // GET: Survey/Detail/5
        public ActionResult Detail(int id)
        {
            SeedSampleData();
            var survey = _surveyService.GetById(id);
            if (survey == null)
                return HttpNotFound("Survey not found.");

            var customer = _customerRepository.GetById(survey.CustomerId);
            var rental = _rentalRepository.GetById(survey.RentalId);
            var movie = rental != null ? _movieRepository.GetById(rental.MovieId) : null;

            var model = new SurveyDetailViewModel
            {
                Survey = survey,
                CustomerName = customer?.Name ?? "Unknown",
                MovieName = movie?.Name ?? rental?.MovieName ?? "Unknown"
            };

            return View(model);
        }

        // GET: Survey/Report
        public ActionResult Report()
        {
            SeedSampleData();
            var report = _surveyService.GenerateReport();
            return View(report);
        }

        private void SeedSampleData()
        {
            if (_surveyService.GetAll().Count > 0) return;

            var customers = _customerRepository.GetAll().ToList();
            var rentals = _rentalRepository.GetAll()
                .Where(r => r.ReturnDate.HasValue).ToList();
            if (customers.Count == 0 || rentals.Count == 0) return;

            var random = new Random(42);
            var seeded = 0;

            foreach (var rental in rentals.Take(15))
            {
                try
                {
                    var nps = random.Next(3, 11); // 3-10
                    var satisfaction = random.Next(2, 6); // 2-5
                    var cats = new Dictionary<SurveyCategory, int>();
                    foreach (SurveyCategory cat in Enum.GetValues(typeof(SurveyCategory)))
                    {
                        if (random.NextDouble() > 0.3)
                            cats[cat] = random.Next(2, 6);
                    }

                    var comments = new[]
                    {
                        "Great experience overall!",
                        "Could use more new releases.",
                        "Staff was very friendly.",
                        "Disc was scratched, disappointing.",
                        "Love the selection here.",
                        "Checkout was slow today.",
                        "Will definitely be back!",
                        null, null, null
                    };

                    var rentAgain = random.NextDouble() > 0.2
                        ? RentAgainResponse.Yes
                        : (random.NextDouble() > 0.5 ? RentAgainResponse.Maybe : RentAgainResponse.No);

                    _surveyService.Submit(rental.CustomerId, rental.Id,
                        nps, satisfaction, cats,
                        comments[random.Next(comments.Length)],
                        rentAgain);
                    seeded++;
                }
                catch { /* skip if validation fails */ }
            }
        }
    }
}
