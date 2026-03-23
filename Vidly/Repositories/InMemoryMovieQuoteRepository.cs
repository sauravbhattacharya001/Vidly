using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    public interface IMovieQuoteRepository
    {
        IEnumerable<MovieQuote> GetAll();
        IEnumerable<MovieQuote> GetByMovieId(int movieId);
        MovieQuote GetById(int id);
        MovieQuote Add(MovieQuote quote);
        void Upvote(int id);
        void Delete(int id);
    }

    public class InMemoryMovieQuoteRepository : IMovieQuoteRepository
    {
        private static readonly Dictionary<int, MovieQuote> _quotes = new Dictionary<int, MovieQuote>
        {
            [1] = new MovieQuote
            {
                Id = 1, MovieId = 2, MovieName = "The Godfather",
                Text = "I'm gonna make him an offer he can't refuse.",
                Character = "Don Vito Corleone",
                SubmittedByCustomerId = 1, SubmittedByName = "System",
                SubmittedAt = new DateTime(2026, 1, 1), Votes = 42
            },
            [2] = new MovieQuote
            {
                Id = 2, MovieId = 1, MovieName = "Shrek!",
                Text = "Ogres are like onions.",
                Character = "Shrek",
                SubmittedByCustomerId = 1, SubmittedByName = "System",
                SubmittedAt = new DateTime(2026, 1, 1), Votes = 38
            },
            [3] = new MovieQuote
            {
                Id = 3, MovieId = 3, MovieName = "Toy Story",
                Text = "To infinity and beyond!",
                Character = "Buzz Lightyear",
                SubmittedByCustomerId = 1, SubmittedByName = "System",
                SubmittedAt = new DateTime(2026, 1, 1), Votes = 55
            }
        };

        private static readonly object _lock = new object();
        private static int _nextId = 4;

        public IEnumerable<MovieQuote> GetAll()
        {
            lock (_lock)
            {
                return _quotes.Values
                    .OrderByDescending(q => q.Votes)
                    .Select(Clone)
                    .ToList();
            }
        }

        public IEnumerable<MovieQuote> GetByMovieId(int movieId)
        {
            lock (_lock)
            {
                return _quotes.Values
                    .Where(q => q.MovieId == movieId)
                    .OrderByDescending(q => q.Votes)
                    .Select(Clone)
                    .ToList();
            }
        }

        public MovieQuote GetById(int id)
        {
            lock (_lock)
            {
                return _quotes.TryGetValue(id, out var q) ? Clone(q) : null;
            }
        }

        public MovieQuote Add(MovieQuote quote)
        {
            lock (_lock)
            {
                quote.Id = _nextId++;
                quote.SubmittedAt = DateTime.UtcNow;
                quote.Votes = 0;
                _quotes[quote.Id] = Clone(quote);
                return Clone(quote);
            }
        }

        public void Upvote(int id)
        {
            lock (_lock)
            {
                if (_quotes.TryGetValue(id, out var q))
                    q.Votes++;
            }
        }

        public void Delete(int id)
        {
            lock (_lock)
            {
                _quotes.Remove(id);
            }
        }

        private static MovieQuote Clone(MovieQuote q)
        {
            return new MovieQuote
            {
                Id = q.Id,
                MovieId = q.MovieId,
                MovieName = q.MovieName,
                Text = q.Text,
                Character = q.Character,
                SubmittedByCustomerId = q.SubmittedByCustomerId,
                SubmittedByName = q.SubmittedByName,
                SubmittedAt = q.SubmittedAt,
                Votes = q.Votes
            };
        }
    }
}
