using System.Configuration;
using System.Data;
using System.Windows;

using PDA.Media.Desktop.Views;
using PDA.Media.Desktop.ViewModels;

namespace PDA.Media.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        MainWindow mainWindow = new MainWindow() {DataContext = new MainWindowViewModel()};
        mainWindow.Show();
    }
}