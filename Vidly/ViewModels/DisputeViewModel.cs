using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the Disputes dashboard — lists all disputes with
    /// filters, stats, and forms for submission and resolution.
    /// </summary>
    public class DisputeViewModel
    {
        public IEnumerable<Dispute> Disputes { get; set; } = Enumerable.Empty<Dispute>();
        public DisputeStats Stats { get; set; } = new DisputeStats();

        // Filters
        public DisputeStatus? FilterStatus { get; set; }
        public DisputeType? FilterType { get; set; }
        public DisputePriority? FilterPriority { get; set; }
        public string SearchQuery { get; set; }

        // Flash messages
        public string StatusMessage { get; set; }
        public bool IsError { get; set; }
    }

    /// <summary>
    /// Aggregate statistics for the disputes dashboard.
    /// </summary>
    public class DisputeStats
    {
        public int Total { get; set; }
        public int Open { get; set; }
        public int UnderReview { get; set; }
        public int Approved { get; set; }
        public int PartiallyApproved { get; set; }
        public int Denied { get; set; }
        public int Expired { get; set; }
        public decimal TotalDisputed { get; set; }
        public decimal TotalRefunded { get; set; }
        public double ApprovalRate { get; set; }
        public double AverageResolutionDays { get; set; }
    }
}
