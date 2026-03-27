using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    public class InMemoryDirectorRepository : IDirectorRepository
    {
        private static readonly Dictionary<int, Director> _directors = new Dictionary<int, Director>
        {
            [1] = new Director
            {
                Id = 1, Name = "Andrew Adamson", Bio = "New Zealand filmmaker known for directing the first two Shrek films and The Chronicles of Narnia.",
                BirthDate = new DateTime(1966, 12, 1), Nationality = "New Zealand", KnownFor = "Shrek, Chronicles of Narnia"
            },
            [2] = new Director
            {
                Id = 2, Name = "Francis Ford Coppola", Bio = "Legendary American director, screenwriter, and producer. Five-time Academy Award winner.",
                BirthDate = new DateTime(1939, 4, 7), Nationality = "American", KnownFor = "The Godfather, Apocalypse Now"
            },
            [3] = new Director
            {
                Id = 3, Name = "John Lasseter", Bio = "American animator and filmmaker who was the chief creative officer at Pixar and directed Toy Story.",
                BirthDate = new DateTime(1957, 1, 12), Nationality = "American", KnownFor = "Toy Story, A Bug's Life, Cars"
            }
        };

        private static readonly List<DirectorMovie> _links = new List<DirectorMovie>
        {
            new DirectorMovie { DirectorId = 1, MovieId = 1, Role = "Director" },
            new DirectorMovie { DirectorId = 2, MovieId = 2, Role = "Director" },
            new DirectorMovie { DirectorId = 3, MovieId = 3, Role = "Director" }
        };

        public IReadOnlyList<Director> GetAll()
        {
            return _directors.Values.ToList().AsReadOnly();
        }

        public Director GetById(int id)
        {
            return _directors.TryGetValue(id, out var d) ? d : null;
        }

        public IReadOnlyList<DirectorMovie> GetMovieLinks(int directorId)
        {
            return _links.Where(l => l.DirectorId == directorId).ToList().AsReadOnly();
        }

        public IReadOnlyList<Director> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return GetAll();

            var q = query.Trim().ToLowerInvariant();
            return _directors.Values
                .Where(d => d.Name.ToLowerInvariant().Contains(q)
                          || (d.KnownFor != null && d.KnownFor.ToLowerInvariant().Contains(q))
                          || (d.Nationality != null && d.Nationality.ToLowerInvariant().Contains(q)))
                .ToList().AsReadOnly();
        }
    }
}
