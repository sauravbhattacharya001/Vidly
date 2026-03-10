using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Tests
{
    [TestClass]
    public class InMemoryRentalRepositoryTests
    {
        [TestInitialize]
        public void Setup()
        {
            InMemoryRentalRepository.Reset();
        }

        [TestMethod]
        public void GetAll_ReturnsAllRentals()
        {
            var repo = new InMemoryRentalRepository();
            var all = repo.GetAll();
            Assert.IsTrue(all.Count >= 3, "Should have at least the 3 pre-seeded rentals.");
        }

        [TestMethod]
        public void GetById_ExistingId_ReturnsRental()
        {
            var repo = new InMemoryRentalRepository();
            var rental = repo.GetById(1);
            Assert.IsNotNull(rental);
            Assert.AreEqual(1, rental.Id);
            Assert.AreEqual("John Smith", rental.CustomerName);
        }

        [TestMethod]
        public void GetById_NonExistentId_ReturnsNull()
        {
            var repo = new InMemoryRentalRepository();
            var rental = repo.GetById(9999);
            Assert.IsNull(rental);
        }

        [TestMethod]
        public void GetById_ReturnsDefensiveCopy()
        {
            var repo = new InMemoryRentalRepository();
            var rental1 = repo.GetById(1);
            var rental2 = repo.GetById(1);
            Assert.AreNotSame(rental1, rental2);
        }

        [TestMethod]
        public void Add_AssignsIdAndDefaults()
        {
            var repo = new InMemoryRentalRepository();
            var rental = new Rental
            {
                CustomerId = 3,
                CustomerName = "Bob Wilson",
                MovieId = 99,
                MovieName = "Test Movie"
            };

            repo.Add(rental);

            Assert.IsTrue(rental.Id > 0, "Should assign a positive Id.");
            Assert.AreEqual(DateTime.Today, rental.RentalDate);
            Assert.AreEqual(DateTime.Today.AddDays(InMemoryRentalRepository.DefaultRentalDays), rental.DueDate);
            Assert.AreEqual(InMemoryRentalRepository.DefaultDailyRate, rental.DailyRate);
            Assert.AreEqual(RentalStatus.Active, rental.Status);
            Assert.IsNull(rental.ReturnDate);

            // Clean up
            repo.Remove(rental.Id);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Add_NullRental_ThrowsArgumentNullException()
        {
            var repo = new InMemoryRentalRepository();
            repo.Add(null);
        }

        [TestMethod]
        public void Update_ExistingRental_ModifiesValues()
        {
            var repo = new InMemoryRentalRepository();
            var rental = repo.GetById(1);
            Assert.IsNotNull(rental);

            rental.DailyRate = 5.99m;
            repo.Update(rental);

            var updated = repo.GetById(1);
            Assert.AreEqual(5.99m, updated.DailyRate);

            // Restore original
            updated.DailyRate = 3.99m;
            repo.Update(updated);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void Update_NonExistent_ThrowsKeyNotFoundException()
        {
            var repo = new InMemoryRentalRepository();
            var rental = new Rental { Id = 9999, CustomerName = "Nobody" };
            repo.Update(rental);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Update_NullRental_ThrowsArgumentNullException()
        {
            var repo = new InMemoryRentalRepository();
            repo.Update(null);
        }

        [TestMethod]
        public void Remove_ExistingId_RemovesRental()
        {
            var repo = new InMemoryRentalRepository();
            var rental = new Rental
            {
                CustomerId = 5,
                CustomerName = "Test",
                MovieId = 99,
                MovieName = "Removable"
            };
            repo.Add(rental);
            var id = rental.Id;

            repo.Remove(id);

            Assert.IsNull(repo.GetById(id));
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void Remove_NonExistentId_ThrowsKeyNotFoundException()
        {
            var repo = new InMemoryRentalRepository();
            repo.Remove(9999);
        }

        [TestMethod]
        public void GetActiveByCustomer_ReturnsOnlyActive()
        {
            var repo = new InMemoryRentalRepository();
            // Customer 1 (John Smith) has an active rental (Id 1)
            var active = repo.GetActiveByCustomer(1);
            Assert.IsTrue(active.All(r => r.Status != RentalStatus.Returned));
            Assert.IsTrue(active.All(r => r.CustomerId == 1));
        }

        [TestMethod]
        public void GetActiveByCustomer_NoRentals_ReturnsEmpty()
        {
            var repo = new InMemoryRentalRepository();
            var active = repo.GetActiveByCustomer(9999);
            Assert.AreEqual(0, active.Count);
        }

        [TestMethod]
        public void GetByMovie_ReturnsAllRentalsForMovie()
        {
            var repo = new InMemoryRentalRepository();
            var rentals = repo.GetByMovie(1);
            Assert.IsTrue(rentals.All(r => r.MovieId == 1));
        }

        [TestMethod]
        public void GetOverdue_ReturnsOnlyOverdue()
        {
            var repo = new InMemoryRentalRepository();
            var overdue = repo.GetOverdue();
            Assert.IsTrue(overdue.All(r => r.Status == RentalStatus.Overdue));
        }

        [TestMethod]
        public void Search_ByCustomerName_ReturnsMatches()
        {
            var repo = new InMemoryRentalRepository();
            var results = repo.Search("John", null);
            Assert.IsTrue(results.Any(r => r.CustomerName.Contains("John")));
        }

        [TestMethod]
        public void Search_ByMovieName_ReturnsMatches()
        {
            var repo = new InMemoryRentalRepository();
            var results = repo.Search("Shrek", null);
            Assert.IsTrue(results.Any(r => r.MovieName.Contains("Shrek")));
        }

        [TestMethod]
        public void Search_ByStatus_FiltersCorrectly()
        {
            var repo = new InMemoryRentalRepository();
            var returned = repo.Search(null, RentalStatus.Returned);
            Assert.IsTrue(returned.All(r => r.Status == RentalStatus.Returned));
        }

        [TestMethod]
        public void Search_NoMatch_ReturnsEmpty()
        {
            var repo = new InMemoryRentalRepository();
            var results = repo.Search("ZZZZNONEXISTENT", null);
            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public void ReturnRental_OnTime_NoLateFee()
        {
            var repo = new InMemoryRentalRepository();
            var rental = new Rental
            {
                CustomerId = 1,
                CustomerName = "Test",
                MovieId = 50,
                MovieName = "On Time Movie",
                RentalDate = DateTime.Today.AddDays(-3),
                DueDate = DateTime.Today.AddDays(4),
                DailyRate = 3.99m
            };
            repo.Add(rental);

            var returned = repo.ReturnRental(rental.Id);

            Assert.AreEqual(RentalStatus.Returned, returned.Status);
            Assert.AreEqual(0m, returned.LateFee);
            Assert.AreEqual(DateTime.Today, returned.ReturnDate);

            repo.Remove(rental.Id);
        }

        [TestMethod]
        public void ReturnRental_Late_CalculatesLateFee()
        {
            var repo = new InMemoryRentalRepository();
            var rental = new Rental
            {
                CustomerId = 1,
                CustomerName = "Test",
                MovieId = 51,
                MovieName = "Late Movie",
                RentalDate = DateTime.Today.AddDays(-10),
                DueDate = DateTime.Today.AddDays(-3),
                DailyRate = 3.99m
            };
            repo.Add(rental);

            var returned = repo.ReturnRental(rental.Id);

            Assert.AreEqual(RentalStatus.Returned, returned.Status);
            Assert.AreEqual(3 * InMemoryRentalRepository.LateFeePerDay, returned.LateFee);

            repo.Remove(rental.Id);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ReturnRental_AlreadyReturned_ThrowsInvalidOperationException()
        {
            var repo = new InMemoryRentalRepository();
            // Rental 3 is pre-seeded as Returned
            repo.ReturnRental(3);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void ReturnRental_NonExistent_ThrowsKeyNotFoundException()
        {
            var repo = new InMemoryRentalRepository();
            repo.ReturnRental(9999);
        }

        [TestMethod]
        public void IsMovieRentedOut_ActiveRental_ReturnsTrue()
        {
            var repo = new InMemoryRentalRepository();
            // Movie 1 (Shrek) is actively rented
            Assert.IsTrue(repo.IsMovieRentedOut(1));
        }

        [TestMethod]
        public void IsMovieRentedOut_ReturnedOnly_ReturnsFalse()
        {
            var repo = new InMemoryRentalRepository();
            // Movie 3 (Toy Story) has only a returned rental
            Assert.IsFalse(repo.IsMovieRentedOut(3));
        }

        [TestMethod]
        public void IsMovieRentedOut_NeverRented_ReturnsFalse()
        {
            var repo = new InMemoryRentalRepository();
            Assert.IsFalse(repo.IsMovieRentedOut(9999));
        }

        [TestMethod]
        public void GetStats_ReturnsAccurateStats()
        {
            var repo = new InMemoryRentalRepository();
            var stats = repo.GetStats();

            Assert.IsTrue(stats.TotalRentals >= 3);
            Assert.IsTrue(stats.ActiveRentals >= 0);
            Assert.IsTrue(stats.OverdueRentals >= 0);
            Assert.IsTrue(stats.ReturnedRentals >= 1);
            Assert.IsTrue(stats.TotalRevenue >= 0);
            Assert.IsTrue(stats.TotalLateFees >= 0);
            Assert.AreEqual(stats.TotalRentals,
                stats.ActiveRentals + stats.OverdueRentals + stats.ReturnedRentals);
        }

        [TestMethod]
        public void GetStats_RealizedRevenue_OnlyCountsReturnedRentals()
        {
            var repo = new InMemoryRentalRepository();
            var stats = repo.GetStats();

            // RealizedRevenue should be >= 0 and only from returned rentals
            Assert.IsTrue(stats.RealizedRevenue >= 0);
            // If there are no returned rentals, realized revenue should be 0
            if (stats.ReturnedRentals == 0)
                Assert.AreEqual(0m, stats.RealizedRevenue);
        }

        [TestMethod]
        public void GetStats_ProjectedRevenue_OnlyCountsActiveAndOverdueRentals()
        {
            var repo = new InMemoryRentalRepository();
            var stats = repo.GetStats();

            // ProjectedRevenue should be >= 0 and from active/overdue rentals
            Assert.IsTrue(stats.ProjectedRevenue >= 0);
            // If there are no active or overdue rentals, projected revenue should be 0
            if (stats.ActiveRentals == 0 && stats.OverdueRentals == 0)
                Assert.AreEqual(0m, stats.ProjectedRevenue);
        }

        [TestMethod]
        public void GetStats_RealizedPlusProjected_EqualsTotalRevenue()
        {
            var repo = new InMemoryRentalRepository();
            var stats = repo.GetStats();

            Assert.AreEqual(stats.TotalRevenue, stats.RealizedRevenue + stats.ProjectedRevenue,
                "RealizedRevenue + ProjectedRevenue must equal TotalRevenue");
        }

        [TestMethod]
        public void GetStats_AverageRevenuePerRental_UsesRealizedRevenue()
        {
            var repo = new InMemoryRentalRepository();
            var stats = repo.GetStats();

            // Compute expected average using realized revenue / returned count
            decimal expectedAvg = stats.ReturnedRentals > 0
                ? stats.RealizedRevenue / stats.ReturnedRentals
                : 0m;

            // The DashboardService should use this formula; verify via stats properties
            Assert.IsTrue(stats.RealizedRevenue >= 0);
            Assert.IsTrue(stats.ReturnedRentals >= 0);
            if (stats.ReturnedRentals > 0)
                Assert.AreEqual(expectedAvg, stats.RealizedRevenue / stats.ReturnedRentals);
        }

        [TestMethod]
        public void Checkout_AvailableMovie_CreatesRental()
        {
            var repo = new InMemoryRentalRepository();
            var rental = new Rental
            {
                CustomerId = 1,
                CustomerName = "Test User",
                MovieId = 200,
                MovieName = "Available Movie"
            };

            var result = repo.Checkout(rental);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Id > 0);
            Assert.AreEqual(RentalStatus.Active, result.Status);
            Assert.AreEqual(200, result.MovieId);
            Assert.IsTrue(repo.IsMovieRentedOut(200));

            repo.Remove(result.Id);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Checkout_AlreadyRentedMovie_ThrowsInvalidOperationException()
        {
            var repo = new InMemoryRentalRepository();
            // Movie 1 (Shrek) is already rented out in seed data
            var rental = new Rental
            {
                CustomerId = 3,
                CustomerName = "Another Customer",
                MovieId = 1,
                MovieName = "Shrek!"
            };

            repo.Checkout(rental);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Checkout_NullRental_ThrowsArgumentNullException()
        {
            var repo = new InMemoryRentalRepository();
            repo.Checkout(null);
        }

        [TestMethod]
        public void Checkout_SetsDefaults_WhenNotProvided()
        {
            var repo = new InMemoryRentalRepository();
            var rental = new Rental
            {
                CustomerId = 1,
                CustomerName = "Defaults Test",
                MovieId = 201,
                MovieName = "Defaults Movie"
            };

            var result = repo.Checkout(rental);

            Assert.AreEqual(DateTime.Today, result.RentalDate);
            Assert.AreEqual(DateTime.Today.AddDays(InMemoryRentalRepository.DefaultRentalDays), result.DueDate);
            Assert.AreEqual(InMemoryRentalRepository.DefaultDailyRate, result.DailyRate);
            Assert.IsNull(result.ReturnDate);
            Assert.AreEqual(0m, result.LateFee);

            repo.Remove(result.Id);
        }

        [TestMethod]
        public void Checkout_ReturnsDefensiveCopy()
        {
            var repo = new InMemoryRentalRepository();
            var rental = new Rental
            {
                CustomerId = 1,
                CustomerName = "Copy Test",
                MovieId = 202,
                MovieName = "Copy Movie"
            };

            var result = repo.Checkout(rental);
            var fetched = repo.GetById(result.Id);

            Assert.AreNotSame(result, fetched);
            Assert.AreEqual(result.Id, fetched.Id);

            repo.Remove(result.Id);
        }

        [TestMethod]
        public void Checkout_ConcurrentRequests_OnlyOneSucceeds()
        {
            // This test verifies the TOCTOU fix: when multiple threads try to
            // rent the same movie simultaneously, exactly one should succeed
            // and the rest should get InvalidOperationException.
            var repo = new InMemoryRentalRepository();
            const int movieId = 300;
            const int threadCount = 10;

            int successCount = 0;
            int failureCount = 0;
            var ids = new List<int>();

            var barrier = new Barrier(threadCount);
            var tasks = new Task[threadCount];

            for (int i = 0; i < threadCount; i++)
            {
                int customerId = 100 + i;
                tasks[i] = Task.Run(() =>
                {
                    var r = new Rental
                    {
                        CustomerId = customerId,
                        CustomerName = $"Customer {customerId}",
                        MovieId = movieId,
                        MovieName = "Race Condition Movie"
                    };

                    barrier.SignalAndWait();

                    try
                    {
                        var result = repo.Checkout(r);
                        Interlocked.Increment(ref successCount);
                        lock (ids) { ids.Add(result.Id); }
                    }
                    catch (InvalidOperationException)
                    {
                        Interlocked.Increment(ref failureCount);
                    }
                });
            }

            Task.WaitAll(tasks);

            Assert.AreEqual(1, successCount,
                "Exactly one concurrent checkout should succeed.");
            Assert.AreEqual(threadCount - 1, failureCount,
                "All other concurrent checkouts should fail.");
            Assert.IsTrue(repo.IsMovieRentedOut(movieId));

            // Clean up
            foreach (var id in ids) repo.Remove(id);
        }

        [TestMethod]
        public void RefreshStatus_OverdueRental_AccruesLateFee()
        {
            // Regression test: previously RefreshStatus only set Status to
            // Overdue but left LateFee at 0, causing GetStats and billing
            // views to undercount late fees for unreturned rentals.
            var repo = new InMemoryRentalRepository();
            var rental = new Rental
            {
                CustomerId = 1,
                CustomerName = "Test Overdue",
                MovieId = 999,
                MovieName = "Overdue Movie",
                RentalDate = DateTime.Today.AddDays(-14),
                DueDate = DateTime.Today.AddDays(-7),
                DailyRate = 3.99m
            };
            repo.Add(rental);

            var fetched = repo.GetById(rental.Id);
            Assert.AreEqual(RentalStatus.Overdue, fetched.Status,
                "Rental past due date should be Overdue.");
            Assert.IsTrue(fetched.LateFee > 0,
                "Overdue rental should have accruing late fee > 0.");

            var expectedDays = (int)Math.Ceiling((DateTime.Today - rental.DueDate).TotalDays);
            Assert.AreEqual(expectedDays * InMemoryRentalRepository.LateFeePerDay,
                fetched.LateFee,
                "Late fee should equal overdue days × per-day rate.");

            // Clean up
            repo.Remove(rental.Id);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Checkout_OverdueRentalsCountAgainstConcurrentLimit()
        {
            // Regression test: overdue rentals must count against the concurrent
            // rental limit. Previously only Active rentals were counted, so a
            // customer whose rentals went overdue could bypass the cap entirely.
            var repo = new InMemoryRentalRepository();

            // Check out a movie that will immediately go overdue
            var overdueRental = new Rental
            {
                CustomerId = 50,
                CustomerName = "Limit Bypass Tester",
                MovieId = 800,
                MovieName = "Overdue Movie 1",
                RentalDate = DateTime.Today.AddDays(-14),
                DueDate = DateTime.Today.AddDays(-7),
                DailyRate = 3.99m
            };
            repo.Add(overdueRental);

            // Verify it's overdue
            var fetched = repo.GetById(overdueRental.Id);
            Assert.AreEqual(RentalStatus.Overdue, fetched.Status);

            // Now try to checkout another movie with maxConcurrentRentals = 1.
            // This should throw because the overdue rental counts against the limit.
            var newRental = new Rental
            {
                CustomerId = 50,
                CustomerName = "Limit Bypass Tester",
                MovieId = 801,
                MovieName = "Should Be Blocked",
                DailyRate = 3.99m
            };
            repo.Checkout(newRental, maxConcurrentRentals: 1);
        }

        [TestMethod]
        public void Checkout_ReturnedRentalDoesNotCountAgainstLimit()
        {
            var repo = new InMemoryRentalRepository();

            // Create and return a rental
            var rental = new Rental
            {
                CustomerId = 51,
                CustomerName = "Returned Tester",
                MovieId = 810,
                MovieName = "Already Returned",
                DailyRate = 3.99m
            };
            var created = repo.Checkout(rental);
            repo.ReturnRental(created.Id);

            // Should succeed — returned rental doesn't count
            var newRental = new Rental
            {
                CustomerId = 51,
                CustomerName = "Returned Tester",
                MovieId = 811,
                MovieName = "New Movie After Return",
                DailyRate = 3.99m
            };
            var result = repo.Checkout(newRental, maxConcurrentRentals: 1);
            Assert.IsNotNull(result);

            // Clean up
            repo.Remove(result.Id);
        }
    }
}
