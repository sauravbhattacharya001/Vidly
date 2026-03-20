using System;
using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    public interface IScreeningRoomRepository
    {
        IReadOnlyList<ScreeningRoom> GetAllRooms();
        ScreeningRoom GetRoomById(int id);
        IReadOnlyList<ScreeningBooking> GetAllBookings();
        IReadOnlyList<ScreeningBooking> GetBookingsByDate(DateTime date);
        IReadOnlyList<ScreeningBooking> GetBookingsByRoom(int roomId);
        IReadOnlyList<ScreeningBooking> GetBookingsByCustomer(int customerId);
        ScreeningBooking GetBookingById(int id);
        void AddBooking(ScreeningBooking booking);
        void CancelBooking(int id);
        bool IsSlotAvailable(int roomId, DateTime date, int startHour, int durationHours);
    }
}
