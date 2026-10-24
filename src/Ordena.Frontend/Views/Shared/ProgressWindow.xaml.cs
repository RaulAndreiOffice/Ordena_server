using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using Ordena.Backend.Dtos;
using Ordena.Frontend.Resources;

namespace Ordena.Frontend.Views.Shared;

public partial class ProgressWindow : Window
{
    private bool _finished;

    public ProgressWindow(string title)
    {
        InitializeComponent();
        TitleText.Text = title;
        AutomationProperties.SetName(Bar, title);
    }

    // One is open: the main window doesn't close under it (from the taskbar, say) while files are copied.
    public static bool IsOpen { get; private set; }

    // Cancelar, or the window's close button.
    public event EventHandler? CancelRequested;

    // "Archivo 120 de 3.001 · 45 MB de 1,2 GB"; the bar runs back and forth until the first report.
    public void Report(ArchiveProgress progress)
    {
        if (progress.FilesTotal == 0)
            return;

        Bar.IsIndeterminate = false;
        Bar.Maximum = progress.FilesTotal;
        Bar.Value = progress.FilesDone;
        DetailText.Text = string.Format(CultureInfo.CurrentCulture, Strings.Progress_Files,
            progress.FilesDone, progress.FilesTotal, FormatBytes(progress.BytesDone), FormatBytes(progress.BytesTotal));
    }

    // The job has ended, one way or another.
    public void Finish()
    {
        _finished = true;
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IsOpen = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_finished)
            return;

        e.Cancel = true; // it closes once the job has stopped
        RequestCancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        IsOpen = false;
        base.OnClosed(e);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => RequestCancel();

    private void RequestCancel()
    {
        CancelButton.IsEnabled = false;
        DetailText.Text = Strings.Progress_Cancelling;
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    // "850 KB", "45 MB", "1,2 GB", in the app's culture.
    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var format = unit == 0 || value >= 10 ? "0" : "0.#";
        return value.ToString(format, CultureInfo.CurrentCulture) + " " + units[unit];
    }
}
