using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    public enum NegotiationStrategy
    {
        FirstTime,
        Casual,
        Loyal,
        Premium,
        Bulk
    }

    public enum NegotiationStatus
    {
        Negotiating,
        Accepted,
        Rejected,
        Expired
    }

    public class NegotiationOffer
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public decimal OriginalRate { get; set; }
        public decimal OfferedRate { get; set; }
        public decimal DiscountPct { get; set; }
        public int ExtendedDays { get; set; }
        public List<string> BonusPerks { get; set; } = new List<string>();
        public DateTime ExpiresAt { get; set; }
        public double Confidence { get; set; }
    }

    public class NegotiationRound
    {
        public int RoundNumber { get; set; }
        public string CustomerArgument { get; set; }
        public string SystemResponse { get; set; }
        public decimal OfferAdjustment { get; set; }
        public NegotiationOffer NewOffer { get; set; }
    }

    public class NegotiationSession
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public List<NegotiationRound> Rounds { get; set; } = new List<NegotiationRound>();
        public NegotiationStrategy Strategy { get; set; }
        public int LoyaltyScore { get; set; }
        public NegotiationOffer InitialOffer { get; set; }
        public NegotiationOffer FinalOffer { get; set; }
        public NegotiationStatus Status { get; set; }
        public List<string> ProactiveInsights { get; set; } = new List<string>();
    }

    public class NegotiationViewModel
    {
        public List<Customer> Customers { get; set; } = new List<Customer>();
        public List<Movie> AvailableMovies { get; set; } = new List<Movie>();
        public int? SelectedCustomerId { get; set; }
        public int? SelectedMovieId { get; set; }
        public NegotiationSession Session { get; set; }
        public string ErrorMessage { get; set; }
    }
}
