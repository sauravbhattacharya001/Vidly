using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    public interface IGiftRegistryRepository
    {
        IReadOnlyList<GiftRegistry> GetAll();
        GiftRegistry GetById(int id);
        GiftRegistry GetByShareCode(string shareCode);
        IReadOnlyList<GiftRegistry> GetByCustomerId(int customerId);
        void Add(GiftRegistry registry);
        void Update(GiftRegistry registry);
        void Remove(int id);
        void AddItem(int registryId, GiftRegistryItem item);
        void FulfillItem(int registryId, int itemId, string fulfilledBy);
        void RemoveItem(int registryId, int itemId);
    }
}
