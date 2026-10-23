using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ordena.Frontend.Behaviors;

// A read-only list grid driven by its ViewModel:
// - OpenCommand: double-click on a row, or Enter, with that row's item
// - EditCommand: F2; DeleteCommand: Supr/Delete, with the selected item
// - SortCommand: a header click, with the column's SortMemberPath. The ViewModel sorts in the database
//   (the grid holds only the first 500 rows), so the grid's own sorting is cancelled, and the arrow
//   follows SortBy / SortDescending as the ViewModel sets them. The grid clears its arrows whenever its
//   ItemsSource is replaced (every search), so the arrow is put back after each new list.
public static class DataGridBehavior
{
    public static readonly DependencyProperty OpenCommandProperty = RegisterCommand("OpenCommand");
    public static readonly DependencyProperty EditCommandProperty = RegisterCommand("EditCommand");
    public static readonly DependencyProperty DeleteCommandProperty = RegisterCommand("DeleteCommand");
    public static readonly DependencyProperty SortCommandProperty = RegisterCommand("SortCommand");

    public static readonly DependencyProperty SortByProperty = DependencyProperty.RegisterAttached(
        "SortBy", typeof(object), typeof(DataGridBehavior), new PropertyMetadata(null, OnSortChanged));

    public static readonly DependencyProperty SortDescendingProperty = DependencyProperty.RegisterAttached(
        "SortDescending", typeof(bool), typeof(DataGridBehavior), new PropertyMetadata(false, OnSortChanged));

    // Set once the grid's events are hooked, so several commands don't hook them twice.
    private static readonly DependencyProperty IsHookedProperty = DependencyProperty.RegisterAttached(
        "IsHooked", typeof(bool), typeof(DataGridBehavior), new PropertyMetadata(false));

    public static ICommand? GetOpenCommand(DependencyObject d) => (ICommand?)d.GetValue(OpenCommandProperty);
    public static void SetOpenCommand(DependencyObject d, ICommand? value) => d.SetValue(OpenCommandProperty, value);
    public static ICommand? GetEditCommand(DependencyObject d) => (ICommand?)d.GetValue(EditCommandProperty);
    public static void SetEditCommand(DependencyObject d, ICommand? value) => d.SetValue(EditCommandProperty, value);
    public static ICommand? GetDeleteCommand(DependencyObject d) => (ICommand?)d.GetValue(DeleteCommandProperty);
    public static void SetDeleteCommand(DependencyObject d, ICommand? value) => d.SetValue(DeleteCommandProperty, value);
    public static ICommand? GetSortCommand(DependencyObject d) => (ICommand?)d.GetValue(SortCommandProperty);
    public static void SetSortCommand(DependencyObject d, ICommand? value) => d.SetValue(SortCommandProperty, value);
    public static object? GetSortBy(DependencyObject d) => d.GetValue(SortByProperty);
    public static void SetSortBy(DependencyObject d, object? value) => d.SetValue(SortByProperty, value);
    public static bool GetSortDescending(DependencyObject d) => (bool)d.GetValue(SortDescendingProperty);
    public static void SetSortDescending(DependencyObject d, bool value) => d.SetValue(SortDescendingProperty, value);

    private static DependencyProperty RegisterCommand(string name) => DependencyProperty.RegisterAttached(
        name, typeof(ICommand), typeof(DataGridBehavior), new PropertyMetadata(null, OnCommandChanged));

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid || (bool)grid.GetValue(IsHookedProperty))
            return;

        grid.SetValue(IsHookedProperty, true);
        grid.MouseDoubleClick += OnDoubleClick;
        grid.PreviewKeyDown += OnKeyDown;
        grid.Sorting += OnSorting;
        grid.Loaded += (_, _) => ShowSort(grid);
        ((INotifyCollectionChanged)grid.Items).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                grid.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => ShowSort(grid));
        };
    }

    private static void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only on a row: a double-click on a header or the empty area below the rows opens nothing.
        var grid = (DataGrid)sender;
        if (e.ChangedButton == MouseButton.Left && FindRow(e.OriginalSource as DependencyObject) is { } row)
            Execute(GetOpenCommand(grid), row.Item, e);
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        var grid = (DataGrid)sender;
        if (grid.SelectedItem is not { } item || Keyboard.Modifiers != ModifierKeys.None)
            return;

        var command = e.Key switch
        {
            Key.Enter => GetOpenCommand(grid),
            Key.F2 => GetEditCommand(grid),
            Key.Delete => GetDeleteCommand(grid),
            _ => null,
        };
        Execute(command, item, e);
    }

    private static void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        Execute(GetSortCommand((DataGrid)sender), e.Column.SortMemberPath, null);
    }

    private static void OnSortChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid grid)
            ShowSort(grid);
    }

    // The arrow on the column the ViewModel sorts by, and none on the others.
    private static void ShowSort(DataGrid grid)
    {
        var sortBy = GetSortBy(grid)?.ToString();
        var direction = GetSortDescending(grid) ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var column in grid.Columns)
            column.SortDirection = column.SortMemberPath == sortBy && sortBy is not null ? direction : null;
    }

    private static void Execute(ICommand? command, object? parameter, RoutedEventArgs? e)
    {
        if (command?.CanExecute(parameter) != true)
            return;

        command.Execute(parameter);
        if (e is not null)
            e.Handled = true;
    }

    private static DataGridRow? FindRow(DependencyObject? element)
    {
        while (element is not null and not DataGridRow and not DataGridColumnHeader)
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        return element as DataGridRow;
    }
}
