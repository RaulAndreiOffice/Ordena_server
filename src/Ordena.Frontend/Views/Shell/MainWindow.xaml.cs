using System.Windows;
using Ordena.Frontend.ViewModels;

namespace Ordena.Frontend.Views.Shell;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
