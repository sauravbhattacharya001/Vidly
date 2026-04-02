using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidly.Services
{
    /// <summary>
    /// Provides pre-built Movie Connections puzzles — group 16 movies into
    /// 4 categories of 4 that share a hidden connection.
    /// </summary>
    public class ConnectionsService
    {
        private static readonly List<ConnectionsPuzzleData> _puzzles = new List<ConnectionsPuzzleData>
        {
            new ConnectionsPuzzleData
            {
                Title = "Classic Connections #1",
                Groups = new[]
                {
                    new GroupData("Movies with a color in the title", "yellow",
                        new[] { "The Green Mile", "Scarlet Street", "Blue Velvet", "The Red Shoes" }),
                    new GroupData("Movies set in space", "green",
                        new[] { "Gravity", "Alien", "Interstellar", "Moon" }),
                    new GroupData("One-word animal titles", "blue",
                        new[] { "Jaws", "Bambi", "Ratatouille", "Babe" }),
                    new GroupData("Directors who act in their own films", "purple",
                        new[] { "Get Out", "Reservoir Dogs", "Annie Hall", "The Sixth Sense" })
                }
            },
            new ConnectionsPuzzleData
            {
                Title = "Classic Connections #2",
                Groups = new[]
                {
                    new GroupData("Movie titles that are character names", "yellow",
                        new[] { "Rocky", "Forrest Gump", "Napoleon", "Amadeus" }),
                    new GroupData("Movies with twist endings", "green",
                        new[] { "Fight Club", "The Usual Suspects", "Primal Fear", "Gone Girl" }),
                    new GroupData("Movies set in high school", "blue",
                        new[] { "Mean Girls", "Grease", "Clueless", "Heathers" }),
                    new GroupData("Spielberg films", "purple",
                        new[] { "Duel", "Munich", "Hook", "1941" })
                }
            },
            new ConnectionsPuzzleData
            {
                Title = "Classic Connections #3",
                Groups = new[]
                {
                    new GroupData("Sequels better than the original", "yellow",
                        new[] { "The Dark Knight", "Terminator 2", "Aliens", "The Godfather Part II" }),
                    new GroupData("Movies about heists", "green",
                        new[] { "Heat", "Ocean's Eleven", "The Italian Job", "Inside Man" }),
                    new GroupData("Black & white films", "blue",
                        new[] { "Casablanca", "Psycho", "12 Angry Men", "Schindler's List" }),
                    new GroupData("Movies with food in the title", "purple",
                        new[] { "Chocolat", "Sausage Party", "Mystic Pizza", "Pineapple Express" })
                }
            },
            new ConnectionsPuzzleData
            {
                Title = "Classic Connections #4",
                Groups = new[]
                {
                    new GroupData("Movies set on boats/ships", "yellow",
                        new[] { "Titanic", "Life of Pi", "Master and Commander", "Jaws" }),
                    new GroupData("Tom Hanks movies", "green",
                        new[] { "Cast Away", "Big", "Philadelphia", "The Terminal" }),
                    new GroupData("Movies with numbers in the title", "blue",
                        new[] { "Se7en", "21 Jump Street", "300", "District 9" }),
                    new GroupData("Animated Pixar films", "purple",
                        new[] { "Up", "Coco", "Brave", "Soul" })
                }
            },
            new ConnectionsPuzzleData
            {
                Title = "Classic Connections #5",
                Groups = new[]
                {
                    new GroupData("Movies about dreams/sleep", "yellow",
                        new[] { "Inception", "Eternal Sunshine", "Dreamscape", "Waking Life" }),
                    new GroupData("Based on true stories", "green",
                        new[] { "The Social Network", "Catch Me If You Can", "Spotlight", "Erin Brockovich" }),
                    new GroupData("Post-apocalyptic movies", "blue",
                        new[] { "Mad Max: Fury Road", "I Am Legend", "WALL-E", "Children of Men" }),
                    new GroupData("Movies with city names as titles", "purple",
                        new[] { "Chicago", "Troy", "Fargo", "Munich" })
                }
            }
        };

        private static readonly Random _rng = new Random();

        public ConnectionsPuzzleData GetPuzzle(int index)
        {
            if (index < 0 || index >= _puzzles.Count)
                index = 0;
            return _puzzles[index];
        }

        public ConnectionsPuzzleData GetRandomPuzzle(out int index)
        {
            index = _rng.Next(_puzzles.Count);
            return _puzzles[index];
        }

        public int PuzzleCount => _puzzles.Count;

        public List<string> GetShuffledItems(ConnectionsPuzzleData puzzle)
        {
            var items = puzzle.Groups.SelectMany(g => g.Items).ToList();
            // Fisher-Yates shuffle
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                var temp = items[i];
                items[i] = items[j];
                items[j] = temp;
            }
            return items;
        }
    }

    public class ConnectionsPuzzleData
    {
        public string Title { get; set; }
        public GroupData[] Groups { get; set; }
    }

    public class GroupData
    {
        public string Category { get; set; }
        public string Difficulty { get; set; }
        public string[] Items { get; set; }

        public GroupData(string category, string difficulty, string[] items)
        {
            Category = category;
            Difficulty = difficulty;
            Items = items;
        }
    }
}
