namespace MeshChat.Relay.Services;

using System.Collections.Concurrent;
using MeshChat.Core.Models;

/// <summary>
/// In-memory message queue service for storing packets destined to offline users.
/// Maintains a queue of up to 100 messages per user; oldest messages are dropped if exceeded.
/// TODO: Replace with persistent storage (Redis, database) for production deployments.
/// </summary>
public class MessageQueueService
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<MeshPacket>> _queues;
    private const int MaxQueueSizePerUser = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageQueueService"/> class.
    /// </summary>
    public MessageQueueService()
    {
        _queues = new ConcurrentDictionary<string, ConcurrentQueue<MeshPacket>>();
    }

    /// <summary>
    /// Enqueues a message for a user. If the queue exceeds the maximum size, the oldest message is dropped.
    /// </summary>
    /// <param name="packet">The mesh packet to queue.</param>
    public void Enqueue(MeshPacket packet)
    {
        if (packet == null)
        {
            throw new ArgumentNullException(nameof(packet));
        }

        var queue = _queues.GetOrAdd(packet.DestinationHash, _ => new ConcurrentQueue<MeshPacket>());

        queue.Enqueue(packet);

        // Enforce maximum queue size
        while (queue.Count > MaxQueueSizePerUser)
        {
            queue.TryDequeue(out _); // Drop oldest message
        }
    }

    /// <summary>
    /// Dequeues all messages for a user and clears their queue.
    /// </summary>
    /// <param name="userId">The user's unique identifier.</param>
    /// <returns>A list of queued messages for the user, or an empty list if no messages are queued.</returns>
    public List<MeshPacket> Dequeue(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));
        }

        var messages = new List<MeshPacket>();

        if (_queues.TryRemove(userId, out var queue))
        {
            while (queue.TryDequeue(out var packet))
            {
                messages.Add(packet);
            }
        }

        return messages;
    }

    /// <summary>
    /// Gets the current queue size for a user without removing messages.
    /// </summary>
    /// <param name="userId">The user's unique identifier.</param>
    /// <returns>The number of queued messages for the user.</returns>
    public int GetQueueSize(string userId)
    {
        if (_queues.TryGetValue(userId, out var queue))
        {
            return queue.Count;
        }

        return 0;
    }

    /// <summary>
    /// Clears the queue for a user.
    /// </summary>
    /// <param name="userId">The user's unique identifier.</param>
    public void ClearQueue(string userId)
    {
        _queues.TryRemove(userId, out _);
    }

    /// <summary>
    /// Gets the total number of queued messages across all users.
    /// </summary>
    /// <returns>The total queue size.</returns>
    public int GetTotalQueueSize()
    {
        return _queues.Values.Sum(q => q.Count);
    }
}
