using System.Collections.ObjectModel;

namespace TicketCompletionTimeline.Core;

public sealed record ImportWarning(int RowNumber, string Reason);

public sealed record CompletionRecord(
    int SourceRowNumber,
    string SourceFile,
    IReadOnlyDictionary<string, string> SourceValues,
    string AssignedUser,
    DateTimeOffset Completion,
    string? TicketKey,
    string Fingerprint);

public sealed record CsvImportResult(
    IReadOnlyList<CompletionRecord> ValidRows,
    IReadOnlyList<ImportWarning> Warnings,
    IReadOnlyList<string> Headers)
{
    public int RejectedRows => Warnings.Select(w => w.RowNumber).Distinct().Count();
}

public enum ConflictResolution
{
    OverwriteConflicts,
    ClearCurrentDataAndImport,
    CancelImport
}

public sealed record ImportConflict(string TicketKey, CompletionRecord Existing, CompletionRecord Incoming);

public sealed record MergeResult(
    IReadOnlyList<CompletionRecord> Records,
    IReadOnlyList<ImportConflict> Conflicts,
    bool WasCancelled);

public enum GapBand
{
    None,
    Green,
    Amber,
    Red
}

public sealed record CdcColorSettings(
    string Blue = "#3B82F6",
    string Green = "#22C55E",
    string White = "#FFFFFF",
    string Red = "#EF4444",
    string Purple = "#8B5CF6",
    string Black = "#111827")
{
    public static CdcColorSettings Default { get; } = new();

    public bool IsValid =>
        IsHexColor(Blue) && IsHexColor(Green) && IsHexColor(White) &&
        IsHexColor(Red) && IsHexColor(Purple) && IsHexColor(Black);

    private static bool IsHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.StartsWith('#')) text = text[1..];
        return (text.Length == 6 || text.Length == 8) && text.All(Uri.IsHexDigit);
    }
}

public sealed record GapThresholds(
    double GreenBelowMinutes = 60,
    double RedAboveMinutes = 120,
    bool ShowReviewSignals = false,
    int WorkdayStartMinutes = 360,
    int WorkdayEndMinutes = 1080,
    bool ShowReviewPriority = false,
    int ReviewMinActiveDays = 3,
    int ReviewMinCompletions = 10,
    int LongGapWeight = 2,
    int DenseBurstWeight = 1,
    int EndOfShiftBatchWeight = 1,
    int BaselineChangeWeight = 2,
    int LowConsistencyWeight = 1,
    int VolumeOutlierWeight = 1,
    int ModeratePriorityThreshold = 2,
    int HighPriorityThreshold = 5,
    double VolumeOutlierRatio = 0.5,
    double LowConsistencyRatio = 0.5,
    CdcColorSettings? CdcColors = null)
{
    public static GapThresholds Default { get; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public CdcColorSettings EffectiveCdcColors => CdcColors is { IsValid: true } ? CdcColors : CdcColorSettings.Default;

    public bool IsValid =>
        GreenBelowMinutes >= 0 &&
        RedAboveMinutes > GreenBelowMinutes &&
        WorkdayStartMinutes >= 0 && WorkdayStartMinutes < 1440 &&
        WorkdayEndMinutes >= 0 && WorkdayEndMinutes < 1440 &&
        WorkdayStartMinutes != WorkdayEndMinutes &&
        ReviewMinActiveDays > 0 &&
        ReviewMinCompletions > 0 &&
        LongGapWeight >= 0 && DenseBurstWeight >= 0 && EndOfShiftBatchWeight >= 0 &&
        BaselineChangeWeight >= 0 && LowConsistencyWeight >= 0 && VolumeOutlierWeight >= 0 &&
        ModeratePriorityThreshold >= 1 &&
        HighPriorityThreshold > ModeratePriorityThreshold &&
        VolumeOutlierRatio > 0 && VolumeOutlierRatio < 1 &&
        LowConsistencyRatio > 0 && LowConsistencyRatio <= 1 &&
        EffectiveCdcColors.IsValid;

    public TimeSpan WorkdayStart => TimeSpan.FromMinutes(WorkdayStartMinutes);
    public TimeSpan WorkdayEnd => TimeSpan.FromMinutes(WorkdayEndMinutes);

    public bool IsWithinWorkHours(DateTimeOffset completion)
    {
        var time = completion.TimeOfDay.TotalMinutes;
        return WorkdayStartMinutes < WorkdayEndMinutes
            ? time >= WorkdayStartMinutes && time < WorkdayEndMinutes
            : time >= WorkdayStartMinutes || time < WorkdayEndMinutes;
    }

    public GapBand GetBand(double? minutes)
    {
        if (minutes is null) return GapBand.None;
        if (minutes < GreenBelowMinutes) return GapBand.Green;
        if (minutes <= RedAboveMinutes) return GapBand.Amber;
        return GapBand.Red;
    }
}

public sealed record CompletionBucket(int QuarterHour, IReadOnlyList<CompletionRecord> Events);

public sealed record UserDayMetrics(
    string AssignedUser,
    IReadOnlyList<CompletionRecord> Events,
    DateTimeOffset? First,
    DateTimeOffset? Last,
    double? LongestGapMinutes,
    GapBand LongestGapBand,
    IReadOnlyList<CompletionBucket> Buckets)
{
    public int Total => Events.Count;
}

public sealed record DayMetrics(
    DateOnly Date,
    IReadOnlyList<UserDayMetrics> Users,
    UserDayMetrics AllUsers)
{
    public bool HasData => AllUsers.Total > 0;
}

public enum ReportPeriodKind
{
    Week,
    Month
}

public sealed record PeriodUserMetrics(
    string AssignedUser,
    int Total,
    double AverageDailyTotal,
    TimeSpan? AverageFirstTime,
    TimeSpan? AverageLastTime,
    double? AverageLongestGapMinutes,
    GapBand AverageLongestGapBand,
    IReadOnlyList<CompletionRecord> Events,
    int ActiveDays);

public sealed record PeriodMetrics(
    ReportPeriodKind Kind,
    DateOnly StartDate,
    DateOnly EndDate,
    int ActiveDays,
    IReadOnlyList<PeriodUserMetrics> Users,
    PeriodUserMetrics AllUsers)
{
    public bool HasData => AllUsers.Total > 0;
}

public enum ReviewSignalKind
{
    LongIdleGap,
    AfterHoursCompletion,
    DenseCompletionBurst,
    EndOfShiftBatch,
    ChangeFromBaseline
}

public sealed record ReviewSignal(
    ReviewSignalKind Kind,
    string AssignedUser,
    DateOnly Date,
    string Title,
    string Evidence,
    GapBand Band);

public enum WorkHourFilter
{
    Any,
    WorkHours,
    OnCall
}

public enum ReviewPriorityBand
{
    Any,
    InsufficientData,
    Low,
    Moderate,
    High
}

public sealed record CompletionFilters(
    IReadOnlySet<string>? AssignedUsers = null,
    int? MinimumCompletions = null,
    int? MaximumCompletions = null,
    GapBand LongestGapBand = GapBand.None,
    WorkHourFilter WorkHours = WorkHourFilter.Any,
    ReviewPriorityBand ReviewPriority = ReviewPriorityBand.Any,
    int? MinimumActiveDays = null,
    int? MaximumActiveDays = null,
    string? AssignedTeamName = null)
{
    public bool HasUserFilter => AssignedUsers is { Count: > 0 };
    public bool HasTeamFilter => !string.IsNullOrWhiteSpace(AssignedTeamName);
    public bool HasValue => HasUserFilter || MinimumCompletions.HasValue || MaximumCompletions.HasValue ||
        LongestGapBand != GapBand.None || WorkHours != WorkHourFilter.Any ||
        ReviewPriority != ReviewPriorityBand.Any || MinimumActiveDays.HasValue || MaximumActiveDays.HasValue || HasTeamFilter;
}

public sealed record ReviewPriorityEvidence(
    string Title,
    string Detail,
    DateOnly? Date,
    int Points);

public sealed record UserReviewPriority(
    string AssignedUser,
    ReviewPriorityBand Band,
    int Points,
    int Total,
    int ActiveDays,
    IReadOnlyList<ReviewPriorityEvidence> Evidence)
{
    public bool HasEnoughData => Band != ReviewPriorityBand.InsufficientData;
}

public sealed record AppPreferences(string? AssignedTeamName = null, bool FullDayTimeline = false);

public sealed class SettingsStore
{
    private readonly string _path;
    private readonly string _preferencesPath;

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TicketCompletionTimeline",
            "settings.json");
        _preferencesPath = _path + ".preferences";
    }

    public GapThresholds Load()
    {
        try
        {
            if (!File.Exists(_path)) return GapThresholds.Default;
            var settings = System.Text.Json.JsonSerializer.Deserialize<GapThresholds>(File.ReadAllText(_path));
            return settings is { IsValid: true } ? settings : GapThresholds.Default;
        }
        catch
        {
            return GapThresholds.Default;
        }
    }

    public void Save(GapThresholds settings)
    {
        if (!settings.IsValid) throw new ArgumentException("The red threshold must be greater than the green threshold.", nameof(settings));
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(_path, System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    public AppPreferences LoadPreferences()
    {
        try
        {
            if (!File.Exists(_preferencesPath)) return new AppPreferences();
            return System.Text.Json.JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(_preferencesPath)) ?? new AppPreferences();
        }
        catch
        {
            return new AppPreferences();
        }
    }

    public void SavePreferences(AppPreferences preferences)
    {
        var directory = Path.GetDirectoryName(_preferencesPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(_preferencesPath, System.Text.Json.JsonSerializer.Serialize(preferences, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
