using System.Windows;
using Ordena.Frontend.ViewModels;

namespace Ordena.Frontend.Views.FirstRun;

public partial class FirstRunWindow : Window
{
    public FirstRunWindow(FirstRunViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Saved: close with a positive result, so startup goes on to the main window.
        viewModel.Completed += (_, _) => DialogResult = true;

        // Another language: close, and startup shows first run again in it.
        viewModel.LanguageChanged += (_, _) =>
        {
            SwitchingLanguage = true;
            DialogResult = false;
        };
    }

    public FirstRunViewModel ViewModel => (FirstRunViewModel)DataContext;

    // Closed to be shown again in the language just picked, not by the user.
    public bool SwitchingLanguage { get; private set; }
}
