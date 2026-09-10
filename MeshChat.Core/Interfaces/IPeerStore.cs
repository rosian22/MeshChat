namespace MeshChat.Core.Interfaces;

using MeshChat.Core.Models;

/// <summary>
/// Persistent storage for peer information.
/// </summary>
public interface IPeerStore
{
    /// <summary>
    /// Saves or updates a peer in the store.
    /// </summary>
    /// <param name="peer">The peer to save.</param>
    Task Save(Peer peer);

    /// <summary>
    /// Retrieves a peer by its ID (hash of public key).
    /// </summary>
    /// <param name="id">The peer ID.</param>
    /// <returns>The peer if found; otherwise null.</returns>
    Task<Peer?> GetById(string id);

    /// <summary>
    /// Retrieves all known peers.
    /// </summary>
    /// <returns>A read-only list of all peers.</returns>
    Task<IReadOnlyList<Peer>> GetAll();

    /// <summary>
    /// Updates the last seen time and transport type for a peer.
    /// </summary>
    /// <param name="peerId">The peer ID.</param>
    /// <param name="lastSeen">The timestamp of last contact.</param>
    /// <param name="via">The transport type used for contact.</param>
    Task UpdateLastSeen(string peerId, DateTime lastSeen, TransportType via);

    /// <summary>
    /// Removes a peer from the store.
    /// </summary>
    /// <param name="peerId">The peer ID.</param>
    Task Remove(string peerId);
}
