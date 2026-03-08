using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Repository interface for movie tags and tag assignments.
    /// </summary>
    public interface ITagRepository
    {
        // ── Tag CRUD ──────────────────────────────────────────

        MovieTag GetTagById(int id);
        MovieTag GetTagByName(string name);
        IReadOnlyList<MovieTag> GetAllTags(bool includeInactive = false);
        MovieTag AddTag(MovieTag tag);
        void UpdateTag(MovieTag tag);
        void DeleteTag(int id);

        // ── Assignments ───────────────────────────────────────

        MovieTagAssignment GetAssignmentById(int id);
        IReadOnlyList<MovieTagAssignment> GetAssignmentsByMovie(int movieId);
        IReadOnlyList<MovieTagAssignment> GetAssignmentsByTag(int tagId);
        IReadOnlyList<MovieTagAssignment> GetAllAssignments();
        MovieTagAssignment AddAssignment(MovieTagAssignment assignment);
        void RemoveAssignment(int id);
        bool HasAssignment(int tagId, int movieId);
        int RemoveAllAssignmentsForTag(int tagId);
        int RemoveAllAssignmentsForMovie(int movieId);
    }
}
