using CommunityToolkit.Mvvm.ComponentModel;

namespace Ordena.Frontend.ViewModels;

// A page shown in the main window. App.xaml maps each page type to its View with a DataTemplate.
public abstract class PageViewModel : ObservableObject
{
    // Shown in the window title ("Ordena — Buscar") and as the page's heading.
    public abstract string Title { get; }

    // True for the pages that can't work without the archive folder (importing): while it is missing,
    // the shell shows a panel instead of them.
    public virtual bool NeedsArchive => false;

    // Before the page is shown: load what it displays.
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;

    // When another page replaces it.
    public virtual void OnNavigatedFrom()
    {
    }
}
