using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Repository interface for gift card management.
    /// </summary>
    public interface IGiftCardRepository
    {
        IReadOnlyList<GiftCard> GetAll();
        GiftCard GetById(int id);
        GiftCard GetByCode(string code);
        void Add(GiftCard giftCard);
        void Update(GiftCard giftCard);
        void AddTransaction(int giftCardId, GiftCardTransaction transaction);
    }
}
