using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
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

        // Tunnel so Ctrl+F works even when the log text box has focus (it would handle the key first).
        AddHandler(KeyDownEvent, Window_PreviewKeyDown, RoutingStrategies.Tunnel);
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

    private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
            e.Handled = true;
        }
    }

    // After each (re)load show the current search match, or the newest lines when not searching.
    // Posted so the TextBox has the new text and has been laid out first.
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LogViewerViewModel.LogText) or nameof(LogViewerViewModel.CurrentMatch))
        {
            Dispatcher.UIThread.Post(ShowCurrentMatchOrEnd, DispatcherPriority.Background);
        }
    }

    private void ScrollToEnd()
    {
        Dispatcher.UIThread.Post(() => LogTextBox.CaretIndex = LogTextBox.Text?.Length ?? 0, DispatcherPriority.Background);
    }

    private void ShowCurrentMatchOrEnd()
    {
        if (_viewModel?.CurrentMatch is not { } match)
        {
            LogTextBox.ClearSelection();
            LogTextBox.CaretIndex = LogTextBox.Text?.Length ?? 0;
            return;
        }

        // Moving the caret scrolls just far enough to show it, which leaves a match on the very edge of the view.
        // Visiting a few lines below and then above first keeps some context around it, whichever way we moved.
        // Each step is posted so a layout pass (which does the scrolling) runs in between.
        string text = LogTextBox.Text ?? string.Empty;
        LogTextBox.CaretIndex = OffsetLines(text, match.Start, MatchContextLines);
        Dispatcher.UIThread.Post(() =>
        {
            LogTextBox.CaretIndex = OffsetLines(text, match.Start, -MatchContextLines);
            Dispatcher.UIThread.Post(() =>
            {
                if (_viewModel?.CurrentMatch != match) return; // superseded by a newer search
                LogTextBox.CaretIndex = match.Start;
                LogTextBox.SelectionStart = match.Start;
                LogTextBox.SelectionEnd = match.Start + match.Length;
            }, DispatcherPriority.Background);
        }, DispatcherPriority.Background);
    }

    private const int MatchContextLines = 5;

    /// <summary>Index of the start of the line <paramref name="lines"/> lines away from <paramref name="index"/>.</summary>
    private static int OffsetLines(string text, int index, int lines)
    {
        int position = index;
        if (lines > 0)
        {
            for (int i = 0; i < lines; i++)
            {
                int next = text.IndexOf('\n', position);
                if (next < 0) return text.Length;
                position = next + 1;
            }
            return position;
        }

        position = text.LastIndexOf('\n', Math.Max(position - 1, 0)) + 1;
        for (int i = 0; i < -lines && position > 0; i++)
        {
            position = text.LastIndexOf('\n', Math.Max(position - 2, 0)) + 1;
        }
        return position;
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
