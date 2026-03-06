using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidly.Utilities
{
    /// <summary>
    /// Defines a named sort column with a primary key selector and an optional
    /// secondary (tie-breaker) key selector.
    /// </summary>
    /// <typeparam name="T">The element type being sorted.</typeparam>
    public class SortColumn<T>
    {
        /// <summary>Primary sort key selector.</summary>
        public Func<T, object> KeySelector { get; }

        /// <summary>Optional tie-breaker key selector (ascending).</summary>
        public Func<T, object> ThenBy { get; }

        /// <summary>Sort primary key descending when true.</summary>
        public bool Descending { get; }

        public SortColumn(Func<T, object> keySelector, bool descending = false, Func<T, object> thenBy = null)
        {
            KeySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
            Descending = descending;
            ThenBy = thenBy;
        }
    }

    /// <summary>
    /// Declarative, dictionary-based sort helper that replaces repetitive
    /// switch statements over sort-key strings in controllers.
    ///
    /// Usage:
    /// <code>
    ///   var sorter = new SortHelper&lt;Movie&gt;("name", new Dictionary&lt;string, SortColumn&lt;Movie&gt;&gt;
    ///   {
    ///       ["name"]    = new SortColumn&lt;Movie&gt;(m => m.Name),
    ///       ["rating"]  = new SortColumn&lt;Movie&gt;(m => m.Rating ?? 0, descending: true, thenBy: m => m.Name),
    ///       ["id"]      = new SortColumn&lt;Movie&gt;(m => m.Id),
    ///   });
    ///   var sorted = sorter.Apply(movies, "rating");
    /// </code>
    /// </summary>
    /// <typeparam name="T">The element type being sorted.</typeparam>
    public class SortHelper<T>
    {
        private readonly string _defaultKey;
        private readonly Dictionary<string, SortColumn<T>> _columns;

        /// <summary>
        /// Creates a SortHelper with named sort columns.
        /// </summary>
        /// <param name="defaultKey">
        /// The column key to use when the caller passes null/empty/unrecognized sort key.
        /// Must exist in <paramref name="columns"/>.
        /// </param>
        /// <param name="columns">
        /// Dictionary mapping lowercase sort-key names to their sort definitions.
        /// Keys are compared case-insensitively.
        /// </param>
        public SortHelper(string defaultKey, Dictionary<string, SortColumn<T>> columns)
        {
            if (string.IsNullOrWhiteSpace(defaultKey))
                throw new ArgumentException("Default key must not be empty.", nameof(defaultKey));
            _columns = columns ?? throw new ArgumentNullException(nameof(columns));

            // Normalize keys to lowercase
            var normalized = new Dictionary<string, SortColumn<T>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in columns)
                normalized[kvp.Key] = kvp.Value;
            _columns = normalized;

            if (!_columns.ContainsKey(defaultKey))
                throw new ArgumentException($"Default key '{defaultKey}' not found in columns.", nameof(defaultKey));
            _defaultKey = defaultKey;
        }

        /// <summary>
        /// Apply sorting to a sequence.
        /// </summary>
        /// <param name="source">The items to sort.</param>
        /// <param name="sortBy">
        /// The sort key (case-insensitive). Falls back to default if null/empty/unrecognized.
        /// </param>
        /// <returns>The sorted sequence as a List.</returns>
        public List<T> Apply(IEnumerable<T> source, string sortBy)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var key = string.IsNullOrWhiteSpace(sortBy) ? _defaultKey : sortBy;
            if (!_columns.TryGetValue(key, out var col))
                col = _columns[_defaultKey];

            IOrderedEnumerable<T> ordered = col.Descending
                ? source.OrderByDescending(col.KeySelector)
                : source.OrderBy(col.KeySelector);

            if (col.ThenBy != null)
                ordered = ordered.ThenBy(col.ThenBy);

            return ordered.ToList();
        }

        /// <summary>
        /// Resolve the effective sort key (useful for passing back to the view model).
        /// Returns the default key if the input is null/empty/unrecognized.
        /// </summary>
        public string ResolveKey(string sortBy)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                return _defaultKey;
            return _columns.ContainsKey(sortBy) ? sortBy : _defaultKey;
        }

        /// <summary>
        /// Returns the list of recognized sort-key names.
        /// </summary>
        public IReadOnlyList<string> AvailableKeys => _columns.Keys.ToList().AsReadOnly();
    }
}
