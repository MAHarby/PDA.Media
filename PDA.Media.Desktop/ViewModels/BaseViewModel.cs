using CommunityToolkit.Mvvm.ComponentModel;
namespace PDA.Media.Desktop.ViewModels
{
    public partial class BaseViewModel : ObservableObject
    {
        [ObservableProperty] private string brandName = "pda media"; //"PDA.Media";
        [ObservableProperty] private string versionString = "0.0.1";
        [ObservableProperty] private string title = string.Empty;
    }
}