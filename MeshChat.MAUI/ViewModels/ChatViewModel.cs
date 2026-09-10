using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;
using MeshChat.Core.Services;

namespace MeshChat.MAUI.ViewModels;

/// <summary>
/// ViewModel for chat page managing messages and peer communication.
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private readonly MeshRouter _meshRouter;
    private readonly TransportOrchestrator _transportOrchestrator;
    private readonly IMessageStore _messageStore;

    [ObservableProperty]
    private ObservableCollection<Message> messages = new();

    [ObservableProperty]
    private string messageText = string.Empty;

    [ObservableProperty]
    private Peer? selectedPeer;

    [ObservableProperty]
    private bool isBusy;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatViewModel"/> class.
    /// </summary>
    public ChatViewModel(
        MeshRouter meshRouter,
        TransportOrchestrator transportOrchestrator,
        IMessageStore messageStore)
    {
        _meshRouter = meshRouter ?? throw new ArgumentNullException(nameof(meshRouter));
        _transportOrchestrator = transportOrchestrator ?? throw new ArgumentNullException(nameof(transportOrchestrator));
        _messageStore = messageStore ?? throw new ArgumentNullException(nameof(messageStore));

        // Subscribe to incoming messages
        _meshRouter.MessageReceived += OnMessageReceived;
    }

    /// <summary>
    /// Loads messages for the selected peer from the message store.
    /// </summary>
    public async Task LoadMessagesAsync()
    {
        if (SelectedPeer == null)
            return;

        try
        {
            IsBusy = true;
            var loadedMessages = await _messageStore.GetConversation(SelectedPeer.Id);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Messages.Clear();
                // GetConversation returns newest-first; reverse for chronological display
                foreach (var msg in loadedMessages.Reverse())
                {
                    Messages.Add(msg);
                }
            });
        }
        catch (Exception ex)
        {
            await Application.Current!.MainPage!.DisplayAlert(
                "Error", $"Failed to load messages: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Sends the current message to the selected peer.
    /// </summary>
    [RelayCommand]
    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(MessageText) || SelectedPeer == null)
            return;

        try
        {
            IsBusy = true;

            var messageId = Guid.NewGuid();

            // Create mesh packet via router
            var packet = _meshRouter.CreatePacket(
                messageId,
                SelectedPeer.Id,
                MessageText,
                SelectedPeer.PublicKey);

            // Send via transport orchestrator
            await _transportOrchestrator.SendPacket(packet);

            // Store the outgoing message locally
            var message = new Message
            {
                Id = messageId,
                SenderId = packet.SenderHash,
                Content = MessageText,
                SentAt = DateTime.UtcNow,
                Status = MessageStatus.Sent
            };

            await _messageStore.Save(message);

            // Add to UI
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Messages.Add(message);
                MessageText = string.Empty;
            });
        }
        catch (Exception ex)
        {
            await Application.Current!.MainPage!.DisplayAlert(
                "Error", $"Failed to send message: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Handles messages received from the mesh network.
    /// </summary>
    private void OnMessageReceived(Message message)
    {
        if (SelectedPeer == null || message.SenderId != SelectedPeer.Id)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Messages.Add(message);
        });
    }

    /// <summary>
    /// Cleans up event subscriptions.
    /// </summary>
    public void Cleanup()
    {
        _meshRouter.MessageReceived -= OnMessageReceived;
    }
}
