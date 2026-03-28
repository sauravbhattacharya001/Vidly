using System;
using System.Web.Mvc;
using Vidly.Filters;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Controllers
{
    /// <summary>
    /// Global search across movies, customers, and rentals.
    /// Accessible via the navbar search bar or /Search?q=term.
    /// </summary>
    [RateLimit(MaxRequests = 20, WindowSeconds = 60,
        Message = "Too many search requests. Please wait before searching again.")]
    public class SearchController : Controller
    {
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IRentalRepository _rentalRepository;

        public SearchController()
            : this(new InMemoryMovieRepository(), new InMemoryCustomerRepository(), new InMemoryRentalRepository())
        {
        }

        public SearchController(
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IRentalRepository rentalRepository)
        {
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
        }

        /// <summary>
        /// GET: Search?q={term} — Performs a global search across movies,
        /// customers, and rentals. Returns empty results when no query is provided.
        /// </summary>
        /// <param name="q">The search query string.</param>
        public ActionResult Index(string q)
        {
            var results = new GlobalSearchResults { Query = q };

            if (!string.IsNullOrWhiteSpace(q))
            {
                results.Movies = _movieRepository.Search(q, null, null);
                results.Customers = _customerRepository.Search(q, null);
                results.Rentals = _rentalRepository.Search(q, null);
            }

            return View(results);
        }
    }
}
