using System.Globalization;
using System.Text.Json.Serialization;

namespace MultiStopwatch.Models;

/// <summary>Everything that is persisted to disk.</summary>
public sealed class AppState
{
    public int Version { get; set; } = 1;
    public List<TimerState> Timers { get; set; } = [];
    public List<SavedRecord> Records { get; set; } = [];
    public bool AlwaysOnTop { get; set; }
    public WindowBounds? Window { get; set; }

    /// <summary>When the first timer was started; cleared once every timer is back at zero.</summary>
    public DateTimeOffset? SessionStartUtc { get; set; }

    public static AppState CreateDefault() => new()
    {
        Timers = Enumerable.Range(1, 3).Select(i => new TimerState { Name = $"Timer {i}" }).ToList(),
    };
}

/// <summary>
/// A timer is stored as the time accumulated while it was previously running, plus the
/// wall-clock moment it was last started. Elapsed time is always recomputed from those,
/// so closing the app or shutting down the PC never loses time.
/// </summary>
public sealed class TimerState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public TimeSpan Accumulated { get; set; }
    public DateTimeOffset? RunningSinceUtc { get; set; }
}

/// <summary>One row of the saved table.</summary>
public sealed class SavedRecord
{
    /// <summary>
    /// When the measuring session began (or, if there wasn't one, when the row was saved).
    /// Stored as "SavedAt" so rows saved by earlier versions keep loading.
    /// </summary>
    [JsonPropertyName("SavedAt")]
    public DateTimeOffset Date { get; set; }

    public string Note { get; set; } = "";
    public string TimerName { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public bool IsTotal { get; set; }

    /// <summary>Date only, in the user's short date format (e.g. 09.10.2026).</summary>
    [JsonIgnore]
    public string DateText => Date.LocalDateTime.ToString("d", CultureInfo.CurrentCulture);

    [JsonIgnore]
    public string DurationText => TimeFormat.Duration(Duration);

    [JsonIgnore]
    public string HoursText => TimeFormat.Hours(Duration);
}

public sealed class WindowBounds
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

public static class TimeFormat
{
    public static TimeSpan WholeSeconds(TimeSpan value) =>
        value <= TimeSpan.Zero ? TimeSpan.Zero : TimeSpan.FromTicks(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond);

    /// <summary>hh:mm:ss, with hours allowed to go past 24.</summary>
    public static string Duration(TimeSpan value)
    {
        value = WholeSeconds(value);
        return $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }

    /// <summary>Local time of day (e.g. 9:12), with the date as well when it isn't today.</summary>
    public static string Clock(DateTimeOffset moment)
    {
        var local = moment.ToLocalTime();
        return local.Date == DateTime.Today ? local.ToString("t") : local.ToString("g");
    }

    /// <summary>Decimal hours in the user's number format (e.g. 1,25 on a Czech system).</summary>
    public static string Hours(TimeSpan value) =>
        WholeSeconds(value).TotalHours.ToString("0.00", CultureInfo.CurrentCulture);
}
