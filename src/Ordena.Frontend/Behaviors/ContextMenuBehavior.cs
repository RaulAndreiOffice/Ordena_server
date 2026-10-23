using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Ordena.Frontend.Behaviors;

// A button that opens its own ContextMenu on a normal click, under the button: the kit's "⋯" row
// actions. The menu is the same one a right-click on the row opens.
public static class ContextMenuBehavior
{
    public static readonly DependencyProperty OpenOnClickProperty = DependencyProperty.RegisterAttached(
        "OpenOnClick", typeof(bool), typeof(ContextMenuBehavior), new PropertyMetadata(false, OnOpenOnClickChanged));

    public static bool GetOpenOnClick(DependencyObject element) => (bool)element.GetValue(OpenOnClickProperty);
    public static void SetOpenOnClick(DependencyObject element, bool value) => element.SetValue(OpenOnClickProperty, value);

    private static void OnOpenOnClickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button)
            return;

        button.Click -= OnClick;
        if (e.NewValue is true)
            button.Click += OnClick;
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase { ContextMenu: { } menu } button)
            return;

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
