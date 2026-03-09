using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Vidly.Models
{
    /// <summary>
    /// Post-rental satisfaction survey response.
    /// </summary>
    public class RentalSurvey
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public int RentalId { get; set; }
        public DateTime SubmittedAt { get; set; }

        /// <summary>NPS score: 0-10 ("How likely to recommend us?")</summary>
        [Range(0, 10)]
        public int NpsScore { get; set; }

        /// <summary>Overall satisfaction: 1-5 stars.</summary>
        [Range(1, 5)]
        public int OverallSatisfaction { get; set; }

        /// <summary>Per-category ratings (1-5).</summary>
        public Dictionary<SurveyCategory, int> CategoryRatings { get; set; }
            = new Dictionary<SurveyCategory, int>();

        /// <summary>Optional free-text feedback.</summary>
        [StringLength(2000)]
        public string Comments { get; set; }

        /// <summary>Would rent again from us? yes/no/maybe.</summary>
        public RentAgainResponse WouldRentAgain { get; set; }

        /// <summary>NPS classification.</summary>
        public NpsCategory NpsCategory =>
            NpsScore >= 9 ? NpsCategory.Promoter :
            NpsScore >= 7 ? NpsCategory.Passive :
            NpsCategory.Detractor;
    }

    public enum SurveyCategory
    {
        [Display(Name = "Movie Selection")]
        MovieSelection = 1,

        [Display(Name = "Pricing")]
        Pricing = 2,

        [Display(Name = "Staff Friendliness")]
        StaffFriendliness = 3,

        [Display(Name = "Store Cleanliness")]
        StoreCleanliness = 4,

        [Display(Name = "Checkout Speed")]
        CheckoutSpeed = 5,

        [Display(Name = "Return Process")]
        ReturnProcess = 6,

        [Display(Name = "Disc Quality")]
        DiscQuality = 7,

        [Display(Name = "Online Experience")]
        OnlineExperience = 8
    }

    public enum RentAgainResponse
    {
        Yes = 1,
        Maybe = 2,
        No = 3
    }

    public enum NpsCategory
    {
        Promoter,
        Passive,
        Detractor
    }

    /// <summary>
    /// Aggregated survey metrics for reporting.
    /// </summary>
    public class SurveyReport
    {
        public int TotalResponses { get; set; }
        public double ResponseRate { get; set; }
        public double AverageNps { get; set; }
        public double NpsScore { get; set; }
        public int Promoters { get; set; }
        public int Passives { get; set; }
        public int Detractors { get; set; }
        public double AverageSatisfaction { get; set; }
        public Dictionary<SurveyCategory, double> CategoryAverages { get; set; }
            = new Dictionary<SurveyCategory, double>();
        public SurveyCategory? StrongestCategory { get; set; }
        public SurveyCategory? WeakestCategory { get; set; }
        public double WouldRentAgainPercent { get; set; }
        public string OverallGrade { get; set; }
        public List<string> KeyInsights { get; set; } = new List<string>();
        public List<SurveyTrend> MonthlyTrends { get; set; } = new List<SurveyTrend>();
    }

    /// <summary>
    /// Monthly trend data point.
    /// </summary>
    public class SurveyTrend
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string Period => $"{Year}-{Month:D2}";
        public int ResponseCount { get; set; }
        public double AverageNps { get; set; }
        public double NpsScore { get; set; }
        public double AverageSatisfaction { get; set; }
    }

    /// <summary>
    /// Pending survey invitation.
    /// </summary>
    public class SurveyInvitation
    {
        public int RentalId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string MovieName { get; set; }
        public DateTime RentalDate { get; set; }
        public DateTime ReturnDate { get; set; }
        public DateTime InvitationSentAt { get; set; }
        public bool IsCompleted { get; set; }
    }
}
