using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class RentalsController : Controller
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public RentalsController()
            : this(
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository())
        {
        }

        /// <summary>
        /// Constructor injection for testability and future DI container use.
        /// </summary>
        public RentalsController(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository)
        {
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        // GET: Rentals
        public ActionResult Index(string query, RentalStatus? status, string sortBy)
        {
            var allRentals = _rentalRepository.GetAll();
            var totalCount = allRentals.Count;

            IReadOnlyList<Rental> rentals;
            if (!string.IsNullOrWhiteSpace(query) || status.HasValue)
            {
                rentals = _rentalRepository.Search(query, status);
            }
            else
            {
                rentals = allRentals;
            }

            // Apply sorting
            var sort = string.IsNullOrWhiteSpace(sortBy) ? "RentalDate" : sortBy;
            IEnumerable<Rental> sorted;
            switch (sort.ToLowerInvariant())
            {
                case "customer":
                    sorted = rentals.OrderBy(r => r.CustomerName ?? "").ThenByDescending(r => r.RentalDate);
                    break;
                case "movie":
                    sorted = rentals.OrderBy(r => r.MovieName ?? "").ThenByDescending(r => r.RentalDate);
                    break;
                case "duedate":
                    sorted = rentals.OrderBy(r => r.DueDate).ThenBy(r => r.CustomerName);
                    break;
                case "status":
                    sorted = rentals.OrderBy(r => r.Status).ThenBy(r => r.DueDate);
                    break;
                case "totalcost":
                    sorted = rentals.OrderByDescending(r => r.TotalCost).ThenByDescending(r => r.RentalDate);
                    break;
                default: // "rentaldate"
                    sorted = rentals.OrderByDescending(r => r.RentalDate).ThenBy(r => r.CustomerName);
                    break;
            }

            var viewModel = new RentalSearchViewModel
            {
                Rentals = sorted.ToList(),
                Query = query,
                Status = status,
                SortBy = sort,
                TotalCount = totalCount,
                Stats = _rentalRepository.GetStats()
            };

            return View(viewModel);
        }

        // GET: Rentals/Details/5
        public ActionResult Details(int id)
        {
            var rental = _rentalRepository.GetById(id);

            if (rental == null)
                return HttpNotFound();

            return View(rental);
        }

        // GET: Rentals/Checkout
        public ActionResult Checkout()
        {
            var movies = _movieRepository.GetAll();
            var availableMovies = movies
                .Where(m => !_rentalRepository.IsMovieRentedOut(m.Id))
                .ToList();

            var viewModel = new RentalCheckoutViewModel
            {
                Rental = new Rental
                {
                    RentalDate = DateTime.Today,
                    DueDate = DateTime.Today.AddDays(InMemoryRentalRepository.DefaultRentalDays),
                    DailyRate = Services.PricingService.DefaultDailyRate
                },
                Customers = _customerRepository.GetAll(),
                AvailableMovies = availableMovies
            };

            return View(viewModel);
        }

        // POST: Rentals/Checkout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Checkout(RentalCheckoutViewModel viewModel)
        {
            if (viewModel?.Rental == null)
                return new HttpStatusCodeResult(400, "Invalid rental data.");

            var rental = viewModel.Rental;

            // Validate customer exists
            var customer = _customerRepository.GetById(rental.CustomerId);
            if (customer == null)
            {
                ModelState.AddModelError("Rental.CustomerId", "Selected customer does not exist.");
            }

            // Validate movie exists
            var movie = _movieRepository.GetById(rental.MovieId);
            if (movie == null)
            {
                ModelState.AddModelError("Rental.MovieId", "Selected movie does not exist.");
            }

            if (!ModelState.IsValid)
            {
                var movies = _movieRepository.GetAll();
                viewModel.Customers = _customerRepository.GetAll();
                viewModel.AvailableMovies = movies
                    .Where(m => !_rentalRepository.IsMovieRentedOut(m.Id))
                    .ToList();
                return View(viewModel);
            }

            // Populate resolved names
            rental.CustomerName = customer.Name;
            rental.MovieName = movie.Name;

            // Security: enforce server-side daily rate — never trust client-submitted pricing.
            // This prevents price manipulation attacks where a user modifies the form to
            // submit a lower DailyRate (e.g., $0.01 instead of $3.99).
            // Uses PricingService logic: new releases get premium rate ($5.99),
            // catalog titles (>1 year old) get discount ($2.99), per-movie overrides
            // take priority, and membership discounts are applied.
            var baseDailyRate = Services.PricingService.GetMovieDailyRate(movie);
            var benefits = Services.PricingService.GetBenefits(customer.MembershipType);
            var discountAmount = baseDailyRate * benefits.DiscountPercent / 100m;
            rental.DailyRate = baseDailyRate - discountAmount;

            // Security: enforce server-side rental period — prevent users from extending
            // due dates via form manipulation to avoid late fees.
            // Membership tiers grant extended rental periods (Silver +1, Gold +2, Platinum +3).
            var rentalDays = InMemoryRentalRepository.DefaultRentalDays + benefits.ExtendedRentalDays;
            rental.RentalDate = DateTime.Today;
            rental.DueDate = DateTime.Today.AddDays(rentalDays);

            // Apply coupon if provided
            var couponDiscount = 0m;
            if (!string.IsNullOrWhiteSpace(viewModel.CouponCode))
            {
                var couponService = new Services.CouponService();
                var subtotal = rental.DailyRate * (decimal)(rental.DueDate - rental.RentalDate).TotalDays;
                couponDiscount = couponService.Apply(viewModel.CouponCode, subtotal);
                if (couponDiscount > 0)
                {
                    // Apply coupon discount by reducing the effective daily rate
                    var rentalDaysCount = Math.Max(1, (decimal)(rental.DueDate - rental.RentalDate).TotalDays);
                    rental.DailyRate = Math.Max(0.01m, rental.DailyRate - (couponDiscount / rentalDaysCount));
                    rental.DailyRate = Math.Round(rental.DailyRate, 2);
                }
            }

            // Enforce membership tier concurrent rental limit (issue #44).
            // Check active rental count against MaxConcurrentRentals BEFORE
            // the atomic Checkout to prevent exceeding the tier's limit.
            var activeRentals = _rentalRepository.GetActiveByCustomer(customer.Id);
            if (activeRentals.Count >= benefits.MaxConcurrentRentals)
            {
                ModelState.AddModelError("Rental.CustomerId",
                    $"{customer.Name} has reached the maximum of {benefits.MaxConcurrentRentals} " +
                    $"concurrent rentals for the {customer.MembershipType} tier.");
                var movies = _movieRepository.GetAll();
                viewModel.Customers = _customerRepository.GetAll();
                viewModel.AvailableMovies = movies
                    .Where(m => !_rentalRepository.IsMovieRentedOut(m.Id))
                    .ToList();
                return View(viewModel);
            }

            // Use atomic Checkout to prevent TOCTOU race: availability check
            // and rental creation happen in a single lock acquisition
            try
            {
                _rentalRepository.Checkout(rental);
            }
            catch (InvalidOperationException)
            {
                ModelState.AddModelError("Rental.MovieId", "This movie is currently rented out.");
                var movies = _movieRepository.GetAll();
                viewModel.Customers = _customerRepository.GetAll();
                viewModel.AvailableMovies = movies
                    .Where(m => !_rentalRepository.IsMovieRentedOut(m.Id))
                    .ToList();
                return View(viewModel);
            }

            return RedirectToAction("Index");
        }

        // POST: Rentals/Return/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Return(int id)
        {
            try
            {
                var rental = _rentalRepository.ReturnRental(id);
                TempData["Message"] = rental.LateFee > 0
                    ? $"'{rental.MovieName}' returned with ${rental.LateFee:F2} late fee ({rental.DaysOverdue} day(s) overdue)."
                    : $"'{rental.MovieName}' returned successfully — no late fee!";
            }
            catch (KeyNotFoundException)
            {
                return HttpNotFound();
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index");
        }

        // POST: Rentals/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            try
            {
                _rentalRepository.Remove(id);
            }
            catch (KeyNotFoundException)
            {
                return HttpNotFound();
            }

            return RedirectToAction("Index");
        }

        // GET: Rentals/Overdue
        public ActionResult Overdue()
        {
            var overdueRentals = _rentalRepository.GetOverdue();
            return View(overdueRentals);
        }
    }
}
