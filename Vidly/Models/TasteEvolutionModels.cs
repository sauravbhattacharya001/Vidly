using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// A snapshot of genre preference at a point in time.
    /// </summary>
    public class GenreSnapshot
    {
        public string Period { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public int RentalCount { get; set; }
        public Dictionary<Genre, double> GenreWeights { get; set; } = new Dictionary<Genre, double>();
        public Genre TopGenre { get; set; }
    }

    /// <summary>
    /// Describes a detected shift in genre preference over time.
    /// </summary>
    public class GenreDrift
    {
        public Genre Genre { get; set; }
        public string GenreName { get; set; }
        public double EarlyWeight { get; set; }
        public double RecentWeight { get; set; }
        public double Delta { get; set; }
        public string Direction { get; set; } // "Rising", "Falling", "Stable"
        public string Emoji { get; set; }
    }

    /// <summary>
    /// A predicted future genre preference based on momentum.
    /// </summary>
    public class GenrePrediction
    {
        public Genre Genre { get; set; }
        public string GenreName { get; set; }
        public double CurrentWeight { get; set; }
        public double PredictedWeight { get; set; }
        public double Momentum { get; set; }
        public double Confidence { get; set; }
    }

    /// <summary>
    /// A proactive movie suggestion based on evolving taste.
    /// </summary>
    public class TasteSuggestion
    {
        public Movie Movie { get; set; }
        public double RelevanceScore { get; set; }
        public string Reason { get; set; }
        public Genre MatchedGenre { get; set; }
        public bool IsEmergingTaste { get; set; }
    }

    /// <summary>
    /// Complete taste evolution report for a customer.
    /// </summary>
    public class TasteEvolutionReport
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int TotalRentals { get; set; }
        public List<GenreSnapshot> Timeline { get; set; } = new List<GenreSnapshot>();
        public List<GenreDrift> Drifts { get; set; } = new List<GenreDrift>();
        public List<GenrePrediction> Predictions { get; set; } = new List<GenrePrediction>();
        public List<TasteSuggestion> Suggestions { get; set; } = new List<TasteSuggestion>();
        public string TastePersona { get; set; }
        public string PersonaEmoji { get; set; }
        public string Insight { get; set; }
    }

    /// <summary>
    /// View model for the Taste Evolution page.
    /// </summary>
    public class TasteEvolutionViewModel
    {
        public TasteEvolutionReport Report { get; set; }
        public List<Customer> Customers { get; set; } = new List<Customer>();
        public int? SelectedCustomerId { get; set; }
        public string ErrorMessage { get; set; }
    }
}
