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

            // O(1) lookup set for franchise movies instead of List.Contains O(n)
            var franchiseMovieSet = new HashSet<int>(franchise.MovieIds);
            // O(1) set of movie IDs this customer has rented within this franchise
            var rentedMovieIds = new HashSet<int>();
            DateTime? firstDate = null;
            DateTime? lastDate = null;

            for (int i = 0; i < rentals.Count; i++)
            {
                var r = rentals[i];
                if (r.CustomerId != customerId) continue;
                if (!franchiseMovieSet.Contains(r.MovieId)) continue;
                rentedMovieIds.Add(r.MovieId);
                if (!firstDate.HasValue || r.RentalDate < firstDate.Value)
                    firstDate = r.RentalDate;
                if (!lastDate.HasValue || r.RentalDate > lastDate.Value)
                    lastDate = r.RentalDate;
            }

            // Preserve franchise order for watched list
            var watchedIds = new List<int>();
            int? nextMovieId = null;
            foreach (var mid in franchise.MovieIds)
            {
                if (rentedMovieIds.Contains(mid))
                    watchedIds.Add(mid);
                else if (!nextMovieId.HasValue)
                    nextMovieId = mid;
            }

            var completedDate = watchedIds.Count == franchise.MovieIds.Count
                ? lastDate : (DateTime?)null;

            return new FranchiseProgress
            {
                CustomerId = customerId,
                FranchiseId = franchise.Id,
                WatchedMovieIds = watchedIds,
                CompletionPercent = franchise.MovieIds.Count > 0
                    ? Math.Round(100.0 * watchedIds.Count / franchise.MovieIds.Count, 1)
                    : 0,
                NextMovieId = nextMovieId,
                StartedDate = firstDate,
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

            var franchiseMovieSet = new HashSet<int>(franchise.MovieIds);
            var franchiseRentals = rentals
                .Where(r => franchiseMovieSet.Contains(r.MovieId))
                .ToList();

            var movieLookup = new Dictionary<int, Movie>();
            foreach (var m in movies)
                if (franchiseMovieSet.Contains(m.Id) && !movieLookup.ContainsKey(m.Id))
                    movieLookup[m.Id] = m;

            // Build per-customer watched sets and per-movie rental counts in a single pass
            var customerWatched = new Dictionary<int, HashSet<int>>();
            var rentalCounts = new Dictionary<int, int>();
            foreach (var mid in franchise.MovieIds)
                rentalCounts[mid] = 0;

            foreach (var r in franchiseRentals)
            {
                rentalCounts[r.MovieId]++;
                HashSet<int> watched;
                if (!customerWatched.TryGetValue(r.CustomerId, out watched))
                {
                    watched = new HashSet<int>();
                    customerWatched[r.CustomerId] = watched;
                }
                watched.Add(r.MovieId);
            }

            var completedCount = 0;
            var totalFranchiseMovies = franchise.MovieIds.Count;
            foreach (var kv in customerWatched)
            {
                if (kv.Value.Count == totalFranchiseMovies)
                    completedCount++;
            }

            var mostPopularId = rentalCounts.OrderByDescending(kv => kv.Value).FirstOrDefault().Key;
            var leastPopularId = rentalCounts.OrderBy(kv => kv.Value).FirstOrDefault().Key;

            // Drop-off analysis using pre-built per-customer sets
            var dropoffs = CalculateDropoffs(franchise, customerWatched, movieLookup);

            var ratingSum = 0.0;
            var ratingCount = 0;
            foreach (var mid in franchise.MovieIds)
            {
                Movie m;
                if (movieLookup.TryGetValue(mid, out m) && m.Rating.HasValue)
                {
                    ratingSum += m.Rating.Value;
                    ratingCount++;
                }
            }
            var avgRating = ratingCount > 0 ? ratingSum / ratingCount : 0.0;

            Movie mostPopular, leastPopular;
            movieLookup.TryGetValue(mostPopularId, out mostPopular);
            movieLookup.TryGetValue(leastPopularId, out leastPopular);

            return new FranchiseReport
            {
                Franchise = franchise,
                TotalMovies = franchise.MovieIds.Count,
                TotalRentals = franchiseRentals.Count,
                AverageRating = Math.Round(avgRating, 2),
                TotalRevenue = franchiseRentals.Sum(r => r.TotalCost),
                CustomersStarted = customerWatched.Count,
                CustomersCompleted = completedCount,
                CompletionRate = customerWatched.Count > 0
                    ? Math.Round(100.0 * completedCount / customerWatched.Count, 1) : 0,
                MostPopularMovieId = mostPopularId,
                MostPopularMovieName = mostPopular?.Name ?? "Unknown",
                LeastPopularMovieId = leastPopularId,
                LeastPopularMovieName = leastPopular?.Name ?? "Unknown",
                Dropoffs = dropoffs
            };
        }

        private List<FranchiseDropoff> CalculateDropoffs(Franchise franchise,
            Dictionary<int, HashSet<int>> customerWatched, Dictionary<int, Movie> movieLookup)
        {
            var dropoffs = new List<FranchiseDropoff>();

            for (int i = 0; i < franchise.MovieIds.Count; i++)
            {
                var mid = franchise.MovieIds[i];
                var watchedThisCount = 0;
                var droppedCount = 0;
                var nextMid = i < franchise.MovieIds.Count - 1
                    ? franchise.MovieIds[i + 1] : -1;

                foreach (var kv in customerWatched)
                {
                    if (kv.Value.Contains(mid))
                    {
                        watchedThisCount++;
                        if (nextMid >= 0 && !kv.Value.Contains(nextMid))
                            droppedCount++;
                    }
                }

                Movie m;
                movieLookup.TryGetValue(mid, out m);

                dropoffs.Add(new FranchiseDropoff
                {
                    MovieId = mid,
                    MovieName = m?.Name ?? "Unknown",
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
            // Pre-build per-franchise movie set for O(1) lookup,
            // then single-pass over rentals to count per-franchise
            var franchiseSets = new Dictionary<int, HashSet<int>>();
            var franchiseCounts = new Dictionary<int, int>();
            foreach (var f in _franchises)
            {
                franchiseSets[f.Id] = new HashSet<int>(f.MovieIds);
                franchiseCounts[f.Id] = 0;
            }

            foreach (var r in rentals)
            {
                foreach (var f in _franchises)
                {
                    if (franchiseSets[f.Id].Contains(r.MovieId))
                        franchiseCounts[f.Id]++;
                }
            }

            return _franchises
                .OrderByDescending(f => franchiseCounts[f.Id])
                .Take(top)
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
