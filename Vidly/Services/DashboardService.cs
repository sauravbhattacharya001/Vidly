using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

// Data models for DashboardService live in Models/DashboardModels.cs

namespace Vidly.Services
{
    /// <summary>
    /// Computes dashboard analytics from rental, movie, and customer data.
    /// All computations are single-pass where possible.
    /// </summary>
    public class DashboardService
    {
        private readonly IRentalRepository _rentalRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IClock _clock;

        public DashboardService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
        }

        /// <summary>
        /// Builds the complete dashboard data model.
        /// Uses a single pass over rentals to compute top movies, top customers,
        /// genre revenue, membership breakdown, recent rentals, and monthly
        /// revenue — replacing 6 separate rental iterations with 1.
        /// </summary>
        public DashboardData GetDashboard()
        {
            var rentals = _rentalRepository.GetAll();
            var movies = _movieRepository.GetAll();
            var customers = _customerRepository.GetAll();
            var stats = _rentalRepository.GetStats();

            var movieLookup = new Dictionary<int, Movie>();
            foreach (var m in movies)
                movieLookup[m.Id] = m;

            var customerLookup = new Dictionary<int, Customer>();
            foreach (var c in customers)
                customerLookup[c.Id] = c;

            // ── Single-pass aggregation ──────────────────────────────
            // Previously 6 separate passes over all rentals; now 1 pass
            // computes all aggregates simultaneously.
            var agg = AggregateSinglePass(rentals, movieLookup, customerLookup, 5, 10, 6);

            return new DashboardData
            {
                Stats = stats,
                CustomerCount = customers.Count,
                MovieCount = movies.Count,
                AverageRevenuePerRental = rentals.Count > 0
                    ? stats.RealizedRevenue / Math.Max(1, stats.ReturnedRentals)
                    : 0m,
                RealizedRevenue = stats.RealizedRevenue,
                ProjectedRevenue = stats.ProjectedRevenue,
                TopMovies = agg.TopMovies,
                TopCustomers = agg.TopCustomers,
                RevenueByGenre = agg.RevenueByGenre,
                MembershipBreakdown = agg.MembershipBreakdown,
                RecentRentals = agg.RecentRentals,
                MonthlyRevenue = agg.MonthlyRevenue
            };
        }

        /// <summary>
        /// Aggregate all dashboard metrics in a single pass over rentals.
        /// Replaces 6 separate O(R) iterations with one O(R) pass plus
        /// O(R × log(recentCount)) for the recent-rentals min-heap.
        /// </summary>
        internal DashboardAggregate AggregateSinglePass(
            IReadOnlyList<Rental> rentals,
            Dictionary<int, Movie> movieLookup,
            Dictionary<int, Customer> customerLookup,
            int topCount,
            int recentCount,
            int monthCount)
        {
            var movieStats = new Dictionary<int, MovieRankEntry>();
            var custStats = new Dictionary<int, CustomerRankEntry>();
            var genreStats = new Dictionary<string, GenreRevenueEntry>();
            var tierStats = new Dictionary<MembershipType, MembershipRevenueEntry>();

            // Monthly revenue setup
            var today = _clock.Today;
            var monthlyLookup = new Dictionary<(int Year, int Month), MonthlyRevenueEntry>();
            var monthlyResult = new List<MonthlyRevenueEntry>();
            for (int i = monthCount - 1; i >= 0; i--)
            {
                var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(-i);
                var entry = new MonthlyRevenueEntry
                {
                    Year = monthStart.Year,
                    Month = monthStart.Month,
                    Label = monthStart.ToString("MMM yyyy")
                };
                monthlyResult.Add(entry);
                monthlyLookup[(entry.Year, entry.Month)] = entry;
            }

            // Recent rentals min-heap
            var recentHeap = new SortedSet<(DateTime Date, int Index)>();

            // ── Single pass ──────────────────────────────────────────
            for (int i = 0; i < rentals.Count; i++)
            {
                var r = rentals[i];

                // --- Top Movies ---
                if (!movieStats.TryGetValue(r.MovieId, out var mEntry))
                {
                    string name = r.MovieName ?? "Unknown";
                    Genre? genre = null;
                    int? rating = null;
                    if (movieLookup.TryGetValue(r.MovieId, out var movie))
                    {
                        name = movie.Name ?? name;
                        genre = movie.Genre;
                        rating = movie.Rating;
                    }
                    mEntry = new MovieRankEntry
                    {
                        MovieId = r.MovieId,
                        MovieName = name,
                        Genre = genre,
                        Rating = rating
                    };
                    movieStats[r.MovieId] = mEntry;
                }
                mEntry.RentalCount++;
                mEntry.TotalRevenue += r.TotalCost;

                // --- Top Customers ---
                if (!custStats.TryGetValue(r.CustomerId, out var cEntry))
                {
                    string name = r.CustomerName ?? "Unknown";
                    MembershipType tier = MembershipType.Basic;
                    if (customerLookup.TryGetValue(r.CustomerId, out var customer))
                    {
                        name = customer.Name ?? name;
                        tier = customer.MembershipType;
                    }
                    cEntry = new CustomerRankEntry
                    {
                        CustomerId = r.CustomerId,
                        CustomerName = name,
                        MembershipType = tier
                    };
                    custStats[r.CustomerId] = cEntry;
                }
                cEntry.RentalCount++;
                cEntry.TotalSpent += r.TotalCost;
                cEntry.LateFees += r.LateFee;

                // --- Revenue by Genre ---
                string genreName = "Unknown";
                if (movieLookup.TryGetValue(r.MovieId, out var mov) && mov.Genre.HasValue)
                    genreName = mov.Genre.Value.ToString();

                if (!genreStats.TryGetValue(genreName, out var gEntry))
                {
                    gEntry = new GenreRevenueEntry { GenreName = genreName };
                    genreStats[genreName] = gEntry;
                }
                gEntry.RentalCount++;
                gEntry.Revenue += r.TotalCost;
                gEntry.LateFees += r.LateFee;

                // --- Membership Breakdown ---
                MembershipType memTier = MembershipType.Basic;
                if (customerLookup.TryGetValue(r.CustomerId, out var cust))
                    memTier = cust.MembershipType;

                if (!tierStats.TryGetValue(memTier, out var tEntry))
                {
                    tEntry = new MembershipRevenueEntry { Tier = memTier };
                    tierStats[memTier] = tEntry;
                }
                tEntry.CustomerIds.Add(r.CustomerId);
                tEntry.RentalCount++;
                tEntry.Revenue += r.TotalCost;

                // --- Monthly Revenue ---
                if (monthlyLookup.TryGetValue((r.RentalDate.Year, r.RentalDate.Month), out var monthEntry))
                {
                    monthEntry.Revenue += r.TotalCost;
                    monthEntry.RentalCount++;
                    monthEntry.LateFees += r.LateFee;
                }

                // --- Recent Rentals (min-heap) ---
                var key = (r.RentalDate, i);
                if (recentHeap.Count < recentCount)
                {
                    recentHeap.Add(key);
                }
                else if (r.RentalDate > recentHeap.Min.Date)
                {
                    recentHeap.Remove(recentHeap.Min);
                    recentHeap.Add(key);
                }
            }

            // ── Finalize: sort and take top N ────────────────────────

            var topMovies = movieStats.Values.ToList();
            topMovies.Sort((a, b) =>
            {
                int cmp = b.RentalCount.CompareTo(a.RentalCount);
                return cmp != 0 ? cmp : b.TotalRevenue.CompareTo(a.TotalRevenue);
            });

            var topCustomers = custStats.Values.ToList();
            topCustomers.Sort((a, b) =>
            {
                int cmp = b.TotalSpent.CompareTo(a.TotalSpent);
                return cmp != 0 ? cmp : b.RentalCount.CompareTo(a.RentalCount);
            });

            var genreResult = genreStats.Values.ToList();
            genreResult.Sort((a, b) => b.Revenue.CompareTo(a.Revenue));

            var tierResult = tierStats.Values.ToList();
            foreach (var te in tierResult)
                te.UniqueCustomers = te.CustomerIds.Count;
            tierResult.Sort((a, b) => b.Revenue.CompareTo(a.Revenue));

            var recentRentals = new List<Rental>(recentHeap.Count);
            foreach (var item in recentHeap.Reverse())
                recentRentals.Add(rentals[item.Index]);

            return new DashboardAggregate
            {
                TopMovies = topMovies.Take(topCount).ToList(),
                TopCustomers = topCustomers.Take(topCount).ToList(),
                RevenueByGenre = genreResult,
                MembershipBreakdown = tierResult,
                RecentRentals = recentRentals,
                MonthlyRevenue = monthlyResult
            };
        }

        /// <summary>
        /// Internal aggregate result from single-pass computation.
        /// </summary>
        internal class DashboardAggregate
        {
            public List<MovieRankEntry> TopMovies { get; set; }
            public List<CustomerRankEntry> TopCustomers { get; set; }
            public List<GenreRevenueEntry> RevenueByGenre { get; set; }
            public List<MembershipRevenueEntry> MembershipBreakdown { get; set; }
            public List<Rental> RecentRentals { get; set; }
            public List<MonthlyRevenueEntry> MonthlyRevenue { get; set; }
        }

        // ── Convenience wrappers ─────────────────────────────────
        // The original per-metric helper methods (ComputeTopMovies,
        // ComputeTopCustomers, ComputeRevenueByGenre,
        // ComputeMembershipBreakdown, GetRecentRentals,
        // ComputeMonthlyRevenue) were removed in favour of the
        // single-pass AggregateSinglePass() above, which computes
        // all six metrics in one O(R) pass.
        //
        // If you need an individual metric in isolation, call
        // AggregateSinglePass and read the relevant property.
        // This avoids maintaining two copies of the same logic.
    }
}
