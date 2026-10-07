using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PDA.Media.Utils.ViewModels;

public partial class FluentColorItem : ObservableObject
{
    public string Name { get; }
    public string Category { get; }
    public Color LightColor { get; }
    public Color DarkColor { get; }
    public string LightHex { get; }
    public string DarkHex { get; }
    public IBrush LightBrush { get; }
    public IBrush DarkBrush { get; }

    public FluentColorItem(string name, string category, Color lightColor, Color darkColor)
    {
        Name = name;
        Category = category;
        LightColor = lightColor;
        DarkColor = darkColor;
        LightHex = $"#{lightColor.A:X2}{lightColor.R:X2}{lightColor.G:X2}{lightColor.B:X2}";
        DarkHex = $"#{darkColor.A:X2}{darkColor.R:X2}{darkColor.G:X2}{darkColor.B:X2}";
        LightBrush = new SolidColorBrush(lightColor);
        DarkBrush = new SolidColorBrush(darkColor);
    }
}

public partial class ColoursViewModel : ViewModelBase
{
    private readonly List<FluentColorItem> _allColors = new();

    public ObservableCollection<FluentColorItem> DisplayedColors { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private string _selectedThemeVariantName = "Default";

    public ObservableCollection<string> ThemeVariantNames { get; } = new()
    {
        "Default",
        "Light",
        "Dark"
    };

    public ColoursViewModel()
    {
        LoadColors();
        PopulateCategories();
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        ApplyFilter();
    }

    private void LoadColors()
    {
        var theme = new FluentTheme();

        var definitions = new List<(string Name, string Category)>
        {
            // Base Colors
            ("SystemBaseHighColor", "Base"),
            ("SystemBaseMediumHighColor", "Base"),
            ("SystemBaseMediumColor", "Base"),
            ("SystemBaseMediumLowColor", "Base"),
            ("SystemBaseLowColor", "Base"),

            // Alt Colors
            ("SystemAltHighColor", "Alt"),
            ("SystemAltMediumHighColor", "Alt"),
            ("SystemAltMediumColor", "Alt"),
            ("SystemAltMediumLowColor", "Alt"),
            ("SystemAltLowColor", "Alt"),

            // Chrome Colors
            ("SystemChromeHighColor", "Chrome"),
            ("SystemChromeMediumColor", "Chrome"),
            ("SystemChromeMediumLowColor", "Chrome"),
            ("SystemChromeLowColor", "Chrome"),
            ("SystemChromeAltLowColor", "Chrome"),
            ("SystemChromeBlackHighColor", "Chrome"),
            ("SystemChromeBlackMediumColor", "Chrome"),
            ("SystemChromeBlackMediumLowColor", "Chrome"),
            ("SystemChromeBlackLowColor", "Chrome"),
            ("SystemChromeWhiteColor", "Chrome"),
            ("SystemChromeGrayColor", "Chrome"),
            ("SystemChromeDisabledHighColor", "Chrome"),
            ("SystemChromeDisabledLowColor", "Chrome"),

            // List / Misc Colors
            ("SystemListLowColor", "List & Region"),
            ("SystemListMediumColor", "List & Region"),
            ("SystemRevealListLowColor", "List & Region"),
            ("SystemRevealListMediumColor", "List & Region"),
            ("SystemRegionColor", "List & Region"),
            ("SystemErrorTextColor", "List & Region"),

            // Accent Colors
            ("SystemAccentColor", "Accent"),
            ("SystemAccentColorLight1", "Accent"),
            ("SystemAccentColorLight2", "Accent"),
            ("SystemAccentColorLight3", "Accent"),
            ("SystemAccentColorDark1", "Accent"),
            ("SystemAccentColorDark2", "Accent"),
            ("SystemAccentColorDark3", "Accent")
        };

        _allColors.Clear();
        foreach (var def in definitions)
        {
            Color lightColor = Colors.Transparent;
            Color darkColor = Colors.Transparent;

            if (theme.TryGetResource(def.Name, ThemeVariant.Light, out var valL) && valL is Color cL)
            {
                lightColor = cL;
            }

            if (theme.TryGetResource(def.Name, ThemeVariant.Dark, out var valD) && valD is Color cD)
            {
                darkColor = cD;
            }

            _allColors.Add(new FluentColorItem(def.Name, def.Category, lightColor, darkColor));
        }
    }

    private void PopulateCategories()
    {
        Categories.Clear();
        Categories.Add("All");
        foreach (var cat in _allColors.Select(c => c.Category).Distinct())
        {
            Categories.Add(cat);
        }
    }

    private void ApplyFilter()
    {
        DisplayedColors.Clear();
        var query = _allColors.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SelectedCategory) && SelectedCategory != "All")
        {
            query = query.Where(c => c.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string search = SearchText.Trim();
            query = query.Where(c => c.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                     c.LightHex.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                     c.DarkHex.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                     c.Category.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in query)
        {
            DisplayedColors.Add(item);
        }
    }
}
