using System.Windows;
using Ordena.Frontend.ViewModels;

namespace Ordena.Frontend.Views.Shared;

public partial class EditDocumentWindow : Window
{
    public EditDocumentWindow(EditDocumentViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Saved: close with a positive result. Cancel (or Esc) closes without one.
        viewModel.Completed += (_, _) => DialogResult = true;
        Closed += (_, _) => viewModel.Release();
    }

    public EditDocumentViewModel ViewModel => (EditDocumentViewModel)DataContext;
}
