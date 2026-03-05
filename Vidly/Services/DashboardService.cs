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

        public DashboardService(
            IRentalRepository rentalRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository)
        {
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
        internal static DashboardAggregate AggregateSinglePass(
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
            var today = DateTime.Today;
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

        /// <summary>
        /// Most-rented movies with rental count and total revenue.
        /// </summary>
        internal static List<MovieRankEntry> ComputeTopMovies(
            IReadOnlyList<Rental> rentals,
            Dictionary<int, Movie> movieLookup,
            int count)
        {
            var movieStats = new Dictionary<int, MovieRankEntry>();

            foreach (var r in rentals)
            {
                if (!movieStats.TryGetValue(r.MovieId, out var entry))
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

                    entry = new MovieRankEntry
                    {
                        MovieId = r.MovieId,
                        MovieName = name,
                        Genre = genre,
                        Rating = rating
                    };
                    movieStats[r.MovieId] = entry;
                }

                entry.RentalCount++;
                entry.TotalRevenue += r.TotalCost;
            }

            var result = movieStats.Values.ToList();
            result.Sort((a, b) =>
            {
                int cmp = b.RentalCount.CompareTo(a.RentalCount);
                return cmp != 0 ? cmp : b.TotalRevenue.CompareTo(a.TotalRevenue);
            });

            return result.Take(count).ToList();
        }

        /// <summary>
        /// Top customers by total spending.
        /// </summary>
        internal static List<CustomerRankEntry> ComputeTopCustomers(
            IReadOnlyList<Rental> rentals,
            Dictionary<int, Customer> customerLookup,
            int count)
        {
            var custStats = new Dictionary<int, CustomerRankEntry>();

            foreach (var r in rentals)
            {
                if (!custStats.TryGetValue(r.CustomerId, out var entry))
                {
                    string name = r.CustomerName ?? "Unknown";
                    MembershipType tier = MembershipType.Basic;

                    if (customerLookup.TryGetValue(r.CustomerId, out var customer))
                    {
                        name = customer.Name ?? name;
                        tier = customer.MembershipType;
                    }

                    entry = new CustomerRankEntry
                    {
                        CustomerId = r.CustomerId,
                        CustomerName = name,
                        MembershipType = tier
                    };
                    custStats[r.CustomerId] = entry;
                }

                entry.RentalCount++;
                entry.TotalSpent += r.TotalCost;
                entry.LateFees += r.LateFee;
            }

            var result = custStats.Values.ToList();
            result.Sort((a, b) =>
            {
                int cmp = b.TotalSpent.CompareTo(a.TotalSpent);
                return cmp != 0 ? cmp : b.RentalCount.CompareTo(a.RentalCount);
            });

            return result.Take(count).ToList();
        }

        /// <summary>
        /// Revenue grouped by movie genre.
        /// </summary>
        internal static List<GenreRevenueEntry> ComputeRevenueByGenre(
            IReadOnlyList<Rental> rentals,
            Dictionary<int, Movie> movieLookup)
        {
            var genreStats = new Dictionary<string, GenreRevenueEntry>();

            foreach (var r in rentals)
            {
                string genreName = "Unknown";
                if (movieLookup.TryGetValue(r.MovieId, out var movie) && movie.Genre.HasValue)
                {
                    genreName = movie.Genre.Value.ToString();
                }

                if (!genreStats.TryGetValue(genreName, out var entry))
                {
                    entry = new GenreRevenueEntry { GenreName = genreName };
                    genreStats[genreName] = entry;
                }

                entry.RentalCount++;
                entry.Revenue += r.TotalCost;
                entry.LateFees += r.LateFee;
            }

            var result = genreStats.Values.ToList();
            result.Sort((a, b) => b.Revenue.CompareTo(a.Revenue));
            return result;
        }

        /// <summary>
        /// Revenue breakdown by customer membership tier.
        /// </summary>
        internal static List<MembershipRevenueEntry> ComputeMembershipBreakdown(
            IReadOnlyList<Rental> rentals,
            Dictionary<int, Customer> customerLookup)
        {
            var tierStats = new Dictionary<MembershipType, MembershipRevenueEntry>();

            foreach (var r in rentals)
            {
                MembershipType tier = MembershipType.Basic;
                if (customerLookup.TryGetValue(r.CustomerId, out var customer))
                {
                    tier = customer.MembershipType;
                }

                if (!tierStats.TryGetValue(tier, out var entry))
                {
                    entry = new MembershipRevenueEntry { Tier = tier };
                    tierStats[tier] = entry;
                }

                entry.CustomerIds.Add(r.CustomerId);
                entry.RentalCount++;
                entry.Revenue += r.TotalCost;
            }

            var result = tierStats.Values.ToList();
            // Compute unique customer count per tier
            foreach (var entry in result)
            {
                entry.UniqueCustomers = entry.CustomerIds.Count;
            }
            result.Sort((a, b) => b.Revenue.CompareTo(a.Revenue));
            return result;
        }

        /// <summary>
        /// Most recent rentals. Uses partial sort (min-heap style) to avoid
        /// sorting the entire rental list when only a small number is needed.
        /// O(R × log(count)) instead of O(R × log(R)) for full sort.
        /// </summary>
        internal static List<Rental> GetRecentRentals(
            IReadOnlyList<Rental> rentals, int count)
        {
            if (rentals.Count <= count)
            {
                var all = rentals.ToList();
                all.Sort((a, b) => b.RentalDate.CompareTo(a.RentalDate));
                return all;
            }

            // Use a SortedSet as a min-heap of size 'count'
            // to find top-N most recent without full sort.
            // We use a comparer that never returns 0 to allow duplicate dates.
            var heap = new SortedSet<(DateTime Date, int Index)>();
            for (int i = 0; i < rentals.Count; i++)
            {
                var key = (rentals[i].RentalDate, i);
                if (heap.Count < count)
                {
                    heap.Add(key);
                }
                else if (rentals[i].RentalDate > heap.Min.Date)
                {
                    heap.Remove(heap.Min);
                    heap.Add(key);
                }
            }

            var result = new List<Rental>(heap.Count);
            foreach (var item in heap.Reverse())
            {
                result.Add(rentals[item.Index]);
            }
            return result;
        }

        /// <summary>
        /// Monthly revenue for the last N months.
        /// Uses a dictionary keyed by (year, month) for O(R) rental matching
        /// instead of O(R × months) nested iteration.
        /// </summary>
        internal static List<MonthlyRevenueEntry> ComputeMonthlyRevenue(
            IReadOnlyList<Rental> rentals, int months)
        {
            var today = DateTime.Today;
            var result = new List<MonthlyRevenueEntry>();
            var lookup = new Dictionary<(int Year, int Month), MonthlyRevenueEntry>();

            for (int i = months - 1; i >= 0; i--)
            {
                var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(-i);
                var entry = new MonthlyRevenueEntry
                {
                    Year = monthStart.Year,
                    Month = monthStart.Month,
                    Label = monthStart.ToString("MMM yyyy")
                };
                result.Add(entry);
                lookup[(entry.Year, entry.Month)] = entry;
            }

            foreach (var r in rentals)
            {
                if (lookup.TryGetValue((r.RentalDate.Year, r.RentalDate.Month), out var entry))
                {
                    entry.Revenue += r.TotalCost;
                    entry.RentalCount++;
                    entry.LateFees += r.LateFee;
                }
            }

            return result;
        }
    }
}
