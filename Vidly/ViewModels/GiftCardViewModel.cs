using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the gift cards management page.
    /// </summary>
    public class GiftCardIndexViewModel
    {
        public IReadOnlyList<GiftCard> GiftCards { get; set; }
        public string StatusFilter { get; set; }
        public int TotalCount { get; set; }
        public int ActiveCount { get; set; }
        public int EmptyCount { get; set; }
        public int ExpiredCount { get; set; }
        public decimal TotalOutstandingBalance { get; set; }
    }

    /// <summary>
    /// View model for creating a new gift card.
    /// </summary>
    public class GiftCardCreateViewModel
    {
        public decimal Value { get; set; } = 25.00m;
        public string PurchaserName { get; set; }
        public string RecipientName { get; set; }
        public string Message { get; set; }
        public bool HasExpiration { get; set; }
        public int ExpirationDays { get; set; } = 365;
    }

    /// <summary>
    /// View model for checking gift card balance.
    /// </summary>
    public class GiftCardBalanceViewModel
    {
        public string Code { get; set; }
        public Services.GiftCardBalanceResult Result { get; set; }
    }
}
