using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Trivia Board — browse, submit, and like fun facts about movies.
    /// </summary>
    public class TriviaController : Controller
    {
        private readonly ITriviaRepository _triviaRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;

        public TriviaController()
            : this(
                new InMemoryTriviaRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository())
        {
        }

        public TriviaController(
            ITriviaRepository triviaRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository)
        {
            _triviaRepository = triviaRepository
                ?? throw new ArgumentNullException(nameof(triviaRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        /// <summary>
        /// GET /Trivia — main trivia board with optional movie/category filter.
        /// </summary>
        public ActionResult Index(int? movieId, string category)
        {
            var facts = _triviaRepository.GetAll();

            if (movieId.HasValue)
                facts = _triviaRepository.GetByMovieId(movieId.Value);
            else if (!string.IsNullOrWhiteSpace(category))
                facts = _triviaRepository.GetByCategory(category);

            var viewModel = new TriviaBoardViewModel
            {
                Facts = facts,
                Movies = _movieRepository.GetAll(),
                Categories = TriviaCategories.All,
                FilterMovieId = movieId,
                FilterCategory = category,
                RandomFact = _triviaRepository.GetRandom()
            };

            return View(viewModel);
        }

        /// <summary>
        /// POST /Trivia/Submit — add a new trivia fact.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Submit(int movieId, string fact, string category, string source, int customerId)
        {
            if (string.IsNullOrWhiteSpace(fact))
            {
                TempData["Error"] = "Trivia fact is required.";
                return RedirectToAction("Index");
            }

            if (fact.Length > 1000)
            {
                TempData["Error"] = "Trivia fact must be under 1000 characters.";
                return RedirectToAction("Index");
            }

            var movie = _movieRepository.GetById(movieId);
            var customer = _customerRepository.GetById(customerId);

            if (movie == null || customer == null)
            {
                TempData["Error"] = "Invalid movie or customer.";
                return RedirectToAction("Index");
            }

            if (string.IsNullOrWhiteSpace(category) || !TriviaCategories.All.Contains(category))
            {
                category = "Fun Fact";
            }

            _triviaRepository.Add(new TriviaFact
            {
                MovieId = movieId,
                MovieName = movie.Name,
                Fact = fact.Trim(),
                Category = category,
                Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim(),
                SubmittedByCustomerId = customerId,
                SubmittedByName = customer.Name
            });

            TempData["Success"] = "Trivia fact submitted! It will appear after verification.";
            return RedirectToAction("Index");
        }

        /// <summary>
        /// POST /Trivia/Like/5 — like a trivia fact.
        /// </summary>
        [HttpPost]
        public ActionResult Like(int id)
        {
            _triviaRepository.Like(id);
            return RedirectToAction("Index");
        }

        /// <summary>
        /// GET /Trivia/Random — returns JSON with a random trivia fact (for AJAX use).
        /// </summary>
        public ActionResult Random()
        {
            var fact = _triviaRepository.GetRandom();
            if (fact == null)
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);

            return Json(new
            {
                success = true,
                fact = fact.Fact,
                movie = fact.MovieName,
                category = fact.Category,
                likes = fact.Likes,
                verified = fact.IsVerified
            }, JsonRequestBehavior.AllowGet);
        }
    }
}
