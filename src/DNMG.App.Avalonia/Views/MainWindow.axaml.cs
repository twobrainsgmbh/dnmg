using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using MainWindowViewModel = DNMG.App.Avalonia.ViewModels.MainWindowViewModel;

namespace DNMG.App.Avalonia.Views;

public partial class MainWindow : Window
{
	private MainWindowViewModel? _viewModel;

	public MainWindow()
	{
		InitializeComponent();

		DataContextChanged += (_, _) =>
		{
			if (DataContext is not MainWindowViewModel vm) return;

			_viewModel = vm;
			_viewModel.SetStorageProvider(StorageProvider);
			_viewModel.FrameUpdated += () =>
			{
				Dispatcher.UIThread.Post(() =>
				{
					RenderImage.InvalidateVisual();
				});
			};
			_viewModel.RomFileLoaded += () => Focus();
		};
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		_viewModel?.OnKeyDown(e);
		base.OnKeyDown(e);
	}

	protected override void OnKeyUp(KeyEventArgs e)
	{
		_viewModel?.OnKeyUp(e);
		base.OnKeyUp(e);
	}
}
