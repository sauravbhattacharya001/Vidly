using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class RandomMovieViewModelTests
    {
        [TestMethod]
        public void ViewModel_Customers_DefaultsToEmptyList()
        {
            var vm = new RandomMovieViewModel();

            Assert.IsNotNull(vm.Customers,
                "Customers list should be initialized by default.");
            Assert.AreEqual(0, vm.Customers.Count,
                "Default Customers list should be empty.");
        }

        [TestMethod]
        public void ViewModel_Movie_DefaultsToNull()
        {
            var vm = new RandomMovieViewModel();

            Assert.IsNull(vm.Movie,
                "Movie should be null by default.");
        }

        [TestMethod]
        public void ViewModel_CanBePopulated()
        {
            var movie = new Movie { Id = 1, Name = "Inception", ReleaseDate = new DateTime(2010, 7, 16) };
            var customers = new List<Customer>
            {
                new Customer { Id = 1, Name = "Alice" },
                new Customer { Id = 2, Name = "Bob" }
            };

            var vm = new RandomMovieViewModel
            {
                Movie = movie,
                Customers = customers
            };

            Assert.AreEqual("Inception", vm.Movie.Name);
            Assert.AreEqual(2, vm.Customers.Count);
            Assert.AreEqual("Alice", vm.Customers[0].Name);
            Assert.AreEqual("Bob", vm.Customers[1].Name);
        }
    }

    // ── CustomerSearchViewModel ─────────────────────────────────────

    [TestClass]
    public class CustomerSearchViewModelTests
    {
        [TestMethod]
        public void Customers_DefaultsToEmptyList()
        {
            var vm = new CustomerSearchViewModel();
            Assert.IsNotNull(vm.Customers);
            Assert.AreEqual(0, vm.Customers.Count);
        }

        [TestMethod]
        public void Query_DefaultsToNull()
        {
            var vm = new CustomerSearchViewModel();
            Assert.IsNull(vm.Query);
        }

        [TestMethod]
        public void MembershipType_DefaultsToNull()
        {
            var vm = new CustomerSearchViewModel();
            Assert.IsNull(vm.MembershipType);
        }

        [TestMethod]
        public void SortBy_DefaultsToNull()
        {
            var vm = new CustomerSearchViewModel();
            Assert.IsNull(vm.SortBy);
        }

        [TestMethod]
        public void TotalCount_DefaultsToZero()
        {
            var vm = new CustomerSearchViewModel();
            Assert.AreEqual(0, vm.TotalCount);
        }

        [TestMethod]
        public void Stats_DefaultsToNull()
        {
            var vm = new CustomerSearchViewModel();
            Assert.IsNull(vm.Stats);
        }

        [TestMethod]
        public void CanBeFullyPopulated()
        {
            var vm = new CustomerSearchViewModel
            {
                Customers = new List<Customer>
                {
                    new Customer { Id = 1, Name = "Alice", MembershipType = MembershipType.Gold }
                },
                Query = "alice",
                MembershipType = MembershipType.Gold,
                SortBy = "Name",
                TotalCount = 5,
                Stats = new CustomerStats { TotalCustomers = 5, GoldCount = 1 }
            };

            Assert.AreEqual(1, vm.Customers.Count);
            Assert.AreEqual("alice", vm.Query);
            Assert.AreEqual(MembershipType.Gold, vm.MembershipType);
            Assert.AreEqual("Name", vm.SortBy);
            Assert.AreEqual(5, vm.TotalCount);
            Assert.IsNotNull(vm.Stats);
            Assert.AreEqual(5, vm.Stats.TotalCustomers);
        }
    }

    // ── MovieSearchViewModel ────────────────────────────────────────

    [TestClass]
    public class MovieSearchViewModelTests
    {
        [TestMethod]
        public void Movies_DefaultsToEmptyList()
        {
            var vm = new MovieSearchViewModel();
            Assert.IsNotNull(vm.Movies);
            Assert.AreEqual(0, vm.Movies.Count);
        }

        [TestMethod]
        public void Query_DefaultsToNull()
        {
            var vm = new MovieSearchViewModel();
            Assert.IsNull(vm.Query);
        }

        [TestMethod]
        public void Genre_DefaultsToNull()
        {
            var vm = new MovieSearchViewModel();
            Assert.IsNull(vm.Genre);
        }

        [TestMethod]
        public void MinRating_DefaultsToNull()
        {
            var vm = new MovieSearchViewModel();
            Assert.IsNull(vm.MinRating);
        }

        [TestMethod]
        public void SortBy_DefaultsToNull()
        {
            var vm = new MovieSearchViewModel();
            Assert.IsNull(vm.SortBy);
        }

        [TestMethod]
        public void TotalCount_DefaultsToZero()
        {
            var vm = new MovieSearchViewModel();
            Assert.AreEqual(0, vm.TotalCount);
        }

        [TestMethod]
        public void CanBeFullyPopulated()
        {
            var vm = new MovieSearchViewModel
            {
                Movies = new List<Movie>
                {
                    new Movie { Id = 1, Name = "Inception", Genre = Genre.Action, Rating = 5 },
                    new Movie { Id = 2, Name = "Shrek", Genre = Genre.Animation, Rating = 4 }
                },
                Query = "inception",
                Genre = Genre.Action,
                MinRating = 4,
                SortBy = "Rating",
                TotalCount = 10
            };

            Assert.AreEqual(2, vm.Movies.Count);
            Assert.AreEqual("inception", vm.Query);
            Assert.AreEqual(Genre.Action, vm.Genre);
            Assert.AreEqual(4, vm.MinRating);
            Assert.AreEqual("Rating", vm.SortBy);
            Assert.AreEqual(10, vm.TotalCount);
        }
    }

    // ── RentalSearchViewModel ───────────────────────────────────────

    [TestClass]
    public class RentalSearchViewModelTests
    {
        [TestMethod]
        public void Rentals_DefaultsToEmptyList()
        {
            var vm = new RentalSearchViewModel();
            Assert.IsNotNull(vm.Rentals);
            Assert.AreEqual(0, vm.Rentals.Count);
        }

        [TestMethod]
        public void Query_DefaultsToNull()
        {
            var vm = new RentalSearchViewModel();
            Assert.IsNull(vm.Query);
        }

        [TestMethod]
        public void Status_DefaultsToNull()
        {
            var vm = new RentalSearchViewModel();
            Assert.IsNull(vm.Status);
        }

        [TestMethod]
        public void SortBy_DefaultsToNull()
        {
            var vm = new RentalSearchViewModel();
            Assert.IsNull(vm.SortBy);
        }

        [TestMethod]
        public void TotalCount_DefaultsToZero()
        {
            var vm = new RentalSearchViewModel();
            Assert.AreEqual(0, vm.TotalCount);
        }

        [TestMethod]
        public void Stats_DefaultsToNull()
        {
            var vm = new RentalSearchViewModel();
            Assert.IsNull(vm.Stats);
        }

        [TestMethod]
        public void CanBeFullyPopulated()
        {
            var rental = new Rental
            {
                Id = 1,
                CustomerName = "Alice",
                MovieName = "Inception",
                RentalDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(7),
                Status = RentalStatus.Active
            };
            var vm = new RentalSearchViewModel
            {
                Rentals = new List<Rental> { rental },
                Query = "alice",
                Status = RentalStatus.Active,
                SortBy = "RentalDate",
                TotalCount = 3,
                Stats = new RentalStats { TotalRentals = 3, ActiveRentals = 1 }
            };

            Assert.AreEqual(1, vm.Rentals.Count);
            Assert.AreEqual("alice", vm.Query);
            Assert.AreEqual(RentalStatus.Active, vm.Status);
            Assert.AreEqual("RentalDate", vm.SortBy);
            Assert.AreEqual(3, vm.TotalCount);
            Assert.IsNotNull(vm.Stats);
        }
    }

    // ── RentalCheckoutViewModel ─────────────────────────────────────

    [TestClass]
    public class RentalCheckoutViewModelTests
    {
        [TestMethod]
        public void Rental_DefaultsToNull()
        {
            var vm = new RentalCheckoutViewModel();
            Assert.IsNull(vm.Rental);
        }

        [TestMethod]
        public void Customers_DefaultsToEmptyList()
        {
            var vm = new RentalCheckoutViewModel();
            Assert.IsNotNull(vm.Customers);
            Assert.AreEqual(0, vm.Customers.Count);
        }

        [TestMethod]
        public void AvailableMovies_DefaultsToEmptyList()
        {
            var vm = new RentalCheckoutViewModel();
            Assert.IsNotNull(vm.AvailableMovies);
            Assert.AreEqual(0, vm.AvailableMovies.Count);
        }

        [TestMethod]
        public void CanBePopulated()
        {
            var vm = new RentalCheckoutViewModel
            {
                Rental = new Rental { RentalDate = DateTime.Today },
                Customers = new List<Customer>
                {
                    new Customer { Id = 1, Name = "Alice" }
                }.AsReadOnly(),
                AvailableMovies = new List<Movie>
                {
                    new Movie { Id = 1, Name = "Inception" }
                }
            };

            Assert.IsNotNull(vm.Rental);
            Assert.AreEqual(1, vm.Customers.Count);
            Assert.AreEqual(1, vm.AvailableMovies.Count);
        }
    }

    // ── ReviewIndexViewModel ────────────────────────────────────────

    [TestClass]
    public class ReviewIndexViewModelTests
    {
        [TestMethod]
        public void Reviews_DefaultsToNull()
        {
            var vm = new ReviewIndexViewModel();
            Assert.IsNull(vm.Reviews);
        }

        [TestMethod]
        public void Summary_DefaultsToNull()
        {
            var vm = new ReviewIndexViewModel();
            Assert.IsNull(vm.Summary);
        }

        [TestMethod]
        public void TopRated_DefaultsToNull()
        {
            var vm = new ReviewIndexViewModel();
            Assert.IsNull(vm.TopRated);
        }

        [TestMethod]
        public void MovieStats_DefaultsToNull()
        {
            var vm = new ReviewIndexViewModel();
            Assert.IsNull(vm.MovieStats);
        }

        [TestMethod]
        public void SelectedMovie_DefaultsToNull()
        {
            var vm = new ReviewIndexViewModel();
            Assert.IsNull(vm.SelectedMovie);
        }

        [TestMethod]
        public void SearchQuery_DefaultsToNull()
        {
            var vm = new ReviewIndexViewModel();
            Assert.IsNull(vm.SearchQuery);
        }

        [TestMethod]
        public void MinStars_DefaultsToNull()
        {
            var vm = new ReviewIndexViewModel();
            Assert.IsNull(vm.MinStars);
        }

        [TestMethod]
        public void IsError_DefaultsToFalse()
        {
            var vm = new ReviewIndexViewModel();
            Assert.IsFalse(vm.IsError);
        }

        [TestMethod]
        public void StatusMessage_DefaultsToNull()
        {
            var vm = new ReviewIndexViewModel();
            Assert.IsNull(vm.StatusMessage);
        }

        [TestMethod]
        public void CanBeFullyPopulated()
        {
            var reviews = new List<Review>
            {
                new Review { Id = 1, Stars = 5, MovieName = "Inception", CustomerName = "Alice" }
            };

            var vm = new ReviewIndexViewModel
            {
                Reviews = reviews,
                Summary = new ReviewSummary { TotalReviews = 1, AverageStars = 5.0 },
                TopRated = new List<MovieRating>(),
                MovieStats = new ReviewStats { TotalReviews = 1, AverageStars = 5.0 },
                SelectedMovie = new Movie { Id = 1, Name = "Inception" },
                SearchQuery = "inception",
                MinStars = 4,
                Customers = new List<Customer>(),
                Movies = new List<Movie>(),
                StatusMessage = "Review submitted!",
                IsError = false
            };

            Assert.AreEqual(1, vm.Reviews.Count);
            Assert.AreEqual(5.0, vm.Summary.AverageStars);
            Assert.AreEqual("inception", vm.SearchQuery);
            Assert.AreEqual(4, vm.MinStars);
            Assert.AreEqual("Review submitted!", vm.StatusMessage);
            Assert.IsFalse(vm.IsError);
        }

        [TestMethod]
        public void ErrorState_CanBeSet()
        {
            var vm = new ReviewIndexViewModel
            {
                StatusMessage = "Something went wrong.",
                IsError = true
            };

            Assert.IsTrue(vm.IsError);
            Assert.AreEqual("Something went wrong.", vm.StatusMessage);
        }
    }

    // ── DashboardViewModel ──────────────────────────────────────────

    [TestClass]
    public class DashboardViewModelTests
    {
        [TestMethod]
        public void Data_DefaultsToNull()
        {
            var vm = new DashboardViewModel();
            Assert.IsNull(vm.Data);
        }

        [TestMethod]
        public void CanBePopulated()
        {
            var vm = new DashboardViewModel
            {
                Data = new DashboardData()
            };
            Assert.IsNotNull(vm.Data);
        }
    }

    // ── RecommendationViewModel ─────────────────────────────────────

    [TestClass]
    public class RecommendationViewModelTests
    {
        [TestMethod]
        public void Customers_DefaultsToEmptyList()
        {
            var vm = new RecommendationViewModel();
            Assert.IsNotNull(vm.Customers);
            Assert.AreEqual(0, vm.Customers.Count);
        }

        [TestMethod]
        public void SelectedCustomerId_DefaultsToNull()
        {
            var vm = new RecommendationViewModel();
            Assert.IsNull(vm.SelectedCustomerId);
        }

        [TestMethod]
        public void SelectedCustomerName_DefaultsToNull()
        {
            var vm = new RecommendationViewModel();
            Assert.IsNull(vm.SelectedCustomerName);
        }

        [TestMethod]
        public void Result_DefaultsToNull()
        {
            var vm = new RecommendationViewModel();
            Assert.IsNull(vm.Result);
        }

        [TestMethod]
        public void CanBePopulated()
        {
            var vm = new RecommendationViewModel
            {
                Customers = new List<Customer>
                {
                    new Customer { Id = 1, Name = "Alice" }
                }.AsReadOnly(),
                SelectedCustomerId = 1,
                SelectedCustomerName = "Alice",
                Result = new RecommendationResult()
            };

            Assert.AreEqual(1, vm.Customers.Count);
            Assert.AreEqual(1, vm.SelectedCustomerId);
            Assert.AreEqual("Alice", vm.SelectedCustomerName);
            Assert.IsNotNull(vm.Result);
        }
    }

    // ── WatchlistViewModel ──────────────────────────────────────────

    [TestClass]
    public class WatchlistViewModelTests
    {
        [TestMethod]
        public void Customers_DefaultsToEmptyList()
        {
            var vm = new WatchlistViewModel();
            Assert.IsNotNull(vm.Customers);
            Assert.AreEqual(0, vm.Customers.Count);
        }

        [TestMethod]
        public void Items_DefaultsToEmptyList()
        {
            var vm = new WatchlistViewModel();
            Assert.IsNotNull(vm.Items);
            Assert.AreEqual(0, vm.Items.Count);
        }

        [TestMethod]
        public void PopularMovies_DefaultsToEmptyList()
        {
            var vm = new WatchlistViewModel();
            Assert.IsNotNull(vm.PopularMovies);
            Assert.AreEqual(0, vm.PopularMovies.Count);
        }

        [TestMethod]
        public void SelectedCustomerId_DefaultsToNull()
        {
            var vm = new WatchlistViewModel();
            Assert.IsNull(vm.SelectedCustomerId);
        }

        [TestMethod]
        public void SelectedCustomerName_DefaultsToNull()
        {
            var vm = new WatchlistViewModel();
            Assert.IsNull(vm.SelectedCustomerName);
        }

        [TestMethod]
        public void Stats_DefaultsToNull()
        {
            var vm = new WatchlistViewModel();
            Assert.IsNull(vm.Stats);
        }

        [TestMethod]
        public void StatusMessage_DefaultsToNull()
        {
            var vm = new WatchlistViewModel();
            Assert.IsNull(vm.StatusMessage);
        }

        [TestMethod]
        public void IsError_DefaultsToFalse()
        {
            var vm = new WatchlistViewModel();
            Assert.IsFalse(vm.IsError);
        }

        [TestMethod]
        public void CanBeFullyPopulated()
        {
            var vm = new WatchlistViewModel
            {
                Customers = new List<Customer>
                {
                    new Customer { Id = 1, Name = "Alice" }
                }.AsReadOnly(),
                SelectedCustomerId = 1,
                SelectedCustomerName = "Alice",
                Items = new List<WatchlistItem>
                {
                    new WatchlistItem { Id = 1, MovieName = "Inception", Priority = WatchlistPriority.High }
                }.AsReadOnly(),
                Stats = new WatchlistStats { TotalItems = 1 },
                PopularMovies = new List<PopularWatchlistMovie>().AsReadOnly(),
                StatusMessage = "Movie added!",
                IsError = false
            };

            Assert.AreEqual(1, vm.Customers.Count);
            Assert.AreEqual(1, vm.SelectedCustomerId);
            Assert.AreEqual("Alice", vm.SelectedCustomerName);
            Assert.AreEqual(1, vm.Items.Count);
            Assert.AreEqual("Inception", vm.Items[0].MovieName);
            Assert.AreEqual(1, vm.Stats.TotalItems);
            Assert.AreEqual("Movie added!", vm.StatusMessage);
            Assert.IsFalse(vm.IsError);
        }

        [TestMethod]
        public void ErrorState_CanBeSet()
        {
            var vm = new WatchlistViewModel
            {
                StatusMessage = "Movie already on watchlist.",
                IsError = true
            };

            Assert.IsTrue(vm.IsError);
            Assert.AreEqual("Movie already on watchlist.", vm.StatusMessage);
        }
    }

    // ── WatchlistAddViewModel ───────────────────────────────────────

    [TestClass]
    public class WatchlistAddViewModelTests
    {
        [TestMethod]
        public void Customers_DefaultsToEmptyList()
        {
            var vm = new WatchlistAddViewModel();
            Assert.IsNotNull(vm.Customers);
            Assert.AreEqual(0, vm.Customers.Count);
        }

        [TestMethod]
        public void AvailableMovies_DefaultsToEmptyList()
        {
            var vm = new WatchlistAddViewModel();
            Assert.IsNotNull(vm.AvailableMovies);
            Assert.AreEqual(0, vm.AvailableMovies.Count);
        }

        [TestMethod]
        public void SelectedCustomerId_DefaultsToNull()
        {
            var vm = new WatchlistAddViewModel();
            Assert.IsNull(vm.SelectedCustomerId);
        }

        [TestMethod]
        public void SelectedMovieId_DefaultsToNull()
        {
            var vm = new WatchlistAddViewModel();
            Assert.IsNull(vm.SelectedMovieId);
        }

        [TestMethod]
        public void Item_DefaultsToNewWatchlistItem()
        {
            var vm = new WatchlistAddViewModel();
            Assert.IsNotNull(vm.Item);
            Assert.AreEqual(0, vm.Item.Id);
        }

        [TestMethod]
        public void CanBePopulated()
        {
            var vm = new WatchlistAddViewModel
            {
                Customers = new List<Customer>
                {
                    new Customer { Id = 1, Name = "Alice" }
                }.AsReadOnly(),
                AvailableMovies = new List<Movie>
                {
                    new Movie { Id = 1, Name = "Inception" }
                }.AsReadOnly(),
                SelectedCustomerId = 1,
                SelectedMovieId = 1,
                Item = new WatchlistItem { CustomerId = 1, MovieId = 1, Priority = WatchlistPriority.MustWatch }
            };

            Assert.AreEqual(1, vm.Customers.Count);
            Assert.AreEqual(1, vm.AvailableMovies.Count);
            Assert.AreEqual(1, vm.SelectedCustomerId);
            Assert.AreEqual(1, vm.SelectedMovieId);
            Assert.AreEqual(WatchlistPriority.MustWatch, vm.Item.Priority);
        }
    }
}
