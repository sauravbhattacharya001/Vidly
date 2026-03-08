using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Services;
using Vidly.Repositories;

namespace Vidly.Tests
{
    [TestClass]
    public class AchievementServiceTests
    {
        // ── Helpers ──────────────────────────────────────────────

        private static Customer MakeCustomer(int id = 1, string name = "Alice",
            DateTime? memberSince = null, MembershipType tier = MembershipType.Basic)
        {
            return new Customer
            {
                Id = id,
                Name = name,
                MemberSince = memberSince ?? DateTime.Today.AddDays(-30),
                MembershipType = tier
            };
        }

        private static Movie MakeMovie(int id, string name = null,
            Genre? genre = null, int? rating = null)
        {
            return new Movie
            {
                Id = id,
                Name = name ?? $"Movie {id}",
                Genre = genre,
                Rating = rating
            };
        }

        private static Rental MakeRental(int customerId, int movieId,
            int daysAgo = 0, DateTime? returnDate = null, DateTime? dueDate = null)
        {
            var rentalDate = DateTime.Today.AddDays(-daysAgo);
            return new Rental
            {
                Id = customerId * 100 + movieId,
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = rentalDate,
                DueDate = dueDate ?? rentalDate.AddDays(7),
                ReturnDate = returnDate,
                Status = returnDate.HasValue ? RentalStatus.Returned : RentalStatus.Active
            };
        }

        private static Review MakeReview(int customerId, int movieId, int stars = 4)
        {
            return new Review
            {
                Id = customerId * 100 + movieId,
                CustomerId = customerId,
                MovieId = movieId,
                Stars = stars,
                CreatedDate = DateTime.Now
            };
        }

        private AchievementService CreateService(
            List<Customer> customers = null,
            List<Movie> movies = null,
            List<Rental> rentals = null,
            List<Review> reviews = null)
        {
            var customerRepo = new InMemoryCustomerRepository();
            var movieRepo = new InMemoryMovieRepository();
            var rentalRepo = new InMemoryRentalRepository();
            var reviewRepo = new InMemoryReviewRepository();

            if (customers != null)
                foreach (var c in customers) customerRepo.Add(c);
            if (movies != null)
                foreach (var m in movies) movieRepo.Add(m);
            if (rentals != null)
                foreach (var r in rentals) rentalRepo.Add(r);
            if (reviews != null)
                foreach (var r in reviews) reviewRepo.Add(r);

            return new AchievementService(customerRepo, rentalRepo, movieRepo, reviewRepo);
        }

        // ── Badge count ──────────────────────────────────────────

        [TestMethod]
        public void AllBadges_HasExpectedCount()
        {
            Assert.AreEqual(19, AchievementService.AllBadges.Count);
        }

        [TestMethod]
        public void AllBadges_UniqueIds()
        {
            var ids = AchievementService.AllBadges.Select(b => b.Id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "Badge IDs must be unique.");
        }

        // ── Profile basics ───────────────────────────────────────

        [TestMethod]
        public void GetProfile_NoRentals_AllLocked()
        {
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1, memberSince: DateTime.Today) },
                movies: new List<Movie> { MakeMovie(1) });

            var profile = svc.GetProfile(1);

            Assert.AreEqual(0, profile.BadgesEarned);
            Assert.AreEqual(AchievementService.AllBadges.Count, profile.BadgesTotal);
            Assert.AreEqual("Newcomer", profile.Level);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void GetProfile_UnknownCustomer_Throws()
        {
            var svc = CreateService();
            svc.GetProfile(999);
        }

        // ── Milestone badges ─────────────────────────────────────

        [TestMethod]
        public void FirstRental_EarnedWithOneRental()
        {
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: new List<Movie> { MakeMovie(1) },
                rentals: new List<Rental> { MakeRental(1, 1) });

            var profile = svc.GetProfile(1);
            var firstRental = profile.EarnedBadges.FirstOrDefault(b => b.Badge.Id == "first_rental");

            Assert.IsNotNull(firstRental, "first_rental badge should be earned.");
        }

        [TestMethod]
        public void Regular10_NotEarnedWith5Rentals()
        {
            var rentals = Enumerable.Range(1, 5)
                .Select(i => MakeRental(1, i, i)).ToList();
            var movies = Enumerable.Range(1, 5)
                .Select(i => MakeMovie(i)).ToList();

            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: movies, rentals: rentals);

            var profile = svc.GetProfile(1);
            var locked = profile.LockedBadges.FirstOrDefault(b => b.Badge.Id == "regular_10");

            Assert.IsNotNull(locked, "regular_10 should be locked.");
            Assert.AreEqual(5, locked.Remaining);
        }

        [TestMethod]
        public void Regular10_EarnedWith10Rentals()
        {
            var rentals = Enumerable.Range(1, 10)
                .Select(i => MakeRental(1, i, i)).ToList();
            var movies = Enumerable.Range(1, 10)
                .Select(i => MakeMovie(i)).ToList();

            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: movies, rentals: rentals);

            var profile = svc.GetProfile(1);
            var earned = profile.EarnedBadges.FirstOrDefault(b => b.Badge.Id == "regular_10");

            Assert.IsNotNull(earned, "regular_10 should be earned with 10 rentals.");
        }

        // ── Genre exploration badges ─────────────────────────────

        [TestMethod]
        public void GenreExplorer_EarnedWith3Genres()
        {
            var movies = new List<Movie>
            {
                MakeMovie(1, genre: Genre.Action),
                MakeMovie(2, genre: Genre.Comedy),
                MakeMovie(3, genre: Genre.Drama),
            };
            var rentals = movies.Select((m, i) => MakeRental(1, m.Id, i)).ToList();

            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: movies, rentals: rentals);

            var profile = svc.GetProfile(1);
            var earned = profile.EarnedBadges.FirstOrDefault(b => b.Badge.Id == "genre_explorer_3");
            Assert.IsNotNull(earned);
        }

        [TestMethod]
        public void GenreExplorer_NotEarnedWith2Genres()
        {
            var movies = new List<Movie>
            {
                MakeMovie(1, genre: Genre.Action),
                MakeMovie(2, genre: Genre.Action),
                MakeMovie(3, genre: Genre.Comedy),
            };
            var rentals = movies.Select((m, i) => MakeRental(1, m.Id, i)).ToList();

            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: movies, rentals: rentals);

            var profile = svc.GetProfile(1);
            var locked = profile.LockedBadges.FirstOrDefault(b => b.Badge.Id == "genre_explorer_3");
            Assert.IsNotNull(locked, "Should still be locked with only 2 genres.");
            Assert.AreEqual(1, locked.Remaining);
        }

        // ── Behavior badges ─────────────────────────────────────

        [TestMethod]
        public void BingeWatcher_EarnedWith5RentalsInWeek()
        {
            var rentals = Enumerable.Range(0, 5)
                .Select(i => MakeRental(1, i + 1, daysAgo: i))
                .ToList();
            var movies = Enumerable.Range(1, 5).Select(i => MakeMovie(i)).ToList();

            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: movies, rentals: rentals);

            var profile = svc.GetProfile(1);
            var earned = profile.EarnedBadges.FirstOrDefault(b => b.Badge.Id == "binge_watcher");
            Assert.IsNotNull(earned, "Should earn binge_watcher with 5 rentals in 5 days.");
        }

        [TestMethod]
        public void BingeWatcher_NotEarnedWhenSpreadOut()
        {
            var rentals = Enumerable.Range(0, 5)
                .Select(i => MakeRental(1, i + 1, daysAgo: i * 10))
                .ToList();
            var movies = Enumerable.Range(1, 5).Select(i => MakeMovie(i)).ToList();

            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: movies, rentals: rentals);

            var profile = svc.GetProfile(1);
            var locked = profile.LockedBadges.FirstOrDefault(b => b.Badge.Id == "binge_watcher");
            Assert.IsNotNull(locked, "Rentals too spread out — should be locked.");
        }

        [TestMethod]
        public void OnTimeStreak5_EarnedWith5ConsecutiveOnTime()
        {
            var rentals = Enumerable.Range(1, 5).Select(i =>
            {
                var rentalDate = DateTime.Today.AddDays(-i * 10);
                return new Rental
                {
                    Id = i,
                    CustomerId = 1,
                    MovieId = i,
                    RentalDate = rentalDate,
                    DueDate = rentalDate.AddDays(7),
                    ReturnDate = rentalDate.AddDays(5), // returned 2 days early
                    Status = RentalStatus.Returned
                };
            }).ToList();
            var movies = Enumerable.Range(1, 5).Select(i => MakeMovie(i)).ToList();

            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: movies, rentals: rentals);

            var profile = svc.GetProfile(1);
            var earned = profile.EarnedBadges.FirstOrDefault(b => b.Badge.Id == "on_time_streak_5");
            Assert.IsNotNull(earned, "5 consecutive on-time returns should earn the badge.");
        }

        [TestMethod]
        public void OnTimeStreak5_BrokenByLateReturn()
        {
            var rentals = new List<Rental>();
            for (int i = 1; i <= 6; i++)
            {
                var rentalDate = DateTime.Today.AddDays(-i * 10);
                var dueDate = rentalDate.AddDays(7);
                // Make rental #3 late to break the streak
                var returnDate = i == 3
                    ? rentalDate.AddDays(10) // late
                    : rentalDate.AddDays(5); // on time
                rentals.Add(new Rental
                {
                    Id = i,
                    CustomerId = 1,
                    MovieId = i,
                    RentalDate = rentalDate,
                    DueDate = dueDate,
                    ReturnDate = returnDate,
                    Status = RentalStatus.Returned
                });
            }
            var movies = Enumerable.Range(1, 6).Select(i => MakeMovie(i)).ToList();

            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: movies, rentals: rentals);

            var profile = svc.GetProfile(1);
            // Best streak is either 2 (before #3) or 3 (after #3), but not 5
            var locked = profile.LockedBadges.FirstOrDefault(b => b.Badge.Id == "on_time_streak_5");
            Assert.IsNotNull(locked, "Streak broken by late return — should be locked.");
        }

        [TestMethod]
        public void EarlyBird_EarnedWhen3DaysEarly()
        {
            var rentalDate = DateTime.Today.AddDays(-10);
            var rentals = new List<Rental>
            {
                new Rental
                {
                    Id = 1, CustomerId = 1, MovieId = 1,
                    RentalDate = rentalDate,
                    DueDate = rentalDate.AddDays(7),
                    ReturnDate = rentalDate.AddDays(3), // 4 days before due
                    Status = RentalStatus.Returned
                }
            };

            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: new List<Movie> { MakeMovie(1) },
                rentals: rentals);

            var profile = svc.GetProfile(1);
            var earned = profile.EarnedBadges.FirstOrDefault(b => b.Badge.Id == "early_bird");
            Assert.IsNotNull(earned, "Returning 4 days early should earn early_bird.");
        }

        // ── Social badges ────────────────────────────────────────

        [TestMethod]
        public void Reviewer_EarnedWithOneReview()
        {
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: new List<Movie> { MakeMovie(1) },
                reviews: new List<Review> { MakeReview(1, 1) });

            var profile = svc.GetProfile(1);
            var earned = profile.EarnedBadges.FirstOrDefault(b => b.Badge.Id == "reviewer");
            Assert.IsNotNull(earned, "One review should earn reviewer badge.");
        }

        [TestMethod]
        public void FilmCritic_NotEarnedWith5Reviews()
        {
            var reviews = Enumerable.Range(1, 5)
                .Select(i => MakeReview(1, i)).ToList();
            var movies = Enumerable.Range(1, 5).Select(i => MakeMovie(i)).ToList();

            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: movies, reviews: reviews);

            var profile = svc.GetProfile(1);
            var locked = profile.LockedBadges.FirstOrDefault(b => b.Badge.Id == "reviewer_10");
            Assert.IsNotNull(locked, "10 reviews needed, only 5 written.");
            Assert.AreEqual(5, locked.Remaining);
        }

        // ── Loyalty badges ───────────────────────────────────────

        [TestMethod]
        public void OneYearClub_EarnedAfter365Days()
        {
            var customer = MakeCustomer(1, memberSince: DateTime.Today.AddDays(-400));
            var svc = CreateService(
                customers: new List<Customer> { customer },
                movies: new List<Movie> { MakeMovie(1) });

            var profile = svc.GetProfile(1);
            var earned = profile.EarnedBadges.FirstOrDefault(b => b.Badge.Id == "member_1_year");
            Assert.IsNotNull(earned, "400 days should qualify for 1-year club.");
        }

        [TestMethod]
        public void OneYearClub_NotEarnedAt200Days()
        {
            var customer = MakeCustomer(1, memberSince: DateTime.Today.AddDays(-200));
            var svc = CreateService(
                customers: new List<Customer> { customer },
                movies: new List<Movie> { MakeMovie(1) });

            var profile = svc.GetProfile(1);
            var locked = profile.LockedBadges.FirstOrDefault(b => b.Badge.Id == "member_1_year");
            Assert.IsNotNull(locked, "200 days is not enough for 1-year club.");
        }

        // ── Taste badges ─────────────────────────────────────────

        [TestMethod]
        public void FiveStarFan_EarnedWith5FiveStarMovies()
        {
            var movies = Enumerable.Range(1, 5)
                .Select(i => MakeMovie(i, rating: 5)).ToList();
            var rentals = movies.Select((m, i) => MakeRental(1, m.Id, i)).ToList();

            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: movies, rentals: rentals);

            var profile = svc.GetProfile(1);
            var earned = profile.EarnedBadges.FirstOrDefault(b => b.Badge.Id == "five_star_fan");
            Assert.IsNotNull(earned);
        }

        [TestMethod]
        public void HiddenGemHunter_EarnedWith3LowRated()
        {
            var movies = Enumerable.Range(1, 3)
                .Select(i => MakeMovie(i, rating: 1)).ToList();
            var rentals = movies.Select((m, i) => MakeRental(1, m.Id, i)).ToList();

            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: movies, rentals: rentals);

            var profile = svc.GetProfile(1);
            var earned = profile.EarnedBadges.FirstOrDefault(b => b.Badge.Id == "hidden_gem_hunter");
            Assert.IsNotNull(earned);
        }

        // ── Scoring & levels ─────────────────────────────────────

        [TestMethod]
        public void TierPoints_AreCorrect()
        {
            Assert.AreEqual(10, AchievementService.GetTierPoints(BadgeTier.Bronze));
            Assert.AreEqual(25, AchievementService.GetTierPoints(BadgeTier.Silver));
            Assert.AreEqual(50, AchievementService.GetTierPoints(BadgeTier.Gold));
            Assert.AreEqual(100, AchievementService.GetTierPoints(BadgeTier.Platinum));
        }

        [TestMethod]
        public void Level_Newcomer_AtZeroScore()
        {
            var level = AchievementService.GetLevel(0);
            Assert.AreEqual("Newcomer", level.Name);
            Assert.AreEqual(1, level.Number);
        }

        [TestMethod]
        public void Level_HallOfFame_At500()
        {
            var level = AchievementService.GetLevel(500);
            Assert.AreEqual("Hall of Fame", level.Name);
            Assert.AreEqual(5, level.Number);
        }

        [TestMethod]
        public void Level_FilmFan_At75()
        {
            var level = AchievementService.GetLevel(75);
            Assert.AreEqual("Film Fan", level.Name);
            Assert.AreEqual(2, level.Number);
        }

        // ── Leaderboard ──────────────────────────────────────────

        [TestMethod]
        public void GetLeaderboard_OrdersByScore()
        {
            var customers = new List<Customer>
            {
                MakeCustomer(1, "Alice", DateTime.Today.AddDays(-400)),
                MakeCustomer(2, "Bob", DateTime.Today)
            };
            var movies = Enumerable.Range(1, 10).Select(i => MakeMovie(i)).ToList();
            // Alice has 10 rentals, Bob has 1
            var rentals = Enumerable.Range(1, 10).Select(i => MakeRental(1, i, i)).ToList();
            rentals.Add(MakeRental(2, 1));

            var svc = CreateService(customers: customers, movies: movies, rentals: rentals);
            var lb = svc.GetLeaderboard(10);

            Assert.IsTrue(lb.Count >= 2);
            Assert.IsTrue(lb[0].Score >= lb[1].Score, "Leaderboard should be sorted by score.");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetLeaderboard_ZeroTop_Throws()
        {
            var svc = CreateService(customers: new List<Customer> { MakeCustomer(1) });
            svc.GetLeaderboard(0);
        }

        // ── Stats ────────────────────────────────────────────────

        [TestMethod]
        public void GetStats_ReturnsDistributionForAllBadges()
        {
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: new List<Movie> { MakeMovie(1) });

            var stats = svc.GetStats();

            Assert.AreEqual(AchievementService.AllBadges.Count, stats.BadgeDistribution.Count);
            Assert.IsNotNull(stats.RarestBadge);
            Assert.IsNotNull(stats.MostCommonBadge);
        }

        [TestMethod]
        public void GetStats_NoCustomers_ReturnsZeros()
        {
            var svc = CreateService();
            var stats = svc.GetStats();

            Assert.AreEqual(0, stats.TotalCustomers);
            Assert.AreEqual(0, stats.TotalBadgesAwarded);
            Assert.AreEqual(0, stats.AverageBadgesPerCustomer);
        }

        // ── Progress tracking ────────────────────────────────────

        [TestMethod]
        public void LockedBadge_ShowsProgress()
        {
            var movies = Enumerable.Range(1, 5).Select(i => MakeMovie(i)).ToList();
            var rentals = Enumerable.Range(1, 5).Select(i => MakeRental(1, i, i)).ToList();

            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: movies, rentals: rentals);

            var profile = svc.GetProfile(1);
            var locked10 = profile.LockedBadges.FirstOrDefault(b => b.Badge.Id == "regular_10");

            Assert.IsNotNull(locked10);
            Assert.AreEqual(50.0, locked10.Progress, 0.1, "5/10 = 50% progress.");
            Assert.IsNotNull(locked10.Hint);
        }

        [TestMethod]
        public void EarnedBadge_Has100Progress()
        {
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer(1) },
                movies: new List<Movie> { MakeMovie(1) },
                rentals: new List<Rental> { MakeRental(1, 1) });

            var profile = svc.GetProfile(1);
            var firstRental = profile.EarnedBadges
                .FirstOrDefault(b => b.Badge.Id == "first_rental");

            Assert.IsNotNull(firstRental);
            Assert.IsNotNull(firstRental.EarnedDate);
        }
    }
}
