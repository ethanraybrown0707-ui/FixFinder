using System.Text.Json;

namespace FixFinder.Core.Engine;

/// <summary>Forgiving readers for JSON that came off someone else's API.</summary>
public static class JsonReading
{
    public static bool TryGet(this JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
            return value.ValueKind != JsonValueKind.Null;

        value = default;
        return false;
    }

    public static string? StringOrNull(this JsonElement element, string name) =>
        element.TryGet(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static string StringOrEmpty(this JsonElement element, string name) =>
        element.StringOrNull(name) ?? "";

    public static int IntOrZero(this JsonElement element, string name) =>
        element.TryGet(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var parsed) ? parsed : 0;

    public static long? Int64OrNull(this JsonElement element, string name) =>
        element.TryGet(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var parsed) ? parsed : null;

    public static bool BoolOrFalse(this JsonElement element, string name) =>
        element.TryGet(name, out var value) && value.ValueKind == JsonValueKind.True;

    public static IEnumerable<JsonElement> ArrayOrEmpty(this JsonElement element, string name) =>
        element.TryGet(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    public static IReadOnlyList<string> StringArray(this JsonElement element, string name) =>
        element.ArrayOrEmpty(name)
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();

    public static DateTimeOffset? UnixSecondsOrNull(this JsonElement element, string name) =>
        element.Int64OrNull(name) is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    public static DateTimeOffset? DateOrNull(this JsonElement element, string name) =>
        element.StringOrNull(name) is { } text &&
        DateTimeOffset.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}
