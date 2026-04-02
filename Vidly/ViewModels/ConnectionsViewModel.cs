using System.Collections.Generic;

namespace Vidly.ViewModels
{
    public class ConnectionsViewModel
    {
        public List<ConnectionsPuzzle> Puzzles { get; set; }
        public int PuzzleIndex { get; set; }
    }

    public class ConnectionsPuzzle
    {
        public string Title { get; set; }
        public List<ConnectionsGroup> Groups { get; set; }
    }

    public class ConnectionsGroup
    {
        public string Category { get; set; }
        public string Difficulty { get; set; } // yellow, green, blue, purple
        public List<string> Items { get; set; }
    }
}
