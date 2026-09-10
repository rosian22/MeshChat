namespace MeshChat.Core.Models;

/// <summary>
/// Represents the delivery status of a message.
/// </summary>
public enum MessageStatus
{
    /// <summary>Message was sent by the local user.</summary>
    Sent,

    /// <summary>Message was relayed by another peer.</summary>
    Relayed,

    /// <summary>Message was delivered to the recipient.</summary>
    Delivered,

    /// <summary>Message was read by the recipient.</summary>
    Read
}

/// <summary>
/// Represents a chat message in the application.
/// </summary>
public record Message
{
    /// <summary>
    /// Unique identifier for the message.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Hash of the sender's public key.
    /// </summary>
    public string SenderId { get; init; } = string.Empty;

    /// <summary>
    /// The message content text.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Timestamp when the message was sent.
    /// </summary>
    public DateTime SentAt { get; init; }

    /// <summary>
    /// Current delivery status of the message.
    /// </summary>
    public MessageStatus Status { get; init; }
}
