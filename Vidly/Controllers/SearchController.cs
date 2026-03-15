using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Controllers
{
    /// <summary>
    /// Global search across movies, customers, and rentals.
    /// Accessible via the navbar search bar or /Search?q=term.
    /// </summary>
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

        // GET: Search?q=term
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
