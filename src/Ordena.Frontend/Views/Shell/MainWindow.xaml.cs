using System.ComponentModel;
using System.Windows;
using Ordena.Frontend.ViewModels;
using Ordena.Frontend.Views.Shared;

namespace Ordena.Frontend.Views.Shell;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    // Not while the archive is being moved or checked (closed from the taskbar, say): that window's
    // Cancelar stops the job cleanly first.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (ProgressWindow.IsOpen)
            e.Cancel = true;
        base.OnClosing(e);
    }
}
