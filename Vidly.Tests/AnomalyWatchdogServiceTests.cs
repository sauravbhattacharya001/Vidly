using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Xunit;

namespace Vidly.Tests
{
    public class AnomalyWatchdogServiceTests : IDisposable
    {
        private readonly InMemoryMovieRepository _movieRepo;
        private readonly InMemoryRentalRepository _rentalRepo;
        private readonly InMemoryCustomerRepository _customerRepo;
        private readonly TestClock _clock;

        public AnomalyWatchdogServiceTests()
        {
            _movieRepo = new InMemoryMovieRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _customerRepo = new InMemoryCustomerRepository();
            _clock = new TestClock(new DateTime(2025, 6, 15, 12, 0, 0));
            AnomalyWatchdogService.Reset();
        }

        public void Dispose()
        {
            AnomalyWatchdogService.Reset();
        }

        private AnomalyWatchdogService CreateService(WatchdogConfig config = null)
        {
            return new AnomalyWatchdogService(_rentalRepo, _movieRepo, _customerRepo, _clock, config);
        }

        private Customer AddCustomer(string name, string email = null, string phone = null)
        {
            var c = new Customer { Name = name, Email = email, Phone = phone, MembershipType = MembershipType.Basic };
            return _customerRepo.Add(c);
        }

        private Movie AddMovie(string name, Genre genre, decimal dailyRate = 3.99m)
        {
            var m = new Movie { Name = name, Genre = genre, DailyRate = dailyRate };
            return _movieRepo.Add(m);
        }

        private Rental AddRental(int customerId, int movieId, DateTime rentalDate,
            DateTime dueDate, DateTime? returnDate = null, decimal dailyRate = 3.99m)
        {
            var status = returnDate.HasValue ? RentalStatus.Returned : RentalStatus.Active;
            var r = new Rental
            {
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = rentalDate,
                DueDate = dueDate,
                ReturnDate = returnDate,
                DailyRate = dailyRate,
                Status = status
            };
            return _rentalRepo.Add(r);
        }

        // ─── Rental Burst Tests ────────────────────────────────────

        [Fact]
        public void DetectRentalBursts_NoRecentRentals_ReturnsEmpty()
        {
            var customer = AddCustomer("Alice", "a@b.com", "555-0001");
            var movie = AddMovie("Old Movie", Genre.Action);
            // Rental from 5 days ago — outside 24h window
            AddRental(customer.Id, movie.Id, _clock.Now.AddDays(-5), _clock.Now.AddDays(-2),
                _clock.Now.AddDays(-3));

            var service = CreateService();
            var alerts = service.DetectRentalBursts();

            Assert.Empty(alerts);
        }

        [Fact]
        public void DetectRentalBursts_BelowThreshold_ReturnsEmpty()
        {
            var customer = AddCustomer("Bob", "b@b.com", "555-0002");
            // 3 rentals in last 24h — below default threshold of 5
            for (int i = 0; i < 3; i++)
            {
                var movie = AddMovie("Movie " + i, Genre.Comedy);
                AddRental(customer.Id, movie.Id, _clock.Now.AddHours(-i - 1), _clock.Now.AddDays(7));
            }

            var service = CreateService();
            var alerts = service.DetectRentalBursts();

            Assert.Empty(alerts);
        }

        [Fact]
        public void DetectRentalBursts_AtThreshold_GeneratesAlert()
        {
            var customer = AddCustomer("Charlie", "c@b.com", "555-0003");
            for (int i = 0; i < 5; i++)
            {
                var movie = AddMovie("Burst Movie " + i, Genre.Action);
                AddRental(customer.Id, movie.Id, _clock.Now.AddHours(-i - 1), _clock.Now.AddDays(7));
            }

            var service = CreateService();
            var alerts = service.DetectRentalBursts();

            Assert.Single(alerts);
            Assert.Equal(AnomalyType.RentalBurst, alerts[0].Type);
            Assert.Equal(customer.Id, alerts[0].CustomerId);
            Assert.Equal(5, alerts[0].Evidence.Count);
        }

        [Fact]
        public void DetectRentalBursts_HighBurst_SeverityScales()
        {
            var customer = AddCustomer("Dave", "d@b.com", "555-0004");
            for (int i = 0; i < 15; i++) // 3x threshold
            {
                var movie = AddMovie("Spam Movie " + i, Genre.Horror);
                AddRental(customer.Id, movie.Id, _clock.Now.AddHours(-1), _clock.Now.AddDays(7));
            }

            var service = CreateService();
            var alerts = service.DetectRentalBursts();

            Assert.Single(alerts);
            Assert.Equal(AnomalySeverity.Critical, alerts[0].Severity);
        }

        // ─── Genre Shift Tests ─────────────────────────────────────

        [Fact]
        public void DetectGenreShifts_InsufficientHistory_ReturnsEmpty()
        {
            var customer = AddCustomer("Eve", "e@b.com", "555-0005");
            var movie = AddMovie("Only Movie", Genre.Drama);
            AddRental(customer.Id, movie.Id, _clock.Now.AddDays(-60), _clock.Now.AddDays(-53),
                _clock.Now.AddDays(-55));

            var service = CreateService();
            var alerts = service.DetectGenreShifts();

            Assert.Empty(alerts);
        }

        [Fact]
        public void DetectGenreShifts_ConsistentPreferences_ReturnsEmpty()
        {
            var customer = AddCustomer("Frank", "f@b.com", "555-0006");
            // Historical: all Action
            for (int i = 0; i < 5; i++)
            {
                var movie = AddMovie("Action Old " + i, Genre.Action);
                AddRental(customer.Id, movie.Id, _clock.Now.AddDays(-60 + i), _clock.Now.AddDays(-53 + i),
                    _clock.Now.AddDays(-55 + i));
            }
            // Recent: all Action
            for (int i = 0; i < 3; i++)
            {
                var movie = AddMovie("Action New " + i, Genre.Action);
                AddRental(customer.Id, movie.Id, _clock.Now.AddDays(-10 + i), _clock.Now.AddDays(-3 + i));
            }

            var service = CreateService();
            var alerts = service.DetectGenreShifts();

            Assert.Empty(alerts);
        }

        [Fact]
        public void DetectGenreShifts_DramaticShift_GeneratesAlert()
        {
            var customer = AddCustomer("Grace", "g@b.com", "555-0007");
            // Historical: all Action
            for (int i = 0; i < 5; i++)
            {
                var movie = AddMovie("Action Hist " + i, Genre.Action);
                AddRental(customer.Id, movie.Id, _clock.Now.AddDays(-90 + i), _clock.Now.AddDays(-83 + i),
                    _clock.Now.AddDays(-85 + i));
            }
            // Recent: all Romance (complete shift)
            for (int i = 0; i < 4; i++)
            {
                var movie = AddMovie("Romance New " + i, Genre.Romance);
                AddRental(customer.Id, movie.Id, _clock.Now.AddDays(-10 + i), _clock.Now.AddDays(-3 + i));
            }

            var service = CreateService();
            var alerts = service.DetectGenreShifts();

            Assert.Single(alerts);
            Assert.Equal(AnomalyType.GenreShift, alerts[0].Type);
            Assert.True(alerts[0].Evidence.Count > 0);
        }

        // ─── Return Pattern Tests ──────────────────────────────────

        [Fact]
        public void DetectReturnAnomalies_GoodReturnRate_ReturnsEmpty()
        {
            var customer = AddCustomer("Hank", "h@b.com", "555-0008");
            // 5 returns, all on time
            for (int i = 0; i < 5; i++)
            {
                var movie = AddMovie("Good Return " + i, Genre.Comedy);
                var rentalDate = _clock.Now.AddDays(-30 + i * 5);
                AddRental(customer.Id, movie.Id, rentalDate, rentalDate.AddDays(7),
                    rentalDate.AddDays(5), 3.99m);
            }

            var service = CreateService();
            var alerts = service.DetectReturnAnomalies();

            Assert.Empty(alerts);
        }

        [Fact]
        public void DetectReturnAnomalies_HighLateRate_GeneratesAlert()
        {
            var customer = AddCustomer("Ivy", "i@b.com", "555-0009");
            // 4 of 5 returns late
            for (int i = 0; i < 5; i++)
            {
                var movie = AddMovie("Late Movie " + i, Genre.Drama);
                var rentalDate = _clock.Now.AddDays(-50 + i * 8);
                var dueDate = rentalDate.AddDays(7);
                var returnDate = i < 4 ? dueDate.AddDays(3) : dueDate.AddDays(-1); // 4 late, 1 early
                AddRental(customer.Id, movie.Id, rentalDate, dueDate, returnDate, 3.99m);
            }

            var service = CreateService();
            var alerts = service.DetectReturnAnomalies();

            Assert.True(alerts.Any(a => a.Title.Contains("late return rate")));
        }

        [Fact]
        public void DetectReturnAnomalies_PerfectTiming_GeneratesAlert()
        {
            var customer = AddCustomer("Jack", "j@b.com", "555-0010");
            // 6 returns all on exact due date
            for (int i = 0; i < 6; i++)
            {
                var movie = AddMovie("Perfect Movie " + i, Genre.SciFi);
                var rentalDate = _clock.Now.AddDays(-60 + i * 8);
                var dueDate = rentalDate.AddDays(7);
                AddRental(customer.Id, movie.Id, rentalDate, dueDate, dueDate, 3.99m);
            }

            var service = CreateService();
            var alerts = service.DetectReturnAnomalies();

            Assert.True(alerts.Any(a => a.Title.Contains("perfect timing")));
        }

        // ─── Inventory Concentration Tests ─────────────────────────

        [Fact]
        public void DetectInventoryConcentration_NoActiveRentals_ReturnsEmpty()
        {
            var customer = AddCustomer("Kate", "k@b.com", "555-0011");
            var movie = AddMovie("Done Movie", Genre.Action);
            AddRental(customer.Id, movie.Id, _clock.Now.AddDays(-10), _clock.Now.AddDays(-3),
                _clock.Now.AddDays(-4));

            var service = CreateService();
            var alerts = service.DetectInventoryConcentration();

            Assert.Empty(alerts);
        }

        [Fact]
        public void DetectInventoryConcentration_OneCustomerHoldsAll_GeneratesAlert()
        {
            var hoarder = AddCustomer("Larry", "l@b.com", "555-0012");
            var other = AddCustomer("Mike", "m@b.com", "555-0013");

            // Hoarder has 4 active, other has 1 (80% concentration)
            for (int i = 0; i < 4; i++)
            {
                var movie = AddMovie("Hoard Movie " + i, Genre.Thriller);
                AddRental(hoarder.Id, movie.Id, _clock.Now.AddDays(-1), _clock.Now.AddDays(6));
            }
            var otherMovie = AddMovie("Other Movie", Genre.Comedy);
            AddRental(other.Id, otherMovie.Id, _clock.Now.AddDays(-1), _clock.Now.AddDays(6));

            var service = CreateService();
            var alerts = service.DetectInventoryConcentration();

            Assert.Single(alerts);
            Assert.Equal(AnomalyType.InventoryConcentration, alerts[0].Type);
            Assert.Equal(hoarder.Id, alerts[0].CustomerId);
        }

        [Fact]
        public void DetectInventoryConcentration_EvenDistribution_ReturnsEmpty()
        {
            // 5 customers each with 1 active rental = 20% each, below 30% threshold
            for (int i = 0; i < 5; i++)
            {
                var customer = AddCustomer("Even " + i, "even" + i + "@b.com", "555-02" + i);
                var movie = AddMovie("Even Movie " + i, Genre.Action);
                AddRental(customer.Id, movie.Id, _clock.Now.AddDays(-1), _clock.Now.AddDays(6));
            }

            var service = CreateService();
            var alerts = service.DetectInventoryConcentration();

            Assert.Empty(alerts);
        }

        // ─── Rate Abuse Tests ──────────────────────────────────────

        [Fact]
        public void DetectRateAbuse_NormalRates_ReturnsEmpty()
        {
            var customer = AddCustomer("Nancy", "n@b.com", "555-0020");
            for (int i = 0; i < 4; i++)
            {
                var movie = AddMovie("Normal " + i, Genre.Comedy, 3.99m);
                var rentalDate = _clock.Now.AddDays(-30 + i * 5);
                AddRental(customer.Id, movie.Id, rentalDate, rentalDate.AddDays(7),
                    rentalDate.AddDays(5), 3.99m);
            }

            var service = CreateService();
            var alerts = service.DetectRateAbuse();

            Assert.Empty(alerts);
        }

        [Fact]
        public void DetectRateAbuse_PremiumRenterPerfectReturns_GeneratesAlert()
        {
            // Need a baseline of cheap rentals from another customer
            var normalCustomer = AddCustomer("Oscar", "o@b.com", "555-0021");
            for (int i = 0; i < 5; i++)
            {
                var cheapMovie = AddMovie("Cheap " + i, Genre.Comedy, 2.00m);
                var rd = _clock.Now.AddDays(-40 + i * 5);
                AddRental(normalCustomer.Id, cheapMovie.Id, rd, rd.AddDays(7), rd.AddDays(5), 2.00m);
            }

            var premiumCustomer = AddCustomer("Pete", "p@b.com", "555-0022");
            for (int i = 0; i < 4; i++)
            {
                var expMovie = AddMovie("Premium " + i, Genre.SciFi, 9.99m);
                var rd = _clock.Now.AddDays(-30 + i * 5);
                AddRental(premiumCustomer.Id, expMovie.Id, rd, rd.AddDays(7), rd.AddDays(6), 9.99m);
            }

            var service = CreateService();
            var alerts = service.DetectRateAbuse();

            Assert.True(alerts.Any(a => a.Type == AnomalyType.RateAbuse && a.CustomerId == premiumCustomer.Id));
        }

        // ─── Identity Risk Tests ───────────────────────────────────

        [Fact]
        public void DetectIdentityRisks_CompleteProfile_ReturnsEmpty()
        {
            AddCustomer("Quinn", "q@b.com", "555-0030");

            var service = CreateService();
            var alerts = service.DetectIdentityRisks();

            Assert.Empty(alerts);
        }

        [Fact]
        public void DetectIdentityRisks_EmailOnly_ReturnsEmpty()
        {
            AddCustomer("Rachel", "r@b.com");

            var service = CreateService();
            var alerts = service.DetectIdentityRisks();

            Assert.Empty(alerts);
        }

        [Fact]
        public void DetectIdentityRisks_NoContactInfo_GeneratesAlert()
        {
            var customer = AddCustomer("Shadow");

            var service = CreateService();
            var alerts = service.DetectIdentityRisks();

            Assert.True(alerts.Any(a => a.Type == AnomalyType.IdentityRisk && a.CustomerId == customer.Id));
            var alert = alerts.First(a => a.CustomerId == customer.Id);
            Assert.Equal(2, alert.Evidence.Count); // missing email + phone
        }

        // ─── Threat Level Classification ───────────────────────────

        [Fact]
        public void ClassifyThreatLevel_NoAlerts_Green()
        {
            var service = CreateService();
            Assert.Equal("Green", service.ClassifyThreatLevel(new List<AnomalyAlert>()));
        }

        [Fact]
        public void ClassifyThreatLevel_OnlyLow_Green()
        {
            var service = CreateService();
            var alerts = new List<AnomalyAlert>
            {
                new AnomalyAlert { Severity = AnomalySeverity.Low }
            };
            Assert.Equal("Green", service.ClassifyThreatLevel(alerts));
        }

        [Fact]
        public void ClassifyThreatLevel_Medium_Yellow()
        {
            var service = CreateService();
            var alerts = new List<AnomalyAlert>
            {
                new AnomalyAlert { Severity = AnomalySeverity.Medium }
            };
            Assert.Equal("Yellow", service.ClassifyThreatLevel(alerts));
        }

        [Fact]
        public void ClassifyThreatLevel_High_Orange()
        {
            var service = CreateService();
            var alerts = new List<AnomalyAlert>
            {
                new AnomalyAlert { Severity = AnomalySeverity.High }
            };
            Assert.Equal("Orange", service.ClassifyThreatLevel(alerts));
        }

        [Fact]
        public void ClassifyThreatLevel_Critical_Red()
        {
            var service = CreateService();
            var alerts = new List<AnomalyAlert>
            {
                new AnomalyAlert { Severity = AnomalySeverity.Critical }
            };
            Assert.Equal("Red", service.ClassifyThreatLevel(alerts));
        }

        // ─── Full Scan & Watchlist ─────────────────────────────────

        [Fact]
        public void RunFullScan_CleanData_ReturnsGreenReport()
        {
            AddCustomer("Clean Alice", "alice@b.com", "555-1000");

            var service = CreateService();
            var report = service.RunFullScan();

            Assert.Equal("Green", report.ThreatLevel);
            Assert.Equal(0, report.TotalAlertsGenerated);
            Assert.Contains("All clear", report.Summary);
        }

        [Fact]
        public void RunFullScan_WithAnomalies_BuildsWatchlist()
        {
            // Create identity risk customer (no email, no phone)
            var risky = AddCustomer("Ghost User");

            // Create burst customer
            var burster = AddCustomer("Burst User", "burst@b.com", "555-2000");
            for (int i = 0; i < 6; i++)
            {
                var movie = AddMovie("Burst Scan " + i, Genre.Action);
                AddRental(burster.Id, movie.Id, _clock.Now.AddHours(-i - 1), _clock.Now.AddDays(7));
            }

            var service = CreateService();
            var report = service.RunFullScan();

            Assert.True(report.TotalAlertsGenerated >= 2);
            Assert.True(report.Watchlist.Count >= 1);
            Assert.NotEqual("Green", report.ThreatLevel);
        }

        [Fact]
        public void AcknowledgeAlert_MarksAlertAcknowledged()
        {
            var customer = AddCustomer("Ack Test");
            var service = CreateService();
            var report = service.RunFullScan();

            // The identity risk alert should exist
            var alert = report.Alerts.FirstOrDefault(a => a.CustomerId == customer.Id);
            if (alert != null)
            {
                Assert.False(alert.Acknowledged);
                service.AcknowledgeAlert(alert.Id);
                Assert.True(alert.Acknowledged);
            }
        }

        [Fact]
        public void RunFullScan_MultipleCustomerAlerts_WatchlistAggregates()
        {
            // Customer with both identity risk AND burst
            var multi = AddCustomer("Multi Risk");
            for (int i = 0; i < 5; i++)
            {
                var movie = AddMovie("Multi Movie " + i, Genre.Horror);
                AddRental(multi.Id, movie.Id, _clock.Now.AddHours(-i - 1), _clock.Now.AddDays(7));
            }

            var service = CreateService();
            var report = service.RunFullScan();

            var watchEntry = report.Watchlist.FirstOrDefault(w => w.CustomerId == multi.Id);
            Assert.NotNull(watchEntry);
            Assert.True(watchEntry.AlertCount >= 2); // identity + burst
        }

        // ─── Edge Cases ────────────────────────────────────────────

        [Fact]
        public void DetectRentalBursts_NoCustomers_ReturnsEmpty()
        {
            var service = CreateService();
            var alerts = service.DetectRentalBursts();
            Assert.Empty(alerts);
        }

        [Fact]
        public void DetectReturnAnomalies_TooFewReturns_ReturnsEmpty()
        {
            var customer = AddCustomer("Single", "s@b.com", "555-3000");
            var movie = AddMovie("Only One", Genre.Drama);
            AddRental(customer.Id, movie.Id, _clock.Now.AddDays(-10), _clock.Now.AddDays(-3),
                _clock.Now.AddDays(-1));

            var service = CreateService();
            var alerts = service.DetectReturnAnomalies();

            // Less than 3 returns — should not trigger
            Assert.Empty(alerts);
        }

        [Fact]
        public void CustomConfig_LowThreshold_MoreSensitive()
        {
            var customer = AddCustomer("Config Test", "ct@b.com", "555-4000");
            // 2 rentals in 24h
            for (int i = 0; i < 2; i++)
            {
                var movie = AddMovie("Config Movie " + i, Genre.Action);
                AddRental(customer.Id, movie.Id, _clock.Now.AddHours(-i - 1), _clock.Now.AddDays(7));
            }

            // Default threshold (5) should not trigger
            var service1 = CreateService();
            Assert.Empty(service1.DetectRentalBursts());

            // Low threshold (2) should trigger
            var service2 = CreateService(new WatchdogConfig { RentalBurstThreshold = 2 });
            Assert.Single(service2.DetectRentalBursts());
        }

        [Fact]
        public void Constructor_NullRepo_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new AnomalyWatchdogService(null, _movieRepo, _customerRepo, _clock));
            Assert.Throws<ArgumentNullException>(() =>
                new AnomalyWatchdogService(_rentalRepo, null, _customerRepo, _clock));
            Assert.Throws<ArgumentNullException>(() =>
                new AnomalyWatchdogService(_rentalRepo, _movieRepo, null, _clock));
            Assert.Throws<ArgumentNullException>(() =>
                new AnomalyWatchdogService(_rentalRepo, _movieRepo, _customerRepo, null));
        }
    }
}
