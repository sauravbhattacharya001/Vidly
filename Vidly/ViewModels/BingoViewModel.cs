using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class BingoViewModel
    {
        public BingoCard Card { get; set; }
        public BingoRequest Request { get; set; }
        public IReadOnlyList<string> AvailableThemes { get; set; }

        public BingoViewModel()
        {
            Request = new BingoRequest();
            AvailableThemes = new List<string>();
        }
    }
}
