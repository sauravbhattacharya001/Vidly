using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    public interface ITriviaRepository
    {
        IEnumerable<TriviaFact> GetAll();
        IEnumerable<TriviaFact> GetByMovieId(int movieId);
        IEnumerable<TriviaFact> GetByCategory(string category);
        TriviaFact GetById(int id);
        void Add(TriviaFact fact);
        void Like(int id);
        void Verify(int id);
        TriviaFact GetRandom();
    }
}
