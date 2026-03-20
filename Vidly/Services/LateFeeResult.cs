namespace Vidly.Services
{
    /// <summary>
    /// Detailed late fee calculation result.
    /// </summary>
    public class LateFeeResult
    {
        public int RentalId { get; set; }
        public int RawDaysLate { get; set; }
        public int GracePeriodDays { get; set; }
        public int EffectiveDaysLate { get; set; }
        public decimal BaseFee { get; set; }
        public decimal LateFeeDiscount { get; set; }
        public decimal FinalFee { get; set; }
        public bool WasFeeWaived { get; set; }
        public string Explanation { get; set; }
    }
}
