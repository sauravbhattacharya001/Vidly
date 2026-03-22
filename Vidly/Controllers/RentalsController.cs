using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Filters;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.Utilities;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class RentalsController : Controller
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly CouponService _couponService;

        private static readonly SortHelper<Rental> _sorter = new SortHelper<Rental>(
            "rentaldate",
            new Dictionary<string, SortColumn<Rental>>
            {
                ["rentaldate"] = new SortColumn<Rental>(r => r.RentalDate, descending: true, thenBy: r => r.CustomerName ?? ""),
                ["customer"]   = new SortColumn<Rental>(r => r.CustomerName ?? "", thenBy: r => r.RentalDate),
                ["movie"]      = new SortColumn<Rental>(r => r.MovieName ?? "", thenBy: r => r.RentalDate),
                ["duedate"]    = new SortColumn<Rental>(r => r.DueDate, thenBy: r => r.CustomerName ?? ""),
                ["status"]     = new SortColumn<Rental>(r => r.Status, thenBy: r => r.DueDate),
                ["totalcost"]  = new SortColumn<Rental>(r => r.TotalCost, descending: true, thenBy: r => r.RentalDate),
            });

        /// <summary>
        /// Parameterless constructor for ASP.NET MVC default controller factory.
        /// </summary>
        public RentalsController()
            : this(
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository(),
                new InMemoryCouponRepository())
        {
        }

        /// <summary>
        /// Constructor injection for testability and future DI container use.
        /// </summary>
        public RentalsController(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            ICouponRepository couponRepository)
        {
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _couponService = new CouponService(couponRepository
                ?? throw new ArgumentNullException(nameof(couponRepository)));
        }

        /// <summary>
        /// GET: Rentals — Lists all rentals with optional search, status filter,
        /// and configurable sort order. Includes aggregate statistics.
        /// </summary>
        /// <param name="query">Case-insensitive search on customer or movie name.</param>
        /// <param name="status">Optional rental status filter.</param>
        /// <param name="sortBy">Sort column key (rentaldate, customer, movie, duedate, status, totalcost).</param>
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

            // Apply sorting via declarative SortHelper (replaces switch block)
            var sort = _sorter.ResolveKey(sortBy);

            var viewModel = new RentalSearchViewModel
            {
                Rentals = _sorter.Apply(rentals, sort),
                Query = query,
                Status = status,
                SortBy = sort,
                TotalCount = totalCount,
                Stats = _rentalRepository.GetStats()
            };

            return View(viewModel);
        }

        /// <summary>
        /// GET: Rentals/Details/{id} — Shows full details for a single rental.
        /// Returns 404 if the rental does not exist.
        /// </summary>
        /// <param name="id">The rental identifier.</param>
        public ActionResult Details(int id)
        {
            var rental = _rentalRepository.GetById(id);

            if (rental == null)
                return HttpNotFound();

            return View(rental);
        }

        /// <summary>
        /// GET: Rentals/Checkout — Renders the checkout form with available movies
        /// (excludes currently rented-out titles) and all customers.
        /// </summary>
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

        /// <summary>
        /// POST: Rentals/Checkout — Validates and creates a new rental.
        /// Enforces server-side pricing, rental periods, concurrent rental limits,
        /// and movie availability via atomic checkout to prevent TOCTOU races.
        /// Rate-limited to 15 requests per minute per IP.
        /// </summary>
        /// <param name="viewModel">The checkout form data including rental details and optional coupon.</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RateLimit(MaxRequests = 15, WindowSeconds = 60,
            Message = "Too many checkout attempts. Please wait and try again.")]
        public ActionResult Checkout(RentalCheckoutViewModel viewModel)
        {
            if (viewModel?.Rental == null)
                return new HttpStatusCodeResult(400, "Invalid rental data.");

            var rental = viewModel.Rental;

            // Security: reset server-controlled fields to prevent over-posting.
            // An attacker could POST Rental.Status=Returned, Rental.LateFee=-100,
            // or Rental.ReturnDate to manipulate rental state (CWE-915).
            rental.Status = RentalStatus.Active;
            rental.LateFee = 0;
            rental.ReturnDate = null;

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
            var baseDailyRate = Services.PricingService.GetMovieDailyRate(movie, DateTime.Today);
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
                var subtotal = rental.DailyRate * (decimal)(rental.DueDate - rental.RentalDate).TotalDays;
                couponDiscount = _couponService.Apply(viewModel.CouponCode, subtotal);
                if (couponDiscount > 0)
                {
                    // Apply coupon discount by reducing the effective daily rate
                    var rentalDaysCount = Math.Max(1, (decimal)(rental.DueDate - rental.RentalDate).TotalDays);
                    rental.DailyRate = Math.Max(0.01m, rental.DailyRate - (couponDiscount / rentalDaysCount));
                    rental.DailyRate = Math.Round(rental.DailyRate, 2);
                }
            }

            // Use atomic Checkout to prevent TOCTOU race: availability check,
            // concurrent rental limit check, and rental creation happen in a
            // single lock acquisition
            try
            {
                _rentalRepository.Checkout(rental, benefits.MaxConcurrentRentals);
            }
            catch (InvalidOperationException ex)
            {
                var errorField = ex.Message.Contains("concurrent rental limit")
                    ? "Rental.CustomerId"
                    : "Rental.MovieId";
                ModelState.AddModelError(errorField, ex.Message);
                var movies = _movieRepository.GetAll();
                viewModel.Customers = _customerRepository.GetAll();
                viewModel.AvailableMovies = movies
                    .Where(m => !_rentalRepository.IsMovieRentedOut(m.Id))
                    .ToList();
                return View(viewModel);
            }

            return RedirectToAction("Index");
        }

        /// <summary>
        /// POST: Rentals/Return/{id} — Marks a rental as returned, calculates
        /// late fees based on membership tier grace periods and fee caps.
        /// Sets a TempData message indicating the return result.
        /// </summary>
        /// <param name="id">The rental identifier.</param>
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

        /// <summary>
        /// POST: Rentals/Delete/{id} — Permanently removes a rental record.
        /// Returns 404 if the rental does not exist.
        /// </summary>
        /// <param name="id">The rental identifier.</param>
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

        /// <summary>
        /// GET: Rentals/Receipt/{id} — Displays a printable receipt for a rental.
        /// Returns 404 if the rental does not exist.
        /// </summary>
        /// <param name="id">The rental identifier.</param>
        public ActionResult Receipt(int id)
        {
            var rental = _rentalRepository.GetById(id);

            if (rental == null)
                return HttpNotFound();

            return View(rental);
        }

        /// <summary>
        /// GET: Rentals/Overdue — Lists all active rentals that are past their due date.
        /// </summary>
        public ActionResult Overdue()
        {
            var overdueRentals = _rentalRepository.GetOverdue();
            return View(overdueRentals);
        }

        /// <summary>
        /// GET: Rentals/Extend/{id} — Renders the rental extension form.
        /// A rental can only be extended once and must be active (not returned).
        /// </summary>
        /// <param name="id">The rental identifier.</param>
        public ActionResult Extend(int id)
        {
            var rental = _rentalRepository.GetById(id);

            if (rental == null)
                return HttpNotFound();

            if (rental.Status == RentalStatus.Returned)
            {
                TempData["Error"] = "Cannot extend a returned rental.";
                return RedirectToAction("Index");
            }

            if (_rentalRepository.IsExtended(id))
            {
                TempData["Error"] = "This rental has already been extended.";
                return RedirectToAction("Index");
            }

            var viewModel = new RentalExtendViewModel
            {
                Rental = rental,
                ExtensionDays = 3,
                ExtensionFeePerDay = Math.Round(rental.DailyRate * 0.5m, 2),
                IsAlreadyExtended = false
            };

            return View(viewModel);
        }

        /// <summary>
        /// POST: Rentals/Extend/{id} — Extends the rental's due date by 1–7 days.
        /// Charges an extension fee at half the daily rate per extension day.
        /// A rental can only be extended once.
        /// </summary>
        /// <param name="id">The rental identifier.</param>
        /// <param name="viewModel">Extension form data including the number of days.</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Extend(int id, RentalExtendViewModel viewModel)
        {
            if (viewModel == null || viewModel.ExtensionDays < 1 || viewModel.ExtensionDays > 7)
            {
                ModelState.AddModelError("ExtensionDays", "Extension must be between 1 and 7 days.");
            }

            if (!ModelState.IsValid)
            {
                var rental = _rentalRepository.GetById(id);
                if (rental == null)
                    return HttpNotFound();

                viewModel.Rental = rental;
                viewModel.ExtensionFeePerDay = Math.Round(rental.DailyRate * 0.5m, 2);
                viewModel.IsAlreadyExtended = _rentalRepository.IsExtended(id);
                return View(viewModel);
            }

            try
            {
                var updated = _rentalRepository.ExtendRental(id, viewModel.ExtensionDays);
                var fee = Math.Round(updated.DailyRate * 0.5m * viewModel.ExtensionDays, 2);
                TempData["Message"] = $"'{updated.MovieName}' extended by {viewModel.ExtensionDays} day(s). New due date: {updated.DueDate:MMM dd, yyyy}. Extension fee: ${fee:F2}.";
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
    }
}
