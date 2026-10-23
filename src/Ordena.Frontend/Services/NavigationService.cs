using Microsoft.Extensions.DependencyInjection;
using Ordena.Frontend.ViewModels;

namespace Ordena.Frontend.Services;

internal sealed class NavigationService(IServiceProvider services) : INavigationService
{
    public PageViewModel? CurrentPage { get; private set; }

    public event EventHandler? CurrentPageChanged;

    public async Task NavigateToAsync(Type pageType)
    {
        if (!typeof(PageViewModel).IsAssignableFrom(pageType))
            throw new ArgumentException($"{pageType.Name} is not a page.", nameof(pageType));

        // Singleton pages (Search, Import) come back with their state; the others are new each time.
        var page = (PageViewModel)services.GetRequiredService(pageType);
        if (ReferenceEquals(page, CurrentPage))
            return;

        CurrentPage?.OnNavigatedFrom();
        await page.OnNavigatedToAsync();

        CurrentPage = page;
        CurrentPageChanged?.Invoke(this, EventArgs.Empty);
    }
}
