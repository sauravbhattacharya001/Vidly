using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Movie Quotes Board — browse, submit, and vote on memorable movie lines.
    /// </summary>
    public class QuotesController : Controller
    {
        private readonly IMovieQuoteRepository _quoteRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private static readonly Random _random = new Random();

        public QuotesController()
            : this(
                new InMemoryMovieQuoteRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository())
        {
        }

        public QuotesController(
            IMovieQuoteRepository quoteRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository)
        {
            _quoteRepository = quoteRepository
                ?? throw new ArgumentNullException(nameof(quoteRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        /// <summary>
        /// GET /Quotes — main quotes board with optional movie filter.
        /// </summary>
        public ActionResult Index(int? movieId)
        {
            var quotes = movieId.HasValue
                ? _quoteRepository.GetByMovieId(movieId.Value)
                : _quoteRepository.GetAll();

            var allQuotes = _quoteRepository.GetAll().ToList();
            var randomQuote = allQuotes.Any()
                ? allQuotes[_random.Next(allQuotes.Count)]
                : null;

            var viewModel = new QuoteBoardViewModel
            {
                Quotes = quotes,
                Movies = _movieRepository.GetAll(),
                FilterMovieId = movieId,
                RandomQuote = randomQuote
            };

            return View(viewModel);
        }

        /// <summary>
        /// POST /Quotes/Submit — add a new quote.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Submit(int movieId, string text, string character, int customerId)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                TempData["Error"] = "Quote text is required.";
                return RedirectToAction("Index");
            }

            if (text.Length > 500)
            {
                TempData["Error"] = "Quote must be under 500 characters.";
                return RedirectToAction("Index");
            }

            var movie = _movieRepository.GetById(movieId);
            var customer = _customerRepository.GetById(customerId);

            if (movie == null || customer == null)
            {
                TempData["Error"] = "Invalid movie or customer.";
                return RedirectToAction("Index");
            }

            _quoteRepository.Add(new MovieQuote
            {
                MovieId = movieId,
                MovieName = movie.Name,
                Text = text.Trim(),
                Character = string.IsNullOrWhiteSpace(character) ? "Unknown" : character.Trim(),
                SubmittedByCustomerId = customerId,
                SubmittedByName = customer.Name
            });

            TempData["Success"] = "Quote submitted!";
            return RedirectToAction("Index");
        }

        /// <summary>
        /// POST /Quotes/Upvote/5 — vote on a quote.
        /// </summary>
        [HttpPost]
        public ActionResult Upvote(int id)
        {
            _quoteRepository.Upvote(id);
            return RedirectToAction("Index");
        }

        /// <summary>
        /// GET /Quotes/Random — returns JSON with a random quote (for AJAX widget use).
        /// </summary>
        public ActionResult Random()
        {
            var quotes = _quoteRepository.GetAll().ToList();
            if (!quotes.Any())
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);

            var quote = quotes[_random.Next(quotes.Count)];
            return Json(new
            {
                success = true,
                text = quote.Text,
                character = quote.Character,
                movie = quote.MovieName,
                votes = quote.Votes
            }, JsonRequestBehavior.AllowGet);
        }
    }
}
