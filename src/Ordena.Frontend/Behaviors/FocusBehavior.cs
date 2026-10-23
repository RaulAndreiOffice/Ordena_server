using System.Windows;
using System.Windows.Threading;

namespace Ordena.Frontend.Behaviors;

// Moves the keyboard focus to an element when its bound Request number changes, so a ViewModel can
// say "back to the first field" (after each save) without touching the View.
public static class FocusBehavior
{
    public static readonly DependencyProperty RequestProperty = DependencyProperty.RegisterAttached(
        "Request", typeof(int), typeof(FocusBehavior), new PropertyMetadata(0, OnRequestChanged));

    public static int GetRequest(DependencyObject element) => (int)element.GetValue(RequestProperty);
    public static void SetRequest(DependencyObject element, int value) => element.SetValue(RequestProperty, value);

    private static void OnRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // After the current layout pass: the element may only just have become visible or enabled.
        if (d is UIElement element)
            element.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => element.Focus());
    }
}
