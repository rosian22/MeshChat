using MeshChat.MAUI.ViewModels;

namespace MeshChat.MAUI.Views;

/// <summary>
/// Peers page for displaying and selecting connected peers.
/// </summary>
public partial class PeersPage : ContentPage
{
	private readonly PeersViewModel _viewModel;

	/// <summary>
	/// Initializes a new instance of the <see cref="PeersPage"/> class.
	/// </summary>
	public PeersPage(PeersViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
		BindingContext = _viewModel;
	}

	/// <summary>
	/// Called when the page is navigated to, loads the list of peers.
	/// </summary>
	protected override async void OnNavigatedTo(NavigatedToEventArgs args)
	{
		base.OnNavigatedTo(args);
		await _viewModel.LoadPeersAsync();
	}
}
