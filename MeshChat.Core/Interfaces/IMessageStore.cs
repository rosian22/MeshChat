namespace MeshChat.Core.Interfaces;

using MeshChat.Core.Models;

/// <summary>
/// Persistent storage for messages.
/// </summary>
public interface IMessageStore
{
    /// <summary>
    /// Saves or updates a message in the store.
    /// </summary>
    /// <param name="message">The message to save.</param>
    Task Save(Message message);

    /// <summary>
    /// Retrieves a message by its ID.
    /// </summary>
    /// <param name="id">The message ID.</param>
    /// <returns>The message if found; otherwise null.</returns>
    Task<Message?> GetById(Guid id);

    /// <summary>
    /// Retrieves messages from a conversation with a specific peer.
    /// </summary>
    /// <param name="peerId">The peer ID (hash of public key).</param>
    /// <param name="limit">Maximum number of messages to return.</param>
    /// <param name="offset">Number of most recent messages to skip.</param>
    /// <returns>Messages in reverse chronological order (newest first).</returns>
    Task<IReadOnlyList<Message>> GetConversation(string peerId, int limit = 50, int offset = 0);

    /// <summary>
    /// Retrieves the most recent messages across all conversations.
    /// </summary>
    /// <param name="limit">Maximum number of messages to return.</param>
    /// <returns>Messages in reverse chronological order (newest first).</returns>
    Task<IReadOnlyList<Message>> GetRecent(int limit = 20);

    /// <summary>
    /// Updates the delivery status of a message.
    /// </summary>
    /// <param name="messageId">The message ID.</param>
    /// <param name="status">The new status.</param>
    Task UpdateStatus(Guid messageId, MessageStatus status);
}
