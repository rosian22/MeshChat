namespace MeshChat.Core.Services;

using SQLite;
using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;

/// <summary>
/// SQLite-based persistent storage for messages.
/// </summary>
public class MessageStore : IMessageStore
{
    private readonly SQLiteAsyncConnection _database;

    /// <summary>
    /// Message database entity for SQLite mapping.
    /// </summary>
    [Table("Messages")]
    private class MessageEntity
    {
        [PrimaryKey]
        public Guid Id { get; set; }

        public string SenderId { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public DateTime SentAt { get; set; }

        public int Status { get; set; }
    }

    /// <summary>
    /// Initializes a new instance of the MessageStore class.
    /// </summary>
    /// <param name="databasePath">Path to the SQLite database file.</param>
    public MessageStore(string databasePath)
    {
        _database = new SQLiteAsyncConnection(databasePath);
        InitializeAsync().Wait();
    }

    private async Task InitializeAsync()
    {
        await _database.CreateTableAsync<MessageEntity>();
    }

    /// <inheritdoc />
    public async Task Save(Message message)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var entity = new MessageEntity
        {
            Id = message.Id,
            SenderId = message.SenderId,
            Content = message.Content,
            SentAt = message.SentAt,
            Status = (int)message.Status
        };

        var existing = await _database.FindAsync<MessageEntity>(message.Id);
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
    public async Task<Message?> GetById(Guid id)
    {
        var entity = await _database.FindAsync<MessageEntity>(id);
        return entity != null ? MapToMessage(entity) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Message>> GetConversation(string peerId, int limit = 50, int offset = 0)
    {
        if (string.IsNullOrEmpty(peerId))
            throw new ArgumentException("Peer ID cannot be null or empty.", nameof(peerId));

        var entities = await _database
            .Table<MessageEntity>()
            .Where(m => m.SenderId == peerId)
            .OrderByDescending(m => m.SentAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return entities.ConvertAll(MapToMessage);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Message>> GetRecent(int limit = 20)
    {
        var entities = await _database
            .Table<MessageEntity>()
            .OrderByDescending(m => m.SentAt)
            .Take(limit)
            .ToListAsync();

        return entities.ConvertAll(MapToMessage);
    }

    /// <inheritdoc />
    public async Task UpdateStatus(Guid messageId, MessageStatus status)
    {
        var entity = await _database.FindAsync<MessageEntity>(messageId);
        if (entity != null)
        {
            entity.Status = (int)status;
            await _database.UpdateAsync(entity);
        }
    }

    private static Message MapToMessage(MessageEntity entity)
    {
        return new Message
        {
            Id = entity.Id,
            SenderId = entity.SenderId,
            Content = entity.Content,
            SentAt = entity.SentAt,
            Status = (MessageStatus)entity.Status
        };
    }
}
