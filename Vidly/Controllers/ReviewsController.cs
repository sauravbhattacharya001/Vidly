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
    public class ReviewsController : Controller
    {
        private readonly IReviewRepository _reviewRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ReviewService _reviewService;

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public ReviewsController()
            : this(
                new InMemoryReviewRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryMovieRepository())
        {
        }

        /// <summary>
        /// Constructor injection for testability.
        /// </summary>
        public ReviewsController(
            IReviewRepository reviewRepository,
            ICustomerRepository customerRepository,
            IMovieRepository movieRepository)
        {
            _reviewRepository = reviewRepository
                ?? throw new ArgumentNullException(nameof(reviewRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _reviewService = new ReviewService(
                _reviewRepository, _customerRepository, _movieRepository);
        }

        // GET: Reviews
        // GET: Reviews?search=...&minStars=...
        public ActionResult Index(string search, int? minStars, string message, bool? error)
        {
            IReadOnlyList<Review> reviews;
            if (!string.IsNullOrWhiteSpace(search) || minStars.HasValue)
            {
                reviews = _reviewRepository.Search(search, minStars);
                // Enrich results with names
                foreach (var r in reviews)
                {
                    if (string.IsNullOrEmpty(r.CustomerName))
                    {
                        var cust = _customerRepository.GetById(r.CustomerId);
                        r.CustomerName = cust?.Name ?? "Unknown";
                    }
                    if (string.IsNullOrEmpty(r.MovieName))
                    {
                        var movie = _movieRepository.GetById(r.MovieId);
                        r.MovieName = movie?.Name ?? "Unknown";
                    }
                }
            }
            else
            {
                reviews = _reviewRepository.GetAll();
                foreach (var r in reviews)
                {
                    if (string.IsNullOrEmpty(r.CustomerName))
                    {
                        var cust = _customerRepository.GetById(r.CustomerId);
                        r.CustomerName = cust?.Name ?? "Unknown";
                    }
                    if (string.IsNullOrEmpty(r.MovieName))
                    {
                        var movie = _movieRepository.GetById(r.MovieId);
                        r.MovieName = movie?.Name ?? "Unknown";
                    }
                }
            }

            var viewModel = new ReviewIndexViewModel
            {
                Reviews = reviews,
                Summary = _reviewService.GetSummary(),
                TopRated = _reviewService.GetTopRated(5),
                SearchQuery = search,
                MinStars = minStars,
                Customers = _customerRepository.GetAll(),
                Movies = _movieRepository.GetAll(),
                StatusMessage = message,
                IsError = error ?? false,
            };

            return View(viewModel);
        }

        // GET: Reviews/Movie/5
        public ActionResult Movie(int id, string message, bool? error)
        {
            var movie = _movieRepository.GetById(id);
            if (movie == null)
                return HttpNotFound("Movie not found.");

            var reviews = _reviewService.GetMovieReviews(id);
            var stats = _reviewService.GetMovieStats(id);

            var viewModel = new ReviewIndexViewModel
            {
                Reviews = reviews,
                MovieStats = stats,
                SelectedMovie = movie,
                Customers = _customerRepository.GetAll(),
                Movies = _movieRepository.GetAll(),
                StatusMessage = message,
                IsError = error ?? false,
            };

            return View("Movie", viewModel);
        }

        // POST: Reviews/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(int customerId, int movieId, int stars, string reviewText)
        {
            try
            {
                if (stars < 1 || stars > 5)
                    return RedirectToAction("Index", new
                    {
                        message = "Rating must be between 1 and 5 stars.",
                        error = true
                    });

                _reviewService.SubmitReview(customerId, movieId, stars, reviewText);

                var movie = _movieRepository.GetById(movieId);
                return RedirectToAction("Index", new
                {
                    message = $"Review submitted for \"{movie?.Name ?? "movie"}\"!",
                });
            }
            catch (InvalidOperationException ex)
            {
                return RedirectToAction("Index", new
                {
                    message = ex.Message,
                    error = true,
                });
            }
            catch (ArgumentException ex)
            {
                return RedirectToAction("Index", new
                {
                    message = ex.Message,
                    error = true,
                });
            }
        }

        // POST: Reviews/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, string returnUrl)
        {
            bool deleted = _reviewService.DeleteReview(id);
            string message = deleted ? "Review deleted." : "Review not found.";

            if (!string.IsNullOrEmpty(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", new { message, error = !deleted });
        }
    }
}
