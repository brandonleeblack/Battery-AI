using System.Globalization;
using System.Text.Json;

namespace Battery.Core.Providers;

static class JsonTime
{
    /// <summary>Reads an instant given as Unix seconds, Unix milliseconds or an ISO 8601 string.</summary>
    public static bool TryRead(JsonElement e, out DateTimeOffset value)
    {
        value = default;
        try
        {
            if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var n) && n > 0)
            {
                // Seconds until the year 5138 stay below 1e11; anything larger is milliseconds.
                value = n >= 1e11
                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)n)
                    : DateTimeOffset.FromUnixTimeMilliseconds((long)(n * 1000));
                return true;
            }
            if (e.ValueKind == JsonValueKind.String)
            {
                return DateTimeOffset.TryParse(e.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
            }
        }
        catch (ArgumentOutOfRangeException) { }
        return false;
    }

    public static bool TryGetNumber(JsonElement obj, string name, out double value)
    {
        value = 0;
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out var p)
            && p.ValueKind == JsonValueKind.Number
            && p.TryGetDouble(out value)
            && double.IsFinite(value);
    }
}
