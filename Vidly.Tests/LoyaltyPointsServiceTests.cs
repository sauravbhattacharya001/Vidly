using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class LoyaltyPointsServiceTests
    {
        private class TestCustomerRepository : ICustomerRepository
        {
            private readonly Dictionary<int, Customer> _customers = new Dictionary<int, Customer>();
            public void Add(Customer c) => _customers[c.Id] = c;
            public Customer GetById(int id) => _customers.TryGetValue(id, out var c) ? c : null;
            public IReadOnlyList<Customer> GetAll() => _customers.Values.ToList().AsReadOnly();
            public void Update(Customer c) { if (_customers.ContainsKey(c.Id)) _customers[c.Id] = c; }
            public void Remove(int id) => _customers.Remove(id);
            public IReadOnlyList<Customer> Search(string query, MembershipType? membershipType) => GetAll();
            public IReadOnlyList<Customer> GetByMemberSince(int year, int month) => new List<Customer>().AsReadOnly();
            public CustomerStats GetStats() => new CustomerStats { TotalCustomers = _customers.Count };
        }

        private class TestRentalRepository : IRentalRepository
        {
            private readonly List<Rental> _rentals = new List<Rental>();
            public void Add(Rental r) { _rentals.Add(r); }
            public Rental GetById(int id) => _rentals.FirstOrDefault(r => r.Id == id);
            public IReadOnlyList<Rental> GetAll() => _rentals.AsReadOnly();
            public void Update(Rental r) { var i = _rentals.FindIndex(x => x.Id == r.Id); if (i >= 0) _rentals[i] = r; }
            public void Remove(int id) { _rentals.RemoveAll(r => r.Id == id); }
            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) =>
                _rentals.Where(r => r.CustomerId == customerId && r.Status != RentalStatus.Returned).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetByMovie(int movieId) =>
                _rentals.Where(r => r.MovieId == movieId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetOverdue() =>
                _rentals.Where(r => r.Status == RentalStatus.Overdue).ToList().AsReadOnly();
            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) => GetAll();
            public Rental ReturnRental(int rentalId) { var r = GetById(rentalId); if (r != null) { r.Status = RentalStatus.Returned; r.ReturnDate = DateTime.Today; } return r; }
            public bool IsMovieRentedOut(int movieId) => _rentals.Any(r => r.MovieId == movieId && r.Status != RentalStatus.Returned);
            public Rental Checkout(Rental rental) { Add(rental); return rental; }

            public Rental Checkout(Rental rental, int maxConcurrentRentals)
            {
                return Checkout(rental);
            }
            public RentalStats GetStats() => new RentalStats { TotalRentals = _rentals.Count };
        }

        private TestCustomerRepository _customers;
        private TestRentalRepository _rentals;
        private LoyaltyPointsService _service;

        [TestInitialize]
        public void Setup()
        {
            _customers = new TestCustomerRepository();
            _rentals = new TestRentalRepository();
            _service = new LoyaltyPointsService(_customers, _rentals);

            _customers.Add(new Customer { Id = 1, Name = "Alice", MembershipType = MembershipType.Basic });
            _customers.Add(new Customer { Id = 2, Name = "Bob", MembershipType = MembershipType.Gold });
            _customers.Add(new Customer { Id = 3, Name = "Carol", MembershipType = MembershipType.Platinum });
        }

        private Rental MakeRental(int id, int customerId, decimal dailyRate, bool returned, bool onTime)
        {
            var rental = new Rental
            {
                Id = id,
                CustomerId = customerId,
                MovieId = 1,
                MovieName = "Test Movie",
                RentalDate = DateTime.Today.AddDays(-10),
                DueDate = DateTime.Today.AddDays(-3),
                DailyRate = dailyRate,
                Status = returned ? RentalStatus.Returned : RentalStatus.Active
            };
            if (returned)
                rental.ReturnDate = onTime ? DateTime.Today.AddDays(-4) : DateTime.Today;
            _rentals.Add(rental);
            return rental;
        }

        // ── Tier multiplier tests ────────────────────────────────────

        [TestMethod]
        public void GetTierMultiplier_Basic_Returns1()
        {
            Assert.AreEqual(1.0m, LoyaltyPointsService.GetTierMultiplier(MembershipType.Basic));
        }

        [TestMethod]
        public void GetTierMultiplier_Gold_Returns1_5()
        {
            Assert.AreEqual(1.5m, LoyaltyPointsService.GetTierMultiplier(MembershipType.Gold));
        }

        [TestMethod]
        public void GetTierMultiplier_Platinum_Returns2()
        {
            Assert.AreEqual(2.0m, LoyaltyPointsService.GetTierMultiplier(MembershipType.Platinum));
        }

        // ── Earn points tests ────────────────────────────────────────

        [TestMethod]
        public void EarnPoints_BasicMember_CalculatesCorrectly()
        {
            MakeRental(1, 1, 3.99m, true, true);
            var tx = _service.EarnPointsForRental(1);

            // 7 days * 3.99 = 27.93 total, but returned early so fewer days
            Assert.IsTrue(tx.Points > 0);
            Assert.AreEqual(TransactionType.Earned, tx.Type);
            Assert.AreEqual(1, tx.CustomerId);
        }

        [TestMethod]
        public void EarnPoints_GoldMember_Gets1_5xMultiplier()
        {
            MakeRental(1, 2, 3.99m, true, true);
            var tx = _service.EarnPointsForRental(1);

            // Gold gets 1.5x multiplier
            Assert.IsTrue(tx.Points > 0);
            Assert.IsTrue(tx.Description.Contains("1.5x"));
        }

        [TestMethod]
        public void EarnPoints_OnTimeReturn_GetsBonus()
        {
            MakeRental(1, 1, 3.99m, true, true);
            var tx = _service.EarnPointsForRental(1);

            Assert.IsTrue(tx.Description.Contains("on-time bonus"));
        }

        [TestMethod]
        public void EarnPoints_LateReturn_NoBonus()
        {
            MakeRental(1, 1, 3.99m, true, false);
            var tx = _service.EarnPointsForRental(1);

            Assert.IsFalse(tx.Description.Contains("on-time bonus"));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void EarnPoints_Duplicate_Throws()
        {
            MakeRental(1, 1, 3.99m, true, true);
            _service.EarnPointsForRental(1);
            _service.EarnPointsForRental(1); // should throw
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void EarnPoints_InvalidRental_Throws()
        {
            _service.EarnPointsForRental(999);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void EarnPoints_ActiveRental_Throws()
        {
            MakeRental(1, 1, 3.99m, false, false);
            _service.EarnPointsForRental(1); // active rental — should throw
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void EarnPoints_OverdueRental_Throws()
        {
            var rental = new Rental
            {
                Id = 50,
                CustomerId = 1,
                MovieId = 1,
                MovieName = "Test Movie",
                RentalDate = DateTime.Today.AddDays(-20),
                DueDate = DateTime.Today.AddDays(-13),
                DailyRate = 3.99m,
                Status = RentalStatus.Overdue
            };
            _rentals.Add(rental);
            _service.EarnPointsForRental(50); // overdue, not returned — should throw
        }

        // ── Redeem points tests ──────────────────────────────────────

        [TestMethod]
        public void RedeemPoints_SufficientBalance_Succeeds()
        {
            // Give Alice some high-value rentals
            for (int i = 1; i <= 10; i++)
            {
                MakeRental(i, 1, 10.00m, true, true);
                _service.EarnPointsForRental(i);
            }

            var balance = _service.GetBalance(1);
            Assert.IsTrue(balance >= LoyaltyPointsService.HalfOffCost);

            var tx = _service.RedeemPoints(1, RewardType.HalfOffRental);
            Assert.AreEqual(TransactionType.Redeemed, tx.Type);
            Assert.IsTrue(tx.Points < 0);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void RedeemPoints_InsufficientBalance_Throws()
        {
            _service.RedeemPoints(1, RewardType.FreeRental);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RedeemPoints_InvalidCustomer_Throws()
        {
            _service.RedeemPoints(999, RewardType.FreeRental);
        }

        // ── Balance & history tests ──────────────────────────────────

        [TestMethod]
        public void GetBalance_NoTransactions_ReturnsZero()
        {
            Assert.AreEqual(0, _service.GetBalance(1));
        }

        [TestMethod]
        public void GetHistory_ReturnsDescendingOrder()
        {
            MakeRental(1, 1, 5.00m, true, true);
            MakeRental(2, 1, 5.00m, true, false);
            _service.EarnPointsForRental(1);
            _service.EarnPointsForRental(2);

            var history = _service.GetHistory(1);
            Assert.AreEqual(2, history.Count);
            Assert.IsTrue(history[0].Timestamp >= history[1].Timestamp);
        }

        // ── Summary tests ────────────────────────────────────────────

        [TestMethod]
        public void GetSummary_ReturnsCorrectData()
        {
            MakeRental(1, 2, 5.00m, true, true);
            _service.EarnPointsForRental(1);

            var summary = _service.GetSummary(2);
            Assert.AreEqual("Bob", summary.CustomerName);
            Assert.AreEqual(MembershipType.Gold, summary.MembershipTier);
            Assert.AreEqual(1.5m, summary.TierMultiplier);
            Assert.IsTrue(summary.CurrentBalance > 0);
            Assert.AreEqual(4, summary.AvailableRewards.Count);
            Assert.IsTrue(summary.RecentTransactions.Count > 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetSummary_InvalidCustomer_Throws()
        {
            _service.GetSummary(999);
        }

        // ── Leaderboard tests ────────────────────────────────────────

        [TestMethod]
        public void GetLeaderboard_RanksCorrectly()
        {
            // Platinum earns more due to 2x multiplier
            MakeRental(1, 1, 5.00m, true, true);
            MakeRental(2, 3, 5.00m, true, true);
            _service.EarnPointsForRental(1);
            _service.EarnPointsForRental(2);

            var board = _service.GetLeaderboard();
            Assert.AreEqual(2, board.Count);
            Assert.AreEqual(1, board[0].Rank);
            Assert.AreEqual("Carol", board[0].CustomerName); // Platinum = 2x
            Assert.IsTrue(board[0].CurrentBalance > board[1].CurrentBalance);
        }

        [TestMethod]
        public void GetLeaderboard_EmptyLedger_ReturnsEmpty()
        {
            var board = _service.GetLeaderboard();
            Assert.AreEqual(0, board.Count);
        }

        // ── Reward cost tests ────────────────────────────────────────

        [TestMethod]
        public void GetRewardCost_FreeRental_Returns500()
        {
            Assert.AreEqual(500, LoyaltyPointsService.GetRewardCost(RewardType.FreeRental));
        }

        [TestMethod]
        public void GetRewardCost_HalfOff_Returns250()
        {
            Assert.AreEqual(250, LoyaltyPointsService.GetRewardCost(RewardType.HalfOffRental));
        }

        [TestMethod]
        public void GetRewardCost_ExtendedRental_Returns150()
        {
            Assert.AreEqual(150, LoyaltyPointsService.GetRewardCost(RewardType.ExtendedRental));
        }

        // ── Constructor validation ───────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new LoyaltyPointsService(null, _rentals);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new LoyaltyPointsService(_customers, null);
        }
    }
}
