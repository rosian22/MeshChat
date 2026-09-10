namespace MeshChat.Core.Services;

using SQLite;
using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;

/// <summary>
/// SQLite-based persistent storage for peer information.
/// </summary>
public class PeerStore : IPeerStore
{
    private readonly SQLiteAsyncConnection _database;

    /// <summary>
    /// Peer database entity for SQLite mapping.
    /// </summary>
    [Table("Peers")]
    private class PeerEntity
    {
        [PrimaryKey]
        public string Id { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public byte[] PublicKey { get; set; } = Array.Empty<byte>();

        public DateTime LastSeen { get; set; }

        public int LastSeenVia { get; set; }
    }

    /// <summary>
    /// Initializes a new instance of the PeerStore class.
    /// </summary>
    /// <param name="databasePath">Path to the SQLite database file.</param>
    public PeerStore(string databasePath)
    {
        _database = new SQLiteAsyncConnection(databasePath);
        InitializeAsync().Wait();
    }

    private async Task InitializeAsync()
    {
        await _database.CreateTableAsync<PeerEntity>();
    }

    /// <inheritdoc />
    public async Task Save(Peer peer)
    {
        if (peer == null)
            throw new ArgumentNullException(nameof(peer));

        var entity = new PeerEntity
        {
            Id = peer.Id,
            DisplayName = peer.DisplayName,
            PublicKey = peer.PublicKey,
            LastSeen = peer.LastSeen,
            LastSeenVia = (int)peer.LastSeenVia
        };

        var existing = await _database.FindAsync<PeerEntity>(peer.Id);
        if (existing != null)
        {
            await _database.UpdateAsync(entity);
        }
        else
        {
            await _database.InsertAsync(entity);
        }
    }

    /// <inheritdoc />
    public async Task<Peer?> GetById(string id)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("Peer ID cannot be null or empty.", nameof(id));

        var entity = await _database.FindAsync<PeerEntity>(id);
        return entity != null ? MapToPeer(entity) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Peer>> GetAll()
    {
        var entities = await _database.Table<PeerEntity>().ToListAsync();
        return entities.ConvertAll(MapToPeer);
    }

    /// <inheritdoc />
    public async Task UpdateLastSeen(string peerId, DateTime lastSeen, TransportType via)
    {
        if (string.IsNullOrEmpty(peerId))
            throw new ArgumentException("Peer ID cannot be null or empty.", nameof(peerId));

        var entity = await _database.FindAsync<PeerEntity>(peerId);
        if (entity != null)
        {
            entity.LastSeen = lastSeen;
            entity.LastSeenVia = (int)via;
            await _database.UpdateAsync(entity);
        }
    }

    /// <inheritdoc />
    public async Task Remove(string peerId)
    {
        if (string.IsNullOrEmpty(peerId))
            throw new ArgumentException("Peer ID cannot be null or empty.", nameof(peerId));

        await _database.DeleteAsync<PeerEntity>(peerId);
    }

    private static Peer MapToPeer(PeerEntity entity)
    {
        return new Peer
        {
            Id = entity.Id,
            DisplayName = entity.DisplayName,
            PublicKey = entity.PublicKey,
            LastSeen = entity.LastSeen,
            LastSeenVia = (TransportType)entity.LastSeenVia
        };
    }
}
