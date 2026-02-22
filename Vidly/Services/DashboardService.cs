using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

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

            return new DashboardData
            {
                Stats = stats,
                CustomerCount = customers.Count,
                MovieCount = movies.Count,
                AverageRevenuePerRental = rentals.Count > 0
                    ? stats.TotalRevenue / rentals.Count
                    : 0m,
                TopMovies = ComputeTopMovies(rentals, movieLookup, 5),
                TopCustomers = ComputeTopCustomers(rentals, customerLookup, 5),
                RevenueByGenre = ComputeRevenueByGenre(rentals, movieLookup),
                MembershipBreakdown = ComputeMembershipBreakdown(rentals, customerLookup),
                RecentRentals = GetRecentRentals(rentals, 10),
                MonthlyRevenue = ComputeMonthlyRevenue(rentals, 6)
            };
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
        /// Most recent rentals.
        /// </summary>
        internal static List<Rental> GetRecentRentals(
            IReadOnlyList<Rental> rentals, int count)
        {
            var sorted = rentals.ToList();
            sorted.Sort((a, b) => b.RentalDate.CompareTo(a.RentalDate));
            return sorted.Take(count).ToList();
        }

        /// <summary>
        /// Monthly revenue for the last N months.
        /// </summary>
        internal static List<MonthlyRevenueEntry> ComputeMonthlyRevenue(
            IReadOnlyList<Rental> rentals, int months)
        {
            var today = DateTime.Today;
            var result = new List<MonthlyRevenueEntry>();

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
            }

            foreach (var r in rentals)
            {
                foreach (var entry in result)
                {
                    if (r.RentalDate.Year == entry.Year && r.RentalDate.Month == entry.Month)
                    {
                        entry.Revenue += r.TotalCost;
                        entry.RentalCount++;
                        entry.LateFees += r.LateFee;
                        break;
                    }
                }
            }

            return result;
        }
    }

    #region Data Models

    /// <summary>
    /// Complete dashboard data model.
    /// </summary>
    public class DashboardData
    {
        public RentalStats Stats { get; set; }
        public int CustomerCount { get; set; }
        public int MovieCount { get; set; }
        public decimal AverageRevenuePerRental { get; set; }
        public List<MovieRankEntry> TopMovies { get; set; }
        public List<CustomerRankEntry> TopCustomers { get; set; }
        public List<GenreRevenueEntry> RevenueByGenre { get; set; }
        public List<MembershipRevenueEntry> MembershipBreakdown { get; set; }
        public List<Rental> RecentRentals { get; set; }
        public List<MonthlyRevenueEntry> MonthlyRevenue { get; set; }
    }

    public class MovieRankEntry
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? Genre { get; set; }
        public int? Rating { get; set; }
        public int RentalCount { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    public class CustomerRankEntry
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public MembershipType MembershipType { get; set; }
        public int RentalCount { get; set; }
        public decimal TotalSpent { get; set; }
        public decimal LateFees { get; set; }
    }

    public class GenreRevenueEntry
    {
        public string GenreName { get; set; }
        public int RentalCount { get; set; }
        public decimal Revenue { get; set; }
        public decimal LateFees { get; set; }
    }

    public class MembershipRevenueEntry
    {
        public MembershipType Tier { get; set; }
        public int UniqueCustomers { get; set; }
        public int RentalCount { get; set; }
        public decimal Revenue { get; set; }

        /// <summary>
        /// Internal tracking — customer IDs for unique count.
        /// </summary>
        internal HashSet<int> CustomerIds { get; set; } = new HashSet<int>();
    }

    public class MonthlyRevenueEntry
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string Label { get; set; }
        public decimal Revenue { get; set; }
        public int RentalCount { get; set; }
        public decimal LateFees { get; set; }
    }

    #endregion
}
