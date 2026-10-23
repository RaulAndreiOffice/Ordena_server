using CommunityToolkit.Mvvm.ComponentModel;

namespace Ordena.Frontend.ViewModels;

// A page shown in the main window. App.xaml maps each page type to its View with a DataTemplate.
public abstract class PageViewModel : ObservableObject
{
    // Shown in the window title ("Ordena — Buscar") and as the page's heading.
    public abstract string Title { get; }

    // Before the page is shown: load what it displays.
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;

    // When another page replaces it.
    public virtual void OnNavigatedFrom()
    {
    }
}
