using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Movie Tagging Service — flexible, user-defined tags for movies
    /// that complement the fixed Genre enum.
    ///
    /// Features:
    ///   - Tag CRUD with validation (name uniqueness, length)
    ///   - Movie-tag assignment/removal with duplicate protection
    ///   - Search movies by tag, tags by movie
    ///   - Tag cloud data with normalized weights
    ///   - Staff pick management
    ///   - Tag suggestions based on genre patterns
    ///   - Bulk operations (tag/untag multiple movies)
    ///   - Tagging analytics and summary
    ///   - Most popular/trending tags
    /// </summary>
    public class TaggingService
    {
        private readonly ITagRepository _tagRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly IClock _clock;

        // Predefined genre-to-tag suggestions
        private static readonly Dictionary<Genre, string[]> GenreTagSuggestions =
            new Dictionary<Genre, string[]>
            {
                { Genre.Action, new[] { "Adrenaline Rush", "Blockbuster", "Stunts" } },
                { Genre.Comedy, new[] { "Feel Good", "Family Friendly", "Laugh Out Loud" } },
                { Genre.Drama, new[] { "Award Winner", "Character Study", "Emotional" } },
                { Genre.Horror, new[] { "Jump Scares", "Psychological", "Gore" } },
                { Genre.SciFi, new[] { "Mind Bending", "Space", "Dystopian" } },
                { Genre.Animation, new[] { "Family Friendly", "Pixar Style", "Anime" } },
                { Genre.Thriller, new[] { "Edge of Seat", "Plot Twist", "Suspenseful" } },
                { Genre.Romance, new[] { "Date Night", "Feel Good", "Tearjerker" } },
                { Genre.Documentary, new[] { "Educational", "True Story", "Eye Opening" } },
                { Genre.Adventure, new[] { "Epic Journey", "Blockbuster", "Feel Good" } },
            };

        public TaggingService(
            ITagRepository tagRepository,
            IMovieRepository movieRepository,
            IClock clock = null)
        {
            _tagRepository = tagRepository
                ?? throw new ArgumentNullException(nameof(tagRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        // ── Tag CRUD ──────────────────────────────────────────

        /// <summary>
        /// Create a new tag with validation.
        /// </summary>
        public MovieTag CreateTag(
            string name,
            string description = null,
            string color = null,
            bool isStaffPick = false,
            string createdBy = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Tag name is required.", nameof(name));

            name = name.Trim();
            if (name.Length < 2 || name.Length > 50)
                throw new ArgumentException(
                    "Tag name must be between 2 and 50 characters.", nameof(name));

            var existing = _tagRepository.GetTagByName(name);
            if (existing != null)
                throw new InvalidOperationException(
                    "A tag named \"" + name + "\" already exists.");

            var tag = new MovieTag
            {
                Name = name,
                Description = description,
                Color = color,
                IsStaffPick = isStaffPick,
                CreatedBy = createdBy ?? "system",
                CreatedDate = _clock.Now,
                IsActive = true,
            };

            return _tagRepository.AddTag(tag);
        }

        /// <summary>
        /// Update an existing tag's properties.
        /// </summary>
        public MovieTag UpdateTag(
            int tagId,
            string name = null,
            string description = null,
            string color = null,
            bool? isStaffPick = null)
        {
            var tag = _tagRepository.GetTagById(tagId);
            if (tag == null)
                throw new ArgumentException("Tag not found.", nameof(tagId));

            if (name != null)
            {
                name = name.Trim();
                if (name.Length < 2 || name.Length > 50)
                    throw new ArgumentException(
                        "Tag name must be between 2 and 50 characters.", nameof(name));

                var dup = _tagRepository.GetTagByName(name);
                if (dup != null && dup.Id != tagId)
                    throw new InvalidOperationException(
                        "A tag named \"" + name + "\" already exists.");
                tag.Name = name;
            }

            if (description != null) tag.Description = description;
            if (color != null) tag.Color = color;
            if (isStaffPick.HasValue) tag.IsStaffPick = isStaffPick.Value;

            _tagRepository.UpdateTag(tag);
            return tag;
        }

        /// <summary>
        /// Deactivate a tag (soft delete). Existing assignments remain
        /// but the tag won't appear in active tag lists.
        /// </summary>
        public void DeactivateTag(int tagId)
        {
            var tag = _tagRepository.GetTagById(tagId);
            if (tag == null)
                throw new ArgumentException("Tag not found.", nameof(tagId));
            tag.IsActive = false;
            _tagRepository.UpdateTag(tag);
        }

        /// <summary>
        /// Reactivate a previously deactivated tag.
        /// </summary>
        public void ReactivateTag(int tagId)
        {
            var tag = _tagRepository.GetTagById(tagId);
            if (tag == null)
                throw new ArgumentException("Tag not found.", nameof(tagId));
            tag.IsActive = true;
            _tagRepository.UpdateTag(tag);
        }

        /// <summary>
        /// Permanently delete a tag and all its assignments.
        /// </summary>
        public int DeleteTag(int tagId)
        {
            var tag = _tagRepository.GetTagById(tagId);
            if (tag == null)
                throw new ArgumentException("Tag not found.", nameof(tagId));

            int removed = _tagRepository.RemoveAllAssignmentsForTag(tagId);
            _tagRepository.DeleteTag(tagId);
            return removed;
        }

        /// <summary>
        /// Get a tag by ID.
        /// </summary>
        public MovieTag GetTag(int tagId)
        {
            return _tagRepository.GetTagById(tagId);
        }

        /// <summary>
        /// Get all active tags (or include inactive).
        /// </summary>
        public IReadOnlyList<MovieTag> GetAllTags(bool includeInactive = false)
        {
            return _tagRepository.GetAllTags(includeInactive);
        }

        /// <summary>
        /// Search tags by partial name match (case-insensitive).
        /// </summary>
        public IReadOnlyList<MovieTag> SearchTags(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<MovieTag>().AsReadOnly();

            query = query.Trim().ToLowerInvariant();
            return _tagRepository.GetAllTags(false)
                .Where(t => t.Name.ToLowerInvariant().Contains(query))
                .ToList().AsReadOnly();
        }

        // ── Tag Assignments ───────────────────────────────────

        /// <summary>
        /// Apply a tag to a movie.
        /// </summary>
        public MovieTagAssignment TagMovie(
            int tagId, int movieId, string appliedBy = null)
        {
            var tag = _tagRepository.GetTagById(tagId);
            if (tag == null)
                throw new ArgumentException("Tag not found.", nameof(tagId));
            if (!tag.IsActive)
                throw new InvalidOperationException(
                    "Cannot assign inactive tag \"" + tag.Name + "\".");

            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                throw new ArgumentException("Movie not found.", nameof(movieId));

            if (_tagRepository.HasAssignment(tagId, movieId))
                throw new InvalidOperationException(
                    "Tag \"" + tag.Name + "\" is already applied to \"" +
                    movie.Name + "\".");

            var assignment = new MovieTagAssignment
            {
                TagId = tagId,
                MovieId = movieId,
                TagName = tag.Name,
                MovieName = movie.Name,
                AppliedBy = appliedBy ?? "system",
                AppliedDate = _clock.Now,
            };

            return _tagRepository.AddAssignment(assignment);
        }

        /// <summary>
        /// Remove a tag from a movie. Returns true if the assignment
        /// existed and was removed.
        /// </summary>
        public bool UntagMovie(int tagId, int movieId)
        {
            var assignments = _tagRepository.GetAssignmentsByMovie(movieId);
            var match = assignments.FirstOrDefault(a => a.TagId == tagId);
            if (match == null) return false;
            _tagRepository.RemoveAssignment(match.Id);
            return true;
        }

        /// <summary>
        /// Get all tags applied to a movie.
        /// </summary>
        public IReadOnlyList<MovieTagAssignment> GetMovieTags(int movieId)
        {
            return _tagRepository.GetAssignmentsByMovie(movieId);
        }

        /// <summary>
        /// Get all movies with a specific tag.
        /// </summary>
        public IReadOnlyList<Movie> GetMoviesByTag(int tagId)
        {
            var assignments = _tagRepository.GetAssignmentsByTag(tagId);
            var movies = new List<Movie>();
            foreach (var a in assignments)
            {
                var movie = _movieRepository.GetById(a.MovieId);
                if (movie != null)
                    movies.Add(movie);
            }
            return movies.AsReadOnly();
        }

        /// <summary>
        /// Get movies that have ALL of the specified tags.
        /// </summary>
        public IReadOnlyList<Movie> GetMoviesByAllTags(IEnumerable<int> tagIds)
        {
            if (tagIds == null)
                throw new ArgumentNullException(nameof(tagIds));

            var tagIdList = tagIds.ToList();
            if (tagIdList.Count == 0) return new List<Movie>().AsReadOnly();

            // Get movies for first tag, then intersect with the rest
            var firstSet = _tagRepository.GetAssignmentsByTag(tagIdList[0])
                .Select(a => a.MovieId).ToList();

            var result = new HashSet<int>(firstSet);
            for (int i = 1; i < tagIdList.Count; i++)
            {
                var movieIds = _tagRepository.GetAssignmentsByTag(tagIdList[i])
                    .Select(a => a.MovieId);
                result.IntersectWith(movieIds);
            }

            return result
                .Select(id => _movieRepository.GetById(id))
                .Where(m => m != null)
                .ToList().AsReadOnly();
        }

        /// <summary>
        /// Get movies that have ANY of the specified tags.
        /// </summary>
        public IReadOnlyList<Movie> GetMoviesByAnyTag(IEnumerable<int> tagIds)
        {
            if (tagIds == null)
                throw new ArgumentNullException(nameof(tagIds));

            var movieIds = new HashSet<int>();
            foreach (var tagId in tagIds)
            {
                foreach (var a in _tagRepository.GetAssignmentsByTag(tagId))
                    movieIds.Add(a.MovieId);
            }

            return movieIds
                .Select(id => _movieRepository.GetById(id))
                .Where(m => m != null)
                .ToList().AsReadOnly();
        }

        // ── Bulk Operations ───────────────────────────────────

        /// <summary>
        /// Apply a tag to multiple movies at once.
        /// Returns count of successful assignments (skips duplicates).
        /// </summary>
        public int BulkTagMovies(int tagId, IEnumerable<int> movieIds,
            string appliedBy = null)
        {
            var tag = _tagRepository.GetTagById(tagId);
            if (tag == null)
                throw new ArgumentException("Tag not found.", nameof(tagId));
            if (!tag.IsActive)
                throw new InvalidOperationException(
                    "Cannot assign inactive tag \"" + tag.Name + "\".");

            int count = 0;
            foreach (var movieId in movieIds)
            {
                if (_tagRepository.HasAssignment(tagId, movieId)) continue;
                var movie = _movieRepository.GetById(movieId);
                if (movie == null) continue;

                _tagRepository.AddAssignment(new MovieTagAssignment
                {
                    TagId = tagId,
                    MovieId = movieId,
                    TagName = tag.Name,
                    MovieName = movie.Name,
                    AppliedBy = appliedBy ?? "system",
                    AppliedDate = _clock.Now,
                });
                count++;
            }
            return count;
        }

        /// <summary>
        /// Remove a tag from multiple movies. Returns count removed.
        /// </summary>
        public int BulkUntagMovies(int tagId, IEnumerable<int> movieIds)
        {
            int count = 0;
            foreach (var movieId in movieIds)
            {
                if (UntagMovie(tagId, movieId))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Apply multiple tags to a single movie.
        /// Returns count of successful assignments.
        /// </summary>
        public int BulkTagSingleMovie(int movieId, IEnumerable<int> tagIds,
            string appliedBy = null)
        {
            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                throw new ArgumentException("Movie not found.", nameof(movieId));

            int count = 0;
            foreach (var tagId in tagIds)
            {
                var tag = _tagRepository.GetTagById(tagId);
                if (tag == null || !tag.IsActive) continue;
                if (_tagRepository.HasAssignment(tagId, movieId)) continue;

                _tagRepository.AddAssignment(new MovieTagAssignment
                {
                    TagId = tagId,
                    MovieId = movieId,
                    TagName = tag.Name,
                    MovieName = movie.Name,
                    AppliedBy = appliedBy ?? "system",
                    AppliedDate = _clock.Now,
                });
                count++;
            }
            return count;
        }

        // ── Staff Picks ───────────────────────────────────────

        /// <summary>
        /// Get all current staff pick movies (movies tagged with any
        /// staff-pick tag).
        /// </summary>
        public IReadOnlyList<Movie> GetStaffPicks()
        {
            var staffTags = _tagRepository.GetAllTags(false)
                .Where(t => t.IsStaffPick).ToList();

            var movieIds = new HashSet<int>();
            foreach (var tag in staffTags)
            {
                foreach (var a in _tagRepository.GetAssignmentsByTag(tag.Id))
                    movieIds.Add(a.MovieId);
            }

            return movieIds
                .Select(id => _movieRepository.GetById(id))
                .Where(m => m != null)
                .ToList().AsReadOnly();
        }

        /// <summary>
        /// Promote a tag to staff pick status.
        /// </summary>
        public void PromoteToStaffPick(int tagId)
        {
            var tag = _tagRepository.GetTagById(tagId);
            if (tag == null)
                throw new ArgumentException("Tag not found.", nameof(tagId));
            tag.IsStaffPick = true;
            _tagRepository.UpdateTag(tag);
        }

        /// <summary>
        /// Remove staff pick status from a tag.
        /// </summary>
        public void DemoteFromStaffPick(int tagId)
        {
            var tag = _tagRepository.GetTagById(tagId);
            if (tag == null)
                throw new ArgumentException("Tag not found.", nameof(tagId));
            tag.IsStaffPick = false;
            _tagRepository.UpdateTag(tag);
        }

        // ── Tag Cloud & Analytics ─────────────────────────────

        /// <summary>
        /// Generate tag cloud data with normalized weights for UI display.
        /// Only includes active tags with at least one movie.
        /// </summary>
        public IReadOnlyList<TagUsageStats> GetTagCloud()
        {
            var tags = _tagRepository.GetAllTags(false);
            var stats = new List<TagUsageStats>();

            int maxCount = 0;
            foreach (var tag in tags)
            {
                int count = _tagRepository.GetAssignmentsByTag(tag.Id).Count;
                if (count == 0) continue;
                stats.Add(new TagUsageStats
                {
                    TagId = tag.Id,
                    TagName = tag.Name,
                    Color = tag.Color,
                    IsStaffPick = tag.IsStaffPick,
                    MovieCount = count,
                });
                if (count > maxCount) maxCount = count;
            }

            // Normalize weights
            foreach (var s in stats)
            {
                s.Weight = maxCount > 0
                    ? Math.Round((double)s.MovieCount / maxCount, 3)
                    : 0.0;
            }

            return stats.OrderByDescending(s => s.MovieCount)
                .ToList().AsReadOnly();
        }

        /// <summary>
        /// Get the most popular tags by movie count.
        /// </summary>
        public IReadOnlyList<TagUsageStats> GetPopularTags(int limit = 10)
        {
            return GetTagCloud().Take(limit).ToList().AsReadOnly();
        }

        /// <summary>
        /// Get tagging summary statistics.
        /// </summary>
        public TaggingSummary GetSummary()
        {
            var allTags = _tagRepository.GetAllTags(true);
            var activeTags = allTags.Where(t => t.IsActive).ToList();
            var allMovies = _movieRepository.GetAll();

            int totalAssignments = 0;
            var taggedMovieIds = new HashSet<int>();
            foreach (var tag in allTags)
            {
                var assignments = _tagRepository.GetAssignmentsByTag(tag.Id);
                totalAssignments += assignments.Count;
                foreach (var a in assignments)
                    taggedMovieIds.Add(a.MovieId);
            }

            int totalMovies = allMovies.Count;
            int untagged = totalMovies - taggedMovieIds.Count;

            return new TaggingSummary
            {
                TotalTags = allTags.Count,
                ActiveTags = activeTags.Count,
                TotalAssignments = totalAssignments,
                StaffPickCount = activeTags.Count(t => t.IsStaffPick),
                UntaggedMovies = Math.Max(0, untagged),
                AverageTagsPerMovie = totalMovies > 0
                    ? Math.Round((double)totalAssignments / totalMovies, 2)
                    : 0.0,
            };
        }

        // ── Tag Suggestions ───────────────────────────────────

        /// <summary>
        /// Suggest tags for a movie based on its genre.
        /// Returns tag names that could be created or applied.
        /// </summary>
        public IReadOnlyList<string> SuggestTagsForMovie(int movieId)
        {
            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                throw new ArgumentException("Movie not found.", nameof(movieId));

            var suggestions = new List<string>();

            // Genre-based suggestions
            if (movie.Genre.HasValue)
            {
                string[] genreSuggestions;
                if (GenreTagSuggestions.TryGetValue(movie.Genre.Value,
                    out genreSuggestions))
                {
                    suggestions.AddRange(genreSuggestions);
                }
            }

            // New release suggestion
            if (movie.IsNewRelease)
                suggestions.Add("New Release");

            // High rating suggestion
            if (movie.Rating.HasValue && movie.Rating.Value >= 4)
                suggestions.Add("Highly Rated");

            // Classic suggestion (released >20 years ago)
            if (movie.ReleaseDate.HasValue &&
                (DateTime.Today - movie.ReleaseDate.Value).TotalDays > 365 * 20)
                suggestions.Add("Classic");

            // Remove tags already applied
            var existing = _tagRepository.GetAssignmentsByMovie(movieId)
                .Select(a => a.TagName.ToLowerInvariant())
                .ToList();

            return suggestions
                .Where(s => !existing.Contains(s.ToLowerInvariant()))
                .Distinct()
                .ToList().AsReadOnly();
        }

        /// <summary>
        /// Find movies that share tags with a given movie (related movies).
        /// Excludes the source movie. Sorted by number of shared tags.
        /// </summary>
        public IReadOnlyList<Movie> FindRelatedMovies(int movieId, int limit = 10)
        {
            var myTags = _tagRepository.GetAssignmentsByMovie(movieId)
                .Select(a => a.TagId).ToList();

            if (myTags.Count == 0) return new List<Movie>().AsReadOnly();

            // Count shared tags per other movie
            var sharedCounts = new Dictionary<int, int>();
            foreach (var tagId in myTags)
            {
                foreach (var a in _tagRepository.GetAssignmentsByTag(tagId))
                {
                    if (a.MovieId == movieId) continue;
                    int current;
                    sharedCounts.TryGetValue(a.MovieId, out current);
                    sharedCounts[a.MovieId] = current + 1;
                }
            }

            return sharedCounts
                .OrderByDescending(kv => kv.Value)
                .Take(limit)
                .Select(kv => _movieRepository.GetById(kv.Key))
                .Where(m => m != null)
                .ToList().AsReadOnly();
        }

        /// <summary>
        /// Merge two tags: moves all assignments from source tag to target tag,
        /// then deletes the source tag. Skips duplicates.
        /// Returns count of assignments moved.
        /// </summary>
        public int MergeTags(int sourceTagId, int targetTagId)
        {
            if (sourceTagId == targetTagId)
                throw new ArgumentException("Cannot merge a tag into itself.");

            var source = _tagRepository.GetTagById(sourceTagId);
            if (source == null)
                throw new ArgumentException("Source tag not found.",
                    nameof(sourceTagId));

            var target = _tagRepository.GetTagById(targetTagId);
            if (target == null)
                throw new ArgumentException("Target tag not found.",
                    nameof(targetTagId));

            var sourceAssignments = _tagRepository.GetAssignmentsByTag(sourceTagId);
            int moved = 0;

            foreach (var a in sourceAssignments)
            {
                if (_tagRepository.HasAssignment(targetTagId, a.MovieId))
                {
                    // Already tagged with target, just remove source assignment
                    _tagRepository.RemoveAssignment(a.Id);
                    continue;
                }

                // Create new assignment with target tag
                _tagRepository.AddAssignment(new MovieTagAssignment
                {
                    TagId = targetTagId,
                    MovieId = a.MovieId,
                    TagName = target.Name,
                    MovieName = a.MovieName,
                    AppliedBy = a.AppliedBy,
                    AppliedDate = a.AppliedDate,
                });
                _tagRepository.RemoveAssignment(a.Id);
                moved++;
            }

            _tagRepository.DeleteTag(sourceTagId);
            return moved;
        }
    }
}
