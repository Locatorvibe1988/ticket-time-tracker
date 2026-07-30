using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TicketCompletionTimeline.Core;

/// <summary>
/// Reads the small subset of CSV semantics the dashboard needs while preserving
/// every source field for the drill-down window. It intentionally does not use a
/// third-party parser so the portable build has no runtime dependency beyond .NET.
/// </summary>
public sealed class CsvImportService
{
    private static readonly string[] EasternZoneNames = ["Eastern Standard Time", "America/New_York"];

    public CsvImportResult Parse(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(reader, Path.GetFileName(path));
    }

    public CsvImportResult Parse(TextReader reader, string sourceFile = "import.csv")
    {
        var warnings = new List<ImportWarning>();
        var rows = ReadRows(reader).ToList();
        if (rows.Count == 0) return new([], [new ImportWarning(1, "The CSV file is empty.")], []);

        var headers = rows[0].Select((value, index) => string.IsNullOrWhiteSpace(value) ? $"Column {index + 1}" : value.Trim()).ToArray();
        var assignedHeader = FindHeader(headers, "Assigned User");
        var completionHeader = FindHeader(headers, "Last Completion");
        var ticketHeader = FindHeader(headers, "Ticket ID") ?? FindHeader(headers, "ID");
        if (assignedHeader is null) warnings.Add(new(1, "Missing required column: Assigned User."));
        if (completionHeader is null) warnings.Add(new(1, "Missing required column: Last Completion."));
        if (assignedHeader is null || completionHeader is null) return new([], warnings, headers);

        var valid = new List<CompletionRecord>();
        var rowNumber = 1;
        foreach (var values in rows.Skip(1))
        {
            rowNumber++;
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            // Keep the original columns, including columns the dashboard does
            // not understand today. That makes a later drill-down lossless.
            var sourceValues = headers.Select((header, index) =>
                new KeyValuePair<string, string>(header, index < values.Count ? values[index] : string.Empty))
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
            var assigned = GetValue(values, headers, assignedHeader).Trim();
            var completionText = GetValue(values, headers, completionHeader).Trim();
            if (assigned.Length == 0)
            {
                warnings.Add(new(rowNumber, "Assigned User is blank."));
                continue;
            }

            if (!TryNormalizeEastern(completionText, out var completion, out var reason))
            {
                warnings.Add(new(rowNumber, reason));
                continue;
            }

            var ticketKey = ticketHeader is null ? null : GetValue(values, headers, ticketHeader).Trim();
            var fingerprint = BuildFingerprint(sourceValues);
            valid.Add(new(rowNumber, sourceFile, sourceValues, assigned, completion, string.IsNullOrWhiteSpace(ticketKey) ? null : ticketKey, fingerprint));
        }

        return new(valid, warnings, headers);
    }

    public static IEnumerable<IReadOnlyList<string>> ReadRows(TextReader reader)
    {
        // This is a streaming state machine rather than a Split(',') call.
        // Quoted commas, embedded line breaks, and escaped quotes are all legal
        // CSV and must stay inside the current field.
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var any = false;
        int value;
        while ((value = reader.Read()) != -1)
        {
            any = true;
            var character = (char)value;
            if (quoted)
            {
                if (character == '"')
                {
                    if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                    else quoted = false;
                }
                else field.Append(character);
                continue;
            }

            if (character == '"' && field.Length == 0) { quoted = true; continue; }
            if (character == ',') { row.Add(field.ToString()); field.Clear(); continue; }
            if (character == '\r') { if (reader.Peek() == '\n') reader.Read(); row.Add(field.ToString()); field.Clear(); yield return row; row = []; continue; }
            if (character == '\n') { row.Add(field.ToString()); field.Clear(); yield return row; row = []; continue; }
            field.Append(character);
        }
        if (quoted) throw new FormatException("The CSV contains an unterminated quoted field.");
        if (any && (field.Length > 0 || row.Count > 0)) { row.Add(field.ToString()); yield return row; }
    }

    private static string? FindHeader(IReadOnlyList<string> headers, string expected) => headers.FirstOrDefault(h => string.Equals(h.Trim(), expected, StringComparison.OrdinalIgnoreCase));

    private static string GetValue(IReadOnlyList<string> values, IReadOnlyList<string> headers, string header)
    {
        var index = -1;
        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i], header, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
        }
        return index >= 0 && index < values.Count ? values[index] : string.Empty;
    }

    private static string BuildFingerprint(IReadOnlyDictionary<string, string> values) => string.Join("\u001f", values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => $"{pair.Key.Trim().ToUpperInvariant()}={pair.Value.Trim()}"));

    private static bool TryNormalizeEastern(string text, out DateTimeOffset normalized, out string reason)
    {
        normalized = default;
        reason = string.Empty;
        // Explicit offsets win. Offset-free values are interpreted as Eastern
        // wall-clock time so the result does not change with the PC's locale.
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var withOffset) && HasExplicitOffset(text))
        {
            normalized = TimeZoneInfo.ConvertTime(withOffset, GetEasternZone());
            return true;
        }

        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
        {
            reason = $"Invalid Last Completion timestamp: {text}";
            return false;
        }
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var eastern = GetEasternZone();
        if (eastern.IsInvalidTime(local)) { reason = "Last Completion falls in a nonexistent Eastern daylight-saving time."; return false; }
        if (eastern.IsAmbiguousTime(local)) { reason = "Last Completion falls in an ambiguous Eastern daylight-saving time."; return false; }
        normalized = new DateTimeOffset(local, eastern.GetUtcOffset(local));
        return true;
    }

    private static bool HasExplicitOffset(string text) => text.TrimEnd().EndsWith("Z", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(text.Trim(), @"(?:T|\s)\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?\s*[+-]\d{2}:?\d{2}\s*$", RegexOptions.CultureInvariant);

    private static TimeZoneInfo GetEasternZone()
    {
        foreach (var name in EasternZoneNames)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(name); } catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
