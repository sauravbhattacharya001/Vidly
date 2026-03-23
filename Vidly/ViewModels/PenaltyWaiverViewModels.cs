using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class PenaltyWaiverIndexViewModel
    {
        public IReadOnlyList<PenaltyWaiver> Waivers { get; set; }
        public WaiverStats Stats { get; set; }
        public string StatusMessage { get; set; }
        public bool IsError { get; set; }
    }

    public class PenaltyWaiverCreateViewModel
    {
        public Rental Rental { get; set; }
        public decimal AlreadyWaived { get; set; }
        public decimal MaxWaivable => Rental != null ? Rental.LateFee - AlreadyWaived : 0;
        public PenaltyWaiver Waiver { get; set; }
    }
}
