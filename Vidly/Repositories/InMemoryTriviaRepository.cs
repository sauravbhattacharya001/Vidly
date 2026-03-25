using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    public class InMemoryTriviaRepository : ITriviaRepository
    {
        private static readonly List<TriviaFact> _facts = new List<TriviaFact>
        {
            new TriviaFact { Id = 1, MovieId = 1, MovieName = "Shrek", Fact = "Mike Myers re-recorded all his dialogue in a Scottish accent after the film was already partially animated — costing the studio $4 million.", Category = "Cast & Crew", SubmittedByCustomerId = 1, SubmittedByName = "Staff", Likes = 12, IsVerified = true },
            new TriviaFact { Id = 2, MovieId = 2, MovieName = "Wall-e", Fact = "The directive 'A113' that appears in the film is a reference to a classroom at CalArts where many Pixar and Disney animators studied.", Category = "Easter Egg", SubmittedByCustomerId = 1, SubmittedByName = "Staff", Likes = 8, IsVerified = true },
            new TriviaFact { Id = 3, MovieId = 3, MovieName = "Toy Story", Fact = "Woody was originally written as a ventriloquist dummy and was much meaner in early drafts. The studio nearly shut down production.", Category = "Behind the Scenes", SubmittedByCustomerId = 1, SubmittedByName = "Staff", Likes = 15, IsVerified = true },
            new TriviaFact { Id = 4, MovieId = 4, MovieName = "The Hangover", Fact = "The famous missing tooth was real — Ed Helms actually has a dental implant and had it removed for the role.", Category = "Cast & Crew", SubmittedByCustomerId = 1, SubmittedByName = "Staff", Likes = 20, IsVerified = true },
            new TriviaFact { Id = 5, MovieId = 1, MovieName = "Shrek", Fact = "Chris Farley had already recorded most of the dialogue for Shrek before he passed away. The role was then recast with Mike Myers.", Category = "Production", SubmittedByCustomerId = 1, SubmittedByName = "Staff", Likes = 18, IsVerified = true },
        };

        private static int _nextId = 6;
        private static readonly Random _random = new Random();

        public IEnumerable<TriviaFact> GetAll() =>
            _facts.OrderByDescending(f => f.Likes).ToList();

        public IEnumerable<TriviaFact> GetByMovieId(int movieId) =>
            _facts.Where(f => f.MovieId == movieId).OrderByDescending(f => f.Likes).ToList();

        public IEnumerable<TriviaFact> GetByCategory(string category) =>
            _facts.Where(f => f.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                  .OrderByDescending(f => f.Likes).ToList();

        public TriviaFact GetById(int id) =>
            _facts.FirstOrDefault(f => f.Id == id);

        public void Add(TriviaFact fact)
        {
            fact.Id = _nextId++;
            fact.SubmittedAt = DateTime.Now;
            _facts.Add(fact);
        }

        public void Like(int id)
        {
            var fact = GetById(id);
            if (fact != null) fact.Likes++;
        }

        public void Verify(int id)
        {
            var fact = GetById(id);
            if (fact != null) fact.IsVerified = !fact.IsVerified;
        }

        public TriviaFact GetRandom()
        {
            if (!_facts.Any()) return null;
            return _facts[_random.Next(_facts.Count)];
        }
    }
}
