using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Ordena.Frontend.Controls;

// A TextBox with a list of suggestions under it, for the partner name. The list opens when new
// suggestions arrive while the user is typing. Down/Up move through it, Enter or a click takes the
// highlighted name, Esc closes it. Enter on a suggestion is consumed, so it doesn't also press the
// window's default button ("Guardar y siguiente").
public sealed class AutoCompleteTextBox : TextBox
{
    public static readonly DependencyProperty SuggestionsProperty = DependencyProperty.Register(
        nameof(Suggestions), typeof(IEnumerable), typeof(AutoCompleteTextBox),
        new PropertyMetadata(null, (d, _) => ((AutoCompleteTextBox)d).OnSuggestionsChanged()));

    private readonly ListBox _list;
    private readonly Popup _popup;

    public AutoCompleteTextBox()
    {
        // The Fluent theme's TextBox look: implicit styles don't reach a subclass on their own.
        SetResourceReference(StyleProperty, typeof(TextBox));

        // Not focusable: the keyboard stays in the text box, which drives the list.
        _list = new ListBox { Focusable = false, MaxHeight = 240, BorderThickness = new Thickness(0) };
        _list.PreviewMouseLeftButtonDown += OnListMouseDown;

        var border = new Border
        {
            Child = _list,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 4, 0, 0),
        };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.White");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");

        _popup = new Popup
        {
            Child = border,
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            AllowsTransparency = true,
        };
    }

    public IEnumerable? Suggestions
    {
        get => (IEnumerable?)GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!_popup.IsOpen)
            return;

        switch (e.Key)
        {
            case Key.Down:
                Highlight(Math.Min(_list.SelectedIndex + 1, _list.Items.Count - 1));
                e.Handled = true;
                break;
            case Key.Up:
                Highlight(Math.Max(_list.SelectedIndex - 1, 0));
                e.Handled = true;
                break;
            case Key.Enter when _list.SelectedItem is string name:
                Take(name);
                e.Handled = true;
                break;
            case Key.Escape:
                _popup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _popup.IsOpen = false;
    }

    private void OnSuggestionsChanged()
    {
        var names = Suggestions?.Cast<object>().OfType<string>().ToList() ?? [];
        _list.ItemsSource = names;
        _list.SelectedIndex = -1;

        // Nothing to offer when the only suggestion is exactly what is already typed.
        var worthShowing = names.Count > 1 || (names.Count == 1 && !string.Equals(names[0], Text, StringComparison.CurrentCultureIgnoreCase));
        _popup.MinWidth = ActualWidth;
        _popup.IsOpen = IsKeyboardFocused && worthShowing;
    }

    private void OnListMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(_list, (DependencyObject)e.OriginalSource) is ListBoxItem { Content: string name })
        {
            Take(name);
            e.Handled = true;
        }
    }

    private void Highlight(int index)
    {
        _list.SelectedIndex = index;
        if (_list.SelectedItem is not null)
            _list.ScrollIntoView(_list.SelectedItem);
    }

    private void Take(string name)
    {
        _popup.IsOpen = false;
        Text = name;
        CaretIndex = Text.Length;
    }
}
