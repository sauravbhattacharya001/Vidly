using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the Store Hours & Locations page.
    /// </summary>
    public class StoreInfoViewModel
    {
        public IReadOnlyList<StoreInfo> Stores { get; set; }
        public StoreInfo SelectedStore { get; set; }
    }
}
