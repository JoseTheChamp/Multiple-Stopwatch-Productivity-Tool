using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using MultiStopwatch.Models;

namespace MultiStopwatch.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public const string AppName = "Stopwatch";

    private enum UndoScope { None, Timers, Records }

    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _noticeTimer;
    private Action? _undo;
    private UndoScope _undoScope;
    private string? _notice;
    private string _totalText = TimeFormat.Duration(TimeSpan.Zero);
    private string _runningText = "";
    private string _windowTitle = AppName;
    private bool _alwaysOnTop;
    private DateTimeOffset? _sessionStart;

    public MainViewModel(AppState state)
    {
        foreach (var timer in state.Timers)
            Attach(new TimerItem(timer), Timers.Count);
        foreach (var record in state.Records)
            Records.Add(record);
        _alwaysOnTop = state.AlwaysOnTop;
        WindowBounds = state.Window;
        // Data saved before this existed has no measuring start; a running timer's start is the best guess.
        _sessionStart = state.SessionStartUtc ?? state.Timers.Min(t => t.RunningSinceUtc);
        Renumber();

        ToggleCommand = new RelayCommand(p => { if (p is TimerItem t) Toggle(t); });
        ResetCommand = new RelayCommand(p => { if (p is TimerItem t) Reset(t); });
        RemoveCommand = new RelayCommand(p => { if (p is TimerItem t) Remove(t); });
        AddTimerCommand = new RelayCommand(() => AddTimer());
        StopAllCommand = new RelayCommand(StopAll);
        ClearAllCommand = new RelayCommand(ClearAll);
        SaveSnapshotCommand = new RelayCommand(SaveSnapshot);
        CopyAllCommand = new RelayCommand(CopyAll);
        ExportCommand = new RelayCommand(Export);
        DeleteRowsCommand = new RelayCommand(DeleteRows, p => p is IList { Count: > 0 });
        ClearTableCommand = new RelayCommand(_ => ClearTable(), _ => Records.Count > 0);
        UndoCommand = new RelayCommand(Undo);

        _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            if (_undo == null)
                Notice = null;
        };
        _clock = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
        Tick();
    }

    /// <summary>Raised after a timer is added so the view can focus its name box.</summary>
    public event Action<TimerItem>? TimerAdded;

    public ObservableCollection<TimerItem> Timers { get; } = [];
    public ObservableCollection<SavedRecord> Records { get; } = [];
    public WindowBounds? WindowBounds { get; set; }

    public ICommand ToggleCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand AddTimerCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand SaveSnapshotCommand { get; }
    public ICommand CopyAllCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand DeleteRowsCommand { get; }
    public ICommand ClearTableCommand { get; }
    public ICommand UndoCommand { get; }

    public string TotalText { get => _totalText; private set => Set(ref _totalText, value); }
    public string RunningText { get => _runningText; private set => Set(ref _runningText, value); }
    public string WindowTitle { get => _windowTitle; private set => Set(ref _windowTitle, value); }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set
        {
            if (Set(ref _alwaysOnTop, value))
                Persist();
        }
    }

    public string? Notice
    {
        get => _notice;
        private set
        {
            if (Set(ref _notice, value))
                OnPropertyChanged(nameof(HasNotice));
        }
    }

    public bool HasNotice => _notice != null;
    public bool CanUndo => _undo != null;

    // ---------------------------------------------------------------- timers

    /// <summary>Starts the timer and stops every other one; or pauses it if it is the one running.</summary>
    public void Toggle(TimerItem item)
    {
        var now = DateTimeOffset.UtcNow;
        if (item.IsRunning)
        {
            item.Stop(now);
        }
        else
        {
            foreach (var other in Timers)
                other.Stop(now);
            item.Start(now);
            _sessionStart ??= now;
        }
        TimersChanged();
    }

    public void ToggleAt(int number)
    {
        if (number >= 1 && number <= Timers.Count)
            Toggle(Timers[number - 1]);
    }

    /// <summary>The ±1 / ±5 minute buttons; works on running and paused timers alike.</summary>
    public void AdjustTime(TimerItem item, TimeSpan delta)
    {
        var now = DateTimeOffset.UtcNow;
        if (delta < TimeSpan.Zero && item.ElapsedAt(now) == TimeSpan.Zero)
            return;
        item.Adjust(delta, now);
        TimersChanged();
    }

    /// <summary>Applies a typed time (see <see cref="TimeInput"/>) to a stopped timer.</summary>
    public void SetTime(TimerItem item, string text)
    {
        if (item.IsRunning)
            return;

        var now = DateTimeOffset.UtcNow;
        var current = item.ElapsedAt(now);
        if (!TimeInput.TryParse(text, current, out var value))
        {
            ShowNotice($"Couldn't read “{text.Trim()}”. Try {TimeInput.Help}.");
            return;
        }
        if (value == current)
            return;

        var before = item.ToState();
        var undo = KeepingSession(() => item.Restore(before, DateTimeOffset.UtcNow));
        item.SetElapsed(value, now);
        TimersChanged();
        ShowNotice($"Set “{item.DisplayName}” to {item.ElapsedText} (was {TimeFormat.Duration(current)}).", undo, UndoScope.Timers);
    }

    public void StopAll()
    {
        if (!Timers.Any(t => t.IsRunning))
        {
            ShowNotice("Nothing is running.");
            return;
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var timer in Timers)
            timer.Stop(now);
        TimersChanged();
    }

    private void Reset(TimerItem item)
    {
        var now = DateTimeOffset.UtcNow;
        if (!item.IsRunning && item.ElapsedAt(now) == TimeSpan.Zero)
            return;

        var before = item.ToState();
        var undo = KeepingSession(() => item.Restore(before, DateTimeOffset.UtcNow));
        var text = $"Cleared “{item.DisplayName}” ({item.ElapsedText}).";
        item.Reset(now);
        TimersChanged();
        ShowNotice(text, undo, UndoScope.Timers);
    }

    private void ClearAll()
    {
        var now = DateTimeOffset.UtcNow;
        if (Timers.All(t => !t.IsRunning && t.ElapsedAt(now) == TimeSpan.Zero))
            return;
        if (!Confirm("Stop all timers and reset them to 00:00:00?"))
            return;

        now = DateTimeOffset.UtcNow;
        var before = Timers.Select(t => (Timer: t, State: t.ToState())).ToList();
        var undo = KeepingSession(() =>
        {
            var at = DateTimeOffset.UtcNow;
            foreach (var (timer, state) in before)
                timer.Restore(state, at);
        });
        foreach (var timer in Timers)
            timer.Reset(now);
        TimersChanged();
        ShowNotice("Cleared all timers.", undo, UndoScope.Timers);
    }

    private void Remove(TimerItem item)
    {
        var index = Timers.IndexOf(item);
        if (index < 0)
            return;

        var undo = KeepingSession(() =>
        {
            Attach(item, Math.Min(index, Timers.Count));
            Renumber();
        });
        Detach(item);
        Timers.RemoveAt(index);
        Renumber();
        TimersChanged();
        ShowNotice($"Removed “{item.DisplayName}” ({item.ElapsedText}).", undo, UndoScope.Timers);
    }

    public TimerItem AddTimer()
    {
        var number = Timers.Count + 1;
        while (Timers.Any(t => t.Name == $"Timer {number}"))
            number++;

        var item = new TimerItem(new TimerState { Name = $"Timer {number}" });
        Attach(item, Timers.Count);
        Renumber();
        Persist();
        Tick();
        TimerAdded?.Invoke(item);
        return item;
    }

    private void Attach(TimerItem item, int index)
    {
        item.PropertyChanged += OnTimerPropertyChanged;
        Timers.Insert(index, item);
    }

    private void Detach(TimerItem item) => item.PropertyChanged -= OnTimerPropertyChanged;

    private void OnTimerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimerItem.Name))
            Persist();
    }

    private void Renumber()
    {
        for (var i = 0; i < Timers.Count; i++)
            Timers[i].Number = i + 1;
    }

    /// <summary>Common tail of every start/stop/clear: save immediately and refresh the display.</summary>
    private void TimersChanged()
    {
        // Once everything is back at zero the measuring session is over; the next start begins a new one.
        var now = DateTimeOffset.UtcNow;
        if (Timers.All(t => !t.IsRunning && t.ElapsedAt(now) == TimeSpan.Zero))
            _sessionStart = null;

        InvalidateUndo(UndoScope.Timers);
        Persist();
        Tick();
    }

    /// <summary>
    /// Wraps an undo action so it also brings back the measuring start as it is now.
    /// Call it before making the change.
    /// </summary>
    private Action KeepingSession(Action undo)
    {
        var session = _sessionStart;
        return () =>
        {
            undo();
            _sessionStart = session;
        };
    }

    private void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        var total = TimeSpan.Zero;
        TimerItem? running = null;
        foreach (var timer in Timers)
        {
            timer.Refresh(now);
            total += timer.Elapsed;
            if (timer.IsRunning)
                running ??= timer;
        }

        TotalText = TimeFormat.Duration(total);
        var status = running == null
            ? Timers.Count == 0 ? "No timers — add one below." : "All timers stopped"
            : $"Running: {running.DisplayName}";
        RunningText = _sessionStart is { } start ? $"Measuring since {TimeFormat.Clock(start)}  ·  {status}" : status;
        WindowTitle = running == null ? AppName : $"{running.ElapsedText}  ▶ {running.DisplayName} — {AppName}";
    }

    // ---------------------------------------------------------------- saved table

    public void SaveSnapshot()
    {
        if (Timers.Count == 0)
        {
            ShowNotice("There are no timers to save.");
            return;
        }

        var utcNow = DateTimeOffset.UtcNow;
        // Rows are dated by when measuring began, not when they were saved.
        var date = (_sessionStart ?? utcNow).ToLocalTime();
        var total = TimeSpan.Zero;
        foreach (var timer in Timers)
        {
            var duration = timer.ElapsedAt(utcNow);
            total += duration;
            Records.Add(new SavedRecord { Date = date, TimerName = timer.DisplayName, Duration = duration });
        }
        Records.Add(new SavedRecord { Date = date, TimerName = "Total", Duration = total, IsTotal = true });

        InvalidateUndo(UndoScope.Records);
        Persist();
        ShowNotice($"Saved {Timers.Count} timer(s) + total ({TimeFormat.Duration(total)}) to the table.");
    }

    private void CopyAll()
    {
        if (Records.Count == 0)
        {
            ShowNotice("The table is empty — save some timers first.");
            return;
        }
        try
        {
            Clipboard.SetText(ToDelimited(Records, "\t"));
            ShowNotice($"Copied {Records.Count} rows. Paste into Excel, Sheets, an email…");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            ShowNotice("The clipboard is busy — try again.");
        }
    }

    private void Export()
    {
        if (Records.Count == 0)
        {
            ShowNotice("The table is empty — save some timers first.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export saved table",
            FileName = $"stopwatch-{DateTime.Now:yyyy-MM-dd}",
            DefaultExt = ".csv",
            AddExtension = true,
            Filter = "CSV for Excel (*.csv)|*.csv|Tab-separated text (*.txt)|*.txt",
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
            return;

        // Excel opens CSV files using the system list separator (";" on e.g. Czech systems).
        var separator = dialog.FilterIndex == 2 ? "\t" : CultureInfo.CurrentCulture.TextInfo.ListSeparator;
        try
        {
            File.WriteAllText(dialog.FileName, ToDelimited(Records, separator), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            ShowNotice($"Exported {Records.Count} rows to {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Application.Current.MainWindow, $"Could not write the file:\n{ex.Message}", AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteRows(object? selection)
    {
        if (selection is not IList { Count: > 0 } list)
            return;

        var removed = list.Cast<SavedRecord>()
            .Select(r => (Index: Records.IndexOf(r), Record: r))
            .Where(x => x.Index >= 0)
            .OrderBy(x => x.Index)
            .ToList();
        foreach (var (_, record) in removed)
            Records.Remove(record);

        Persist();
        ShowNotice($"Deleted {removed.Count} row(s).", () =>
        {
            foreach (var (index, record) in removed)
                Records.Insert(Math.Min(index, Records.Count), record);
        }, UndoScope.Records);
    }

    private void ClearTable()
    {
        if (Records.Count == 0 || !Confirm($"Delete all {Records.Count} rows from the saved table?"))
            return;

        var removed = Records.ToList();
        Records.Clear();
        Persist();
        ShowNotice("Cleared the saved table.", () =>
        {
            foreach (var record in removed)
                Records.Add(record);
        }, UndoScope.Records);
    }

    private static string ToDelimited(IEnumerable<SavedRecord> records, string separator)
    {
        string Field(string value) =>
            value.Contains(separator) || value.IndexOfAny(['"', '\r', '\n', '\t']) >= 0
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;

        var builder = new StringBuilder();
        builder.AppendJoin(separator, "Date", "Note", "Timer", "Duration", "Hours").Append("\r\n");
        foreach (var r in records)
        {
            builder.AppendJoin(separator, Field(r.DateText), Field(r.Note), Field(r.TimerName), Field(r.DurationText), Field(r.HoursText))
                .Append("\r\n");
        }
        return builder.ToString();
    }

    // ---------------------------------------------------------------- persistence, notices, undo

    public void Persist()
    {
        try
        {
            StateStore.Save(new AppState
            {
                Timers = Timers.Select(t => t.ToState()).ToList(),
                Records = Records.ToList(),
                AlwaysOnTop = AlwaysOnTop,
                Window = WindowBounds,
                SessionStartUtc = _sessionStart,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice($"⚠ Could not save data: {ex.Message}");
        }
    }

    private void ShowNotice(string text, Action? undo = null, UndoScope scope = UndoScope.None)
    {
        Notice = text;
        _undo = undo;
        _undoScope = undo == null ? UndoScope.None : scope;
        OnPropertyChanged(nameof(CanUndo));

        // Undo offers stay until something else replaces them; plain messages fade out.
        _noticeTimer.Stop();
        if (undo == null)
            _noticeTimer.Start();
    }

    /// <summary>
    /// An undo for timers is dropped as soon as any timer starts or stops, so undo can never
    /// leave two timers running at once. Likewise for the table.
    /// </summary>
    private void InvalidateUndo(UndoScope scope)
    {
        if (_undo != null && _undoScope == scope)
            ShowNotice(Notice ?? "");
    }

    private void Undo()
    {
        var undo = _undo;
        if (undo == null)
            return;
        undo();
        Persist();
        Tick();
        ShowNotice("Undone.");
    }

    private static bool Confirm(string question) =>
        MessageBox.Show(Application.Current.MainWindow, question, AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
