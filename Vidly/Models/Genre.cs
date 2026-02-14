using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Movie genre classification.
    /// </summary>
    public enum Genre
    {
        [Display(Name = "Action")]
        Action = 1,

        [Display(Name = "Comedy")]
        Comedy = 2,

        [Display(Name = "Drama")]
        Drama = 3,

        [Display(Name = "Horror")]
        Horror = 4,

        [Display(Name = "Sci-Fi")]
        SciFi = 5,

        [Display(Name = "Animation")]
        Animation = 6,

        [Display(Name = "Thriller")]
        Thriller = 7,

        [Display(Name = "Romance")]
        Romance = 8,

        [Display(Name = "Documentary")]
        Documentary = 9,

        [Display(Name = "Adventure")]
        Adventure = 10
    }
}
