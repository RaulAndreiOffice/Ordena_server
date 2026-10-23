using CommunityToolkit.Mvvm.ComponentModel;

namespace Ordena.Frontend.ViewModels;

// One entry of the sidebar. IconKey names a geometry in Resources/Theme/Icons.xaml.
public sealed partial class NavItem(string title, string iconKey, Type pageType) : ObservableObject
{
    public string Title { get; } = title;
    public string IconKey { get; } = iconKey;
    public Type PageType { get; } = pageType;

    // True while its page is the one shown.
    [ObservableProperty]
    private bool _isSelected;
}

// A titled group of sidebar entries ("Documentos", "Sistema").
public sealed record NavSection(string Title, IReadOnlyList<NavItem> Items);
