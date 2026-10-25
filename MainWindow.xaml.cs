using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MultiStopwatch.Models;
using MultiStopwatch.ViewModels;

namespace MultiStopwatch;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        ApplySavedBounds(vm.WindowBounds);

        vm.TimerAdded += FocusTimerName;
        vm.Records.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { Count: > 0 } added)
                ScrollRecordsTo(added[added.Count - 1]);
        };
        Closing += (_, _) =>
        {
            vm.WindowBounds = CaptureBounds();
            vm.Persist();
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.Control)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var digit = key switch
        {
            >= Key.D0 and <= Key.D9 => key - Key.D0,
            >= Key.NumPad0 and <= Key.NumPad9 => key - Key.NumPad0,
            _ => -1,
        };

        var isShortcut = digit >= 0 || key is Key.N or Key.S;
        if (isShortcut && Keyboard.FocusedElement is TextBox { Name: "TimeBox" } timeBox)
            CommitTime(timeBox);   // e.g. typed "1:30" then Ctrl+1: apply the edit before starting

        if (digit == 0)
            _vm.StopAll();
        else if (digit > 0)
            _vm.ToggleAt(digit);
        else if (key == Key.N)
            _vm.AddTimer();
        else if (key == Key.S)
            _vm.SaveSnapshot();
        else
            return;
        e.Handled = true;
    }

    /// <summary>Clicking empty space takes the cursor out of a name box.</summary>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (Keyboard.FocusedElement is TextBox)
            ReleaseTextFocus();
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            ReleaseTextFocus();
            e.Handled = true;
        }
    }

    // ------------------------------------------------ editing a paused timer's time

    private void TimeBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // The first click selects the whole time so whatever is typed replaces it.
        if (sender is TextBox { IsReadOnly: false, IsKeyboardFocusWithin: false } box)
        {
            box.Focus();
            e.Handled = true;
        }
    }

    private void TimeBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
            box.SelectAll();
    }

    private void TimeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: TimerItem item } box)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                CommitTime(box);
                ReleaseTextFocus();
                e.Handled = true;
                break;

            case Key.Escape:
                ShowActualTime(box);
                ReleaseTextFocus();
                e.Handled = true;
                break;

            case Key.Up or Key.Down when !box.IsReadOnly:
                var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(1);
                var shown = TimeInput.TryParse(box.Text, item.Elapsed, out var typed) ? typed : item.Elapsed;
                var next = e.Key == Key.Up ? shown + step : shown - step;
                // SetCurrentValue keeps the binding; assigning Text would remove it.
                box.SetCurrentValue(TextBox.TextProperty, TimeFormat.Duration(next < TimeSpan.Zero ? TimeSpan.Zero : next));
                box.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private void AdjustButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TimerItem item, Tag: string minutes })
            _vm.AdjustTime(item, TimeSpan.FromMinutes(int.Parse(minutes, System.Globalization.CultureInfo.InvariantCulture)));
    }

    private void TimeBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
            CommitTime(box);
    }

    private void CommitTime(TextBox box)
    {
        if (box.DataContext is TimerItem item && !box.IsReadOnly && box.Text != item.ElapsedText)
            _vm.SetTime(item, box.Text);
        ShowActualTime(box);
    }

    /// <summary>Drops whatever was typed and shows the timer's real value again.</summary>
    private static void ShowActualTime(TextBox box) =>
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

    private void ReleaseTextFocus()
    {
        FocusManager.SetFocusedElement(this, null);
        Keyboard.ClearFocus();
    }

    private void FocusTimerName(TimerItem item)
    {
        Tabs.SelectedIndex = 0;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (TimerList.ItemContainerGenerator.ContainerFromItem(item) is not ContentPresenter container)
                return;
            container.BringIntoView();
            if (container.ContentTemplate?.FindName("NameBox", container) is TextBox box)
            {
                box.Focus();
                box.SelectAll();
            }
        });
    }

    private void RecordsGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        // The edited value is committed after this event, so save once it has landed.
        if (e.EditAction == DataGridEditAction.Commit)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, _vm.Persist);
    }

    private void RecordsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Keyboard.FocusedElement is not TextBox
            && _vm.DeleteRowsCommand.CanExecute(RecordsGrid.SelectedItems))
        {
            _vm.DeleteRowsCommand.Execute(RecordsGrid.SelectedItems);
            e.Handled = true;
        }
    }

    private void RecordsGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_vm.Records.Count > 0)
            ScrollRecordsTo(_vm.Records[^1]);
    }

    private void ScrollRecordsTo(object? record)
    {
        if (record != null && RecordsGrid.IsLoaded)
            RecordsGrid.ScrollIntoView(record);
    }

    private void ApplySavedBounds(WindowBounds? bounds)
    {
        if (bounds == null || bounds.Width < MinWidth || bounds.Height < MinHeight)
            return;

        // Only restore if a decent part of the window would be on a screen that still exists.
        var screens = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = Rect.Intersect(screens, new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
        if (visible.IsEmpty || visible.Width < 120 || visible.Height < 60)
            return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        if (bounds.Maximized)
            WindowState = WindowState.Maximized;
    }

    private WindowBounds CaptureBounds()
    {
        var rect = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        return new WindowBounds
        {
            Left = rect.Left,
            Top = rect.Top,
            Width = rect.Width,
            Height = rect.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
    }
}
