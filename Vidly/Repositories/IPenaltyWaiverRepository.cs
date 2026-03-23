using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    public interface IPenaltyWaiverRepository
    {
        IReadOnlyList<PenaltyWaiver> GetAll();
        PenaltyWaiver GetById(int id);
        IReadOnlyList<PenaltyWaiver> GetByRental(int rentalId);
        PenaltyWaiver Add(PenaltyWaiver waiver);
        decimal GetTotalWaivedForRental(int rentalId);
        WaiverStats GetStats();
    }

    public class WaiverStats
    {
        public int TotalWaivers { get; set; }
        public decimal TotalAmountWaived { get; set; }
        public int FullWaivers { get; set; }
        public int PartialWaivers { get; set; }
    }
}
