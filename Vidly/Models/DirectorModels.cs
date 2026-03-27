using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a movie director with biography and filmography info.
    /// </summary>
    public class Director
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Bio { get; set; }
        public string PhotoUrl { get; set; }
        public DateTime? BirthDate { get; set; }
        public string Nationality { get; set; }
        public string KnownFor { get; set; }
    }

    /// <summary>
    /// Links a director to a movie in the store's catalog.
    /// </summary>
    public class DirectorMovie
    {
        public int DirectorId { get; set; }
        public int MovieId { get; set; }
        public string Role { get; set; } // "Director", "Co-Director", "Producer"
    }
}
