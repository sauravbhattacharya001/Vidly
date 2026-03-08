using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// In-memory implementation of ITagRepository for testing and demos.
    /// </summary>
    public class InMemoryTagRepository : ITagRepository
    {
        private readonly Dictionary<int, MovieTag> _tags =
            new Dictionary<int, MovieTag>();
        private readonly Dictionary<int, MovieTagAssignment> _assignments =
            new Dictionary<int, MovieTagAssignment>();
        private int _nextTagId = 1;
        private int _nextAssignmentId = 1;

        // ── Tag CRUD ──────────────────────────────────────────

        public MovieTag GetTagById(int id)
        {
            MovieTag tag;
            return _tags.TryGetValue(id, out tag) ? tag : null;
        }

        public MovieTag GetTagByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _tags.Values.FirstOrDefault(
                t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<MovieTag> GetAllTags(bool includeInactive = false)
        {
            var query = _tags.Values.AsEnumerable();
            if (!includeInactive)
                query = query.Where(t => t.IsActive);
            return query.OrderBy(t => t.Name).ToList().AsReadOnly();
        }

        public MovieTag AddTag(MovieTag tag)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            tag.Id = _nextTagId++;
            _tags[tag.Id] = tag;
            return tag;
        }

        public void UpdateTag(MovieTag tag)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (!_tags.ContainsKey(tag.Id))
                throw new KeyNotFoundException("Tag not found: " + tag.Id);
            _tags[tag.Id] = tag;
        }

        public void DeleteTag(int id)
        {
            _tags.Remove(id);
        }

        // ── Assignments ───────────────────────────────────────

        public MovieTagAssignment GetAssignmentById(int id)
        {
            MovieTagAssignment a;
            return _assignments.TryGetValue(id, out a) ? a : null;
        }

        public IReadOnlyList<MovieTagAssignment> GetAssignmentsByMovie(int movieId)
        {
            return _assignments.Values
                .Where(a => a.MovieId == movieId)
                .OrderBy(a => a.TagName)
                .ToList().AsReadOnly();
        }

        public IReadOnlyList<MovieTagAssignment> GetAssignmentsByTag(int tagId)
        {
            return _assignments.Values
                .Where(a => a.TagId == tagId)
                .OrderBy(a => a.MovieName)
                .ToList().AsReadOnly();
        }

        public IReadOnlyList<MovieTagAssignment> GetAllAssignments()
        {
            return _assignments.Values.ToList().AsReadOnly();
        }

        public MovieTagAssignment AddAssignment(MovieTagAssignment assignment)
        {
            if (assignment == null) throw new ArgumentNullException(nameof(assignment));
            assignment.Id = _nextAssignmentId++;
            _assignments[assignment.Id] = assignment;
            return assignment;
        }

        public void RemoveAssignment(int id)
        {
            _assignments.Remove(id);
        }

        public bool HasAssignment(int tagId, int movieId)
        {
            return _assignments.Values.Any(
                a => a.TagId == tagId && a.MovieId == movieId);
        }

        public int RemoveAllAssignmentsForTag(int tagId)
        {
            var ids = _assignments.Values
                .Where(a => a.TagId == tagId)
                .Select(a => a.Id).ToList();
            foreach (var id in ids) _assignments.Remove(id);
            return ids.Count;
        }

        public int RemoveAllAssignmentsForMovie(int movieId)
        {
            var ids = _assignments.Values
                .Where(a => a.MovieId == movieId)
                .Select(a => a.Id).ToList();
            foreach (var id in ids) _assignments.Remove(id);
            return ids.Count;
        }
    }
}
