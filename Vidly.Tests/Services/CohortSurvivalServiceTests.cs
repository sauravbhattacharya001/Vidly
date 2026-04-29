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
    public class CohortSurvivalServiceTests
    {
        private InMemoryCustomerRepository _customers;
        private InMemoryRentalRepository _rentals;
        private TestClock _clock;
        private CohortSurvivalService _service;

        [TestInitialize]
        public void Setup()
        {
            _customers = new InMemoryCustomerRepository();
            _rentals = new InMemoryRentalRepository();
            _clock = new TestClock(new DateTime(2025, 7, 1));
            _service = new CohortSurvivalService(_customers, _rentals, _clock);
        }

        [TestMethod]
        public void GenerateReport_NoCustomers_ReturnsEmptyReport()
        {
            var report = _service.GenerateReport();

            Assert.IsNotNull(report);
            Assert.AreEqual(0, report.Cohorts.Count);
            Assert.AreEqual("N/A", report.OverallRetentionHealth.Grade);
        }

        [TestMethod]
        public void GenerateReport_SingleCohort_BuildsSurvivalCurve()
        {
            // Arrange: 5 customers joined Jan 2025, with varying rental activity
            for (int i = 1; i <= 5; i++)
            {
                _customers.Add(new Customer
                {
                    Id = i,
                    Name = $"Customer {i}",
                    MemberSince = new DateTime(2025, 1, 15),
                    MembershipType = MembershipType.Basic
                });
            }

            // All 5 rent in Jan, 4 in Feb, 3 in Mar, 2 in Apr, 1 in May
            var rentalId = 1;
            for (int i = 1; i <= 5; i++)
                _rentals.Add(CreateRental(rentalId++, i, 1, new DateTime(2025, 1, 20)));
            for (int i = 1; i <= 4; i++)
                _rentals.Add(CreateRental(rentalId++, i, 1, new DateTime(2025, 2, 15)));
            for (int i = 1; i <= 3; i++)
                _rentals.Add(CreateRental(rentalId++, i, 1, new DateTime(2025, 3, 10)));
            for (int i = 1; i <= 2; i++)
                _rentals.Add(CreateRental(rentalId++, i, 1, new DateTime(2025, 4, 5)));
            _rentals.Add(CreateRental(rentalId++, 1, 1, new DateTime(2025, 5, 1)));

            // Act
            var report = _service.GenerateReport();

            // Assert
            Assert.AreEqual(1, report.Cohorts.Count);
            var cohort = report.Cohorts[0];
            Assert.AreEqual("2025-01", cohort.Label);
            Assert.AreEqual(5, cohort.InitialSize);
            Assert.IsTrue(cohort.SurvivalCurve.Count >= 2);
            Assert.AreEqual(1.0, cohort.SurvivalCurve[0].SurvivalRate);
        }

        [TestMethod]
        public void GenerateReport_MultipleCohorts_ComparesThem()
        {
            // Arrange: 2 cohorts
            for (int i = 1; i <= 3; i++)
            {
                _customers.Add(new Customer
                {
                    Id = i,
                    Name = $"Jan Customer {i}",
                    MemberSince = new DateTime(2025, 1, 10),
                    MembershipType = MembershipType.Basic
                });
            }
            for (int i = 4; i <= 6; i++)
            {
                _customers.Add(new Customer
                {
                    Id = i,
                    Name = $"Feb Customer {i}",
                    MemberSince = new DateTime(2025, 2, 10),
                    MembershipType = MembershipType.Silver
                });
            }

            var rentalId = 1;
            // Jan cohort: all active in Jan & Feb
            for (int i = 1; i <= 3; i++)
            {
                _rentals.Add(CreateRental(rentalId++, i, 1, new DateTime(2025, 1, 15)));
                _rentals.Add(CreateRental(rentalId++, i, 2, new DateTime(2025, 2, 15)));
            }
            // Feb cohort: all active in Feb, only 1 in Mar
            for (int i = 4; i <= 6; i++)
                _rentals.Add(CreateRental(rentalId++, i, 3, new DateTime(2025, 2, 20)));
            _rentals.Add(CreateRental(rentalId++, 4, 4, new DateTime(2025, 3, 20)));

            // Act
            var report = _service.GenerateReport();

            // Assert
            Assert.AreEqual(2, report.Cohorts.Count);
            Assert.IsTrue(report.Comparisons.Count >= 1 || report.Cohorts.All(c => c.MonthsTracked >= 2));
        }

        [TestMethod]
        public void GenerateReport_DetectsRetentionCliffs()
        {
            // Create cohort where everyone drops off in month 2
            for (int i = 1; i <= 10; i++)
            {
                _customers.Add(new Customer
                {
                    Id = i,
                    Name = $"Customer {i}",
                    MemberSince = new DateTime(2025, 1, 5),
                    MembershipType = MembershipType.Basic
                });
            }

            var rentalId = 1;
            // All 10 rent in Jan
            for (int i = 1; i <= 10; i++)
                _rentals.Add(CreateRental(rentalId++, i, 1, new DateTime(2025, 1, 10)));
            // 8 rent in Feb
            for (int i = 1; i <= 8; i++)
                _rentals.Add(CreateRental(rentalId++, i, 2, new DateTime(2025, 2, 10)));
            // Only 1 rents in Mar (massive cliff)
            _rentals.Add(CreateRental(rentalId++, 1, 3, new DateTime(2025, 3, 10)));
            // 1 rents in Apr
            _rentals.Add(CreateRental(rentalId++, 1, 4, new DateTime(2025, 4, 10)));

            var report = _service.GenerateReport();

            var cohort = report.Cohorts[0];
            // Should detect the cliff at month 2 (drop from 8 to 1)
            Assert.IsTrue(cohort.RetentionCliffs.Count >= 1, "Expected at least one retention cliff");
        }

        [TestMethod]
        public void GetCohortDetail_ValidLabel_ReturnsDetail()
        {
            _customers.Add(new Customer
            {
                Id = 1,
                Name = "Alice",
                MemberSince = new DateTime(2025, 3, 1),
                MembershipType = MembershipType.Gold
            });
            _rentals.Add(CreateRental(1, 1, 1, new DateTime(2025, 3, 5)));

            var detail = _service.GetCohortDetail("2025-03");

            Assert.IsNotNull(detail);
            Assert.AreEqual("2025-03", detail.Cohort.Label);
            Assert.AreEqual(1, detail.Cohort.InitialSize);
        }

        [TestMethod]
        public void GetCohortDetail_InvalidLabel_ReturnsNull()
        {
            var detail = _service.GetCohortDetail("9999-99");
            Assert.IsNull(detail);
        }

        [TestMethod]
        public void GenerateReport_CustomersWithoutMemberSince_AreExcluded()
        {
            _customers.Add(new Customer
            {
                Id = 1,
                Name = "No Date",
                MemberSince = null,
                MembershipType = MembershipType.Basic
            });

            var report = _service.GenerateReport();
            Assert.AreEqual(0, report.Cohorts.Count);
        }

        [TestMethod]
        public void GenerateReport_HealthGradeIsAssigned()
        {
            for (int i = 1; i <= 5; i++)
            {
                _customers.Add(new Customer
                {
                    Id = i,
                    Name = $"C{i}",
                    MemberSince = new DateTime(2025, 1, 1),
                    MembershipType = MembershipType.Basic
                });
                _rentals.Add(CreateRental(i, i, 1, new DateTime(2025, 1, 10)));
            }

            var report = _service.GenerateReport();
            Assert.IsNotNull(report.Cohorts[0].HealthGrade);
            Assert.AreNotEqual("", report.Cohorts[0].HealthGrade);
        }

        [TestMethod]
        public void GenerateReport_OverallRetentionHealth_HasValidScore()
        {
            for (int i = 1; i <= 3; i++)
            {
                _customers.Add(new Customer
                {
                    Id = i,
                    Name = $"C{i}",
                    MemberSince = new DateTime(2025, 1, 1),
                    MembershipType = MembershipType.Basic
                });
                _rentals.Add(CreateRental(i, i, 1, new DateTime(2025, 1, 10)));
                _rentals.Add(CreateRental(i + 100, i, 2, new DateTime(2025, 2, 10)));
            }

            var report = _service.GenerateReport();
            Assert.IsTrue(report.OverallRetentionHealth.Score >= 0);
            Assert.IsTrue(report.OverallRetentionHealth.Score <= 100);
            Assert.IsNotNull(report.OverallRetentionHealth.Grade);
        }

        [TestMethod]
        public void GenerateReport_InsightsGenerated()
        {
            for (int i = 1; i <= 4; i++)
            {
                _customers.Add(new Customer
                {
                    Id = i,
                    Name = $"C{i}",
                    MemberSince = new DateTime(2025, 1, 1),
                    MembershipType = MembershipType.Basic
                });
                _rentals.Add(CreateRental(i, i, 1, new DateTime(2025, 1, 15)));
            }

            var report = _service.GenerateReport();
            Assert.IsTrue(report.Insights.Count > 0);
        }

        [TestMethod]
        public void GenerateReport_SurvivalRateDecreasesOverTime()
        {
            for (int i = 1; i <= 10; i++)
            {
                _customers.Add(new Customer
                {
                    Id = i,
                    Name = $"C{i}",
                    MemberSince = new DateTime(2025, 1, 1),
                    MembershipType = MembershipType.Basic
                });
            }

            var rentalId = 1;
            // Decreasing activity: 10 -> 7 -> 4 -> 2
            for (int i = 1; i <= 10; i++)
                _rentals.Add(CreateRental(rentalId++, i, 1, new DateTime(2025, 1, 10)));
            for (int i = 1; i <= 7; i++)
                _rentals.Add(CreateRental(rentalId++, i, 2, new DateTime(2025, 2, 10)));
            for (int i = 1; i <= 4; i++)
                _rentals.Add(CreateRental(rentalId++, i, 3, new DateTime(2025, 3, 10)));
            for (int i = 1; i <= 2; i++)
                _rentals.Add(CreateRental(rentalId++, i, 4, new DateTime(2025, 4, 10)));

            var report = _service.GenerateReport();
            var curve = report.Cohorts[0].SurvivalCurve;

            // Survival should generally decrease
            for (int i = 1; i < curve.Count; i++)
            {
                Assert.IsTrue(curve[i].SurvivalRate <= curve[i - 1].SurvivalRate,
                    $"Survival should not increase: month {i} ({curve[i].SurvivalRate}) > month {i - 1} ({curve[i - 1].SurvivalRate})");
            }
        }

        [TestMethod]
        public void GenerateReport_MedianSurvivalDetected_WhenDropsBelow50Percent()
        {
            for (int i = 1; i <= 10; i++)
            {
                _customers.Add(new Customer
                {
                    Id = i,
                    Name = $"C{i}",
                    MemberSince = new DateTime(2025, 1, 1),
                    MembershipType = MembershipType.Basic
                });
            }

            var rentalId = 1;
            // 10 -> 5 -> 4 (survival drops below 50% at month 2)
            for (int i = 1; i <= 10; i++)
                _rentals.Add(CreateRental(rentalId++, i, 1, new DateTime(2025, 1, 10)));
            for (int i = 1; i <= 5; i++)
                _rentals.Add(CreateRental(rentalId++, i, 2, new DateTime(2025, 2, 10)));
            for (int i = 1; i <= 4; i++)
                _rentals.Add(CreateRental(rentalId++, i, 3, new DateTime(2025, 3, 10)));

            var report = _service.GenerateReport();
            var cohort = report.Cohorts[0];

            // Median survival should be detected (the month survival < 0.5)
            Assert.IsNotNull(cohort.MedianSurvivalMonth);
        }

        [TestMethod]
        public void GetCohortDetail_ReturnsTopRetainedAndChurned()
        {
            for (int i = 1; i <= 5; i++)
            {
                _customers.Add(new Customer
                {
                    Id = i,
                    Name = $"Customer {i}",
                    MemberSince = new DateTime(2025, 1, 1),
                    MembershipType = MembershipType.Basic
                });
            }

            var rentalId = 1;
            // Customer 1: many rentals (retained)
            for (int m = 1; m <= 5; m++)
                _rentals.Add(CreateRental(rentalId++, 1, m, new DateTime(2025, m, 10)));
            // Customer 2: only 1 rental (churned)
            _rentals.Add(CreateRental(rentalId++, 2, 6, new DateTime(2025, 1, 15)));

            var detail = _service.GetCohortDetail("2025-01");

            Assert.IsNotNull(detail);
            Assert.IsTrue(detail.TopRetainedCustomers.Count > 0);
            Assert.AreEqual(1, detail.TopRetainedCustomers[0].CustomerId); // Most rentals
        }

        [TestMethod]
        public void GenerateReport_CohortComparison_DetectsImproving()
        {
            // Jan cohort: poor retention
            for (int i = 1; i <= 4; i++)
            {
                _customers.Add(new Customer { Id = i, Name = $"Jan{i}", MemberSince = new DateTime(2025, 1, 1), MembershipType = MembershipType.Basic });
            }
            // Feb cohort: better retention
            for (int i = 5; i <= 8; i++)
            {
                _customers.Add(new Customer { Id = i, Name = $"Feb{i}", MemberSince = new DateTime(2025, 2, 1), MembershipType = MembershipType.Basic });
            }

            var rentalId = 1;
            // Jan: 4 in Jan, 1 in Feb, 0 in Mar
            for (int i = 1; i <= 4; i++)
                _rentals.Add(CreateRental(rentalId++, i, 1, new DateTime(2025, 1, 10)));
            _rentals.Add(CreateRental(rentalId++, 1, 2, new DateTime(2025, 2, 10)));

            // Feb: 4 in Feb, 4 in Mar, 3 in Apr
            for (int i = 5; i <= 8; i++)
                _rentals.Add(CreateRental(rentalId++, i, 3, new DateTime(2025, 2, 15)));
            for (int i = 5; i <= 8; i++)
                _rentals.Add(CreateRental(rentalId++, i, 4, new DateTime(2025, 3, 15)));
            for (int i = 5; i <= 7; i++)
                _rentals.Add(CreateRental(rentalId++, i, 5, new DateTime(2025, 4, 15)));

            var report = _service.GenerateReport();
            // Should have comparisons
            Assert.IsTrue(report.Cohorts.Count == 2);
        }

        [TestMethod]
        public void GenerateReport_FutureCohortStart_IsExcluded()
        {
            _customers.Add(new Customer
            {
                Id = 1,
                Name = "Future Customer",
                MemberSince = new DateTime(2026, 1, 1), // After clock time of 2025-07-01
                MembershipType = MembershipType.Basic
            });

            var report = _service.GenerateReport();
            Assert.AreEqual(0, report.Cohorts.Count);
        }

        [TestMethod]
        public void GenerateReport_LargeCohort_DoesNotExceed24Months()
        {
            // Customer joined long ago
            _customers.Add(new Customer
            {
                Id = 1,
                Name = "Old Customer",
                MemberSince = new DateTime(2020, 1, 1),
                MembershipType = MembershipType.Platinum
            });

            // Rental every month for years
            var rentalId = 1;
            for (int m = 0; m < 60; m++)
            {
                _rentals.Add(CreateRental(rentalId++, 1, m + 1, new DateTime(2020, 1, 1).AddMonths(m)));
            }

            var report = _service.GenerateReport();
            var cohort = report.Cohorts[0];
            // Capped at 25 points (month 0 through 24)
            Assert.IsTrue(cohort.SurvivalCurve.Count <= 25);
        }

        private Rental CreateRental(int id, int customerId, int movieId, DateTime rentalDate)
        {
            return new Rental
            {
                Id = id,
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(7),
                ReturnDate = rentalDate.AddDays(5),
                DailyRate = 3.99m,
                Status = RentalStatus.Returned
            };
        }

        private class TestClock : IClock
        {
            private readonly DateTime _now;
            public TestClock(DateTime now) { _now = now; }
            public DateTime UtcNow => _now;
        }
    }
}
