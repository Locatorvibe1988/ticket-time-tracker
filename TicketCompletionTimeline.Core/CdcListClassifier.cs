using System.Text.RegularExpressions;

namespace TicketCompletionTimeline.Core;

public enum CdcColor
{
    Blue,
    Green,
    White,
    Red,
    Purple,
    Black
}

public sealed record CdcListStatus(string Code, CdcColor Color)
{
    public string Display => $"{Code} - {Color}";
}

public static partial class CdcListClassifier
{
    [GeneratedRegex(@"(?<!\d)(001|002|004|005|006|009)(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex KnownCodeRegex();

    public static CdcListStatus Classify(string? cdcList)
    {
        var code = KnownCodeRegex().Match(cdcList ?? string.Empty).Value;
        var color = code switch
        {
            "001" => CdcColor.Blue,
            "002" => CdcColor.Green,
            "004" => CdcColor.White,
            "005" or "006" => CdcColor.Red,
            "009" => CdcColor.Purple,
            _ => CdcColor.Black
        };

        return new CdcListStatus(string.IsNullOrEmpty(code) ? "Other" : code, color);
    }
}
