using Avalonia.Controls;
using Avalonia.Styling;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Utils.Views;

public partial class ColoursView : Window
{
    public ColoursView()
    {
        InitializeComponent();
        DataContext = new ColoursViewModel();
    }

    private void OnThemeVariantSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is string selectedTheme)
        {
            RequestedThemeVariant = selectedTheme switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }
    }
}
