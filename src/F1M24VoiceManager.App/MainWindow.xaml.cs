using System.Windows;
using F1M24VoiceManager.App.ViewModels;
using Microsoft.Win32;

namespace F1M24VoiceManager.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void ChooseGameFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Dossier d'installation de F1 Manager 2024" };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.SetGameDirectory(dialog.FolderName);
        }
    }
}
