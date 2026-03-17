using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Computes annual Vidly Awards across multiple categories based on
    /// rental, review, and customer data.
    /// </summary>
    public class AwardsService
    {
        private readonly IRentalRepository _rentalRepo;
        private readonly IMovieRepository _movieRepo;
        private readonly ICustomerRepository _customerRepo;
        private readonly IReviewRepository _reviewRepo;
        private readonly IClock _clock;

        public AwardsService(
            IRentalRepository rentalRepo,
            IMovieRepository movieRepo,
            ICustomerRepository customerRepo,
            IReviewRepository reviewRepo,
            IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _rentalRepo = rentalRepo ?? throw new ArgumentNullException(nameof(rentalRepo));
            _movieRepo = movieRepo ?? throw new ArgumentNullException(nameof(movieRepo));
            _customerRepo = customerRepo ?? throw new ArgumentNullException(nameof(customerRepo));
            _reviewRepo = reviewRepo ?? throw new ArgumentNullException(nameof(reviewRepo));
        }

        /// <summary>
        /// Returns the list of years that have rental data.
        /// </summary>
        public List<int> GetAvailableYears()
        {
            var allRentals = _rentalRepo.GetAll();
            return allRentals
                .Select(r => r.RentalDate.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToList();
        }

        /// <summary>
        /// Generates the full awards ceremony for a given year.
        /// </summary>
        public AwardsCeremony GetCeremony(int year)
        {
            var allRentals = _rentalRepo.GetAll();
            var allMovies = _movieRepo.GetAll();
            var allCustomers = _customerRepo.GetAll();
            var allReviews = _reviewRepo.GetAll();

            var yearRentals = allRentals.Where(r => r.RentalDate.Year == year).ToList();
            var yearReviews = allReviews.Where(r => r.CreatedDate.Year == year).ToList();

            var availableYears = GetAvailableYears();

            var summary = new AwardsYearSummary
            {
                Year = year,
                TotalRentals = yearRentals.Count,
                TotalMovies = yearRentals.Select(r => r.MovieId).Distinct().Count(),
                TotalCustomers = yearRentals.Select(r => r.CustomerId).Distinct().Count(),
                TotalReviews = yearReviews.Count,
                TotalRevenue = yearRentals.Sum(r => r.TotalCost)
            };

            var categories = new List<AwardCategory>();

            // 1. Most Rented Movie
            categories.Add(BuildMostRented(yearRentals, allMovies));

            // 2. Highest Rated Movie
            categories.Add(BuildHighestRated(yearReviews, allMovies));

            // 3. Most Reviewed Movie
            categories.Add(BuildMostReviewed(yearReviews, allMovies));

            // 4. Hidden Gem (high rating, low rental count)
            categories.Add(BuildHiddenGem(yearRentals, yearReviews, allMovies));

            // 5. Most Loyal Customer
            categories.Add(BuildMostLoyal(yearRentals, allCustomers));

            // 6. Top Critic (most reviews written)
            categories.Add(BuildTopCritic(yearReviews, allCustomers));

            // 7. Biggest Spender
            categories.Add(BuildBiggestSpender(yearRentals, allCustomers));

            // 8. Most Popular Genre
            categories.Add(BuildPopularGenre(yearRentals, allMovies));

            // 9. Best New Release
            categories.Add(BuildBestNewRelease(yearRentals, yearReviews, allMovies, year));

            // 10. Comeback Classic (oldest movie rented this year)
            categories.Add(BuildComebackClassic(yearRentals, allMovies));

            // Filter out categories with no winner
            categories = categories.Where(c => c.Winner != null).ToList();

            return new AwardsCeremony
            {
                Year = year,
                Summary = summary,
                Categories = categories,
                AvailableYears = availableYears
            };
        }

        private AwardCategory BuildMostRented(List<Rental> rentals, IReadOnlyList<Movie> movies)
        {
            var grouped = rentals.GroupBy(r => r.MovieId)
                .Select(g => new { MovieId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToList();

            if (!grouped.Any()) return EmptyCategory("Most Rented Movie", "🎬", "The movie everyone had to see");

            var movieMap = movies.ToDictionary(m => m.Id);
            var winner = grouped.First();
            var movie = movieMap.ContainsKey(winner.MovieId) ? movieMap[winner.MovieId] : null;

            return new AwardCategory
            {
                Name = "Most Rented Movie",
                Icon = "🎬",
                Description = "The movie everyone had to see",
                Winner = new AwardWinner
                {
                    Name = movie?.Name ?? $"Movie #{winner.MovieId}",
                    Subtitle = movie?.Genre?.ToString() ?? "Unknown Genre",
                    StatLabel = "Total Rentals",
                    StatValue = winner.Count.ToString()
                },
                Nominees = grouped.Skip(1).Select((n, i) => new AwardNominee
                {
                    Name = movieMap.ContainsKey(n.MovieId) ? movieMap[n.MovieId].Name : $"Movie #{n.MovieId}",
                    StatValue = $"{n.Count} rentals",
                    Rank = i + 2
                }).ToList()
            };
        }

        private AwardCategory BuildHighestRated(List<Review> reviews, IReadOnlyList<Movie> movies)
        {
            var grouped = reviews.GroupBy(r => r.MovieId)
                .Where(g => g.Count() >= 1)
                .Select(g => new { MovieId = g.Key, Avg = g.Average(r => r.Stars), Count = g.Count() })
                .OrderByDescending(x => x.Avg)
                .ThenByDescending(x => x.Count)
                .Take(5)
                .ToList();

            if (!grouped.Any()) return EmptyCategory("Highest Rated", "⭐", "Critics' choice");

            var movieMap = movies.ToDictionary(m => m.Id);
            var winner = grouped.First();
            var movie = movieMap.ContainsKey(winner.MovieId) ? movieMap[winner.MovieId] : null;

            return new AwardCategory
            {
                Name = "Highest Rated",
                Icon = "⭐",
                Description = "Critics' choice — the best of the best",
                Winner = new AwardWinner
                {
                    Name = movie?.Name ?? $"Movie #{winner.MovieId}",
                    Subtitle = $"{winner.Count} review{(winner.Count != 1 ? "s" : "")}",
                    StatLabel = "Average Rating",
                    StatValue = $"{winner.Avg:F1} / 5"
                },
                Nominees = grouped.Skip(1).Select((n, i) => new AwardNominee
                {
                    Name = movieMap.ContainsKey(n.MovieId) ? movieMap[n.MovieId].Name : $"Movie #{n.MovieId}",
                    StatValue = $"{n.Avg:F1} / 5 ({n.Count} reviews)",
                    Rank = i + 2
                }).ToList()
            };
        }

        private AwardCategory BuildMostReviewed(List<Review> reviews, IReadOnlyList<Movie> movies)
        {
            var grouped = reviews.GroupBy(r => r.MovieId)
                .Select(g => new { MovieId = g.Key, Count = g.Count(), Avg = g.Average(r => r.Stars) })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToList();

            if (!grouped.Any()) return EmptyCategory("Most Reviewed", "📝", "Everyone had something to say");

            var movieMap = movies.ToDictionary(m => m.Id);
            var winner = grouped.First();
            var movie = movieMap.ContainsKey(winner.MovieId) ? movieMap[winner.MovieId] : null;

            return new AwardCategory
            {
                Name = "Most Reviewed",
                Icon = "📝",
                Description = "Everyone had something to say about this one",
                Winner = new AwardWinner
                {
                    Name = movie?.Name ?? $"Movie #{winner.MovieId}",
                    Subtitle = $"Avg {winner.Avg:F1} stars",
                    StatLabel = "Reviews",
                    StatValue = winner.Count.ToString()
                },
                Nominees = grouped.Skip(1).Select((n, i) => new AwardNominee
                {
                    Name = movieMap.ContainsKey(n.MovieId) ? movieMap[n.MovieId].Name : $"Movie #{n.MovieId}",
                    StatValue = $"{n.Count} reviews",
                    Rank = i + 2
                }).ToList()
            };
        }

        private AwardCategory BuildHiddenGem(List<Rental> rentals, List<Review> reviews, IReadOnlyList<Movie> movies)
        {
            var rentalCounts = rentals.GroupBy(r => r.MovieId).ToDictionary(g => g.Key, g => g.Count());
            var medianRentals = rentalCounts.Any() ? rentalCounts.Values.OrderBy(v => v).ElementAt(rentalCounts.Count / 2) : 0;

            var gems = reviews.GroupBy(r => r.MovieId)
                .Where(g => g.Average(r => r.Stars) >= 4.0)
                .Select(g => new
                {
                    MovieId = g.Key,
                    Avg = g.Average(r => r.Stars),
                    ReviewCount = g.Count(),
                    RentalCount = rentalCounts.ContainsKey(g.Key) ? rentalCounts[g.Key] : 0
                })
                .Where(x => x.RentalCount <= medianRentals || x.RentalCount <= 3)
                .OrderByDescending(x => x.Avg)
                .ThenBy(x => x.RentalCount)
                .Take(5)
                .ToList();

            if (!gems.Any()) return EmptyCategory("Hidden Gem", "💎", "Underrated treasure");

            var movieMap = movies.ToDictionary(m => m.Id);
            var winner = gems.First();
            var movie = movieMap.ContainsKey(winner.MovieId) ? movieMap[winner.MovieId] : null;

            return new AwardCategory
            {
                Name = "Hidden Gem",
                Icon = "💎",
                Description = "Highly rated but underrented — a treasure waiting to be found",
                Winner = new AwardWinner
                {
                    Name = movie?.Name ?? $"Movie #{winner.MovieId}",
                    Subtitle = $"Only {winner.RentalCount} rental{(winner.RentalCount != 1 ? "s" : "")} but {winner.Avg:F1} stars",
                    StatLabel = "Rating",
                    StatValue = $"{winner.Avg:F1} ⭐"
                },
                Nominees = gems.Skip(1).Select((n, i) => new AwardNominee
                {
                    Name = movieMap.ContainsKey(n.MovieId) ? movieMap[n.MovieId].Name : $"Movie #{n.MovieId}",
                    StatValue = $"{n.Avg:F1} stars, {n.RentalCount} rentals",
                    Rank = i + 2
                }).ToList()
            };
        }

        private AwardCategory BuildMostLoyal(List<Rental> rentals, IReadOnlyList<Customer> customers)
        {
            var grouped = rentals.GroupBy(r => r.CustomerId)
                .Select(g => new { CustomerId = g.Key, Count = g.Count(), Spent = g.Sum(r => r.TotalCost) })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToList();

            if (!grouped.Any()) return EmptyCategory("Most Loyal Customer", "👑", "Our biggest fan");

            var custMap = customers.ToDictionary(c => c.Id);
            var winner = grouped.First();
            var cust = custMap.ContainsKey(winner.CustomerId) ? custMap[winner.CustomerId] : null;

            return new AwardCategory
            {
                Name = "Most Loyal Customer",
                Icon = "👑",
                Description = "Our biggest fan — always coming back for more",
                Winner = new AwardWinner
                {
                    Name = cust?.Name ?? $"Customer #{winner.CustomerId}",
                    Subtitle = $"Spent ${winner.Spent:F2}",
                    StatLabel = "Rentals",
                    StatValue = winner.Count.ToString()
                },
                Nominees = grouped.Skip(1).Select((n, i) => new AwardNominee
                {
                    Name = custMap.ContainsKey(n.CustomerId) ? custMap[n.CustomerId].Name : $"Customer #{n.CustomerId}",
                    StatValue = $"{n.Count} rentals",
                    Rank = i + 2
                }).ToList()
            };
        }

        private AwardCategory BuildTopCritic(List<Review> reviews, IReadOnlyList<Customer> customers)
        {
            var grouped = reviews.GroupBy(r => r.CustomerId)
                .Select(g => new { CustomerId = g.Key, Count = g.Count(), AvgStars = g.Average(r => r.Stars) })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToList();

            if (!grouped.Any()) return EmptyCategory("Top Critic", "🎤", "Most prolific reviewer");

            var custMap = customers.ToDictionary(c => c.Id);
            var winner = grouped.First();
            var cust = custMap.ContainsKey(winner.CustomerId) ? custMap[winner.CustomerId] : null;

            return new AwardCategory
            {
                Name = "Top Critic",
                Icon = "🎤",
                Description = "Our most prolific reviewer",
                Winner = new AwardWinner
                {
                    Name = cust?.Name ?? $"Customer #{winner.CustomerId}",
                    Subtitle = $"Avg rating: {winner.AvgStars:F1} stars",
                    StatLabel = "Reviews Written",
                    StatValue = winner.Count.ToString()
                },
                Nominees = grouped.Skip(1).Select((n, i) => new AwardNominee
                {
                    Name = custMap.ContainsKey(n.CustomerId) ? custMap[n.CustomerId].Name : $"Customer #{n.CustomerId}",
                    StatValue = $"{n.Count} reviews",
                    Rank = i + 2
                }).ToList()
            };
        }

        private AwardCategory BuildBiggestSpender(List<Rental> rentals, IReadOnlyList<Customer> customers)
        {
            var grouped = rentals.GroupBy(r => r.CustomerId)
                .Select(g => new { CustomerId = g.Key, Spent = g.Sum(r => r.TotalCost), Count = g.Count() })
                .OrderByDescending(x => x.Spent)
                .Take(5)
                .ToList();

            if (!grouped.Any()) return EmptyCategory("Biggest Spender", "💰", "Money well spent");

            var custMap = customers.ToDictionary(c => c.Id);
            var winner = grouped.First();
            var cust = custMap.ContainsKey(winner.CustomerId) ? custMap[winner.CustomerId] : null;

            return new AwardCategory
            {
                Name = "Biggest Spender",
                Icon = "💰",
                Description = "Supporting the store one rental at a time",
                Winner = new AwardWinner
                {
                    Name = cust?.Name ?? $"Customer #{winner.CustomerId}",
                    Subtitle = $"{winner.Count} rentals",
                    StatLabel = "Total Spent",
                    StatValue = $"${winner.Spent:F2}"
                },
                Nominees = grouped.Skip(1).Select((n, i) => new AwardNominee
                {
                    Name = custMap.ContainsKey(n.CustomerId) ? custMap[n.CustomerId].Name : $"Customer #{n.CustomerId}",
                    StatValue = $"${n.Spent:F2}",
                    Rank = i + 2
                }).ToList()
            };
        }

        private AwardCategory BuildPopularGenre(List<Rental> rentals, IReadOnlyList<Movie> movies)
        {
            var movieMap = movies.ToDictionary(m => m.Id);
            var grouped = rentals
                .Where(r => movieMap.ContainsKey(r.MovieId) && movieMap[r.MovieId].Genre.HasValue)
                .GroupBy(r => movieMap[r.MovieId].Genre.Value)
                .Select(g => new { Genre = g.Key, Count = g.Count(), Revenue = g.Sum(r => r.TotalCost) })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToList();

            if (!grouped.Any()) return EmptyCategory("Most Popular Genre", "🎭", "The genre that ruled the year");

            var winner = grouped.First();

            return new AwardCategory
            {
                Name = "Most Popular Genre",
                Icon = "🎭",
                Description = "The genre that ruled the year",
                Winner = new AwardWinner
                {
                    Name = winner.Genre.ToString(),
                    Subtitle = $"${winner.Revenue:F2} revenue",
                    StatLabel = "Rentals",
                    StatValue = winner.Count.ToString()
                },
                Nominees = grouped.Skip(1).Select((n, i) => new AwardNominee
                {
                    Name = n.Genre.ToString(),
                    StatValue = $"{n.Count} rentals",
                    Rank = i + 2
                }).ToList()
            };
        }

        private AwardCategory BuildBestNewRelease(List<Rental> rentals, List<Review> reviews, IReadOnlyList<Movie> movies, int year)
        {
            var newReleases = movies.Where(m => m.ReleaseDate.HasValue && m.ReleaseDate.Value.Year == year).ToList();
            if (!newReleases.Any()) return EmptyCategory("Best New Release", "🆕", "Fresh off the press");

            var newIds = new HashSet<int>(newReleases.Select(m => m.Id));
            var rentalCounts = rentals.Where(r => newIds.Contains(r.MovieId))
                .GroupBy(r => r.MovieId).ToDictionary(g => g.Key, g => g.Count());
            var reviewAvg = reviews.Where(r => newIds.Contains(r.MovieId))
                .GroupBy(r => r.MovieId).ToDictionary(g => g.Key, g => g.Average(r => r.Stars));

            var scored = newReleases.Select(m => new
            {
                Movie = m,
                Rentals = rentalCounts.ContainsKey(m.Id) ? rentalCounts[m.Id] : 0,
                Avg = reviewAvg.ContainsKey(m.Id) ? reviewAvg[m.Id] : 0.0,
                Score = (rentalCounts.ContainsKey(m.Id) ? rentalCounts[m.Id] : 0) * 2 +
                        (reviewAvg.ContainsKey(m.Id) ? reviewAvg[m.Id] : 0.0) * 3
            })
            .OrderByDescending(x => x.Score)
            .Take(5)
            .ToList();

            if (!scored.Any() || scored.First().Score == 0)
                return EmptyCategory("Best New Release", "🆕", "Fresh off the press");

            var winner = scored.First();

            return new AwardCategory
            {
                Name = "Best New Release",
                Icon = "🆕",
                Description = "The hottest movie released this year",
                Winner = new AwardWinner
                {
                    Name = winner.Movie.Name,
                    Subtitle = $"{winner.Rentals} rentals, {winner.Avg:F1} avg rating",
                    StatLabel = "Score",
                    StatValue = $"{winner.Score:F0}"
                },
                Nominees = scored.Skip(1).Where(n => n.Score > 0).Select((n, i) => new AwardNominee
                {
                    Name = n.Movie.Name,
                    StatValue = $"{n.Rentals} rentals, {n.Avg:F1} stars",
                    Rank = i + 2
                }).ToList()
            };
        }

        private AwardCategory BuildComebackClassic(List<Rental> rentals, IReadOnlyList<Movie> movies)
        {
            var movieMap = movies.Where(m => m.ReleaseDate.HasValue).ToDictionary(m => m.Id);
            var classics = rentals
                .Where(r => movieMap.ContainsKey(r.MovieId))
                .GroupBy(r => r.MovieId)
                .Select(g => new
                {
                    MovieId = g.Key,
                    Movie = movieMap[g.Key],
                    Rentals = g.Count(),
                    Age = (int)((_clock.Today - movieMap[g.Key].ReleaseDate.Value).TotalDays / 365.25)
                })
                .Where(x => x.Age >= 5)
                .OrderByDescending(x => x.Age)
                .ThenByDescending(x => x.Rentals)
                .Take(5)
                .ToList();

            if (!classics.Any()) return EmptyCategory("Comeback Classic", "🏛️", "Oldies but goldies");

            var winner = classics.First();

            return new AwardCategory
            {
                Name = "Comeback Classic",
                Icon = "🏛️",
                Description = "A classic that still brings people in",
                Winner = new AwardWinner
                {
                    Name = winner.Movie.Name,
                    Subtitle = $"Released {winner.Movie.ReleaseDate.Value.Year} ({winner.Age} years ago)",
                    StatLabel = "Rentals This Year",
                    StatValue = winner.Rentals.ToString()
                },
                Nominees = classics.Skip(1).Select((n, i) => new AwardNominee
                {
                    Name = n.Movie.Name,
                    StatValue = $"{n.Age} years old, {n.Rentals} rentals",
                    Rank = i + 2
                }).ToList()
            };
        }

        private AwardCategory EmptyCategory(string name, string icon, string desc)
        {
            return new AwardCategory
            {
                Name = name,
                Icon = icon,
                Description = desc,
                Winner = null
            };
        }
    }
}
