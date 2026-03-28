using System;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Controllers
{
    /// <summary>
    /// Screening Room Booking — customers book private screening rooms for
    /// movie watch parties, date nights, or team events.
    ///
    /// GET  /ScreeningRoom              — rooms &amp; bookings dashboard
    /// GET  /ScreeningRoom/Availability?roomId=1&amp;date=2026-03-20 — slot grid (JSON)
    /// POST /ScreeningRoom/Book         — create a booking
    /// POST /ScreeningRoom/Cancel/5     — cancel a booking
    /// </summary>
    public class ScreeningRoomController : Controller
    {
        private readonly IScreeningRoomRepository _repo;
        private readonly ICustomerRepository _customerRepo;
        private readonly IMovieRepository _movieRepo;

        public ScreeningRoomController()
        {
            _repo = new InMemoryScreeningRoomRepository();
            _customerRepo = new InMemoryCustomerRepository();
            _movieRepo = new InMemoryMovieRepository();
        }

        internal ScreeningRoomController(
            IScreeningRoomRepository repo,
            ICustomerRepository customerRepo,
            IMovieRepository movieRepo)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _customerRepo = customerRepo ?? throw new ArgumentNullException(nameof(customerRepo));
            _movieRepo = movieRepo ?? throw new ArgumentNullException(nameof(movieRepo));
        }

        // GET: /ScreeningRoom
        public ActionResult Index()
        {
            ViewBag.Rooms = _repo.GetAllRooms();
            ViewBag.Bookings = _repo.GetAllBookings();
            ViewBag.Customers = _customerRepo.GetAll();
            ViewBag.Movies = _movieRepo.GetAll();
            return View();
        }

        // GET: /ScreeningRoom/Availability?roomId=1&date=2026-03-20
        public ActionResult Availability(int roomId, string date)
        {
            if (!DateTime.TryParse(date, out var parsedDate))
                return Json(new { error = "Invalid date" }, JsonRequestBehavior.AllowGet);

            var room = _repo.GetRoomById(roomId);
            if (room == null)
                return Json(new { error = "Room not found" }, JsonRequestBehavior.AllowGet);

            // Operating hours: 10 AM to 10 PM
            var slots = Enumerable.Range(10, 12).Select(hour => new
            {
                hour,
                label = DateTime.Today.AddHours(hour).ToString("h tt"),
                available = _repo.IsSlotAvailable(roomId, parsedDate, hour, 1)
            }).ToList();

            var bookings = _repo.GetBookingsByDate(parsedDate)
                .Where(b => b.RoomId == roomId && b.Status != BookingStatus.Cancelled)
                .Select(b => new
                {
                    b.Id,
                    b.CustomerName,
                    b.MovieName,
                    b.StartHour,
                    b.DurationHours,
                    b.GuestCount,
                    startLabel = DateTime.Today.AddHours(b.StartHour).ToString("h tt"),
                    endLabel = DateTime.Today.AddHours(b.EndHour).ToString("h tt")
                });

            return Json(new { room = room.Name, slots, bookings }, JsonRequestBehavior.AllowGet);
        }

        // POST: /ScreeningRoom/Book
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Book(int roomId, int customerId, int? movieId,
            string date, int startHour, int durationHours, int guestCount, string notes)
        {
            if (!DateTime.TryParse(date, out var parsedDate))
                return Json(new { success = false, error = "Invalid date." });

            if (parsedDate.Date < DateTime.Today)
                return Json(new { success = false, error = "Cannot book in the past." });

            if (startHour < 10 || startHour + durationHours > 22)
                return Json(new { success = false, error = "Bookings must be within 10 AM – 10 PM." });

            var room = _repo.GetRoomById(roomId);
            if (room == null)
                return Json(new { success = false, error = "Room not found." });

            if (guestCount > room.Capacity)
                return Json(new { success = false, error = $"Exceeds room capacity ({room.Capacity})." });

            if (!_repo.IsSlotAvailable(roomId, parsedDate, startHour, durationHours))
                return Json(new { success = false, error = "Time slot not available." });

            var customer = _customerRepo.GetAll().FirstOrDefault(c => c.Id == customerId);
            if (customer == null)
                return Json(new { success = false, error = "Customer not found." });

            string movieName = null;
            if (movieId.HasValue)
            {
                var movie = _movieRepo.GetAll().FirstOrDefault(m => m.Id == movieId.Value);
                movieName = movie?.Name;
            }

            var booking = new ScreeningBooking
            {
                RoomId = roomId,
                RoomName = room.Name,
                CustomerId = customerId,
                CustomerName = customer.Name,
                MovieId = movieId,
                MovieName = movieName ?? "Customer's choice",
                Date = parsedDate,
                StartHour = startHour,
                DurationHours = durationHours,
                GuestCount = guestCount,
                TotalCost = room.HourlyRate * durationHours,
                Notes = notes
            };

            _repo.AddBooking(booking);

            return Json(new
            {
                success = true,
                booking = new
                {
                    booking.Id,
                    booking.RoomName,
                    booking.CustomerName,
                    booking.MovieName,
                    booking.Date,
                    booking.StartHour,
                    booking.DurationHours,
                    booking.TotalCost
                }
            });
        }

        // POST: /ScreeningRoom/Cancel/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Cancel(int id)
        {
            var booking = _repo.GetBookingById(id);
            if (booking == null)
                return Json(new { success = false, error = "Booking not found." });

            if (booking.Status == BookingStatus.Cancelled)
                return Json(new { success = false, error = "Already cancelled." });

            _repo.CancelBooking(id);
            return Json(new { success = true });
        }
    }
}
