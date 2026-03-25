using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    public interface IPlaylistRepository
    {
        IReadOnlyList<Playlist> GetByCustomer(int customerId);
        IReadOnlyList<Playlist> GetPublicPlaylists(int limit = 20);
        Playlist GetById(int id);
        void Add(Playlist playlist);
        void Update(Playlist playlist);
        void Delete(int id);
        void AddEntry(int playlistId, PlaylistEntry entry);
        void RemoveEntry(int playlistId, int entryId);
        void MoveEntry(int playlistId, int entryId, int newPosition);
    }
}
