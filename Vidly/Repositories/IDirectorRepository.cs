using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    public interface IDirectorRepository
    {
        IReadOnlyList<Director> GetAll();
        Director GetById(int id);
        IReadOnlyList<DirectorMovie> GetMovieLinks(int directorId);
        IReadOnlyList<Director> Search(string query);
    }
}
