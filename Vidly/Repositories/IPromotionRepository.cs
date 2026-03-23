using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    public interface IPromotionRepository
    {
        IReadOnlyList<Promotion> GetAll();
        Promotion GetById(int id);
        void Add(Promotion promotion);
        void Update(Promotion promotion);
        void Remove(int id);
    }
}
