using System;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Controllers
{
    /// <summary>
    /// Generates printable return receipts for completed rentals.
    /// Accessible from rental details — provides a professional, print-friendly
    /// receipt that customers can keep for their records.
    /// </summary>
    public class ReceiptController : Controller
    {
        private readonly IRentalRepository _rentalRepository;

        public ReceiptController()
            : this(new InMemoryRentalRepository())
        {
        }

        public ReceiptController(IRentalRepository rentalRepository)
        {
            _rentalRepository = rentalRepository;
        }

        /// <summary>
        /// GET /Receipt/Return/5 — Shows a printable return receipt for rental #5.
        /// Only works for returned rentals.
        /// </summary>
        [HttpGet]
        public ActionResult Return(int id)
        {
            var rental = _rentalRepository.GetById(id);
            if (rental == null)
                return HttpNotFound("Rental not found.");

            if (rental.Status != RentalStatus.Returned || !rental.ReturnDate.HasValue)
            {
                TempData["Error"] = "Receipt is only available for returned rentals.";
                return RedirectToAction("Index", "Rentals");
            }

            var returnDate = rental.ReturnDate.Value;
            var rentalDays = Math.Max(1, (int)Math.Ceiling((returnDate - rental.RentalDate).TotalDays));
            var daysLate = returnDate > rental.DueDate
                ? (int)Math.Ceiling((returnDate - rental.DueDate).TotalDays)
                : 0;

            var receipt = new ReturnReceipt
            {
                ReceiptNumber = $"RR-{rental.Id:D6}",
                GeneratedAt = DateTime.Now,
                RentalId = rental.Id,
                CustomerName = rental.CustomerName ?? "Unknown Customer",
                MovieName = rental.MovieName ?? "Unknown Movie",
                RentalDate = rental.RentalDate,
                DueDate = rental.DueDate,
                ReturnDate = returnDate,
                DailyRate = rental.DailyRate,
                RentalDays = rentalDays,
                RentalCost = rentalDays * rental.DailyRate,
                DaysLate = daysLate,
                LateFee = rental.LateFee,
                TotalCharge = rental.TotalCost,
                WasLate = daysLate > 0
            };

            return View(receipt);
        }
    }
}
