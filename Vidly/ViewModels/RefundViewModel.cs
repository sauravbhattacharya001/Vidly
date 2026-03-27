using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class RefundViewModel
    {
        public List<RefundRequest> Requests { get; set; } = new List<RefundRequest>();
        public RefundRequest CurrentRequest { get; set; }
        public int? RentalId { get; set; }
        public string Message { get; set; }
        public bool IsError { get; set; }

        // Stats
        public int TotalRequests { get; set; }
        public int PendingCount { get; set; }
        public int ApprovedCount { get; set; }
        public int DeniedCount { get; set; }
        public decimal TotalRefunded { get; set; }
    }
}
