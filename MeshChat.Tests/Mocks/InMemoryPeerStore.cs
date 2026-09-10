using System.Collections.Concurrent;
using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;

namespace MeshChat.Tests.Mocks;

/// <summary>
/// In-memory peer store for testing — avoids SQLite dependency in E2E tests.
/// </summary>
public class InMemoryPeerStore : IPeerStore
{
    private readonly ConcurrentDictionary<string, Peer> _peers = new();

    public Task Save(Peer peer)
    {
        _peers[peer.Id] = peer;
        return Task.CompletedTask;
    }

    public Task<Peer?> GetById(string id)
    {
        _peers.TryGetValue(id, out var peer);
        return Task.FromResult(peer);
    }

    public Task<IReadOnlyList<Peer>> GetAll()
    {
        return Task.FromResult<IReadOnlyList<Peer>>(_peers.Values.ToList());
    }

    public Task UpdateLastSeen(string peerId, DateTime lastSeen, TransportType via)
    {
        if (_peers.TryGetValue(peerId, out var peer))
        {
            _peers[peerId] = peer with { LastSeen = lastSeen, LastSeenVia = via };
        }
        return Task.CompletedTask;
    }

    public Task Remove(string peerId)
    {
        _peers.TryRemove(peerId, out _);
        return Task.CompletedTask;
    }

    /// <summary>All stored peers for test assertions.</summary>
    public IReadOnlyCollection<Peer> All => _peers.Values.ToList();
}
