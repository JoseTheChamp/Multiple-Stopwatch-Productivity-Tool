using MultiStopwatch.Models;

namespace MultiStopwatch.ViewModels;

public sealed class TimerItem : ObservableObject
{
    private string _name;
    private TimeSpan _accumulated;
    private DateTimeOffset? _runningSince;
    private TimeSpan _shown;
    private int _number;

    public TimerItem(TimerState state)
    {
        Id = string.IsNullOrEmpty(state.Id) ? Guid.NewGuid().ToString("N") : state.Id;
        _name = state.Name ?? "";
        _accumulated = state.Accumulated < TimeSpan.Zero ? TimeSpan.Zero : state.Accumulated;
        _runningSince = state.RunningSinceUtc;
    }

    public string Id { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, value ?? ""))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(_name) ? $"Timer {_number}" : _name.Trim();

    /// <summary>1-based position in the list; drives the Ctrl+N shortcut hint.</summary>
    public int Number
    {
        get => _number;
        set
        {
            if (Set(ref _number, value))
            {
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public bool IsRunning => _runningSince.HasValue;

    /// <summary>Elapsed time as currently displayed (whole seconds).</summary>
    public TimeSpan Elapsed => _shown;

    public string ElapsedText => TimeFormat.Duration(_shown);

    public string Subtitle
    {
        get
        {
            var shortcut = _number is >= 1 and <= 9 ? $"Ctrl+{_number}" : null;
            if (_runningSince is not { } since)
                return shortcut ?? "";

            var started = TimeFormat.Clock(since);
            return shortcut == null ? $"Running since {started}" : $"Running since {started}  ·  {shortcut}";
        }
    }

    /// <summary>Elapsed time at <paramref name="now"/>, truncated to whole seconds.</summary>
    public TimeSpan ElapsedAt(DateTimeOffset now)
    {
        var elapsed = _accumulated;
        if (_runningSince is { } since && now > since)
            elapsed += now - since;
        return TimeFormat.WholeSeconds(elapsed);
    }

    public void Start(DateTimeOffset now)
    {
        if (IsRunning)
            return;
        _runningSince = now;
        StateChanged(now);
    }

    public void Stop(DateTimeOffset now)
    {
        if (_runningSince is not { } since)
            return;
        if (now > since)
            _accumulated += now - since;
        _runningSince = null;
        StateChanged(now);
    }

    public void Reset(DateTimeOffset now)
    {
        _accumulated = TimeSpan.Zero;
        _runningSince = null;
        StateChanged(now);
    }

    /// <summary>Sets the elapsed time to an exact value (manual edit).</summary>
    public void SetElapsed(TimeSpan value, DateTimeOffset now)
    {
        _accumulated = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        if (IsRunning)
            _runningSince = now;
        StateChanged(now);
    }

    /// <summary>
    /// Adds (or, if negative, removes) time without changing whether the timer is running.
    /// Never goes below zero.
    /// </summary>
    public void Adjust(TimeSpan delta, DateTimeOffset now)
    {
        _accumulated += delta;
        if (_accumulated < TimeSpan.Zero)
        {
            // Removing more than was banked before the current run: shorten the run itself
            // by counting it from a later moment.
            if (_runningSince is { } since)
            {
                var run = (now > since ? now - since : TimeSpan.Zero) + _accumulated;
                _runningSince = run > TimeSpan.Zero ? now - run : now;
            }
            _accumulated = TimeSpan.Zero;
        }
        StateChanged(now);
    }

    /// <summary>Puts the time back exactly as captured (used by undo); keeps the current name.</summary>
    public void Restore(TimerState state, DateTimeOffset now)
    {
        _accumulated = state.Accumulated;
        _runningSince = state.RunningSinceUtc;
        StateChanged(now);
    }

    public void Refresh(DateTimeOffset now)
    {
        var shown = ElapsedAt(now);
        if (shown == _shown)
            return;
        _shown = shown;
        OnPropertyChanged(nameof(Elapsed));
        OnPropertyChanged(nameof(ElapsedText));
    }

    public TimerState ToState() => new()
    {
        Id = Id,
        Name = _name,
        Accumulated = _accumulated,
        RunningSinceUtc = _runningSince,
    };

    private void StateChanged(DateTimeOffset now)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(Subtitle));
        Refresh(now);
    }
}
