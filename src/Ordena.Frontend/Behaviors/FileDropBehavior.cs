using System.IO;
using System.Windows;
using System.Windows.Input;

namespace Ordena.Frontend.Behaviors;

// Lets an element accept PDFs dragged from Explorer: the copy cursor only when the drag holds PDFs,
// then the PDF paths (string[]) to Command. IsDragging is true while such a drag is over the element,
// for a drop overlay. Other files in the same drag are ignored, and drags that hold no files (text
// dragged within a TextBox) are left to the elements under the mouse.
public static class FileDropBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(FileDropBehavior), new PropertyMetadata(null, OnCommandChanged));

    private static readonly DependencyPropertyKey IsDraggingKey = DependencyProperty.RegisterAttachedReadOnly(
        "IsDragging", typeof(bool), typeof(FileDropBehavior), new PropertyMetadata(false));

    public static readonly DependencyProperty IsDraggingProperty = IsDraggingKey.DependencyProperty;

    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);

    public static bool GetIsDragging(DependencyObject element) => (bool)element.GetValue(IsDraggingProperty);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;

        element.AllowDrop = e.NewValue is not null;
        element.PreviewDragEnter -= OnDragOver;
        element.PreviewDragOver -= OnDragOver;
        element.PreviewDragLeave -= OnDragLeave;
        element.PreviewDrop -= OnDrop;
        if (e.NewValue is null)
            return;

        element.PreviewDragEnter += OnDragOver;
        element.PreviewDragOver += OnDragOver;
        element.PreviewDragLeave += OnDragLeave;
        element.PreviewDrop += OnDrop;
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var hasPdfs = Pdfs(e).Length > 0;
        e.Effects = hasPdfs ? DragDropEffects.Copy : DragDropEffects.None;
        ((DependencyObject)sender).SetValue(IsDraggingKey, hasPdfs);
        e.Handled = true;
    }

    private static void OnDragLeave(object sender, DragEventArgs e) =>
        ((DependencyObject)sender).SetValue(IsDraggingKey, false);

    private static void OnDrop(object sender, DragEventArgs e)
    {
        var element = (DependencyObject)sender;
        element.SetValue(IsDraggingKey, false);
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var pdfs = Pdfs(e);
        var command = GetCommand(element);
        if (pdfs.Length > 0 && command?.CanExecute(pdfs) == true)
            command.Execute(pdfs);
        e.Handled = true;
    }

    private static string[] Pdfs(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths.Where(p => string.Equals(Path.GetExtension(p), ".pdf", StringComparison.OrdinalIgnoreCase)).ToArray()
            : [];
}
