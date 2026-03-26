using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class LateFeeViewModel
    {
        public List<LateFeePolicy> Policies { get; set; } = new List<LateFeePolicy>();
        public LateFeePolicy SelectedPolicy { get; set; }
        public LateFeeEstimate Estimate { get; set; }
        public List<LateFeeEstimate> Schedule { get; set; }
        public int? CalculateDays { get; set; }
        public int? SelectedPolicyId { get; set; }
    }
}
