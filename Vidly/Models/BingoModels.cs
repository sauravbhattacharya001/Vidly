using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// A single cell on a bingo card.
    /// </summary>
    public class BingoCell
    {
        public int Row { get; set; }
        public int Col { get; set; }
        public string Text { get; set; }
        public bool IsFreeSpace { get; set; }
        public bool IsMarked { get; set; }
    }

    /// <summary>
    /// A generated bingo card with a 5x5 grid of tropes/prompts.
    /// </summary>
    public class BingoCard
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public Genre? Genre { get; set; }
        public string Theme { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<BingoCell> Cells { get; set; } = new List<BingoCell>();
    }

    /// <summary>
    /// Request to generate a bingo card.
    /// </summary>
    public class BingoRequest
    {
        public Genre? Genre { get; set; }
        public string Theme { get; set; }
        public bool IncludeFreeSpace { get; set; } = true;
    }
}
