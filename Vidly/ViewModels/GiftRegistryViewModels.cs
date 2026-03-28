using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class GiftRegistryIndexViewModel
    {
        public IReadOnlyList<GiftRegistry> Registries { get; set; }
        public string SearchQuery { get; set; }
    }

    public class GiftRegistryDetailViewModel
    {
        public GiftRegistry Registry { get; set; }
        public int WantedCount { get; set; }
        public int FulfilledCount { get; set; }
        public int ProgressPercent { get; set; }
    }

    public class GiftRegistryCreateViewModel
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public GiftRegistryOccasion Occasion { get; set; }
        public string EventDate { get; set; }
        public bool IsPublic { get; set; } = true;
    }

    public class GiftRegistryAddItemViewModel
    {
        public int RegistryId { get; set; }
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public string Note { get; set; }
        public IReadOnlyList<Movie> AvailableMovies { get; set; }
    }

    public class GiftRegistryFulfillViewModel
    {
        public int RegistryId { get; set; }
        public int ItemId { get; set; }
        public string YourName { get; set; }
    }
}
