using System.Collections.Concurrent;
using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;

namespace MeshChat.Tests.Mocks;

/// <summary>
/// In-memory message store for testing — avoids SQLite dependency in E2E tests.
/// </summary>
public class InMemoryMessageStore : IMessageStore
{
    private readonly ConcurrentDictionary<Guid, Message> _messages = new();

    public Task Save(Message message)
    {
        _messages[message.Id] = message;
        return Task.CompletedTask;
    }

    public Task<Message?> GetById(Guid id)
    {
        _messages.TryGetValue(id, out var message);
        return Task.FromResult(message);
    }

    public Task<IReadOnlyList<Message>> GetConversation(string peerId, int limit = 50, int offset = 0)
    {
        var result = _messages.Values
            .Where(m => m.SenderId == peerId)
            .OrderByDescending(m => m.SentAt)
            .Skip(offset)
            .Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<Message>>(result);
    }

    public Task<IReadOnlyList<Message>> GetRecent(int limit = 20)
    {
        var result = _messages.Values
            .OrderByDescending(m => m.SentAt)
            .Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<Message>>(result);
    }

    public Task UpdateStatus(Guid messageId, MessageStatus status)
    {
        if (_messages.TryGetValue(messageId, out var msg))
        {
            _messages[messageId] = msg with { Status = status };
        }
        return Task.CompletedTask;
    }

    /// <summary>All stored messages for test assertions.</summary>
    public IReadOnlyCollection<Message> All => _messages.Values.ToList();
}
