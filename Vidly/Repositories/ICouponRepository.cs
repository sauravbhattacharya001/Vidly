using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Repository interface for coupon management.
    /// </summary>
    public interface ICouponRepository
    {
        IReadOnlyList<Coupon> GetAll();
        Coupon GetById(int id);
        Coupon GetByCode(string code);
        void Add(Coupon coupon);
        void Update(Coupon coupon);
        void Remove(int id);

        /// <summary>
        /// Atomically redeem a coupon: validates it's still usable and increments TimesUsed.
        /// Returns false if the coupon can't be redeemed.
        /// </summary>
        bool TryRedeem(string code);
    }
}
