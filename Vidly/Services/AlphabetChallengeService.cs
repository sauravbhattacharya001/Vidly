using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Alphabet Challenge — track which letters of the alphabet are covered
    /// by movies in the catalog. Goal: collect movies starting with every letter A-Z.
    /// </summary>
    public class AlphabetChallengeService
    {
        private readonly IMovieRepository _movieRepository;

        public AlphabetChallengeService(IMovieRepository movieRepository)
        {
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        /// <summary>
        /// Build the full A-Z board showing which letters have movies and which are missing.
        /// </summary>
        public AlphabetBoard GetBoard()
        {
            var movies = _movieRepository.GetAll();
            var board = new AlphabetBoard();

            // Group movies by their first letter (uppercase, ignoring "The ", "A ", "An ")
            foreach (var movie in movies)
            {
                var letter = GetSortLetter(movie.Name);
                if (letter >= 'A' && letter <= 'Z')
                {
                    if (!board.LetterMovies.ContainsKey(letter))
                        board.LetterMovies[letter] = new List<AlphabetMovieEntry>();

                    board.LetterMovies[letter].Add(new AlphabetMovieEntry
                    {
                        MovieId = movie.Id,
                        Name = movie.Name,
                        Genre = movie.Genre,
                        Rating = movie.Rating,
                        ReleaseYear = movie.ReleaseDate?.Year
                    });
                }
            }

            // Calculate stats
            board.TotalLetters = 26;
            board.CoveredLetters = board.LetterMovies.Count;
            board.MissingLetters = new List<char>();
            for (char c = 'A'; c <= 'Z'; c++)
            {
                if (!board.LetterMovies.ContainsKey(c))
                    board.MissingLetters.Add(c);
            }

            board.CompletionPercent = (int)Math.Round(100.0 * board.CoveredLetters / board.TotalLetters);
            board.TotalMovies = movies.Count;

            // Rarest letter (covered letter with fewest movies)
            if (board.LetterMovies.Any())
            {
                var rarest = board.LetterMovies.OrderBy(kv => kv.Value.Count).First();
                board.RarestLetter = rarest.Key;
                board.RarestCount = rarest.Value.Count;

                var most = board.LetterMovies.OrderByDescending(kv => kv.Value.Count).First();
                board.MostPopularLetter = most.Key;
                board.MostPopularCount = most.Value.Count;
            }

            // Genre diversity per letter
            board.GenreDiversity = board.LetterMovies.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Where(m => m.Genre.HasValue).Select(m => m.Genre.Value).Distinct().Count()
            );

            return board;
        }

        /// <summary>
        /// Get the sort letter for a movie name, stripping common articles.
        /// </summary>
        private static char GetSortLetter(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ' ';

            var trimmed = name.Trim();

            // Strip leading articles
            string[] articles = { "The ", "A ", "An " };
            foreach (var article in articles)
            {
                if (trimmed.StartsWith(article, StringComparison.OrdinalIgnoreCase) && trimmed.Length > article.Length)
                {
                    trimmed = trimmed.Substring(article.Length).TrimStart();
                    break;
                }
            }

            return char.ToUpperInvariant(trimmed[0]);
        }
    }

    public class AlphabetBoard
    {
        public Dictionary<char, List<AlphabetMovieEntry>> LetterMovies { get; set; } = new Dictionary<char, List<AlphabetMovieEntry>>();
        public List<char> MissingLetters { get; set; } = new List<char>();
        public int TotalLetters { get; set; }
        public int CoveredLetters { get; set; }
        public int CompletionPercent { get; set; }
        public int TotalMovies { get; set; }
        public char RarestLetter { get; set; }
        public int RarestCount { get; set; }
        public char MostPopularLetter { get; set; }
        public int MostPopularCount { get; set; }
        public Dictionary<char, int> GenreDiversity { get; set; } = new Dictionary<char, int>();
    }

    public class AlphabetMovieEntry
    {
        public int MovieId { get; set; }
        public string Name { get; set; }
        public Genre? Genre { get; set; }
        public int? Rating { get; set; }
        public int? ReleaseYear { get; set; }
    }
}
