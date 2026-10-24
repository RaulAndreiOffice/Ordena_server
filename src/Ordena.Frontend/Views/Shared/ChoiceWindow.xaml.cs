using System.Windows;
using System.Windows.Controls;

namespace Ordena.Frontend.Views.Shared;

public partial class ChoiceWindow : Window
{
    public ChoiceWindow(string message, IReadOnlyList<string> choices)
    {
        InitializeComponent();
        MessageText.Text = message;

        // Right to left, so the first choice ends up last, at the right edge.
        foreach (var (i, choice) in choices.Index().Reverse())
        {
            var button = new Button
            {
                Content = choice,
                Style = (Style)FindResource(i == 0 ? "Button.Primary" : "Button.Secondary"),
                IsDefault = i == 0,
                Margin = new Thickness(i == choices.Count - 1 ? 0 : 8, 0, 0, 0),
            };
            button.Click += (_, _) =>
            {
                Choice = i;
                DialogResult = true;
            };
            Buttons.Children.Add(button);
        }
    }

    // The index of the button clicked; -1 when the window was closed instead.
    public int Choice { get; private set; } = -1;
}
