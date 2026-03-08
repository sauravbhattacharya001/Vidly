using System;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Condition grades for physical media (DVDs, Blu-rays).
    /// </summary>
    public enum DiscCondition
    {
        [Display(Name = "Mint")]
        Mint = 0,

        [Display(Name = "Good")]
        Good = 1,

        [Display(Name = "Fair")]
        Fair = 2,

        [Display(Name = "Poor")]
        Poor = 3,

        [Display(Name = "Damaged")]
        Damaged = 4,

        [Display(Name = "Unplayable")]
        Unplayable = 5
    }

    /// <summary>
    /// Categories of damage that can occur to rental media.
    /// </summary>
    public enum DamageType
    {
        None,
        Scratches,
        Cracks,
        DiscRot,
        CaseDamage,
        MissingInsert,
        WaterDamage,
        HeatDamage,
        Other
    }

    /// <summary>
    /// A single damage report filed when a movie is returned in
    /// worse condition than when it was rented out.
    /// </summary>
    public class DamageReport
    {
        private static int _nextId = 1;

        public int Id { get; set; }

        [Required]
        public int MovieId { get; set; }

        public string MovieName { get; set; }

        [Required]
        public int CustomerId { get; set; }

        public string CustomerName { get; set; }

        /// <summary>Rental id that triggered the damage report.</summary>
        public int? RentalId { get; set; }

        [Required]
        [Display(Name = "Condition Before")]
        public DiscCondition ConditionBefore { get; set; }

        [Required]
        [Display(Name = "Condition After")]
        public DiscCondition ConditionAfter { get; set; }

        [Required]
        [Display(Name = "Damage Type")]
        public DamageType DamageType { get; set; }

        [StringLength(500)]
        [Display(Name = "Staff Notes")]
        public string Notes { get; set; }

        [Display(Name = "Reported On")]
        public DateTime ReportedOn { get; set; } = DateTime.Now;

        /// <summary>Assessed repair/replacement charge in dollars.</summary>
        [Display(Name = "Damage Charge")]
        [Range(0, 999.99)]
        public decimal DamageCharge { get; set; }

        /// <summary>Whether the charge has been collected from the customer.</summary>
        [Display(Name = "Charge Collected")]
        public bool ChargeCollected { get; set; }

        /// <summary>Whether the disc has been replaced with a new copy.</summary>
        [Display(Name = "Replaced")]
        public bool Replaced { get; set; }

        public void EnsureId()
        {
            if (Id == 0)
                Id = _nextId++;
        }

        public static void ResetIdCounter() => _nextId = 1;
    }

    /// <summary>
    /// Summary statistics for a movie's damage history.
    /// </summary>
    public class MovieDamageSummary
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public int TotalReports { get; set; }
        public decimal TotalCharges { get; set; }
        public decimal OutstandingCharges { get; set; }
        public DiscCondition CurrentCondition { get; set; }
        public DamageType MostCommonDamage { get; set; }
        public bool NeedsReplacement { get; set; }
        public string RiskLevel { get; set; }
    }

    /// <summary>
    /// Summary of a customer's damage track record.
    /// </summary>
    public class CustomerDamageProfile
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int TotalIncidents { get; set; }
        public decimal TotalCharges { get; set; }
        public decimal UnpaidCharges { get; set; }
        public double AverageConditionDrop { get; set; }
        public bool IsRepeatOffender { get; set; }
        public string RiskTier { get; set; }
        public DateTime? LastIncident { get; set; }
    }

    /// <summary>
    /// Overall damage analytics for the store.
    /// </summary>
    public class DamageAnalytics
    {
        public int TotalReports { get; set; }
        public decimal TotalCharges { get; set; }
        public decimal CollectedCharges { get; set; }
        public decimal CollectionRate { get; set; }
        public int MoviesNeedingReplacement { get; set; }
        public int RepeatOffenders { get; set; }
        public DamageType MostCommonDamageType { get; set; }
        public MovieDamageSummary MostDamagedMovie { get; set; }
        public CustomerDamageProfile WorstOffender { get; set; }
    }
}
