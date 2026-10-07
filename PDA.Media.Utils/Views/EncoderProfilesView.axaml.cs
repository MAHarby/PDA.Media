using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Utils.Views;

public partial class EncoderProfilesView : Window
{
    public EncoderProfilesView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is EncoderProfilesViewModel vm)
        {
            vm.RequestClose += (s, args) => Close();
        }
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
