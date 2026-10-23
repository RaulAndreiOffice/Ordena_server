using Ordena.Frontend.ViewModels;

namespace Ordena.Frontend.Services;

// ViewModel-first navigation between the pages shown in the main window.
public interface INavigationService
{
    PageViewModel? CurrentPage { get; }

    event EventHandler? CurrentPageChanged;

    // Resolves the page from DI, calls OnNavigatedFrom on the current page and OnNavigatedToAsync
    // on the new one, then makes it current. Navigating to the page already shown does nothing.
    Task NavigateToAsync(Type pageType);
}

public static class NavigationServiceExtensions
{
    public static Task NavigateToAsync<TPage>(this INavigationService navigation) where TPage : PageViewModel =>
        navigation.NavigateToAsync(typeof(TPage));
}
