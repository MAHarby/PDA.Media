using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Utils.Views;

public partial class LogViewerView : Window
{
    private LogViewerViewModel? _viewModel;

    public LogViewerView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel != null) _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel = DataContext as LogViewerViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ScrollToEnd();
    }

    // Show the newest lines after each (re)load.
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogViewerViewModel.LogText)) ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        Dispatcher.UIThread.Post(() => LogTextBox.CaretIndex = LogTextBox.Text?.Length ?? 0, DispatcherPriority.Background);
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
