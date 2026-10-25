using System.IO;
using System.Text.Json;

namespace MultiStopwatch.Models;

/// <summary>
/// Persists <see cref="AppState"/> as JSON in %APPDATA%\MultiStopwatch.
/// Writes go to a temp file that is flushed to disk and then swapped in, keeping the
/// previous version as a .bak, so a power cut mid-write can't corrupt the data.
/// </summary>
public static class StateStore
{
    /// <summary>%APPDATA%\MultiStopwatch, unless the STOPWATCH_DATA_DIR environment variable points elsewhere.</summary>
    public static string Folder { get; } =
        Environment.GetEnvironmentVariable("STOPWATCH_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiStopwatch");

    private static string StatePath => Path.Combine(Folder, "state.json");
    private static string BackupPath => StatePath + ".bak";
    private static string TempPath => StatePath + ".tmp";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Returns null state on first run. Warning is set when a file existed but could not be read.</summary>
    public static (AppState? State, string? Warning) Load()
    {
        string? warning = null;
        foreach (var path in new[] { StatePath, BackupPath })
        {
            if (!File.Exists(path))
                continue;
            try
            {
                var state = JsonSerializer.Deserialize<AppState>(File.ReadAllBytes(path), Options);
                if (state != null)
                {
                    state.Timers ??= [];
                    state.Records ??= [];
                    if (warning != null)
                        warning += "\n\nThe previous backup was loaded instead.";
                    return (state, warning);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                var copy = Path.Combine(Folder, $"unreadable-{DateTime.Now:yyyyMMdd-HHmmss}-{Path.GetFileName(path)}");
                try { File.Copy(path, copy, overwrite: true); } catch (IOException) { }
                warning ??= $"Saved data could not be read ({ex.Message}).\nA copy of the file was kept at:\n{copy}";
            }
        }
        return (null, warning);
    }

    public static void Save(AppState state)
    {
        Directory.CreateDirectory(Folder);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, Options);

        using (var stream = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        if (!File.Exists(StatePath))
        {
            File.Move(TempPath, StatePath);
            return;
        }
        try
        {
            File.Replace(TempPath, StatePath, BackupPath, ignoreMetadataErrors: true);
        }
        catch (IOException)
        {
            // File.Replace can fail if something (e.g. a virus scanner) briefly holds the file.
            File.Copy(StatePath, BackupPath, overwrite: true);
            File.Move(TempPath, StatePath, overwrite: true);
        }
    }
}
