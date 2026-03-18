using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    public interface ILostAndFoundRepository
    {
        IEnumerable<LostItem> GetAll();
        IEnumerable<LostItem> GetByStatus(LostItemStatus status);
        IEnumerable<LostItem> GetByCategory(LostItemCategory category);
        LostItem GetById(int id);
        void Add(LostItem item);
        void Update(LostItem item);
        void Remove(int id);

        IEnumerable<LostItemClaim> GetClaimsForItem(int itemId);
        LostItemClaim GetClaimById(int id);
        void AddClaim(LostItemClaim claim);
        void UpdateClaim(LostItemClaim claim);

        LostAndFoundReport GetReport();
        IEnumerable<LostItem> GetOverdueItems();
        IEnumerable<LostItem> Search(string query);
    }
}
