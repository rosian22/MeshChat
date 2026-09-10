using MeshChat.MAUI.ViewModels;

namespace MeshChat.MAUI.Views;

/// <summary>
/// Chat page for displaying and sending messages with a peer.
/// </summary>
public partial class ChatPage : ContentPage
{
    private readonly ChatViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatPage"/> class.
    /// </summary>
    public ChatPage(ChatViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = _viewModel;
    }

    /// <summary>
    /// Called when the page is navigated to.
    /// </summary>
    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);

        await _viewModel.LoadMessagesAsync();

        // Scroll to the latest message
        if (_viewModel.Messages.Count > 0)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MessagesCollectionView.ScrollTo(_viewModel.Messages.Count - 1, animate: false);
            });
        }
    }

    /// <summary>
    /// Called when the page is navigated away from.
    /// </summary>
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        _viewModel.Cleanup();
    }
}
