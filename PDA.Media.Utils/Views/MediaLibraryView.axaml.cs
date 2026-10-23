using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Utils.Views;

public partial class MediaLibraryView : Window
{
    private MediaLibraryViewModel? _viewModel;
    private bool _canClose;

    public MediaLibraryView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel != null) _viewModel.CloseRequested -= ViewModel_CloseRequested;
        _viewModel = DataContext as MediaLibraryViewModel;
        if (_viewModel != null) _viewModel.CloseRequested += ViewModel_CloseRequested;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _viewModel?.Search.LoadCategoriesCommand.Execute(null);
        _viewModel?.Scanner.LoadCategoriesCommand.Execute(null);
        _viewModel?.Analysis?.LoadCommand.Execute(null);
    }

    // A running scan or unsaved changes: the view model asks first (on its tab) and raises CloseRequested once answered.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_canClose && _viewModel != null && !_viewModel.CanClose()) e.Cancel = true;
        base.OnClosing(e);
    }

    private void ViewModel_CloseRequested()
    {
        _canClose = true;
        Close();
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
