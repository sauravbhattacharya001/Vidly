using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class LostAndFoundViewModel
    {
        public IEnumerable<LostItem> Items { get; set; } = new List<LostItem>();
        public IEnumerable<LostItemClaim> Claims { get; set; } = new List<LostItemClaim>();
        public IEnumerable<Customer> Customers { get; set; } = new List<Customer>();
        public LostAndFoundReport Report { get; set; }
        public LostItemStatus? FilterStatus { get; set; }
        public LostItemCategory? FilterCategory { get; set; }
        public string SearchQuery { get; set; }
        public string StatusMessage { get; set; }
        public bool IsError { get; set; }
    }
}
