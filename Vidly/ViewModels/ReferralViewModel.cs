using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class ReferralViewModel
    {
        public ReferralProgramStats ProgramStats { get; set; }
        public ReferralSummary CustomerSummary { get; set; }
        public IReadOnlyList<Referral> Referrals { get; set; }
        public IReadOnlyList<Customer> Customers { get; set; }
        public int? SelectedCustomerId { get; set; }
        public string NewReferredName { get; set; }
        public string NewReferredEmail { get; set; }
        public string Message { get; set; }
        public string MessageType { get; set; } // success, danger, info
    }
}
