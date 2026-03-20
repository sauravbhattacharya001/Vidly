using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    public class InMemoryScreeningRoomRepository : IScreeningRoomRepository
    {
        private static readonly List<ScreeningRoom> _rooms;
        private static readonly List<ScreeningBooking> _bookings;
        private static int _nextBookingId;

        static InMemoryScreeningRoomRepository()
        {
            _rooms = new List<ScreeningRoom>
            {
                new ScreeningRoom { Id = 1, Name = "Theater A – The Grand", Capacity = 30, HourlyRate = 49.99m, HasSurroundSound = true, Has4KProjector = true },
                new ScreeningRoom { Id = 2, Name = "Theater B – Cozy Corner", Capacity = 10, HourlyRate = 24.99m, HasSurroundSound = true, Has4KProjector = false },
                new ScreeningRoom { Id = 3, Name = "Theater C – Premiere Suite", Capacity = 20, HourlyRate = 39.99m, HasSurroundSound = true, Has4KProjector = true },
                new ScreeningRoom { Id = 4, Name = "Mini Room – The Nook", Capacity = 4, HourlyRate = 14.99m, HasSurroundSound = false, Has4KProjector = false },
            };

            var today = DateTime.Today;
            _bookings = new List<ScreeningBooking>
            {
                new ScreeningBooking
                {
                    Id = 1, RoomId = 1, RoomName = "Theater A – The Grand",
                    CustomerId = 1, CustomerName = "Alice Johnson",
                    MovieId = 1, MovieName = "The Dark Knight",
                    Date = today, StartHour = 14, DurationHours = 3, GuestCount = 12,
                    TotalCost = 149.97m, Status = BookingStatus.Confirmed,
                    Notes = "Birthday party screening"
                },
                new ScreeningBooking
                {
                    Id = 2, RoomId = 2, RoomName = "Theater B – Cozy Corner",
                    CustomerId = 2, CustomerName = "Bob Smith",
                    MovieId = 2, MovieName = "Inception",
                    Date = today.AddDays(1), StartHour = 19, DurationHours = 2, GuestCount = 6,
                    TotalCost = 49.98m, Status = BookingStatus.Confirmed,
                    Notes = "Date night"
                },
                new ScreeningBooking
                {
                    Id = 3, RoomId = 3, RoomName = "Theater C – Premiere Suite",
                    CustomerId = 3, CustomerName = "Carol Davis",
                    Date = today.AddDays(-1), StartHour = 10, DurationHours = 4, GuestCount = 15,
                    TotalCost = 159.96m, Status = BookingStatus.Completed,
                    Notes = "Team building event"
                },
            };
            _nextBookingId = 4;
        }

        public IReadOnlyList<ScreeningRoom> GetAllRooms() => _rooms.Where(r => r.IsActive).ToList();

        public ScreeningRoom GetRoomById(int id) => _rooms.FirstOrDefault(r => r.Id == id);

        public IReadOnlyList<ScreeningBooking> GetAllBookings() => _bookings.OrderByDescending(b => b.Date).ThenBy(b => b.StartHour).ToList();

        public IReadOnlyList<ScreeningBooking> GetBookingsByDate(DateTime date) =>
            _bookings.Where(b => b.Date.Date == date.Date).OrderBy(b => b.StartHour).ToList();

        public IReadOnlyList<ScreeningBooking> GetBookingsByRoom(int roomId) =>
            _bookings.Where(b => b.RoomId == roomId).OrderByDescending(b => b.Date).ToList();

        public IReadOnlyList<ScreeningBooking> GetBookingsByCustomer(int customerId) =>
            _bookings.Where(b => b.CustomerId == customerId).OrderByDescending(b => b.Date).ToList();

        public ScreeningBooking GetBookingById(int id) => _bookings.FirstOrDefault(b => b.Id == id);

        public void AddBooking(ScreeningBooking booking)
        {
            booking.Id = _nextBookingId++;
            booking.CreatedAt = DateTime.Now;
            _bookings.Add(booking);
        }

        public void CancelBooking(int id)
        {
            var booking = _bookings.FirstOrDefault(b => b.Id == id);
            if (booking != null)
                booking.Status = BookingStatus.Cancelled;
        }

        public bool IsSlotAvailable(int roomId, DateTime date, int startHour, int durationHours)
        {
            int endHour = startHour + durationHours;
            return !_bookings.Any(b =>
                b.RoomId == roomId &&
                b.Date.Date == date.Date &&
                b.Status != BookingStatus.Cancelled &&
                b.StartHour < endHour &&
                b.EndHour > startHour);
        }
    }
}
