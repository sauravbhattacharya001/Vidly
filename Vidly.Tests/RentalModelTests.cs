using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Tests
{
    [TestClass]
    public class RentalModelTests
    {
        [TestMethod]
        public void TotalCost_ActiveRental_CalculatesFromToday()
        {
            var rental = new Rental
            {
                RentalDate = DateTime.Today.AddDays(-5),
                DueDate = DateTime.Today.AddDays(2),
                DailyRate = 3.99m,
                LateFee = 0,
                Status = RentalStatus.Active
            };

            // 5 days * 3.99
            Assert.AreEqual(5 * 3.99m, rental.TotalCost);
        }

        [TestMethod]
        public void TotalCost_ReturnedRental_UsesReturnDate()
        {
            var rental = new Rental
            {
                RentalDate = DateTime.Today.AddDays(-10),
                DueDate = DateTime.Today.AddDays(-3),
                ReturnDate = DateTime.Today.AddDays(-2),
                DailyRate = 3.99m,
                LateFee = 1.50m,
                Status = RentalStatus.Returned
            };

            // 8 days * 3.99 + 1.50 late fee
            Assert.AreEqual((8 * 3.99m) + 1.50m, rental.TotalCost);
        }

        [TestMethod]
        public void TotalCost_MinimumOneDay()
        {
            var rental = new Rental
            {
                RentalDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(7),
                ReturnDate = DateTime.Today,
                DailyRate = 5.00m,
                LateFee = 0,
                Status = RentalStatus.Returned
            };

            // Minimum 1 day
            Assert.AreEqual(5.00m, rental.TotalCost);
        }

        [TestMethod]
        public void DaysOverdue_NotOverdue_ReturnsZero()
        {
            var rental = new Rental
            {
                RentalDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(7),
                Status = RentalStatus.Active
            };

            Assert.AreEqual(0, rental.DaysOverdue);
        }

        [TestMethod]
        public void DaysOverdue_Overdue_ReturnsCorrectDays()
        {
            var rental = new Rental
            {
                RentalDate = DateTime.Today.AddDays(-10),
                DueDate = DateTime.Today.AddDays(-3),
                Status = RentalStatus.Active
            };

            Assert.AreEqual(3, rental.DaysOverdue);
        }

        [TestMethod]
        public void DaysOverdue_ReturnedLate_ReturnsCorrectDays()
        {
            var rental = new Rental
            {
                RentalDate = DateTime.Today.AddDays(-10),
                DueDate = DateTime.Today.AddDays(-5),
                ReturnDate = DateTime.Today.AddDays(-3),
                Status = RentalStatus.Returned
            };

            // Returned 2 days after due
            Assert.AreEqual(2, rental.DaysOverdue);
        }

        [TestMethod]
        public void DaysOverdue_ReturnedOnTime_ReturnsZero()
        {
            var rental = new Rental
            {
                RentalDate = DateTime.Today.AddDays(-5),
                DueDate = DateTime.Today,
                ReturnDate = DateTime.Today,
                Status = RentalStatus.Returned
            };

            Assert.AreEqual(0, rental.DaysOverdue);
        }

        [TestMethod]
        public void IsOverdue_ActiveNotPastDue_ReturnsFalse()
        {
            var rental = new Rental
            {
                DueDate = DateTime.Today.AddDays(1),
                Status = RentalStatus.Active
            };

            Assert.IsFalse(rental.IsOverdue);
        }

        [TestMethod]
        public void IsOverdue_ActivePastDue_ReturnsTrue()
        {
            var rental = new Rental
            {
                DueDate = DateTime.Today.AddDays(-1),
                Status = RentalStatus.Active
            };

            Assert.IsTrue(rental.IsOverdue);
        }

        [TestMethod]
        public void IsOverdue_Returned_AlwaysFalse()
        {
            var rental = new Rental
            {
                DueDate = DateTime.Today.AddDays(-5),
                Status = RentalStatus.Returned
            };

            Assert.IsFalse(rental.IsOverdue);
        }
    }
}
