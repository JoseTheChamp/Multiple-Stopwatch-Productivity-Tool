using System.Globalization;
using System.Text.RegularExpressions;

namespace MultiStopwatch.Models;

/// <summary>
/// Parses a typed time. Accepted forms (spaces ignored, "," or "." as decimal point):
///   1:30:00  or  1:30 (h:mm)        absolute time
///   45m  1h30m  1,5h  90s  2h 5m    absolute time with units (h, m/min, s)
///   90                              a plain number means minutes
///   +15m  -0:10  +1h                adjust the current time instead of replacing it
/// </summary>
public static partial class TimeInput
{
    private static readonly TimeSpan Max = TimeSpan.FromHours(1_000_000);

    public const string Help = "1:30:00, 1:30, 45m, 1,5h, 90 (minutes), or +15m / -10m to adjust";

    public static bool TryParse(string? input, TimeSpan current, out TimeSpan result)
    {
        result = current;
        var text = Regex.Replace(input ?? "", @"\s+", "").ToLowerInvariant();
        if (text.Length == 0)
            return false;

        var sign = 0;
        if (text[0] is '+' or '-')
        {
            sign = text[0] == '+' ? 1 : -1;
            text = text[1..];
        }
        if (!TryParseAmount(text, out var amount) || amount > Max)
            return false;

        var value = sign switch
        {
            > 0 => current + amount,
            < 0 => current - amount,
            _ => amount,
        };
        result = TimeFormat.WholeSeconds(value > Max ? Max : value);
        return true;
    }

    private static bool TryParseAmount(string text, out TimeSpan amount)
    {
        amount = TimeSpan.Zero;

        var clock = ClockPattern().Match(text);
        if (clock.Success)
        {
            amount = TimeSpan.FromHours(long.Parse(clock.Groups["h"].Value, CultureInfo.InvariantCulture))
                + TimeSpan.FromMinutes(long.Parse(clock.Groups["m"].Value, CultureInfo.InvariantCulture))
                + TimeSpan.FromSeconds(clock.Groups["s"].Success ? long.Parse(clock.Groups["s"].Value, CultureInfo.InvariantCulture) : 0);
            return true;
        }

        if (NumberPattern().IsMatch(text))
        {
            amount = TimeSpan.FromMinutes(Number(text));
            return true;
        }

        var units = UnitsPattern().Match(text);
        if (!units.Success)
            return false;
        for (var i = 0; i < units.Groups["n"].Captures.Count; i++)
        {
            var n = Number(units.Groups["n"].Captures[i].Value);
            amount += units.Groups["u"].Captures[i].Value switch
            {
                "h" => TimeSpan.FromHours(n),
                "s" => TimeSpan.FromSeconds(n),
                _ => TimeSpan.FromMinutes(n),
            };
        }
        return true;
    }

    private static double Number(string text) => double.Parse(text.Replace(',', '.'), CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<h>\d{1,7}):(?<m>\d{1,7})(?::(?<s>\d{1,7}))?$")]
    private static partial Regex ClockPattern();

    [GeneratedRegex(@"^\d{1,9}(?:[.,]\d{1,6})?$")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^(?:(?<n>\d{1,9}(?:[.,]\d{1,6})?)(?<u>h|min|m|s))+$")]
    private static partial Regex UnitsPattern();
}
