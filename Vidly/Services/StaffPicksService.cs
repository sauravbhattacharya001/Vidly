using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages staff-curated movie picks organized by themes.
    /// </summary>
    public class StaffPicksService
    {
        private readonly IMovieRepository _movieRepo;
        private readonly IClock _clock;
        private static readonly List<StaffPick> _picks = new List<StaffPick>();
        private static readonly object _lock = new object();
        private static int _nextId = 1;
        private static bool _seeded;

        public StaffPicksService(IMovieRepository movieRepository,
            IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _movieRepo = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
            EnsureSeeded();
        }

        private void EnsureSeeded()
        {
            lock (_lock)
            {
                if (_seeded) return;
                _seeded = true;

                var movies = _movieRepo.GetAll();
                if (movies.Count == 0) return;

                var seeds = new[]
                {
                    new { MovieIndex = 0, Staff = "Maria", Theme = "Feel-Good Favorites",
                          Note = "A timeless classic that never fails to make me smile. Perfect for family movie night!" },
                    new { MovieIndex = 1, Staff = "James", Theme = "Must-Watch Masterpieces",
                          Note = "An absolute masterwork of cinema. The storytelling is unmatched — every scene is deliberate." },
                    new { MovieIndex = 2, Staff = "Maria", Theme = "Feel-Good Favorites",
                          Note = "Pure joy from start to finish. Pixar at their very best." },
                    new { MovieIndex = 0, Staff = "James", Theme = "Hidden Gems",
                          Note = "People overlook this one but the humor is incredibly smart. Give it a second watch!" },
                    new { MovieIndex = 1, Staff = "Sarah", Theme = "Must-Watch Masterpieces",
                          Note = "Changed how I think about storytelling. A film that rewards every rewatch." },
                    new { MovieIndex = 2, Staff = "Sarah", Theme = "Weekend Comfort Watches",
                          Note = "My go-to when I need something warm and familiar. Comfort food for the soul." },
                };

                foreach (var s in seeds)
                {
                    if (s.MovieIndex < movies.Count)
                    {
                        _picks.Add(new StaffPick
                        {
                            Id = _nextId++,
                            MovieId = movies[s.MovieIndex].Id,
                            StaffName = s.Staff,
                            Theme = s.Theme,
                            Note = s.Note,
                            PickedDate = _clock.Today.AddDays(-_nextId * 3),
                            IsFeatured = _nextId == 2 // first real pick is featured
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Get all picks as view models with movie details.
        /// </summary>
        public List<StaffPickViewModel> GetAllPicks()
        {
            lock (_lock)
            {
                return _picks.Select(p => new StaffPickViewModel
                {
                    Pick = p,
                    Movie = _movieRepo.GetById(p.MovieId)
                }).Where(vm => vm.Movie != null).ToList();
            }
        }

        /// <summary>
        /// Get the full page view model.
        /// </summary>
        public StaffPicksPageViewModel GetPageViewModel(string filterStaff = null, string filterTheme = null)
        {
            var all = GetAllPicks();

            var filtered = all;
            if (!string.IsNullOrEmpty(filterStaff))
                filtered = filtered.Where(p => p.Pick.StaffName.Equals(filterStaff, StringComparison.OrdinalIgnoreCase)).ToList();
            if (!string.IsNullOrEmpty(filterTheme))
                filtered = filtered.Where(p => p.Pick.Theme.Equals(filterTheme, StringComparison.OrdinalIgnoreCase)).ToList();

            var themedLists = filtered
                .GroupBy(p => p.Pick.Theme)
                .Select(g => new StaffPicksList
                {
                    Theme = g.Key,
                    Description = GetThemeDescription(g.Key),
                    Picks = g.OrderByDescending(p => p.Pick.PickedDate).ToList()
                })
                .OrderBy(l => l.Theme)
                .ToList();

            var featured = all.FirstOrDefault(p => p.Pick.IsFeatured) ?? all.FirstOrDefault();

            return new StaffPicksPageViewModel
            {
                FeaturedPick = featured,
                ThemedLists = themedLists,
                AllStaff = all.Select(p => p.Pick.StaffName).Distinct().OrderBy(s => s).ToList(),
                AllThemes = all.Select(p => p.Pick.Theme).Distinct().OrderBy(t => t).ToList(),
                TotalPicks = all.Count
            };
        }

        /// <summary>
        /// Get picks by a specific staff member.
        /// </summary>
        public List<StaffPickViewModel> GetPicksByStaff(string staffName)
        {
            return GetAllPicks()
                .Where(p => p.Pick.StaffName.Equals(staffName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.Pick.PickedDate)
                .ToList();
        }

        /// <summary>
        /// Get picks for a specific theme.
        /// </summary>
        public List<StaffPickViewModel> GetPicksByTheme(string theme)
        {
            return GetAllPicks()
                .Where(p => p.Pick.Theme.Equals(theme, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.Pick.PickedDate)
                .ToList();
        }

        /// <summary>
        /// Add a new staff pick.
        /// </summary>
        public StaffPick AddPick(int movieId, string staffName, string theme, string note, bool isFeatured = false)
        {
            if (string.IsNullOrWhiteSpace(staffName))
                throw new ArgumentException("Staff name is required.", nameof(staffName));
            if (string.IsNullOrWhiteSpace(theme))
                throw new ArgumentException("Theme is required.", nameof(theme));
            if (_movieRepo.GetById(movieId) == null)
                throw new ArgumentException("Movie not found.", nameof(movieId));

            lock (_lock)
            {
                if (isFeatured)
                    _picks.ForEach(p => p.IsFeatured = false);

                var pick = new StaffPick
                {
                    Id = _nextId++,
                    MovieId = movieId,
                    StaffName = staffName.Trim(),
                    Theme = theme.Trim(),
                    Note = (note ?? "").Trim(),
                    PickedDate = _clock.Now,
                    IsFeatured = isFeatured
                };
                _picks.Add(pick);
                return pick;
            }
        }

        /// <summary>
        /// Remove a pick by ID.
        /// </summary>
        public bool RemovePick(int id)
        {
            lock (_lock)
            {
                return _picks.RemoveAll(p => p.Id == id) > 0;
            }
        }

        /// <summary>
        /// Get unique staff names.
        /// </summary>
        public List<string> GetStaffNames()
        {
            lock (_lock)
            {
                return _picks.Select(p => p.StaffName).Distinct().OrderBy(s => s).ToList();
            }
        }

        /// <summary>
        /// Get unique themes.
        /// </summary>
        public List<string> GetThemes()
        {
            lock (_lock)
            {
                return _picks.Select(p => p.Theme).Distinct().OrderBy(t => t).ToList();
            }
        }

        private static string GetThemeDescription(string theme)
        {
            var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Feel-Good Favorites"] = "Movies that guarantee a smile — our staff's comfort picks.",
                ["Must-Watch Masterpieces"] = "The films our staff considers essential viewing.",
                ["Hidden Gems"] = "Under-the-radar picks that deserve more love.",
                ["Weekend Comfort Watches"] = "Perfect for lazy weekend afternoons on the couch."
            };
            return descriptions.TryGetValue(theme, out var desc) ? desc : "Curated picks from our team.";
        }

        /// <summary>Reset for testing.</summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _picks.Clear();
                _nextId = 1;
                _seeded = false;
            }
        }
    }
}
