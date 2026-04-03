using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the Drinking Game Generator page.
    /// </summary>
    public class DrinkingGameViewModel
    {
        /// <summary>All available movies to choose from.</summary>
        public IEnumerable<Movie> Movies { get; set; }

        /// <summary>Currently selected movie ID, if any.</summary>
        public int? SelectedMovieId { get; set; }

        /// <summary>Chosen difficulty level.</summary>
        public Difficulty SelectedDifficulty { get; set; } = Difficulty.Standard;

        /// <summary>The generated game, null until a movie is selected.</summary>
        public DrinkingGame Game { get; set; }
    }
}
