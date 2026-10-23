using Microsoft.Extensions.DependencyInjection;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;

namespace Ordena.Tests.Frontend;

public sealed class NavigationServiceTests
{
    private readonly List<string> _log = [];
    private readonly NavigationService _navigation;
    private int _changes;

    public NavigationServiceTests()
    {
        var services = new ServiceCollection()
            .AddSingleton(_log)
            .AddSingleton<PageA>()
            .AddTransient<PageB>()
            .BuildServiceProvider();
        _navigation = new NavigationService(services);
        _navigation.CurrentPageChanged += (_, _) => _changes++;
    }

    [Fact]
    public async Task The_old_page_is_left_before_the_new_one_is_shown()
    {
        await _navigation.NavigateToAsync<PageA>();
        await _navigation.NavigateToAsync<PageB>();

        Assert.Equal(["to A", "from A", "to B"], _log);
        Assert.IsType<PageB>(_navigation.CurrentPage);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public async Task Navigating_to_the_page_already_shown_does_nothing()
    {
        await _navigation.NavigateToAsync<PageA>();
        await _navigation.NavigateToAsync<PageA>();

        Assert.Equal(["to A"], _log);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public async Task A_type_that_is_not_a_page_throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _navigation.NavigateToAsync(typeof(string)));
    }

    public sealed class PageA(List<string> log) : PageViewModel
    {
        public override string Title => "A";

        public override Task OnNavigatedToAsync()
        {
            log.Add("to A");
            return Task.CompletedTask;
        }

        public override void OnNavigatedFrom() => log.Add("from A");
    }

    public sealed class PageB(List<string> log) : PageViewModel
    {
        public override string Title => "B";

        public override Task OnNavigatedToAsync()
        {
            log.Add("to B");
            return Task.CompletedTask;
        }
    }
}
