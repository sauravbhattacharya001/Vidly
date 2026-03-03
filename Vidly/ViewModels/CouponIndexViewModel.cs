using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the coupon management index page.
    /// </summary>
    public class CouponIndexViewModel
    {
        public IReadOnlyList<Coupon> Coupons { get; set; } = new List<Coupon>();
        public string StatusFilter { get; set; }
        public int TotalCount { get; set; }
        public int ActiveCount { get; set; }
        public int ExpiredCount { get; set; }
        public int ExhaustedCount { get; set; }
    }
}
