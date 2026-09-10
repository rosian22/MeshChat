using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;

namespace MeshChat.MAUI.ViewModels;

/// <summary>
/// ViewModel for peers page managing the list of known peers.
/// </summary>
public partial class PeersViewModel : ObservableObject
{
    private readonly IPeerStore _peerStore;

    [ObservableProperty]
    private ObservableCollection<Peer> peers = new();

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isRefreshing;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeersViewModel"/> class.
    /// </summary>
    public PeersViewModel(IPeerStore peerStore)
    {
        _peerStore = peerStore ?? throw new ArgumentNullException(nameof(peerStore));
    }

    /// <summary>
    /// Loads all peers from the peer store.
    /// </summary>
    public async Task LoadPeersAsync()
    {
        try
        {
            IsBusy = true;
            var allPeers = await _peerStore.GetAll();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Peers.Clear();
                foreach (var peer in allPeers.OrderByDescending(p => p.LastSeen))
                {
                    Peers.Add(peer);
                }
            });
        }
        catch (Exception ex)
        {
            await Application.Current!.MainPage!.DisplayAlert(
                "Error", $"Failed to load peers: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Refreshes the list of peers.
    /// </summary>
    [RelayCommand]
    private async Task RefreshPeers()
    {
        try
        {
            IsRefreshing = true;
            await LoadPeersAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Navigates to the chat page for the selected peer.
    /// </summary>
    [RelayCommand]
    private async Task SelectPeer(Peer peer)
    {
        if (peer == null)
            return;

        try
        {
            await Shell.Current.GoToAsync($"ChatPage?peerId={peer.Id}");
        }
        catch (Exception ex)
        {
            await Application.Current!.MainPage!.DisplayAlert(
                "Error", $"Navigation failed: {ex.Message}", "OK");
        }
    }
}
