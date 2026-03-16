using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests.Services
{
    [TestClass]
    public class RentalCalendarServiceTests
    {
        private InMemoryRentalRepository _rentalRepo;
        private InMemoryCustomerRepository _customerRepo;
        private RentalCalendarService _service;

        [TestInitialize]
        public void Setup()
        {
            _rentalRepo = new InMemoryRentalRepository();
            _customerRepo = new InMemoryCustomerRepository();
            _service = new RentalCalendarService(_rentalRepo, _customerRepo);
        }

        [TestMethod]
        public void GetCalendarMonth_ReturnsCorrectMonthName()
        {
            var result = _service.GetCalendarMonth(2026, 3);
            Assert.AreEqual("March 2026", result.MonthName);
        }

        [TestMethod]
        public void GetCalendarMonth_ReturnsCorrectDayCount()
        {
            var result = _service.GetCalendarMonth(2026, 3);
            Assert.AreEqual(31, result.Days.Count);
        }

        [TestMethod]
        public void GetCalendarMonth_February_Returns28Or29Days()
        {
            var result = _service.GetCalendarMonth(2024, 2); // leap year
            Assert.AreEqual(29, result.Days.Count);

            var result2 = _service.GetCalendarMonth(2025, 2);
            Assert.AreEqual(28, result2.Days.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetCalendarMonth_InvalidMonth_Throws()
        {
            _service.GetCalendarMonth(2026, 13);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetCalendarMonth_MonthZero_Throws()
        {
            _service.GetCalendarMonth(2026, 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetCalendarMonth_YearTooLow_Throws()
        {
            _service.GetCalendarMonth(1999, 1);
        }

        [TestMethod]
        public void GetCalendarMonth_HasNavigationDates()
        {
            var result = _service.GetCalendarMonth(2026, 6);
            Assert.AreEqual(new DateTime(2026, 5, 1), result.PreviousMonth);
            Assert.AreEqual(new DateTime(2026, 7, 1), result.NextMonth);
        }

        [TestMethod]
        public void GetCalendarMonth_JanuaryPreviousIsDecember()
        {
            var result = _service.GetCalendarMonth(2026, 1);
            Assert.AreEqual(2025, result.PreviousMonth.Year);
            Assert.AreEqual(12, result.PreviousMonth.Month);
        }

        [TestMethod]
        public void GetCalendarMonth_SetsFirstDayOfWeek()
        {
            // March 2026 starts on Sunday = 0
            var result = _service.GetCalendarMonth(2026, 3);
            Assert.AreEqual((int)new DateTime(2026, 3, 1).DayOfWeek, result.FirstDayOfWeek);
        }

        [TestMethod]
        public void GetCalendarMonth_MarksWeekends()
        {
            var result = _service.GetCalendarMonth(2026, 3);
            var saturday = result.Days.First(d => d.Date.DayOfWeek == DayOfWeek.Saturday);
            var sunday = result.Days.First(d => d.Date.DayOfWeek == DayOfWeek.Sunday);
            var monday = result.Days.First(d => d.Date.DayOfWeek == DayOfWeek.Monday);

            Assert.IsTrue(saturday.IsWeekend);
            Assert.IsTrue(sunday.IsWeekend);
            Assert.IsFalse(monday.IsWeekend);
        }

        [TestMethod]
        public void GetCalendarMonth_FiltersbyCustomer()
        {
            var all = _service.GetCalendarMonth(DateTime.Today.Year, DateTime.Today.Month);
            var filtered = _service.GetCalendarMonth(DateTime.Today.Year, DateTime.Today.Month, customerId: 1);

            // Filtered should have <= events than unfiltered
            var allEvents = all.Days.Sum(d => d.Events.Count);
            var filteredEvents = filtered.Days.Sum(d => d.Events.Count);
            Assert.IsTrue(filteredEvents <= allEvents);
        }

        [TestMethod]
        public void GetCalendarMonth_CustomerId_IsPreserved()
        {
            var result = _service.GetCalendarMonth(2026, 3, customerId: 5);
            Assert.AreEqual(5, result.CustomerId);
        }

        [TestMethod]
        public void GetCalendarMonth_NoCustomerFilter_CustomerIdIsNull()
        {
            var result = _service.GetCalendarMonth(2026, 3);
            Assert.IsNull(result.CustomerId);
        }

        [TestMethod]
        public void GetUpcomingEvents_ReturnsOrderedByDate()
        {
            var result = _service.GetUpcomingEvents(30);

            for (int i = 1; i < result.Count; i++)
            {
                Assert.IsTrue(result[i].Date >= result[i - 1].Date,
                    "Events should be sorted by date.");
            }
        }

        [TestMethod]
        public void GetUpcomingEvents_ClampsNegativeDays()
        {
            // Should not throw, treats as 1
            var result = _service.GetUpcomingEvents(-5);
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void GetUpcomingEvents_ClampsLargeDays()
        {
            var result = _service.GetUpcomingEvents(999);
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void CalendarEvent_HasCorrectTypes()
        {
            var today = DateTime.Today;
            var result = _service.GetCalendarMonth(today.Year, today.Month);
            var allEvents = result.Days.SelectMany(d => d.Events).ToList();

            foreach (var evt in allEvents)
            {
                Assert.IsTrue(
                    evt.Type == CalendarEventType.Checkout ||
                    evt.Type == CalendarEventType.DueDate ||
                    evt.Type == CalendarEventType.Return ||
                    evt.Type == CalendarEventType.Overdue,
                    "Event type should be a valid CalendarEventType.");
            }
        }

        [TestMethod]
        public void CalendarDay_HasEvents_ReflectsEventsList()
        {
            var today = DateTime.Today;
            var result = _service.GetCalendarMonth(today.Year, today.Month);

            foreach (var day in result.Days)
            {
                Assert.AreEqual(day.Events.Any(), day.HasEvents);
            }
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new RentalCalendarService(null, _customerRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new RentalCalendarService(_rentalRepo, null);
        }

        [TestMethod]
        public void GetCalendarMonth_EventLabelsAreNotEmpty()
        {
            var today = DateTime.Today;
            var result = _service.GetCalendarMonth(today.Year, today.Month);
            var events = result.Days.SelectMany(d => d.Events).ToList();

            foreach (var evt in events)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(evt.Label),
                    "Event label should not be empty.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(evt.MovieName),
                    "Movie name should not be empty.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(evt.CustomerName),
                    "Customer name should not be empty.");
            }
        }

        [TestMethod]
        public void GetCalendarMonth_TotalStatsAreNonNegative()
        {
            var result = _service.GetCalendarMonth(2026, 3);
            Assert.IsTrue(result.TotalCheckouts >= 0);
            Assert.IsTrue(result.TotalDueDates >= 0);
            Assert.IsTrue(result.TotalReturns >= 0);
            Assert.IsTrue(result.TotalOverdue >= 0);
        }
    }
}
