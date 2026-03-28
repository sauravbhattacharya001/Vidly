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

        // ── Extended coverage — behavior edge cases ──────────────

        [TestMethod]
        public void GenreMaster_NotEarned_MissingOneGenre()
        {
            var allGenres = (Genre[])Enum.GetValues(typeof(Genre));
            var movies = new List<Movie>();
            var rentals = new List<Rental>();
            int id = 1;
            // Add all genres except the last one
            foreach (var g in allGenres.Take(allGenres.Length - 1))
            {
                movies.Add(MakeMovie(id, genre: g));
                rentals.Add(MakeRental(1, id, daysAgo: id));
                id++;
            }
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer() },
                movies: movies,
                rentals: rentals);
            var profile = svc.GetProfile(1);
            var genreMaster = profile.LockedBadges.FirstOrDefault(b => b.Badge.Id == "genre_master_all");
            Assert.IsNotNull(genreMaster, "genre_master_all should be locked");
            Assert.AreEqual(1, genreMaster.Remaining);
        }

        [TestMethod]
        public void GenreMaster_Earned_AllGenres()
        {
            var allGenres = (Genre[])Enum.GetValues(typeof(Genre));
            var movies = new List<Movie>();
            var rentals = new List<Rental>();
            int id = 1;
            foreach (var g in allGenres)
            {
                movies.Add(MakeMovie(id, genre: g));
                rentals.Add(MakeRental(1, id, daysAgo: id));
                id++;
            }
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer() },
                movies: movies,
                rentals: rentals);
            var profile = svc.GetProfile(1);
            Assert.IsTrue(profile.EarnedBadges.Any(b => b.Badge.Id == "genre_master_all"));
        }

        [TestMethod]
        public void OnTimeStreak15_EarnedWith15ConsecutiveOnTime()
        {
            var rentals = new List<Rental>();
            for (int i = 1; i <= 15; i++)
            {
                var due = DateTime.Today.AddDays(-100 + i * 5);
                rentals.Add(new Rental
                {
                    Id = i, CustomerId = 1, MovieId = i,
                    RentalDate = due.AddDays(-7),
                    DueDate = due,
                    ReturnDate = due.AddDays(-1), // on time
                    Status = RentalStatus.Returned
                });
            }
            var movies = Enumerable.Range(1, 15).Select(i => MakeMovie(i, genre: Genre.Action)).ToList();
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer() },
                movies: movies,
                rentals: rentals);
            var profile = svc.GetProfile(1);
            Assert.IsTrue(profile.EarnedBadges.Any(b => b.Badge.Id == "on_time_streak_15"));
        }

        [TestMethod]
        public void OnTimeStreak15_NotEarned_LateReturnBreaksStreak()
        {
            var rentals = new List<Rental>();
            for (int i = 1; i <= 15; i++)
            {
                var due = DateTime.Today.AddDays(-100 + i * 5);
                var retDate = i == 8 ? due.AddDays(2) : due.AddDays(-1); // late on 8th
                rentals.Add(new Rental
                {
                    Id = i, CustomerId = 1, MovieId = i,
                    RentalDate = due.AddDays(-7), DueDate = due,
                    ReturnDate = retDate, Status = RentalStatus.Returned
                });
            }
            var movies = Enumerable.Range(1, 15).Select(i => MakeMovie(i, genre: Genre.Action)).ToList();
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer() },
                movies: movies,
                rentals: rentals);
            var profile = svc.GetProfile(1);
            var badge = profile.LockedBadges.FirstOrDefault(b => b.Badge.Id == "on_time_streak_15");
            Assert.IsNotNull(badge, "Should be locked because streak was broken");
        }

        [TestMethod]
        public void EarlyBird_NotEarned_OnlyTwoDaysEarly()
        {
            var due = DateTime.Today.AddDays(-5);
            var rentals = new List<Rental>
            {
                new Rental
                {
                    Id = 1, CustomerId = 1, MovieId = 1,
                    RentalDate = due.AddDays(-7), DueDate = due,
                    ReturnDate = due.AddDays(-2), // only 2 days early
                    Status = RentalStatus.Returned
                }
            };
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer() },
                movies: new List<Movie> { MakeMovie(1) },
                rentals: rentals);
            var profile = svc.GetProfile(1);
            var badge = profile.LockedBadges.FirstOrDefault(b => b.Badge.Id == "early_bird");
            Assert.IsNotNull(badge, "2 days early is not enough (need 3+)");
        }

        [TestMethod]
        public void FiveStarFan_NotEarned_OnlyFourFiveStarMovies()
        {
            var movies = new List<Movie>();
            var rentals = new List<Rental>();
            for (int i = 1; i <= 4; i++)
            {
                movies.Add(MakeMovie(i, rating: 5));
                rentals.Add(MakeRental(1, i, daysAgo: i));
            }
            movies.Add(MakeMovie(5, rating: 3)); // not 5-star
            rentals.Add(MakeRental(1, 5, daysAgo: 5));

            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer() },
                movies: movies,
                rentals: rentals);
            var profile = svc.GetProfile(1);
            var badge = profile.LockedBadges.FirstOrDefault(b => b.Badge.Id == "five_star_fan");
            Assert.IsNotNull(badge, "Only 4 five-star rentals, need 5");
            Assert.AreEqual(1, badge.Remaining);
        }

        [TestMethod]
        public void HiddenGemHunter_NotEarned_Only2LowRated()
        {
            var movies = new List<Movie>
            {
                MakeMovie(1, rating: 1),
                MakeMovie(2, rating: 2),
                MakeMovie(3, rating: 4), // not low-rated
            };
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, daysAgo: 3),
                MakeRental(1, 2, daysAgo: 2),
                MakeRental(1, 3, daysAgo: 1),
            };
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer() },
                movies: movies,
                rentals: rentals);
            var profile = svc.GetProfile(1);
            Assert.IsTrue(profile.LockedBadges.Any(b => b.Badge.Id == "hidden_gem_hunter"));
        }

        [TestMethod]
        public void Loyalty_NotEarned_NoMemberSince()
        {
            var customer = MakeCustomer(memberSince: null);
            customer.MemberSince = null;
            var svc = CreateService(
                customers: new List<Customer> { customer },
                movies: new List<Movie>(),
                rentals: new List<Rental>());
            var profile = svc.GetProfile(1);
            // All loyalty badges should be locked with hint about membership date
            var loyaltyLocked = profile.LockedBadges
                .Where(b => b.Badge.Category == BadgeCategory.Loyalty)
                .ToList();
            Assert.AreEqual(3, loyaltyLocked.Count); // 1yr, 3yr, 5yr
            Assert.IsTrue(loyaltyLocked.All(b => b.Hint.Contains("Membership")));
        }

        // ── Profile scoring & levels ─────────────────────────────

        [TestMethod]
        public void Profile_Score_SumOfTierPoints()
        {
            // Create enough rentals to earn first_rental (Bronze=10) and early_bird (Bronze=10)
            var due = DateTime.Today.AddDays(-10);
            var rentals = new List<Rental>
            {
                new Rental
                {
                    Id = 1, CustomerId = 1, MovieId = 1,
                    RentalDate = due.AddDays(-7), DueDate = due,
                    ReturnDate = due.AddDays(-4), // 4 days early
                    Status = RentalStatus.Returned
                }
            };
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer() },
                movies: new List<Movie> { MakeMovie(1) },
                rentals: rentals);
            var profile = svc.GetProfile(1);
            // first_rental (Bronze 10) + early_bird (Bronze 10) should be earned
            var earnedIds = profile.EarnedBadges.Select(b => b.Badge.Id).ToList();
            Assert.IsTrue(earnedIds.Contains("first_rental"));
            Assert.IsTrue(earnedIds.Contains("early_bird"));
            Assert.AreEqual(profile.TotalScore, profile.EarnedBadges.Sum(b => AchievementService.GetTierPoints(b.Badge.Tier)));
        }

        [TestMethod]
        public void Level_MovieMaven_At150()
        {
            var level = AchievementService.GetLevel(150);
            Assert.AreEqual("Movie Maven", level.Name);
            Assert.AreEqual(3, level.Number);
            Assert.AreEqual(300, level.NextThreshold);
        }

        [TestMethod]
        public void Level_Cinephile_At300()
        {
            var level = AchievementService.GetLevel(300);
            Assert.AreEqual("Cinephile", level.Name);
            Assert.AreEqual(4, level.Number);
        }

        [TestMethod]
        public void Level_Boundaries()
        {
            Assert.AreEqual("Newcomer", AchievementService.GetLevel(0).Name);
            Assert.AreEqual("Newcomer", AchievementService.GetLevel(49).Name);
            Assert.AreEqual("Film Fan", AchievementService.GetLevel(50).Name);
            Assert.AreEqual("Film Fan", AchievementService.GetLevel(149).Name);
            Assert.AreEqual("Movie Maven", AchievementService.GetLevel(150).Name);
            Assert.AreEqual("Cinephile", AchievementService.GetLevel(300).Name);
            Assert.AreEqual("Hall of Fame", AchievementService.GetLevel(500).Name);
            Assert.AreEqual("Hall of Fame", AchievementService.GetLevel(1000).Name);
        }

        [TestMethod]
        public void TierPoints_AllTiers()
        {
            Assert.AreEqual(10, AchievementService.GetTierPoints(BadgeTier.Bronze));
            Assert.AreEqual(25, AchievementService.GetTierPoints(BadgeTier.Silver));
            Assert.AreEqual(50, AchievementService.GetTierPoints(BadgeTier.Gold));
            Assert.AreEqual(100, AchievementService.GetTierPoints(BadgeTier.Platinum));
        }

        // ── Leaderboard edge cases ───────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Leaderboard_ThrowsOnZero()
        {
            var svc = CreateService(
                customers: new List<Customer>(),
                movies: new List<Movie>(),
                rentals: new List<Rental>());
            svc.GetLeaderboard(0);
        }

        [TestMethod]
        public void Leaderboard_MultipleCustomers_OrderedByScore()
        {
            var customers = new List<Customer>
            {
                MakeCustomer(1, "Alice"),
                MakeCustomer(2, "Bob")
            };
            var movies = new List<Movie>
            {
                MakeMovie(1, genre: Genre.Action),
                MakeMovie(2, genre: Genre.Comedy),
                MakeMovie(3, genre: Genre.Drama),
            };
            // Alice has 3 rentals (first_rental badge), Bob has 0
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, daysAgo: 5),
                MakeRental(1, 2, daysAgo: 4),
                MakeRental(1, 3, daysAgo: 3),
            };
            var svc = CreateService(customers: customers, movies: movies, rentals: rentals);
            var board = svc.GetLeaderboard(10);
            Assert.AreEqual(2, board.Count);
            Assert.IsTrue(board[0].Score >= board[1].Score);
            Assert.AreEqual(1, board[0].CustomerId); // Alice has badges
        }

        [TestMethod]
        public void Leaderboard_TopLimitsResults()
        {
            var customers = Enumerable.Range(1, 5)
                .Select(i => MakeCustomer(i, $"Cust{i}"))
                .ToList();
            var svc = CreateService(
                customers: customers,
                movies: new List<Movie>(),
                rentals: new List<Rental>());
            var board = svc.GetLeaderboard(2);
            Assert.AreEqual(2, board.Count);
        }

        // ── Stats edge cases ─────────────────────────────────────

        [TestMethod]
        public void Stats_MultipleCustomers_CorrectCounts()
        {
            var customers = new List<Customer>
            {
                MakeCustomer(1, "Alice"),
                MakeCustomer(2, "Bob")
            };
            var movies = new List<Movie> { MakeMovie(1, genre: Genre.Action) };
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, daysAgo: 5),
                MakeRental(2, 1, daysAgo: 3),
            };
            var svc = CreateService(customers: customers, movies: movies, rentals: rentals);
            var stats = svc.GetStats();
            Assert.AreEqual(2, stats.TotalCustomers);
            Assert.IsTrue(stats.TotalBadgesAwarded > 0);
            Assert.IsNotNull(stats.RarestBadge);
            Assert.IsNotNull(stats.MostCommonBadge);
            Assert.IsTrue(stats.BadgeDistribution.Count == AchievementService.AllBadges.Count);
        }

        [TestMethod]
        public void Stats_AverageBadgesPerCustomer_Calculated()
        {
            var customers = new List<Customer>
            {
                MakeCustomer(1, "Alice"),
                MakeCustomer(2, "Bob")
            };
            var movies = new List<Movie> { MakeMovie(1) };
            var rentals = new List<Rental> { MakeRental(1, 1, daysAgo: 1) };
            var svc = CreateService(customers: customers, movies: movies, rentals: rentals);
            var stats = svc.GetStats();
            // Only Alice has first_rental badge
            Assert.IsTrue(stats.AverageBadgesPerCustomer > 0);
        }

        // ── GetProfile error handling ────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void GetProfile_UnknownCustomer_EmptyLists_Throws()
        {
            var svc = CreateService(
                customers: new List<Customer>(),
                movies: new List<Movie>(),
                rentals: new List<Rental>());
            svc.GetProfile(999);
        }

        [TestMethod]
        public void GetProfile_NoReviewRepo_SocialBadgesLocked()
        {
            // Pass null for review repository
            var customerRepo = new InMemoryCustomerRepository();
            var movieRepo = new InMemoryMovieRepository();
            var rentalRepo = new InMemoryRentalRepository();
            customerRepo.Add(MakeCustomer());
            var svc = new AchievementService(customerRepo, rentalRepo, movieRepo, null);
            var profile = svc.GetProfile(1);
            var socialLocked = profile.LockedBadges.Where(b => b.Badge.Category == BadgeCategory.Social).ToList();
            Assert.AreEqual(2, socialLocked.Count); // reviewer, reviewer_10
        }

        // ── Binge watcher edge cases ─────────────────────────────

        [TestMethod]
        public void BingeWatcher_ExactlyOnWeekBoundary()
        {
            // 5 rentals on the same day
            var day = DateTime.Today.AddDays(-10);
            var rentals = Enumerable.Range(1, 5)
                .Select(i => new Rental
                {
                    Id = i, CustomerId = 1, MovieId = i,
                    RentalDate = day,
                    DueDate = day.AddDays(7)
                })
                .ToList();
            var movies = Enumerable.Range(1, 5).Select(i => MakeMovie(i)).ToList();
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer() },
                movies: movies,
                rentals: rentals);
            var profile = svc.GetProfile(1);
            Assert.IsTrue(profile.EarnedBadges.Any(b => b.Badge.Id == "binge_watcher"));
        }

        [TestMethod]
        public void BingeWatcher_FourRentalsInWeek_NotEarned()
        {
            var day = DateTime.Today.AddDays(-10);
            var rentals = Enumerable.Range(1, 4)
                .Select(i => new Rental
                {
                    Id = i, CustomerId = 1, MovieId = i,
                    RentalDate = day.AddDays(i - 1),
                    DueDate = day.AddDays(7)
                })
                .ToList();
            var movies = Enumerable.Range(1, 4).Select(i => MakeMovie(i)).ToList();
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer() },
                movies: movies,
                rentals: rentals);
            var profile = svc.GetProfile(1);
            Assert.IsTrue(profile.LockedBadges.Any(b => b.Badge.Id == "binge_watcher"));
        }

        // ── Profile structure ────────────────────────────────────

        [TestMethod]
        public void Profile_EarnedBadges_OrderedByDateDescending()
        {
            var rentals = new List<Rental>();
            var movies = new List<Movie>();
            for (int i = 1; i <= 10; i++)
            {
                movies.Add(MakeMovie(i, genre: Genre.Action));
                rentals.Add(MakeRental(1, i, daysAgo: 20 - i));
            }
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer() },
                movies: movies,
                rentals: rentals);
            var profile = svc.GetProfile(1);
            // Should have first_rental and regular_10
            Assert.IsTrue(profile.EarnedBadges.Count >= 2);
            // Verify descending order by EarnedDate
            for (int i = 0; i < profile.EarnedBadges.Count - 1; i++)
            {
                if (profile.EarnedBadges[i].EarnedDate.HasValue && profile.EarnedBadges[i + 1].EarnedDate.HasValue)
                    Assert.IsTrue(profile.EarnedBadges[i].EarnedDate >= profile.EarnedBadges[i + 1].EarnedDate);
            }
        }

        [TestMethod]
        public void Profile_LockedBadges_OrderedByRemainingAscending()
        {
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer() },
                movies: new List<Movie>(),
                rentals: new List<Rental>());
            var profile = svc.GetProfile(1);
            for (int i = 0; i < profile.LockedBadges.Count - 1; i++)
                Assert.IsTrue(profile.LockedBadges[i].Remaining <= profile.LockedBadges[i + 1].Remaining);
        }

        [TestMethod]
        public void Profile_BadgesTotal_MatchesAllBadgesCount()
        {
            var svc = CreateService(
                customers: new List<Customer> { MakeCustomer() },
                movies: new List<Movie>(),
                rentals: new List<Rental>());
            var profile = svc.GetProfile(1);
            Assert.AreEqual(AchievementService.AllBadges.Count, profile.BadgesTotal);
            Assert.AreEqual(profile.EarnedBadges.Count + profile.LockedBadges.Count, profile.BadgesTotal);
        }

    }
}
