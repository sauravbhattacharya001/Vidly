using System;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Award categories commonly tracked for movies.
    /// </summary>
    public enum AwardCategory
    {
        [Display(Name = "Best Picture")]
        BestPicture,

        [Display(Name = "Best Director")]
        BestDirector,

        [Display(Name = "Best Actor")]
        BestActor,

        [Display(Name = "Best Actress")]
        BestActress,

        [Display(Name = "Best Supporting Actor")]
        BestSupportingActor,

        [Display(Name = "Best Supporting Actress")]
        BestSupportingActress,

        [Display(Name = "Best Screenplay")]
        BestScreenplay,

        [Display(Name = "Best Cinematography")]
        BestCinematography,

        [Display(Name = "Best Animation")]
        BestAnimation,

        [Display(Name = "Best Score")]
        BestScore,

        [Display(Name = "Best Editing")]
        BestEditing,

        [Display(Name = "Best Visual Effects")]
        BestVisualEffects,

        [Display(Name = "Best Documentary")]
        BestDocumentary,

        [Display(Name = "Best Foreign Film")]
        BestForeignFilm,

        [Display(Name = "Audience Choice")]
        AudienceChoice
    }

    /// <summary>
    /// Award-granting organizations.
    /// </summary>
    public enum AwardBody
    {
        [Display(Name = "Academy Awards (Oscars)")]
        Oscar,

        [Display(Name = "Golden Globes")]
        GoldenGlobe,

        [Display(Name = "BAFTA")]
        BAFTA,

        [Display(Name = "Screen Actors Guild (SAG)")]
        SAG,

        [Display(Name = "Cannes Film Festival")]
        Cannes,

        [Display(Name = "Critics' Choice")]
        CriticsChoice,

        [Display(Name = "Sundance")]
        Sundance,

        [Display(Name = "Venice Film Festival")]
        Venice,

        [Display(Name = "Other")]
        Other
    }

    /// <summary>
    /// Represents a single award nomination (which may or may not have been won).
    /// </summary>
    public class AwardNomination
    {
        public int Id { get; set; }

        [Required]
        public int MovieId { get; set; }

        public string MovieName { get; set; }

        [Required]
        public AwardBody AwardBody { get; set; }

        [Required]
        public AwardCategory Category { get; set; }

        [Required]
        [Range(1927, 2100, ErrorMessage = "Year must be between 1927 and 2100.")]
        public int Year { get; set; }

        [StringLength(200)]
        public string Nominee { get; set; }

        public bool Won { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Summary statistics for a movie's award history.
    /// </summary>
    public class AwardSummary
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public int TotalNominations { get; set; }
        public int TotalWins { get; set; }
        public double WinRate => TotalNominations > 0
            ? Math.Round((double)TotalWins / TotalNominations * 100, 1)
            : 0;
    }
}
