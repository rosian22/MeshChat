namespace MeshChat.Relay.Hubs;

using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using MeshChat.Core.Models;
using MeshChat.Relay.Services;

/// <summary>
/// SignalR hub for routing mesh packets between connected clients.
/// Routes packets to online recipients or queues them for offline delivery.
/// </summary>
public class MeshHub : Hub
{
    private readonly MessageQueueService _messageQueueService;
    private static readonly ConcurrentDictionary<string, string> UserConnections = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MeshHub"/> class.
    /// </summary>
    /// <param name="messageQueueService">The message queue service for offline message storage.</param>
    public MeshHub(MessageQueueService messageQueueService)
    {
        _messageQueueService = messageQueueService;
    }

    /// <summary>
    /// Sends a mesh packet to a recipient, either online or queued for later delivery.
    /// </summary>
    /// <param name="packet">The mesh packet to send.</param>
    public async Task SendPacket(MeshPacket packet)
    {
        if (packet == null)
        {
            throw new ArgumentNullException(nameof(packet));
        }

        // Check if recipient is online
        if (UserConnections.TryGetValue(packet.DestinationHash, out var connectionId))
        {
            // Send directly to the connected client
            await Clients.Client(connectionId).SendAsync("ReceivePacket", packet);
        }
        else
        {
            // Queue for later delivery
            _messageQueueService.Enqueue(packet);
        }
    }

    /// <summary>
    /// Called when a client connects to the hub.
    /// Registers the user and delivers any queued messages.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        // Extract userId from query parameters
        var userId = Context.GetHttpContext()?.Request.Query["userId"].ToString() ?? Context.ConnectionId;

        // Register this user
        UserConnections[userId] = Context.ConnectionId;

        System.Diagnostics.Debug.WriteLine($"User connected: {userId} (ConnectionId: {Context.ConnectionId})");

        // Deliver any queued messages
        var queuedMessages = _messageQueueService.Dequeue(userId);
        foreach (var message in queuedMessages)
        {
            await Clients.Client(Context.ConnectionId).SendAsync("ReceivePacket", message);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects from the hub.
    /// Unregisters the user from the online connections.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Extract userId from query parameters
        var userId = Context.GetHttpContext()?.Request.Query["userId"].ToString() ?? Context.ConnectionId;

        // Unregister this user
        UserConnections.TryRemove(userId, out _);

        System.Diagnostics.Debug.WriteLine($"User disconnected: {userId}");

        if (exception != null)
        {
            System.Diagnostics.Debug.WriteLine($"Disconnection exception: {exception.Message}");
        }

        await base.OnDisconnectedAsync(exception);
    }
}
