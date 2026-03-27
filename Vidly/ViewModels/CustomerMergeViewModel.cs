using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class CustomerMergeViewModel
    {
        /// <summary>Detected duplicate pairs, sorted by confidence.</summary>
        public IReadOnlyList<DuplicateCandidate> Duplicates { get; set; }
            = new List<DuplicateCandidate>();

        /// <summary>Merge history log.</summary>
        public IReadOnlyList<MergeAuditEntry> AuditLog { get; set; }
            = new List<MergeAuditEntry>();

        /// <summary>Result of the last merge operation, if any.</summary>
        public MergeResult LastResult { get; set; }

        /// <summary>All customers for manual merge selection.</summary>
        public IReadOnlyList<Customer> AllCustomers { get; set; }
            = new List<Customer>();
    }
}
