using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Staff-curated themed movie lists -- create, manage, vote on, and
    /// analyze curated movie collections. Supports themes (Staff Picks,
    /// Hidden Gems, Date Night, etc.), featuring/rotation, voting,
    /// rental impact analysis, and store-wide curation reports.
    /// </summary>
    public class MovieCurationService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly IRentalRepository _rentalRepository;
        private readonly List<CuratedList> _lists = new List<CuratedList>();
        private int _nextId = 1;

        public MovieCurationService(
            IMovieRepository movieRepository,
            IRentalRepository rentalRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _rentalRepository = rentalRepository
                ?? throw new ArgumentNullException(nameof(rentalRepository));
        }

        /// <summary>
        /// Create a new curated list.
        /// </summary>
        public CuratedList CreateList(string title, string description,
            string theme, string curatorName, int curatorStaffId,
            DateTime? expiresAt = null)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Title is required.", nameof(title));
            if (string.IsNullOrWhiteSpace(curatorName))
                throw new ArgumentException("Curator name is required.", nameof(curatorName));
            if (string.IsNullOrWhiteSpace(theme))
                throw new ArgumentException("Theme is required.", nameof(theme));
            if (!CurationThemes.All.Contains(theme))
                throw new ArgumentException(
                    "Unknown theme: " + theme + ". Valid: " + string.Join(", ", CurationThemes.All),
                    nameof(theme));
            if (expiresAt.HasValue && expiresAt.Value <= DateTime.UtcNow)
                throw new ArgumentException("Expiry must be in the future.", nameof(expiresAt));

            var list = new CuratedList
            {
                Id = _nextId++,
                Title = title,
                Description = description ?? "",
                Theme = theme,
                CuratorName = curatorName,
                CuratorStaffId = curatorStaffId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
                IsFeatured = false,
                UpVotes = 0,
                DownVotes = 0
            };
            _lists.Add(list);
            return list;
        }

        /// <summary>
        /// Get a curated list by ID.
        /// </summary>
        public CuratedList GetList(int listId)
        {
            return _lists.FirstOrDefault(l => l.Id == listId);
        }

        /// <summary>
        /// Get all curated lists, optionally filtered.
        /// </summary>
        public List<CuratedList> GetAllLists(
            string theme = null,
            string curatorName = null,
            bool? featuredOnly = null,
            bool excludeExpired = true)
        {
            var query = _lists.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(theme))
                query = query.Where(l => l.Theme == theme);
            if (!string.IsNullOrWhiteSpace(curatorName))
                query = query.Where(l => l.CuratorName.Equals(curatorName, StringComparison.OrdinalIgnoreCase));
            if (featuredOnly == true)
                query = query.Where(l => l.IsFeatured);
            if (excludeExpired)
                query = query.Where(l => !l.ExpiresAt.HasValue || l.ExpiresAt.Value > DateTime.UtcNow);

            return query.OrderByDescending(l => l.IsFeatured)
                        .ThenByDescending(l => l.UpVotes - l.DownVotes)
                        .ThenByDescending(l => l.CreatedAt)
                        .ToList();
        }

        /// <summary>
        /// Delete a curated list.
        /// </summary>
        public bool DeleteList(int listId)
        {
            var list = GetList(listId);
            if (list == null) return false;
            _lists.Remove(list);
            return true;
        }

        /// <summary>
        /// Add a movie to a curated list.
        /// </summary>
        public CuratedListEntry AddMovie(int listId, int movieId, string curatorNote = null)
        {
            var list = GetList(listId);
            if (list == null)
                throw new InvalidOperationException("List " + listId + " not found.");

            var movie = _movieRepository.GetAll().FirstOrDefault(m => m.Id == movieId);
            if (movie == null)
                throw new InvalidOperationException("Movie " + movieId + " not found.");

            if (list.Entries.Any(e => e.MovieId == movieId))
                throw new InvalidOperationException("Movie " + movieId + " is already in list " + listId + ".");

            var entry = new CuratedListEntry
            {
                MovieId = movieId,
                CuratorNote = curatorNote ?? "",
                Position = list.Entries.Count + 1,
                AddedAt = DateTime.UtcNow
            };
            list.Entries.Add(entry);
            return entry;
        }

        /// <summary>
        /// Remove a movie from a curated list.
        /// </summary>
        public bool RemoveMovie(int listId, int movieId)
        {
            var list = GetList(listId);
            if (list == null) return false;

            var entry = list.Entries.FirstOrDefault(e => e.MovieId == movieId);
            if (entry == null) return false;

            list.Entries.Remove(entry);

            int pos = 1;
            foreach (var e in list.Entries.OrderBy(e => e.Position))
                e.Position = pos++;

            return true;
        }

        /// <summary>
        /// Reorder a movie within a curated list.
        /// </summary>
        public bool ReorderMovie(int listId, int movieId, int newPosition)
        {
            var list = GetList(listId);
            if (list == null) return false;
            if (newPosition < 1 || newPosition > list.Entries.Count) return false;

            var entry = list.Entries.FirstOrDefault(e => e.MovieId == movieId);
            if (entry == null) return false;

            list.Entries.Remove(entry);
            list.Entries.Insert(newPosition - 1, entry);

            int pos = 1;
            foreach (var e in list.Entries)
                e.Position = pos++;

            return true;
        }

        /// <summary>
        /// Set a list as featured (max 3 featured at a time).
        /// </summary>
        public bool FeatureList(int listId)
        {
            var list = GetList(listId);
            if (list == null) return false;
            if (list.IsFeatured) return true;

            var featured = _lists.Where(l => l.IsFeatured).ToList();
            if (featured.Count >= 3)
            {
                var oldest = featured.OrderBy(l => l.CreatedAt).First();
                oldest.IsFeatured = false;
            }

            list.IsFeatured = true;
            return true;
        }

        /// <summary>
        /// Remove featured status from a list.
        /// </summary>
        public bool UnfeatureList(int listId)
        {
            var list = GetList(listId);
            if (list == null) return false;
            list.IsFeatured = false;
            return true;
        }

        /// <summary>
        /// Upvote a curated list.
        /// </summary>
        public bool UpVote(int listId)
        {
            var list = GetList(listId);
            if (list == null) return false;
            list.UpVotes++;
            return true;
        }

        /// <summary>
        /// Downvote a curated list.
        /// </summary>
        public bool DownVote(int listId)
        {
            var list = GetList(listId);
            if (list == null) return false;
            list.DownVotes++;
            return true;
        }

        /// <summary>
        /// Get statistics for a specific curated list.
        /// </summary>
        public CuratedListStats GetListStats(int listId)
        {
            var list = GetList(listId);
            if (list == null) return null;

            var allMovies = _movieRepository.GetAll().ToDictionary(m => m.Id);
            var allRentals = _rentalRepository.GetAll();
            var movieIds = new HashSet<int>(list.Entries.Select(e => e.MovieId));

            var genreBreakdown = new Dictionary<Genre, int>();
            double ratingSum = 0;
            int ratedCount = 0;

            foreach (var entry in list.Entries)
            {
                if (allMovies.TryGetValue(entry.MovieId, out var movie))
                {
                    if (movie.Genre.HasValue)
                    {
                        if (!genreBreakdown.ContainsKey(movie.Genre.Value))
                            genreBreakdown[movie.Genre.Value] = 0;
                        genreBreakdown[movie.Genre.Value]++;
                    }
                    if (movie.Rating.HasValue)
                    {
                        ratingSum += movie.Rating.Value;
                        ratedCount++;
                    }
                }
            }

            int totalVotes = list.UpVotes + list.DownVotes;
            int rentalsFromList = allRentals.Count(r => movieIds.Contains(r.MovieId));

            return new CuratedListStats
            {
                ListId = list.Id,
                Title = list.Title,
                MovieCount = list.Entries.Count,
                UpVotes = list.UpVotes,
                DownVotes = list.DownVotes,
                ApprovalRate = totalVotes > 0 ? (double)list.UpVotes / totalVotes * 100 : 0,
                TotalRentalsFromList = rentalsFromList,
                AvgMovieRating = ratedCount > 0 ? ratingSum / ratedCount : 0,
                GenreBreakdown = genreBreakdown
            };
        }

        /// <summary>
        /// Get the top-N curated lists by net votes.
        /// </summary>
        public List<CuratedList> GetTopLists(int count = 5)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            return _lists
                .OrderByDescending(l => l.UpVotes - l.DownVotes)
                .ThenByDescending(l => l.UpVotes)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Get lists by a specific curator.
        /// </summary>
        public List<CuratedList> GetListsByCurator(int staffId)
        {
            return _lists
                .Where(l => l.CuratorStaffId == staffId)
                .OrderByDescending(l => l.CreatedAt)
                .ToList();
        }

        /// <summary>
        /// Find movies that appear in multiple curated lists.
        /// </summary>
        public Dictionary<int, List<string>> GetFrequentlyCuratedMovies(int minLists = 2)
        {
            if (minLists < 1) throw new ArgumentOutOfRangeException(nameof(minLists));

            var movieListMap = new Dictionary<int, List<string>>();
            foreach (var list in _lists)
            {
                foreach (var entry in list.Entries)
                {
                    if (!movieListMap.ContainsKey(entry.MovieId))
                        movieListMap[entry.MovieId] = new List<string>();
                    movieListMap[entry.MovieId].Add(list.Title);
                }
            }

            return movieListMap
                .Where(kvp => kvp.Value.Count >= minLists)
                .OrderByDescending(kvp => kvp.Value.Count)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// Generate a comprehensive curation report for the store.
        /// </summary>
        public CurationReport GenerateReport()
        {
            var report = new CurationReport
            {
                TotalLists = _lists.Count,
                FeaturedLists = _lists.Count(l => l.IsFeatured),
                ExpiredLists = _lists.Count(l => l.ExpiresAt.HasValue && l.ExpiresAt.Value <= DateTime.UtcNow),
                TotalCurators = _lists.Select(l => l.CuratorStaffId).Distinct().Count(),
                TotalMoviesCurated = _lists.SelectMany(l => l.Entries).Select(e => e.MovieId).Distinct().Count()
            };

            foreach (var list in _lists)
            {
                if (!report.ThemeDistribution.ContainsKey(list.Theme))
                    report.ThemeDistribution[list.Theme] = 0;
                report.ThemeDistribution[list.Theme]++;
            }

            if (report.ThemeDistribution.Any())
                report.MostPopularTheme = report.ThemeDistribution
                    .OrderByDescending(kvp => kvp.Value).First().Key;

            var curatorGroups = _lists.GroupBy(l => l.CuratorName)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();

            foreach (var c in curatorGroups)
                report.CuratorLeaderboard[c.Name] = c.Count;

            if (curatorGroups.Any())
            {
                report.TopCurator = curatorGroups.First().Name;
                report.TopCuratorListCount = curatorGroups.First().Count;
            }

            if (_lists.Any())
            {
                report.MostUpvotedList = _lists
                    .OrderByDescending(l => l.UpVotes - l.DownVotes).First();
                report.MostRecentList = _lists
                    .OrderByDescending(l => l.CreatedAt).First();
            }

            if (report.TotalLists == 0)
                report.Recommendations.Add("Start curating! Create your first themed list.");
            if (report.FeaturedLists == 0 && report.TotalLists > 0)
                report.Recommendations.Add("Feature some lists to highlight them for customers.");
            if (report.TotalCurators == 1 && report.TotalLists > 2)
                report.Recommendations.Add("Encourage more staff to curate -- diversity of perspectives enriches the collection.");

            var unusedThemes = CurationThemes.All.Where(t => !report.ThemeDistribution.ContainsKey(t)).ToList();
            if (unusedThemes.Any())
                report.Recommendations.Add("Try these unused themes: " + string.Join(", ", unusedThemes.Take(3)) + ".");

            var lowVoteLists = _lists.Where(l => l.UpVotes + l.DownVotes == 0).ToList();
            if (lowVoteLists.Count > 0)
                report.Recommendations.Add(lowVoteLists.Count + " list(s) have no votes -- promote them to customers.");

            return report;
        }

        /// <summary>
        /// Suggest movies for a theme based on genre affinity.
        /// </summary>
        public List<MovieSuggestion> SuggestMoviesForTheme(string theme, int maxSuggestions = 10)
        {
            if (string.IsNullOrWhiteSpace(theme))
                throw new ArgumentException("Theme is required.", nameof(theme));
            if (maxSuggestions < 1)
                throw new ArgumentOutOfRangeException(nameof(maxSuggestions));

            var themeGenres = new Dictionary<string, Genre[]>
            {
                [CurationThemes.StaffPicks] = new[] { Genre.Drama, Genre.Thriller, Genre.SciFi },
                [CurationThemes.HiddenGems] = new[] { Genre.Documentary, Genre.Drama, Genre.SciFi },
                [CurationThemes.DateNight] = new[] { Genre.Romance, Genre.Comedy, Genre.Drama },
                [CurationThemes.RainyDay] = new[] { Genre.Drama, Genre.Thriller, Genre.Horror },
                [CurationThemes.FamilyFun] = new[] { Genre.Animation, Genre.Comedy, Genre.Adventure },
                [CurationThemes.MindBenders] = new[] { Genre.SciFi, Genre.Thriller, Genre.Drama },
                [CurationThemes.FeelGood] = new[] { Genre.Comedy, Genre.Romance, Genre.Animation },
                [CurationThemes.ClassicCinema] = new[] { Genre.Drama, Genre.Action, Genre.Romance },
                [CurationThemes.WeekendBinge] = new[] { Genre.Action, Genre.SciFi, Genre.Adventure },
                [CurationThemes.CultFavorites] = new[] { Genre.Horror, Genre.SciFi, Genre.Comedy }
            };

            var preferredGenres = themeGenres.ContainsKey(theme)
                ? new HashSet<Genre>(themeGenres[theme])
                : new HashSet<Genre>();

            var alreadyCurated = new HashSet<int>(
                _lists.Where(l => l.Theme == theme)
                      .SelectMany(l => l.Entries)
                      .Select(e => e.MovieId));

            var allMovies = _movieRepository.GetAll();
            var suggestions = new List<MovieSuggestion>();

            foreach (var movie in allMovies)
            {
                if (alreadyCurated.Contains(movie.Id)) continue;

                double score = 0;
                if (movie.Genre.HasValue && preferredGenres.Contains(movie.Genre.Value))
                    score += 50;
                if (movie.Rating.HasValue)
                    score += movie.Rating.Value * 10;

                suggestions.Add(new MovieSuggestion
                {
                    MovieId = movie.Id,
                    MovieName = movie.Name,
                    Score = score,
                    Reason = movie.Genre.HasValue && preferredGenres.Contains(movie.Genre.Value)
                        ? "Great " + movie.Genre.Value + " pick for " + theme
                        : "Potential addition based on rating"
                });
            }

            return suggestions
                .OrderByDescending(s => s.Score)
                .Take(maxSuggestions)
                .ToList();
        }
    }

    /// <summary>
    /// A movie suggestion with scoring.
    /// </summary>
    public class MovieSuggestion
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public double Score { get; set; }
        public string Reason { get; set; }
    }
}
