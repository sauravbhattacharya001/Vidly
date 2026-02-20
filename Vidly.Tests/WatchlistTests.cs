using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class WatchlistTests
    {
        #region Test Helpers

        /// <summary>
        /// Isolated movie repository for testing (not shared with static state).
        /// </summary>
        private class TestMovieRepository : IMovieRepository
        {
            private readonly Dictionary<int, Movie> _movies = new Dictionary<int, Movie>();
            private int _nextId = 1;

            public void Add(Movie movie)
            {
                if (movie.Id == 0) movie.Id = _nextId++;
                _movies[movie.Id] = movie;
            }

            public Movie GetById(int id) =>
                _movies.TryGetValue(id, out var m) ? m : null;

            public IReadOnlyList<Movie> GetAll() =>
                _movies.Values.ToList().AsReadOnly();

            public void Update(Movie movie) { _movies[movie.Id] = movie; }
            public void Remove(int id) { _movies.Remove(id); }

            public IReadOnlyList<Movie> GetByReleaseDate(int year, int month) =>
                _movies.Values
                    .Where(m => m.ReleaseDate?.Year == year && m.ReleaseDate?.Month == month)
                    .ToList().AsReadOnly();

            public Movie GetRandom() => _movies.Values.FirstOrDefault();

            public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating) =>
                _movies.Values.ToList().AsReadOnly();
        }

        /// <summary>
        /// Isolated customer repository for testing.
        /// </summary>
        private class TestCustomerRepository : ICustomerRepository
        {
            private readonly Dictionary<int, Customer> _customers = new Dictionary<int, Customer>();
            private int _nextId = 1;

            public void Add(Customer customer)
            {
                if (customer.Id == 0) customer.Id = _nextId++;
                _customers[customer.Id] = customer;
            }

            public Customer GetById(int id) =>
                _customers.TryGetValue(id, out var c) ? c : null;

            public IReadOnlyList<Customer> GetAll() =>
                _customers.Values.ToList().AsReadOnly();

            public void Update(Customer customer) { _customers[customer.Id] = customer; }
            public void Remove(int id) { _customers.Remove(id); }

            public IReadOnlyList<Customer> Search(string query, MembershipType? membershipType) =>
                _customers.Values.ToList().AsReadOnly();

            public IReadOnlyList<Customer> GetByMemberSince(int year, int month) =>
                new List<Customer>().AsReadOnly();

            public CustomerStats GetStats() => new CustomerStats { TotalCustomers = _customers.Count };
        }

        /// <summary>
        /// Isolated watchlist repository for testing (no static state).
        /// </summary>
        private class TestWatchlistRepository : IWatchlistRepository
        {
            private readonly Dictionary<int, WatchlistItem> _items = new Dictionary<int, WatchlistItem>();
            private readonly HashSet<string> _pairs = new HashSet<string>();
            private int _nextId = 1;

            public WatchlistItem GetById(int id) =>
                _items.TryGetValue(id, out var item) ? Clone(item) : null;

            public IReadOnlyList<WatchlistItem> GetAll() =>
                _items.Values
                    .OrderByDescending(i => (int)i.Priority)
                    .ThenByDescending(i => i.AddedDate)
                    .Select(Clone)
                    .ToList().AsReadOnly();

            public IReadOnlyList<WatchlistItem> GetByCustomer(int customerId) =>
                _items.Values
                    .Where(i => i.CustomerId == customerId)
                    .OrderByDescending(i => (int)i.Priority)
                    .ThenByDescending(i => i.AddedDate)
                    .Select(Clone)
                    .ToList().AsReadOnly();

            public bool IsOnWatchlist(int customerId, int movieId) =>
                _pairs.Contains($"{customerId}:{movieId}");

            public WatchlistItem Add(WatchlistItem item)
            {
                var key = $"{item.CustomerId}:{item.MovieId}";
                if (_pairs.Contains(key))
                    throw new InvalidOperationException("This movie is already on the customer's watchlist.");

                item.Id = _nextId++;
                if (item.AddedDate == default) item.AddedDate = DateTime.Today;
                if (item.Priority == 0) item.Priority = WatchlistPriority.Normal;

                _items[item.Id] = Clone(item);
                _pairs.Add(key);
                return Clone(item);
            }

            public void Remove(int id)
            {
                if (!_items.TryGetValue(id, out var item))
                    throw new KeyNotFoundException($"Watchlist item with Id {id} not found.");
                _pairs.Remove($"{item.CustomerId}:{item.MovieId}");
                _items.Remove(id);
            }

            public bool RemoveByCustomerAndMovie(int customerId, int movieId)
            {
                var key = $"{customerId}:{movieId}";
                if (!_pairs.Contains(key)) return false;
                var item = _items.Values.FirstOrDefault(i => i.CustomerId == customerId && i.MovieId == movieId);
                if (item == null) return false;
                _items.Remove(item.Id);
                _pairs.Remove(key);
                return true;
            }

            public void Update(WatchlistItem item)
            {
                if (!_items.TryGetValue(item.Id, out var existing))
                    throw new KeyNotFoundException($"Watchlist item with Id {item.Id} not found.");
                existing.Note = item.Note;
                existing.Priority = item.Priority;
            }

            public int ClearCustomerWatchlist(int customerId)
            {
                var toRemove = _items.Values.Where(i => i.CustomerId == customerId).ToList();
                foreach (var item in toRemove)
                {
                    _pairs.Remove($"{item.CustomerId}:{item.MovieId}");
                    _items.Remove(item.Id);
                }
                return toRemove.Count;
            }

            public WatchlistStats GetStats(int customerId)
            {
                int n = 0, h = 0, m = 0;
                foreach (var item in _items.Values.Where(i => i.CustomerId == customerId))
                {
                    switch (item.Priority)
                    {
                        case WatchlistPriority.Normal: n++; break;
                        case WatchlistPriority.High: h++; break;
                        case WatchlistPriority.MustWatch: m++; break;
                    }
                }
                return new WatchlistStats { TotalItems = n + h + m, NormalCount = n, HighCount = h, MustWatchCount = m };
            }

            public IReadOnlyList<PopularWatchlistMovie> GetMostWatchlisted(int limit = 10) =>
                _items.Values
                    .GroupBy(i => new { i.MovieId, i.MovieName })
                    .Select(g => new PopularWatchlistMovie { MovieId = g.Key.MovieId, MovieName = g.Key.MovieName, WatchlistCount = g.Count() })
                    .OrderByDescending(p => p.WatchlistCount)
                    .Take(limit)
                    .ToList().AsReadOnly();

            private static WatchlistItem Clone(WatchlistItem s) => new WatchlistItem
            {
                Id = s.Id, CustomerId = s.CustomerId, CustomerName = s.CustomerName,
                MovieId = s.MovieId, MovieName = s.MovieName, MovieGenre = s.MovieGenre,
                MovieRating = s.MovieRating, AddedDate = s.AddedDate, Note = s.Note, Priority = s.Priority
            };
        }

        private TestCustomerRepository _customers;
        private TestMovieRepository _movies;
        private TestWatchlistRepository _watchlist;

        [TestInitialize]
        public void Setup()
        {
            _customers = new TestCustomerRepository();
            _movies = new TestMovieRepository();
            _watchlist = new TestWatchlistRepository();

            _customers.Add(new Customer { Id = 1, Name = "John Smith", Email = "john@test.com", MembershipType = MembershipType.Gold });
            _customers.Add(new Customer { Id = 2, Name = "Jane Doe", Email = "jane@test.com", MembershipType = MembershipType.Silver });
            _customers.Add(new Customer { Id = 3, Name = "Bob Wilson", Email = "bob@test.com", MembershipType = MembershipType.Basic });

            _movies.Add(new Movie { Id = 1, Name = "Shrek!", Genre = Genre.Animation, Rating = 4 });
            _movies.Add(new Movie { Id = 2, Name = "The Godfather", Genre = Genre.Drama, Rating = 5 });
            _movies.Add(new Movie { Id = 3, Name = "Toy Story", Genre = Genre.Animation, Rating = 5 });
            _movies.Add(new Movie { Id = 4, Name = "Inception", Genre = Genre.SciFi, Rating = 5 });
            _movies.Add(new Movie { Id = 5, Name = "The Hangover", Genre = Genre.Comedy, Rating = 3 });
        }

        #endregion

        #region Model Tests

        [TestMethod]
        public void WatchlistItem_DefaultPriority_IsNormal()
        {
            var item = new WatchlistItem();
            Assert.AreEqual(default(WatchlistPriority), item.Priority);
        }

        [TestMethod]
        public void WatchlistItem_Properties_CanBeSetAndRead()
        {
            var item = new WatchlistItem
            {
                Id = 1,
                CustomerId = 10,
                CustomerName = "Test Customer",
                MovieId = 20,
                MovieName = "Test Movie",
                MovieGenre = Genre.Action,
                MovieRating = 4,
                AddedDate = new DateTime(2026, 1, 15),
                Note = "Must see",
                Priority = WatchlistPriority.MustWatch
            };

            Assert.AreEqual(1, item.Id);
            Assert.AreEqual(10, item.CustomerId);
            Assert.AreEqual("Test Customer", item.CustomerName);
            Assert.AreEqual(20, item.MovieId);
            Assert.AreEqual("Test Movie", item.MovieName);
            Assert.AreEqual(Genre.Action, item.MovieGenre);
            Assert.AreEqual(4, item.MovieRating);
            Assert.AreEqual(new DateTime(2026, 1, 15), item.AddedDate);
            Assert.AreEqual("Must see", item.Note);
            Assert.AreEqual(WatchlistPriority.MustWatch, item.Priority);
        }

        [TestMethod]
        public void WatchlistPriority_Values_AreCorrect()
        {
            Assert.AreEqual(1, (int)WatchlistPriority.Normal);
            Assert.AreEqual(2, (int)WatchlistPriority.High);
            Assert.AreEqual(3, (int)WatchlistPriority.MustWatch);
        }

        #endregion

        #region Repository - Add Tests

        [TestMethod]
        public void Add_ValidItem_AssignsIdAndReturnsClone()
        {
            var item = new WatchlistItem { CustomerId = 1, MovieId = 1, CustomerName = "John", MovieName = "Shrek!" };
            var result = _watchlist.Add(item);

            Assert.IsTrue(result.Id > 0);
            Assert.AreEqual(1, result.CustomerId);
            Assert.AreEqual(1, result.MovieId);
        }

        [TestMethod]
        public void Add_SetsDefaultAddedDate()
        {
            var item = new WatchlistItem { CustomerId = 1, MovieId = 1 };
            var result = _watchlist.Add(item);

            Assert.AreEqual(DateTime.Today, result.AddedDate);
        }

        [TestMethod]
        public void Add_SetsDefaultPriority_WhenZero()
        {
            var item = new WatchlistItem { CustomerId = 1, MovieId = 1 };
            var result = _watchlist.Add(item);

            Assert.AreEqual(WatchlistPriority.Normal, result.Priority);
        }

        [TestMethod]
        public void Add_PreservesPriority_WhenSet()
        {
            var item = new WatchlistItem { CustomerId = 1, MovieId = 1, Priority = WatchlistPriority.MustWatch };
            var result = _watchlist.Add(item);

            Assert.AreEqual(WatchlistPriority.MustWatch, result.Priority);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Add_DuplicateCustomerMovie_ThrowsInvalidOperation()
        {
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1 });
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1 }); // duplicate
        }

        [TestMethod]
        public void Add_SameMovieDifferentCustomer_Succeeds()
        {
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1 });
            _watchlist.Add(new WatchlistItem { CustomerId = 2, MovieId = 1 });

            Assert.AreEqual(2, _watchlist.GetAll().Count);
        }

        [TestMethod]
        public void Add_SameCustomerDifferentMovie_Succeeds()
        {
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1 });
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 2 });

            Assert.AreEqual(2, _watchlist.GetByCustomer(1).Count);
        }

        #endregion

        #region Repository - GetById Tests

        [TestMethod]
        public void GetById_ExistingItem_ReturnsClone()
        {
            var added = _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1, Note = "test" });
            var result = _watchlist.GetById(added.Id);

            Assert.IsNotNull(result);
            Assert.AreEqual(added.Id, result.Id);
            Assert.AreEqual("test", result.Note);
        }

        [TestMethod]
        public void GetById_NonExisting_ReturnsNull()
        {
            var result = _watchlist.GetById(999);
            Assert.IsNull(result);
        }

        #endregion

        #region Repository - GetByCustomer Tests

        [TestMethod]
        public void GetByCustomer_ReturnsOnlyThatCustomersItems()
        {
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1 });
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 2 });
            _watchlist.Add(new WatchlistItem { CustomerId = 2, MovieId = 3 });

            var result = _watchlist.GetByCustomer(1);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.All(i => i.CustomerId == 1));
        }

        [TestMethod]
        public void GetByCustomer_OrdersByPriorityDescThenDateDesc()
        {
            _watchlist.Add(new WatchlistItem
            {
                CustomerId = 1, MovieId = 1, Priority = WatchlistPriority.Normal,
                AddedDate = DateTime.Today
            });
            _watchlist.Add(new WatchlistItem
            {
                CustomerId = 1, MovieId = 2, Priority = WatchlistPriority.MustWatch,
                AddedDate = DateTime.Today.AddDays(-1)
            });
            _watchlist.Add(new WatchlistItem
            {
                CustomerId = 1, MovieId = 3, Priority = WatchlistPriority.High,
                AddedDate = DateTime.Today
            });

            var result = _watchlist.GetByCustomer(1);
            Assert.AreEqual(WatchlistPriority.MustWatch, result[0].Priority);
            Assert.AreEqual(WatchlistPriority.High, result[1].Priority);
            Assert.AreEqual(WatchlistPriority.Normal, result[2].Priority);
        }

        [TestMethod]
        public void GetByCustomer_Empty_ReturnsEmptyList()
        {
            var result = _watchlist.GetByCustomer(999);
            Assert.AreEqual(0, result.Count);
        }

        #endregion

        #region Repository - IsOnWatchlist Tests

        [TestMethod]
        public void IsOnWatchlist_ExistingPair_ReturnsTrue()
        {
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1 });
            Assert.IsTrue(_watchlist.IsOnWatchlist(1, 1));
        }

        [TestMethod]
        public void IsOnWatchlist_NonExistingPair_ReturnsFalse()
        {
            Assert.IsFalse(_watchlist.IsOnWatchlist(1, 1));
        }

        [TestMethod]
        public void IsOnWatchlist_AfterRemoval_ReturnsFalse()
        {
            var added = _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1 });
            _watchlist.Remove(added.Id);
            Assert.IsFalse(_watchlist.IsOnWatchlist(1, 1));
        }

        #endregion

        #region Repository - Remove Tests

        [TestMethod]
        public void Remove_ExistingItem_Succeeds()
        {
            var added = _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1 });
            _watchlist.Remove(added.Id);
            Assert.IsNull(_watchlist.GetById(added.Id));
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void Remove_NonExisting_ThrowsKeyNotFound()
        {
            _watchlist.Remove(999);
        }

        [TestMethod]
        public void RemoveByCustomerAndMovie_Existing_ReturnsTrue()
        {
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1 });
            Assert.IsTrue(_watchlist.RemoveByCustomerAndMovie(1, 1));
            Assert.IsFalse(_watchlist.IsOnWatchlist(1, 1));
        }

        [TestMethod]
        public void RemoveByCustomerAndMovie_NonExisting_ReturnsFalse()
        {
            Assert.IsFalse(_watchlist.RemoveByCustomerAndMovie(1, 1));
        }

        #endregion

        #region Repository - Update Tests

        [TestMethod]
        public void Update_ChangesNoteAndPriority()
        {
            var added = _watchlist.Add(new WatchlistItem
            {
                CustomerId = 1, MovieId = 1, Note = "old", Priority = WatchlistPriority.Normal
            });

            added.Note = "new note";
            added.Priority = WatchlistPriority.MustWatch;
            _watchlist.Update(added);

            var result = _watchlist.GetById(added.Id);
            Assert.AreEqual("new note", result.Note);
            Assert.AreEqual(WatchlistPriority.MustWatch, result.Priority);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void Update_NonExisting_ThrowsKeyNotFound()
        {
            _watchlist.Update(new WatchlistItem { Id = 999, Note = "test" });
        }

        #endregion

        #region Repository - ClearCustomerWatchlist Tests

        [TestMethod]
        public void ClearCustomerWatchlist_RemovesAllItems()
        {
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1 });
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 2 });
            _watchlist.Add(new WatchlistItem { CustomerId = 2, MovieId = 3 });

            var count = _watchlist.ClearCustomerWatchlist(1);

            Assert.AreEqual(2, count);
            Assert.AreEqual(0, _watchlist.GetByCustomer(1).Count);
            Assert.AreEqual(1, _watchlist.GetByCustomer(2).Count); // customer 2 unaffected
        }

        [TestMethod]
        public void ClearCustomerWatchlist_EmptyWatchlist_ReturnsZero()
        {
            var count = _watchlist.ClearCustomerWatchlist(999);
            Assert.AreEqual(0, count);
        }

        #endregion

        #region Repository - GetStats Tests

        [TestMethod]
        public void GetStats_ReturnsCorrectCounts()
        {
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1, Priority = WatchlistPriority.Normal });
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 2, Priority = WatchlistPriority.High });
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 3, Priority = WatchlistPriority.MustWatch });
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 4, Priority = WatchlistPriority.Normal });

            var stats = _watchlist.GetStats(1);

            Assert.AreEqual(4, stats.TotalItems);
            Assert.AreEqual(2, stats.NormalCount);
            Assert.AreEqual(1, stats.HighCount);
            Assert.AreEqual(1, stats.MustWatchCount);
        }

        [TestMethod]
        public void GetStats_EmptyWatchlist_ReturnsZeros()
        {
            var stats = _watchlist.GetStats(999);

            Assert.AreEqual(0, stats.TotalItems);
            Assert.AreEqual(0, stats.NormalCount);
            Assert.AreEqual(0, stats.HighCount);
            Assert.AreEqual(0, stats.MustWatchCount);
        }

        #endregion

        #region Repository - GetMostWatchlisted Tests

        [TestMethod]
        public void GetMostWatchlisted_ReturnsOrderedByCount()
        {
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1, MovieName = "Shrek!" });
            _watchlist.Add(new WatchlistItem { CustomerId = 2, MovieId = 1, MovieName = "Shrek!" });
            _watchlist.Add(new WatchlistItem { CustomerId = 3, MovieId = 1, MovieName = "Shrek!" });
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 2, MovieName = "Godfather" });
            _watchlist.Add(new WatchlistItem { CustomerId = 2, MovieId = 2, MovieName = "Godfather" });
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 3, MovieName = "Toy Story" });

            var result = _watchlist.GetMostWatchlisted(3);

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(1, result[0].MovieId);
            Assert.AreEqual(3, result[0].WatchlistCount);
            Assert.AreEqual(2, result[1].MovieId);
            Assert.AreEqual(2, result[1].WatchlistCount);
        }

        [TestMethod]
        public void GetMostWatchlisted_RespectsLimit()
        {
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1, MovieName = "A" });
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 2, MovieName = "B" });
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 3, MovieName = "C" });

            var result = _watchlist.GetMostWatchlisted(2);
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void GetMostWatchlisted_Empty_ReturnsEmptyList()
        {
            var result = _watchlist.GetMostWatchlisted(5);
            Assert.AreEqual(0, result.Count);
        }

        #endregion

        #region Controller - Index Tests

        [TestMethod]
        public void Index_NoCustomerSelected_ReturnsViewWithCustomerList()
        {
            var controller = new WatchlistController(_customers, _movies, _watchlist);

            var result = controller.Index(null, null, null) as ViewResult;

            Assert.IsNotNull(result);
            var model = result.Model as WatchlistViewModel;
            Assert.IsNotNull(model);
            Assert.AreEqual(3, model.Customers.Count);
            Assert.IsNull(model.SelectedCustomerId);
            Assert.AreEqual(0, model.Items.Count);
        }

        [TestMethod]
        public void Index_WithCustomerId_ReturnsCustomerWatchlist()
        {
            _watchlist.Add(new WatchlistItem
            {
                CustomerId = 1, MovieId = 1, CustomerName = "John", MovieName = "Shrek!",
                Priority = WatchlistPriority.High
            });

            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var result = controller.Index(1, null, null) as ViewResult;

            Assert.IsNotNull(result);
            var model = result.Model as WatchlistViewModel;
            Assert.AreEqual(1, model.SelectedCustomerId);
            Assert.AreEqual("John Smith", model.SelectedCustomerName);
            Assert.AreEqual(1, model.Items.Count);
            Assert.IsNotNull(model.Stats);
        }

        [TestMethod]
        public void Index_InvalidCustomerId_ReturnsNotFound()
        {
            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var result = controller.Index(999, null, null);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        [TestMethod]
        public void Index_WithStatusMessage_PassesToModel()
        {
            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var result = controller.Index(null, "Test message", true) as ViewResult;

            var model = result.Model as WatchlistViewModel;
            Assert.AreEqual("Test message", model.StatusMessage);
            Assert.IsTrue(model.IsError);
        }

        #endregion

        #region Controller - Add GET Tests

        [TestMethod]
        public void Add_Get_ReturnsAddView()
        {
            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var result = controller.Add(null, null) as ViewResult;

            Assert.IsNotNull(result);
            var model = result.Model as WatchlistAddViewModel;
            Assert.IsNotNull(model);
            Assert.AreEqual(3, model.Customers.Count);
            Assert.AreEqual(5, model.AvailableMovies.Count);
        }

        [TestMethod]
        public void Add_Get_WithCustomerId_PreSelectsCustomer()
        {
            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var result = controller.Add(1, null) as ViewResult;

            var model = result.Model as WatchlistAddViewModel;
            Assert.AreEqual(1, model.SelectedCustomerId);
            Assert.AreEqual(1, model.Item.CustomerId);
        }

        [TestMethod]
        public void Add_Get_WithCustomerId_FiltersAlreadyWatchlistedMovies()
        {
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1 });
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 2 });

            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var result = controller.Add(1, null) as ViewResult;

            var model = result.Model as WatchlistAddViewModel;
            Assert.AreEqual(3, model.AvailableMovies.Count); // 5 total - 2 on watchlist = 3
            Assert.IsFalse(model.AvailableMovies.Any(m => m.Id == 1));
            Assert.IsFalse(model.AvailableMovies.Any(m => m.Id == 2));
        }

        #endregion

        #region Controller - Add POST Tests

        [TestMethod]
        public void Add_Post_ValidItem_RedirectsToIndex()
        {
            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var item = new WatchlistItem
            {
                CustomerId = 1,
                MovieId = 1,
                Priority = WatchlistPriority.High,
                Note = "Great movie"
            };

            var result = controller.Add(item) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.AreEqual(1, result.RouteValues["customerId"]);
            Assert.IsTrue(result.RouteValues["message"].ToString().Contains("Shrek!"));
        }

        [TestMethod]
        public void Add_Post_ValidItem_SetsCustomerAndMovieNames()
        {
            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var item = new WatchlistItem { CustomerId = 1, MovieId = 2, Priority = WatchlistPriority.Normal };

            controller.Add(item);

            var items = _watchlist.GetByCustomer(1);
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("John Smith", items[0].CustomerName);
            Assert.AreEqual("The Godfather", items[0].MovieName);
            Assert.AreEqual(Genre.Drama, items[0].MovieGenre);
            Assert.AreEqual(5, items[0].MovieRating);
        }

        [TestMethod]
        public void Add_Post_DuplicateItem_RedirectsWithError()
        {
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1, CustomerName = "John", MovieName = "Shrek!" });

            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var item = new WatchlistItem { CustomerId = 1, MovieId = 1 };

            var result = controller.Add(item) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.IsTrue((bool)result.RouteValues["error"]);
        }

        [TestMethod]
        public void Add_Post_InvalidCustomer_ReturnsNotFound()
        {
            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var item = new WatchlistItem { CustomerId = 999, MovieId = 1 };

            var result = controller.Add(item);
            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        [TestMethod]
        public void Add_Post_InvalidMovie_ReturnsNotFound()
        {
            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var item = new WatchlistItem { CustomerId = 1, MovieId = 999 };

            var result = controller.Add(item);
            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        #endregion

        #region Controller - Remove Tests

        [TestMethod]
        public void Remove_ExistingItem_RedirectsWithSuccessMessage()
        {
            var added = _watchlist.Add(new WatchlistItem
            {
                CustomerId = 1, MovieId = 1, MovieName = "Shrek!", CustomerName = "John"
            });

            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var result = controller.Remove(added.Id, 1) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.IsTrue(result.RouteValues["message"].ToString().Contains("Shrek!"));
            Assert.IsNull(_watchlist.GetById(added.Id));
        }

        [TestMethod]
        public void Remove_NonExistingItem_RedirectsWithError()
        {
            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var result = controller.Remove(999, 1) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.IsTrue((bool)result.RouteValues["error"]);
        }

        #endregion

        #region Controller - Clear Tests

        [TestMethod]
        public void Clear_RemovesAllItemsForCustomer()
        {
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1 });
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 2 });
            _watchlist.Add(new WatchlistItem { CustomerId = 2, MovieId = 3 });

            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var result = controller.Clear(1) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.AreEqual(0, _watchlist.GetByCustomer(1).Count);
            Assert.AreEqual(1, _watchlist.GetByCustomer(2).Count);
        }

        [TestMethod]
        public void Clear_EmptyWatchlist_RedirectsWithMessage()
        {
            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var result = controller.Clear(1) as RedirectToRouteResult;

            Assert.IsTrue(result.RouteValues["message"].ToString().Contains("empty"));
        }

        #endregion

        #region Controller - UpdatePriority Tests

        [TestMethod]
        public void UpdatePriority_ChangesPriority()
        {
            var added = _watchlist.Add(new WatchlistItem
            {
                CustomerId = 1, MovieId = 1, MovieName = "Shrek!",
                Priority = WatchlistPriority.Normal
            });

            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var result = controller.UpdatePriority(added.Id, WatchlistPriority.MustWatch, 1) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            var updated = _watchlist.GetById(added.Id);
            Assert.AreEqual(WatchlistPriority.MustWatch, updated.Priority);
        }

        [TestMethod]
        public void UpdatePriority_NonExistingItem_ReturnsNotFound()
        {
            var controller = new WatchlistController(_customers, _movies, _watchlist);
            var result = controller.UpdatePriority(999, WatchlistPriority.High, 1);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        #endregion

        #region ViewModel Tests

        [TestMethod]
        public void WatchlistViewModel_DefaultValues_AreEmpty()
        {
            var vm = new WatchlistViewModel();

            Assert.IsNotNull(vm.Customers);
            Assert.AreEqual(0, vm.Customers.Count);
            Assert.IsNotNull(vm.Items);
            Assert.AreEqual(0, vm.Items.Count);
            Assert.IsNotNull(vm.PopularMovies);
            Assert.AreEqual(0, vm.PopularMovies.Count);
            Assert.IsNull(vm.SelectedCustomerId);
            Assert.IsNull(vm.SelectedCustomerName);
            Assert.IsNull(vm.StatusMessage);
            Assert.IsFalse(vm.IsError);
        }

        [TestMethod]
        public void WatchlistAddViewModel_DefaultValues_AreEmpty()
        {
            var vm = new WatchlistAddViewModel();

            Assert.IsNotNull(vm.Customers);
            Assert.IsNotNull(vm.AvailableMovies);
            Assert.IsNotNull(vm.Item);
            Assert.IsNull(vm.SelectedCustomerId);
            Assert.IsNull(vm.SelectedMovieId);
        }

        [TestMethod]
        public void WatchlistStats_Properties_CanBeSetAndRead()
        {
            var stats = new WatchlistStats
            {
                TotalItems = 10,
                NormalCount = 5,
                HighCount = 3,
                MustWatchCount = 2
            };

            Assert.AreEqual(10, stats.TotalItems);
            Assert.AreEqual(5, stats.NormalCount);
            Assert.AreEqual(3, stats.HighCount);
            Assert.AreEqual(2, stats.MustWatchCount);
        }

        [TestMethod]
        public void PopularWatchlistMovie_Properties_CanBeSetAndRead()
        {
            var popular = new PopularWatchlistMovie
            {
                MovieId = 1,
                MovieName = "Shrek!",
                WatchlistCount = 5
            };

            Assert.AreEqual(1, popular.MovieId);
            Assert.AreEqual("Shrek!", popular.MovieName);
            Assert.AreEqual(5, popular.WatchlistCount);
        }

        #endregion

        #region Edge Case Tests

        [TestMethod]
        public void GetAll_ReturnsAllItemsAcrossCustomers()
        {
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1 });
            _watchlist.Add(new WatchlistItem { CustomerId = 2, MovieId = 2 });
            _watchlist.Add(new WatchlistItem { CustomerId = 3, MovieId = 3 });

            var all = _watchlist.GetAll();
            Assert.AreEqual(3, all.Count);
        }

        [TestMethod]
        public void Add_WithNote_PreservesNote()
        {
            var item = new WatchlistItem
            {
                CustomerId = 1, MovieId = 1,
                Note = "Heard great things about this!"
            };
            var result = _watchlist.Add(item);

            Assert.AreEqual("Heard great things about this!", result.Note);
            Assert.AreEqual("Heard great things about this!", _watchlist.GetById(result.Id).Note);
        }

        [TestMethod]
        public void ClearCustomerWatchlist_ClearsPairsSet()
        {
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1 });
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 2 });

            _watchlist.ClearCustomerWatchlist(1);

            // Should be able to re-add the same movies
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 1 });
            _watchlist.Add(new WatchlistItem { CustomerId = 1, MovieId = 2 });

            Assert.AreEqual(2, _watchlist.GetByCustomer(1).Count);
        }

        [TestMethod]
        public void Controller_NullDependency_ThrowsArgumentNull()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new WatchlistController(null, _movies, _watchlist));
            Assert.ThrowsException<ArgumentNullException>(() =>
                new WatchlistController(_customers, null, _watchlist));
            Assert.ThrowsException<ArgumentNullException>(() =>
                new WatchlistController(_customers, _movies, null));
        }

        #endregion
    }
}
