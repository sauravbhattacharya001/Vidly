using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    public class PlaylistIndexViewModel
    {
        public IReadOnlyList<Customer> Customers { get; set; }
        public int? SelectedCustomerId { get; set; }
        public string SelectedCustomerName { get; set; }
        public IReadOnlyList<Playlist> CustomerPlaylists { get; set; }
        public IReadOnlyList<Playlist> PublicPlaylists { get; set; }
        public string StatusMessage { get; set; }
        public bool IsError { get; set; }
    }

    public class PlaylistDetailViewModel
    {
        public Playlist Playlist { get; set; }
        public bool IsOwner { get; set; }
        public int? ViewerCustomerId { get; set; }
    }

    public class PlaylistCreateViewModel
    {
        public IReadOnlyList<Customer> Customers { get; set; }
        public Playlist Playlist { get; set; }
    }

    public class PlaylistAddMovieViewModel
    {
        public int PlaylistId { get; set; }
        public string PlaylistName { get; set; }
        public IReadOnlyList<Movie> AvailableMovies { get; set; }
        public int? SelectedMovieId { get; set; }
        public string Note { get; set; }
    }
}
