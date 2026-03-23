using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Penalty Waiver System — staff can review overdue rentals and grant
    /// full or partial late-fee waivers with documented reasons.
    /// </summary>
    public class PenaltyWaiverController : Controller
    {
        private readonly IPenaltyWaiverRepository _waiverRepository;
        private readonly IRentalRepository _rentalRepository;

        public PenaltyWaiverController()
            : this(new InMemoryPenaltyWaiverRepository(), new InMemoryRentalRepository())
        {
        }

        public PenaltyWaiverController(
            IPenaltyWaiverRepository waiverRepository,
            IRentalRepository rentalRepository)
        {
            _waiverRepository = waiverRepository
                ?? throw new ArgumentNullException(nameof(waiverRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
        }

        /// <summary>
        /// GET /PenaltyWaiver — list all waivers with stats.
        /// </summary>
        public ActionResult Index(string message, bool? error)
        {
            var viewModel = new PenaltyWaiverIndexViewModel
            {
                Waivers = _waiverRepository.GetAll(),
                Stats = _waiverRepository.GetStats(),
                StatusMessage = message,
                IsError = error ?? false
            };
            return View(viewModel);
        }

        /// <summary>
        /// GET /PenaltyWaiver/Create?rentalId=5 — waiver form for a specific rental.
        /// </summary>
        public ActionResult Create(int? rentalId)
        {
            if (!rentalId.HasValue)
                return RedirectToAction("Index", new { message = "Please specify a rental.", error = true });

            var rental = _rentalRepository.GetById(rentalId.Value);
            if (rental == null)
                return HttpNotFound("Rental not found.");

            if (rental.LateFee <= 0)
                return RedirectToAction("Index", new { message = "This rental has no late fees to waive.", error = true });

            var alreadyWaived = _waiverRepository.GetTotalWaivedForRental(rentalId.Value);
            if (alreadyWaived >= rental.LateFee)
                return RedirectToAction("Index", new { message = "Late fee has already been fully waived.", error = true });

            var viewModel = new PenaltyWaiverCreateViewModel
            {
                Rental = rental,
                AlreadyWaived = alreadyWaived,
                Waiver = new PenaltyWaiver
                {
                    RentalId = rental.Id,
                    OriginalLateFee = rental.LateFee,
                    Type = WaiverType.Full,
                    AmountWaived = rental.LateFee - alreadyWaived
                }
            };
            return View(viewModel);
        }

        /// <summary>
        /// POST /PenaltyWaiver/Create — submit the waiver.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(PenaltyWaiver waiver)
        {
            var rental = _rentalRepository.GetById(waiver.RentalId);
            if (rental == null)
                return HttpNotFound("Rental not found.");

            if (rental.LateFee <= 0)
            {
                ModelState.AddModelError("", "This rental has no late fees to waive.");
            }

            var alreadyWaived = _waiverRepository.GetTotalWaivedForRental(waiver.RentalId);
            var maxWaivable = rental.LateFee - alreadyWaived;

            if (waiver.AmountWaived > maxWaivable)
            {
                ModelState.AddModelError("AmountWaived",
                    $"Cannot waive more than {maxWaivable:C}. Already waived: {alreadyWaived:C}.");
            }

            if (string.IsNullOrWhiteSpace(waiver.Reason))
                ModelState.AddModelError("Reason", "Please provide a reason for the waiver.");

            if (!ModelState.IsValid)
            {
                var viewModel = new PenaltyWaiverCreateViewModel
                {
                    Rental = rental,
                    AlreadyWaived = alreadyWaived,
                    Waiver = waiver
                };
                return View(viewModel);
            }

            // Populate resolved fields
            waiver.CustomerName = rental.CustomerName;
            waiver.MovieName = rental.MovieName;
            waiver.OriginalLateFee = rental.LateFee;
            waiver.GrantedDate = DateTime.Today;
            waiver.ApprovedBy = "Staff"; // placeholder

            // Auto-detect waiver type based on amount
            if (waiver.AmountWaived >= maxWaivable)
                waiver.Type = WaiverType.Full;
            else if (waiver.Type == WaiverType.Full)
                waiver.Type = WaiverType.Partial;

            _waiverRepository.Add(waiver);

            return RedirectToAction("Index", new
            {
                message = $"Waived {waiver.AmountWaived:C} for '{rental.MovieName}' (rented by {rental.CustomerName})."
            });
        }

        /// <summary>
        /// GET /PenaltyWaiver/Eligible — list overdue rentals with late fees eligible for waivers.
        /// </summary>
        public ActionResult Eligible()
        {
            var overdueRentals = _rentalRepository.GetOverdue()
                .Where(r => r.LateFee > 0)
                .Where(r => _waiverRepository.GetTotalWaivedForRental(r.Id) < r.LateFee)
                .ToList();

            // Also include returned rentals that still have un-waived late fees
            var allRentals = _rentalRepository.GetAll();
            var returnedWithFees = allRentals
                .Where(r => r.Status == RentalStatus.Returned && r.LateFee > 0)
                .Where(r => _waiverRepository.GetTotalWaivedForRental(r.Id) < r.LateFee)
                .ToList();

            var eligible = overdueRentals
                .Union(returnedWithFees, new RentalIdComparer())
                .OrderByDescending(r => r.LateFee)
                .ToList();

            return View(eligible);
        }

        private class RentalIdComparer : System.Collections.Generic.IEqualityComparer<Rental>
        {
            public bool Equals(Rental x, Rental y) => x?.Id == y?.Id;
            public int GetHashCode(Rental obj) => obj?.Id.GetHashCode() ?? 0;
        }
    }
}
