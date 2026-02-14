using System.Collections.Generic;

namespace Vidly.Repositories
{
    /// <summary>
    /// Generic repository interface for basic CRUD operations.
    /// Implementations may back this with in-memory stores, EF DbContext, etc.
    /// </summary>
    public interface IRepository<T> where T : class
    {
        T GetById(int id);
        IReadOnlyList<T> GetAll();
        void Add(T entity);
        void Update(T entity);
        void Remove(int id);
    }
}
