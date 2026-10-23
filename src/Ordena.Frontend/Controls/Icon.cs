using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Ordena.Frontend.Controls;

public enum IconKind
{
    // Outline, the kit's "linear" set: a 1.5 stroke with round ends.
    Linear,

    // Filled, the kit's "bold" set.
    Bold,
}

// Draws one of the 24×24 icon geometries in Resources/Theme/Icons.xaml at any size.
// The color is Foreground, inherited like text, so an icon in a button follows the button's text.
public sealed class Icon : FrameworkElement
{
    private const double GridSize = 24;
    private const double StrokeThickness = 1.5;

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(Icon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(IconKind), typeof(Icon),
        new FrameworkPropertyMetadata(IconKind.Linear, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Icon),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    // Centered by default: next to text in a taller row, an icon belongs in the middle, not at the top.
    static Icon()
    {
        VerticalAlignmentProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(VerticalAlignment.Center));
        HorizontalAlignmentProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(HorizontalAlignment.Center));
    }

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IconKind Kind
    {
        get => (IconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Data is null)
            return;

        // The stroke is scaled with the path, so it keeps the kit's proportions at every size.
        drawingContext.PushTransform(new ScaleTransform(Size / GridSize, Size / GridSize));
        if (Kind == IconKind.Bold)
        {
            drawingContext.DrawGeometry(Foreground, null, Data);
        }
        else
        {
            var pen = new Pen(Foreground, StrokeThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            drawingContext.DrawGeometry(null, pen, Data);
        }
        drawingContext.Pop();
    }
}
