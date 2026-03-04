using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Manages movie franchises/series — creation, customer progress tracking,
    /// drop-off analysis, and franchise-aware recommendations.
    /// </summary>
    public class FranchiseTrackerService
    {
        private readonly List<Franchise> _franchises = new List<Franchise>();
        private int _nextId = 1;

        // --- Franchise CRUD ---

        public Franchise Create(string name, List<int> movieIds, string description = null,
            int? startYear = null, bool isOngoing = false, List<string> tags = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Franchise name is required.", nameof(name));
            if (movieIds == null || movieIds.Count == 0)
                throw new ArgumentException("Franchise must contain at least one movie.", nameof(movieIds));
            if (movieIds.Distinct().Count() != movieIds.Count)
                throw new ArgumentException("Franchise cannot contain duplicate movies.", nameof(movieIds));
            if (_franchises.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Franchise '{name}' already exists.");

            var franchise = new Franchise
            {
                Id = _nextId++,
                Name = name,
                Description = description,
                MovieIds = new List<int>(movieIds),
                StartYear = startYear,
                IsOngoing = isOngoing,
                Tags = tags != null ? new List<string>(tags) : new List<string>()
            };
            _franchises.Add(franchise);
            return franchise;
        }

        public Franchise GetById(int id) => _franchises.FirstOrDefault(f => f.Id == id);

        public List<Franchise> GetAll() => new List<Franchise>(_franchises);

        public List<Franchise> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return GetAll();
            var q = query.ToLowerInvariant();
            return _franchises.Where(f =>
                f.Name.ToLowerInvariant().Contains(q) ||
                (f.Description ?? "").ToLowerInvariant().Contains(q) ||
                f.Tags.Any(t => t.ToLowerInvariant().Contains(q))
            ).ToList();
        }

        public Franchise AddMovie(int franchiseId, int movieId, int? position = null)
        {
            var franchise = GetById(franchiseId)
                ?? throw new KeyNotFoundException($"Franchise {franchiseId} not found.");
            if (franchise.MovieIds.Contains(movieId))
                throw new InvalidOperationException($"Movie {movieId} already in franchise.");

            if (position.HasValue && position.Value >= 0 && position.Value < franchise.MovieIds.Count)
                franchise.MovieIds.Insert(position.Value, movieId);
            else
                franchise.MovieIds.Add(movieId);

            return franchise;
        }

        public Franchise RemoveMovie(int franchiseId, int movieId)
        {
            var franchise = GetById(franchiseId)
                ?? throw new KeyNotFoundException($"Franchise {franchiseId} not found.");
            if (!franchise.MovieIds.Remove(movieId))
                throw new KeyNotFoundException($"Movie {movieId} not found in franchise.");
            if (franchise.MovieIds.Count == 0)
                throw new InvalidOperationException("Cannot remove the last movie from a franchise.");
            return franchise;
        }

        public Franchise ReorderMovies(int franchiseId, List<int> newOrder)
        {
            var franchise = GetById(franchiseId)
                ?? throw new KeyNotFoundException($"Franchise {franchiseId} not found.");
            if (newOrder == null || newOrder.Count != franchise.MovieIds.Count)
                throw new ArgumentException("New order must contain all franchise movies.");
            if (!new HashSet<int>(newOrder).SetEquals(franchise.MovieIds))
                throw new ArgumentException("New order must contain exactly the same movies.");

            franchise.MovieIds = new List<int>(newOrder);
            return franchise;
        }

        public bool Delete(int franchiseId) =>
            _franchises.RemoveAll(f => f.Id == franchiseId) > 0;

        // --- Customer Progress ---

        public FranchiseProgress GetProgress(int customerId, Franchise franchise, List<Rental> rentals)
        {
            if (franchise == null) throw new ArgumentNullException(nameof(franchise));
            if (rentals == null) throw new ArgumentNullException(nameof(rentals));

            var customerRentals = rentals.Where(r => r.CustomerId == customerId).ToList();
            var watchedIds = franchise.MovieIds
                .Where(mid => customerRentals.Any(r => r.MovieId == mid))
                .ToList();

            int? nextMovieId = null;
            foreach (var mid in franchise.MovieIds)
            {
                if (!watchedIds.Contains(mid))
                {
                    nextMovieId = mid;
                    break;
                }
            }

            var firstRental = customerRentals
                .Where(r => franchise.MovieIds.Contains(r.MovieId))
                .OrderBy(r => r.RentalDate)
                .FirstOrDefault();

            var completedDate = watchedIds.Count == franchise.MovieIds.Count
                ? customerRentals
                    .Where(r => franchise.MovieIds.Contains(r.MovieId))
                    .Max(r => r.RentalDate)
                : (DateTime?)null;

            return new FranchiseProgress
            {
                CustomerId = customerId,
                FranchiseId = franchise.Id,
                WatchedMovieIds = watchedIds,
                CompletionPercent = franchise.MovieIds.Count > 0
                    ? Math.Round(100.0 * watchedIds.Count / franchise.MovieIds.Count, 1)
                    : 0,
                NextMovieId = nextMovieId,
                StartedDate = firstRental?.RentalDate,
                CompletedDate = completedDate
            };
        }

        public List<FranchiseProgress> GetAllProgress(int customerId, List<Rental> rentals)
        {
            return _franchises
                .Select(f => GetProgress(customerId, f, rentals))
                .Where(p => p.WatchedMovieIds.Count > 0)
                .OrderByDescending(p => p.CompletionPercent)
                .ToList();
        }

        // --- Franchise Analytics ---

        public FranchiseReport GetReport(Franchise franchise, List<Rental> rentals, List<Movie> movies)
        {
            if (franchise == null) throw new ArgumentNullException(nameof(franchise));
            if (rentals == null) throw new ArgumentNullException(nameof(rentals));
            if (movies == null) throw new ArgumentNullException(nameof(movies));

            var franchiseRentals = rentals
                .Where(r => franchise.MovieIds.Contains(r.MovieId))
                .ToList();

            var franchiseMovies = movies
                .Where(m => franchise.MovieIds.Contains(m.Id))
                .ToList();

            var customerIds = franchiseRentals.Select(r => r.CustomerId).Distinct().ToList();

            // Per-customer progress
            var completedCount = 0;
            foreach (var cid in customerIds)
            {
                var watched = franchise.MovieIds
                    .Where(mid => franchiseRentals.Any(r => r.CustomerId == cid && r.MovieId == mid))
                    .Count();
                if (watched == franchise.MovieIds.Count) completedCount++;
            }

            // Rental counts per movie
            var rentalCounts = franchise.MovieIds.ToDictionary(
                mid => mid,
                mid => franchiseRentals.Count(r => r.MovieId == mid));

            var mostPopularId = rentalCounts.OrderByDescending(kv => kv.Value).FirstOrDefault().Key;
            var leastPopularId = rentalCounts.OrderBy(kv => kv.Value).FirstOrDefault().Key;

            // Drop-off analysis
            var dropoffs = CalculateDropoffs(franchise, franchiseRentals, movies);

            var avgRating = franchiseMovies.Where(m => m.Rating.HasValue).Select(m => m.Rating.Value).DefaultIfEmpty(0).Average();

            return new FranchiseReport
            {
                Franchise = franchise,
                TotalMovies = franchise.MovieIds.Count,
                TotalRentals = franchiseRentals.Count,
                AverageRating = Math.Round(avgRating, 2),
                TotalRevenue = franchiseRentals.Sum(r => r.TotalCost),
                CustomersStarted = customerIds.Count,
                CustomersCompleted = completedCount,
                CompletionRate = customerIds.Count > 0
                    ? Math.Round(100.0 * completedCount / customerIds.Count, 1) : 0,
                MostPopularMovieId = mostPopularId,
                MostPopularMovieName = movies.FirstOrDefault(m => m.Id == mostPopularId)?.Name ?? "Unknown",
                LeastPopularMovieId = leastPopularId,
                LeastPopularMovieName = movies.FirstOrDefault(m => m.Id == leastPopularId)?.Name ?? "Unknown",
                Dropoffs = dropoffs
            };
        }

        private List<FranchiseDropoff> CalculateDropoffs(Franchise franchise, List<Rental> rentals, List<Movie> movies)
        {
            var dropoffs = new List<FranchiseDropoff>();
            var customerIds = rentals.Select(r => r.CustomerId).Distinct().ToList();

            for (int i = 0; i < franchise.MovieIds.Count; i++)
            {
                var mid = franchise.MovieIds[i];
                var watchedThisCount = customerIds.Count(cid =>
                    rentals.Any(r => r.CustomerId == cid && r.MovieId == mid));

                // "Dropped" = watched this one but not the next one
                var droppedCount = 0;
                if (i < franchise.MovieIds.Count - 1)
                {
                    var nextMid = franchise.MovieIds[i + 1];
                    droppedCount = customerIds.Count(cid =>
                        rentals.Any(r => r.CustomerId == cid && r.MovieId == mid) &&
                        !rentals.Any(r => r.CustomerId == cid && r.MovieId == nextMid));
                }

                dropoffs.Add(new FranchiseDropoff
                {
                    MovieId = mid,
                    MovieName = movies.FirstOrDefault(m => m.Id == mid)?.Name ?? "Unknown",
                    Position = i + 1,
                    WatchedCount = watchedThisCount,
                    DroppedCount = droppedCount,
                    DropoffRate = watchedThisCount > 0
                        ? Math.Round(100.0 * droppedCount / watchedThisCount, 1) : 0
                });
            }

            return dropoffs;
        }

        // --- Franchise Recommendations ---

        public List<FranchiseRecommendation> GetRecommendations(int customerId,
            List<Rental> rentals, List<Movie> movies, int maxResults = 5)
        {
            if (rentals == null) throw new ArgumentNullException(nameof(rentals));
            if (movies == null) throw new ArgumentNullException(nameof(movies));

            var recommendations = new List<FranchiseRecommendation>();

            foreach (var franchise in _franchises)
            {
                var progress = GetProgress(customerId, franchise, rentals);

                if (progress.CompletedDate.HasValue) continue; // Already completed

                if (progress.WatchedMovieIds.Count > 0 && progress.NextMovieId.HasValue)
                {
                    // In-progress franchise — recommend continuing
                    var nextMovie = movies.FirstOrDefault(m => m.Id == progress.NextMovieId.Value);
                    var remaining = franchise.MovieIds.Count - progress.WatchedMovieIds.Count;

                    recommendations.Add(new FranchiseRecommendation
                    {
                        Franchise = franchise,
                        Reason = $"You've watched {progress.WatchedMovieIds.Count}/{franchise.MovieIds.Count} — " +
                                 $"only {remaining} left!",
                        Score = progress.CompletionPercent + 20, // Boost in-progress
                        NextMovieId = progress.NextMovieId,
                        NextMovieName = nextMovie?.Name ?? "Unknown",
                        Type = remaining <= 1
                            ? RecommendationType.CompleteFranchise
                            : RecommendationType.ContinueFranchise
                    });
                }
                else if (progress.WatchedMovieIds.Count == 0)
                {
                    // Haven't started — recommend based on genre affinity
                    var customerGenres = rentals
                        .Where(r => r.CustomerId == customerId)
                        .Select(r => movies.FirstOrDefault(m => m.Id == r.MovieId))
                        .Where(m => m?.Genre != null)
                        .GroupBy(m => m.Genre)
                        .OrderByDescending(g => g.Count())
                        .Select(g => g.Key)
                        .ToList();

                    var franchiseMovieGenres = franchise.MovieIds
                        .Select(mid => movies.FirstOrDefault(m => m.Id == mid))
                        .Where(m => m?.Genre != null)
                        .Select(m => m.Genre)
                        .Distinct()
                        .ToList();

                    var genreOverlap = customerGenres.Count > 0 && franchiseMovieGenres.Count > 0
                        ? franchiseMovieGenres.Count(g => customerGenres.Take(3).Contains(g))
                        : 0;

                    if (genreOverlap > 0)
                    {
                        var firstMovie = movies.FirstOrDefault(m => m.Id == franchise.MovieIds[0]);
                        recommendations.Add(new FranchiseRecommendation
                        {
                            Franchise = franchise,
                            Reason = $"Matches your taste — {franchise.MovieIds.Count}-movie franchise",
                            Score = genreOverlap * 25.0,
                            NextMovieId = franchise.MovieIds[0],
                            NextMovieName = firstMovie?.Name ?? "Unknown",
                            Type = RecommendationType.StartNewFranchise
                        });
                    }
                }
            }

            return recommendations
                .OrderByDescending(r => r.Score)
                .Take(maxResults)
                .ToList();
        }

        // --- Franchise Discovery ---

        public List<Franchise> FindByMovie(int movieId) =>
            _franchises.Where(f => f.MovieIds.Contains(movieId)).ToList();

        public List<Franchise> FindByTag(string tag) =>
            _franchises.Where(f => f.Tags.Any(t =>
                t.Equals(tag, StringComparison.OrdinalIgnoreCase))).ToList();

        public List<Franchise> GetPopularFranchises(List<Rental> rentals, int top = 10)
        {
            return _franchises
                .Select(f => new
                {
                    Franchise = f,
                    RentalCount = rentals.Count(r => f.MovieIds.Contains(r.MovieId))
                })
                .OrderByDescending(x => x.RentalCount)
                .Take(top)
                .Select(x => x.Franchise)
                .ToList();
        }

        // --- Text Summary ---

        public string GenerateSummary(FranchiseReport report)
        {
            var lines = new List<string>
            {
                $"=== Franchise Report: {report.Franchise.Name} ===",
                $"Movies: {report.TotalMovies} | Rentals: {report.TotalRentals} | Revenue: ${report.TotalRevenue:F2}",
                $"Avg Rating: {report.AverageRating}/5",
                $"Customers: {report.CustomersStarted} started, {report.CustomersCompleted} completed ({report.CompletionRate}%)",
                $"Most Popular: {report.MostPopularMovieName}",
                $"Least Popular: {report.LeastPopularMovieName}",
                "",
                "Drop-off Analysis:"
            };

            foreach (var d in report.Dropoffs)
            {
                lines.Add($"  #{d.Position} {d.MovieName}: {d.WatchedCount} watched, " +
                          $"{d.DroppedCount} dropped ({d.DropoffRate}%)");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
