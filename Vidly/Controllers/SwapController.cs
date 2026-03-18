using System;
using System.Web.Mvc;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    public class SwapController : Controller
    {
        private readonly RentalSwapService _swapService;
        private readonly IRentalRepository _rentalRepository;

        public SwapController()
            : this(
                new InMemoryRentalRepository(),
                new InMemoryMovieRepository(),
                new InMemoryCustomerRepository())
        {
        }

        public SwapController(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository)
        {
            _rentalRepository = rentalRepository ?? throw new ArgumentNullException(nameof(rentalRepository));
            _swapService = new RentalSwapService(rentalRepository, movieRepository, customerRepository);
        }

        // GET: Swap?rentalId=1
        public ActionResult Index(int? rentalId, int? newMovieId, string message, bool? error)
        {
            if (!rentalId.HasValue)
                return RedirectToAction("Index", "Rentals");

            var rental = _rentalRepository.GetById(rentalId.Value);
            if (rental == null)
                return HttpNotFound("Rental not found.");

            var viewModel = new SwapViewModel
            {
                CurrentRental = rental,
                AvailableMovies = _swapService.GetSwapCandidates(rentalId.Value),
                AlreadySwapped = _swapService.IsSwappedRental(rentalId.Value),
                SwapHistory = _swapService.GetCustomerSwapHistory(rental.CustomerId),
                NewMovieId = newMovieId,
                Message = message,
                IsError = error ?? false
            };

            // If a new movie is selected, get a quote
            if (newMovieId.HasValue && !viewModel.AlreadySwapped)
            {
                try
                {
                    viewModel.Quote = _swapService.GetQuote(rentalId.Value, newMovieId.Value);
                }
                catch (Exception ex)
                {
                    viewModel.Message = ex.Message;
                    viewModel.IsError = true;
                }
            }

            return View(viewModel);
        }

        // POST: Swap/Execute
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Execute(int rentalId, int newMovieId)
        {
            var result = _swapService.ExecuteSwap(rentalId, newMovieId);

            if (!result.Success)
            {
                return RedirectToAction("Index", new
                {
                    rentalId,
                    message = result.Error,
                    error = true
                });
            }

            var totalCost = result.SwapFee + result.RateDifference;
            var msg = $"Swapped to \"{result.NewRental.MovieName}\" — {result.RemainingDays} days remaining. "
                    + $"Swap fee: ${result.SwapFee:F2}"
                    + (result.RateDifference > 0 ? $" + rate upgrade: ${result.RateDifference:F2}" : "")
                    + $" = ${totalCost:F2} total.";

            return RedirectToAction("Index", new
            {
                rentalId = result.NewRental.Id,
                message = msg
            });
        }

        // GET: Swap/History
        public ActionResult History()
        {
            var viewModel = new SwapViewModel
            {
                Stats = _swapService.GetStats(),
                SwapHistory = _swapService.GetAllSwapHistory()
            };
            return View(viewModel);
        }
    }
}
