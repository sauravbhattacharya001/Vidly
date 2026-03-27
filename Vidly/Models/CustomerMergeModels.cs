using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a pair of customers detected as potential duplicates.
    /// </summary>
    public class DuplicateCandidate
    {
        public Customer CustomerA { get; set; }
        public Customer CustomerB { get; set; }

        /// <summary>Match confidence from 0.0 to 1.0.</summary>
        public double Confidence { get; set; }

        /// <summary>Human-readable reason the pair was flagged.</summary>
        public string Reason { get; set; }
    }

    /// <summary>
    /// Specifies how to merge two customer records.
    /// </summary>
    public class MergeRequest
    {
        /// <summary>The customer ID to keep (primary).</summary>
        public int PrimaryId { get; set; }

        /// <summary>The customer ID to retire (secondary).</summary>
        public int SecondaryId { get; set; }

        /// <summary>Which name to keep: "primary" or "secondary".</summary>
        public string KeepName { get; set; } = "primary";

        /// <summary>Which email to keep.</summary>
        public string KeepEmail { get; set; } = "primary";

        /// <summary>Which phone to keep.</summary>
        public string KeepPhone { get; set; } = "primary";

        /// <summary>Which membership type to keep.</summary>
        public string KeepMembership { get; set; } = "higher";
    }

    /// <summary>
    /// Result of a merge operation.
    /// </summary>
    public class MergeResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public Customer MergedCustomer { get; set; }
        public int RentalsTransferred { get; set; }
        public DateTime MergedAt { get; set; }
    }

    /// <summary>
    /// Audit log entry for a completed merge.
    /// </summary>
    public class MergeAuditEntry
    {
        public int Id { get; set; }
        public int PrimaryId { get; set; }
        public string PrimaryName { get; set; }
        public int SecondaryId { get; set; }
        public string SecondaryName { get; set; }
        public int RentalsTransferred { get; set; }
        public DateTime MergedAt { get; set; }
    }
}
