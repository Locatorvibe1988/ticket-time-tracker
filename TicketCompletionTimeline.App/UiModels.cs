using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using TicketCompletionTimeline.Core;

namespace TicketCompletionTimeline.App;

public sealed class DateChoice
{
    public DateOnly Date { get; }
    public string Display => Date.ToString("dddd, MMMM d, yyyy");
    public DateChoice(DateOnly date) => Date = date;
}

public sealed class PeriodChoice
{
    public ReportPeriodKind Kind { get; }
    public DateOnly StartDate { get; }
    public DateOnly EndDate { get; }
    public int ActiveDays { get; }
    public string Display { get; }

    public PeriodChoice(ReportPeriodKind kind, DateOnly startDate, DateOnly endDate, int activeDays)
    {
        Kind = kind;
        StartDate = startDate;
        EndDate = endDate;
        ActiveDays = activeDays;
        Display = kind == ReportPeriodKind.Week
            ? $"Week of {startDate:MMM d, yyyy} ({activeDays} active day{(activeDays == 1 ? "" : "s")})"
            : $"{startDate:MMMM yyyy} ({activeDays} active day{(activeDays == 1 ? "" : "s")})";
    }
}

public enum ReportViewKind
{
    Daily,
    Weekly,
    Monthly
}

public sealed class UserRowView
{
    public bool IsTeamHeader { get; }
    public Visibility TeamHeaderVisibility => IsTeamHeader ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UserRowVisibility => IsTeamHeader ? Visibility.Collapsed : Visibility.Visible;
    public string AssignedUser { get; }
    public string AssignedTeamName { get; }
    public int Total { get; }
    public string FirstText { get; }
    public string LastText { get; }
    public string LongestGapText { get; }
    public Brush LongestGapBrush { get; }
    public Brush LongestGapTextBrush { get; }
    public GapBand LongestGapBand { get; }
    public IReadOnlyList<CompletionRecord> Events { get; }
    public IReadOnlyList<CdcCountView> CdcCounts { get; }
    public UserReviewPriority? ReviewPriority { get; }
    public string ReviewPriorityText => ReviewPriority?.Band switch
    {
        ReviewPriorityBand.InsufficientData => "Insufficient data",
        ReviewPriorityBand.Low => "Low",
        ReviewPriorityBand.Moderate => "Moderate",
        ReviewPriorityBand.High => "High",
        _ => "—"
    };
    public Brush ReviewPriorityBrush => PriorityPalette.ToBrush(ReviewPriority?.Band ?? ReviewPriorityBand.Any);
    public Brush ReviewPriorityTextBrush => ReviewPriority is null || ReviewPriority.Band == ReviewPriorityBand.InsufficientData ? GapPalette.NeutralText : Brushes.White;

    private UserRowView(string teamName)
    {
        IsTeamHeader = true;
        AssignedUser = teamName;
        AssignedTeamName = teamName;
        Total = 0;
        FirstText = string.Empty;
        LastText = string.Empty;
        LongestGapText = string.Empty;
        LongestGapBand = GapBand.None;
        LongestGapBrush = Brushes.Transparent;
        LongestGapTextBrush = Brushes.Transparent;
        Events = Array.Empty<CompletionRecord>();
        CdcCounts = Array.Empty<CdcCountView>();
    }

    public static UserRowView TeamHeader(string teamName) => new(teamName);

    public UserRowView(UserDayMetrics metrics, double? displayedGap = null, bool aggregate = false, UserReviewPriority? reviewPriority = null)
    {
        IsTeamHeader = false;
        AssignedUser = metrics.AssignedUser;
        var teams = metrics.Events
            .Select(FilterEngine.TeamName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(team => team, StringComparer.OrdinalIgnoreCase)
            .ToList();
        AssignedTeamName = aggregate ? "All teams" : string.Join(", ", teams.DefaultIfEmpty("No team"));
        Total = metrics.Total;
        FirstText = metrics.First?.ToString("h:mm:ss tt") ?? "—";
        LastText = metrics.Last?.ToString("h:mm:ss tt") ?? "—";
        LongestGapText = displayedGap.HasValue ? FormatGap(displayedGap.Value) : FormatGap(metrics.LongestGapMinutes);
        LongestGapBand = displayedGap.HasValue ? metrics.LongestGapBand : metrics.LongestGapBand;
        LongestGapBrush = GapPalette.ToBrush(LongestGapBand);
        LongestGapTextBrush = LongestGapBand == GapBand.None ? GapPalette.NeutralText : Brushes.White;
        Events = metrics.Events;
        CdcCounts = CdcCountView.Build(metrics.Events);
        ReviewPriority = aggregate ? null : reviewPriority;
    }

    private static string FormatGap(double? minutes) => minutes is null ? "—" : $"{minutes:0.0} min";
}

public sealed class CdcCountView
{
    public string Code { get; }
    public int Count { get; }
    public string Display => $"{Code}  {Count:N0}";
    public string Tooltip => Code == "Other" ? "Other CDC codes" : $"CDC {Code}";
    public Brush Brush { get; }
    public Brush TextBrush { get; }
    public Brush BorderBrush { get; }

    private CdcCountView(string code, int count, CdcColor color)
    {
        Code = code;
        Count = count;
        Brush = CdcPalette.Brush(color);
        TextBrush = CdcPalette.TextBrush(color);
        BorderBrush = CdcPalette.BorderBrush(color);
    }

    public static IReadOnlyList<CdcCountView> Build(IEnumerable<CompletionRecord> events)
    {
        return events
            .Select(record => CdcListClassifier.Classify(GetCdcList(record)))
            .GroupBy(status => status.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CdcCountView(group.Key, group.Count(), group.First().Color))
            .OrderBy(view => view.Code == "Other" ? "999" : view.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetCdcList(CompletionRecord record)
    {
        var match = record.SourceValues.FirstOrDefault(pair => string.Equals(pair.Key, "CDC List", StringComparison.OrdinalIgnoreCase));
        return match.Value ?? string.Empty;
    }
}

public static class CdcPalette
{
    public static SolidColorBrush WhiteBorder { get; } = Create(Color.FromRgb(180, 190, 200));

    private static readonly object Sync = new();
    private static Dictionary<CdcColor, Color> _colors = ToColors(CdcColorSettings.Default);

    public static void Configure(CdcColorSettings settings)
    {
        if (!settings.IsValid) settings = CdcColorSettings.Default;
        lock (Sync) _colors = ToColors(settings);
    }

    public static Brush Brush(CdcColor color)
    {
        Color value;
        lock (Sync) value = _colors[color];
        return Create(value);
    }

    public static Brush TextBrush(CdcColor color)
    {
        Color value;
        lock (Sync) value = _colors[color];
        var luminance = (0.299 * value.R + 0.587 * value.G + 0.114 * value.B) / 255;
        return luminance > 0.68 ? Brushes.Black : Brushes.White;
    }

    public static Brush BorderBrush(CdcColor color) => TextBrush(color) == Brushes.Black ? WhiteBorder : Brush(color);

    private static Dictionary<CdcColor, Color> ToColors(CdcColorSettings settings) => new()
    {
        [CdcColor.Blue] = Parse(settings.Blue, Colors.RoyalBlue),
        [CdcColor.Green] = Parse(settings.Green, Colors.LimeGreen),
        [CdcColor.White] = Parse(settings.White, Colors.White),
        [CdcColor.Red] = Parse(settings.Red, Colors.Red),
        [CdcColor.Purple] = Parse(settings.Purple, Colors.MediumPurple),
        [CdcColor.Black] = Parse(settings.Black, Colors.Black)
    };

    private static Color Parse(string value, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(value.Trim())!; }
        catch { return fallback; }
    }

    private static SolidColorBrush Create(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public sealed class PeriodRowView
{
    public string AssignedUser { get; }
    public string TotalText { get; }
    public string AverageDailyText { get; }
    public string AverageFirstText { get; }
    public string AverageLastText { get; }
    public string AverageLongestGapText { get; }
    public Brush AverageLongestGapBrush { get; }
    public Brush AverageLongestGapTextBrush { get; }
    public IReadOnlyList<CompletionRecord> Events { get; }
    public UserReviewPriority? ReviewPriority { get; }
    public string ReviewPriorityText => ReviewPriority?.Band switch
    {
        ReviewPriorityBand.InsufficientData => "Insufficient data",
        ReviewPriorityBand.Low => "Low",
        ReviewPriorityBand.Moderate => "Moderate",
        ReviewPriorityBand.High => "High",
        _ => "—"
    };
    public Brush ReviewPriorityBrush => PriorityPalette.ToBrush(ReviewPriority?.Band ?? ReviewPriorityBand.Any);
    public Brush ReviewPriorityTextBrush => ReviewPriority is null || ReviewPriority.Band == ReviewPriorityBand.InsufficientData ? GapPalette.NeutralText : Brushes.White;

    public PeriodRowView(PeriodUserMetrics metrics, UserReviewPriority? reviewPriority = null)
    {
        AssignedUser = metrics.AssignedUser;
        TotalText = metrics.Total.ToString("N0");
        AverageDailyText = metrics.AverageDailyTotal.ToString("0.0");
        AverageFirstText = FormatTime(metrics.AverageFirstTime);
        AverageLastText = FormatTime(metrics.AverageLastTime);
        AverageLongestGapText = FormatGap(metrics.AverageLongestGapMinutes);
        AverageLongestGapBrush = GapPalette.ToBrush(metrics.AverageLongestGapBand);
        AverageLongestGapTextBrush = metrics.AverageLongestGapBand == GapBand.None ? GapPalette.NeutralText : Brushes.White;
        Events = metrics.Events;
        ReviewPriority = string.Equals(metrics.AssignedUser, "All Users", StringComparison.OrdinalIgnoreCase) ? null : reviewPriority;
    }

    private static string FormatTime(TimeSpan? value)
    {
        if (value is null) return "â€”";
        var baseDate = DateTime.Today.Add(value.Value);
        return baseDate.ToString("h:mm tt");
    }

    private static string FormatGap(double? minutes) => minutes is null ? "â€”" : $"{minutes:0.0} min";
}

public sealed class FilterUserOption : INotifyPropertyChanged
{
    private bool _isSelected;
    public string AssignedUser { get; }
    public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public FilterUserOption(string assignedUser, bool isSelected = false) { AssignedUser = assignedUser; _isSelected = isSelected; }
}

public sealed class DrilldownRowView
{
    public IReadOnlyDictionary<string, string> Fields { get; }

    public DrilldownRowView(CompletionRecord record)
    {
        Fields = new Dictionary<string, string>(record.SourceValues, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class PeriodDayCellView
{
    public DateOnly Date { get; }
    public string DayLabel { get; }
    public string CountText { get; }
    public string TooltipText { get; }
    public Brush Background { get; }
    public Brush Foreground { get; }
    public bool HasData { get; }

    public PeriodDayCellView(DateOnly date, int count, int maximum, string dayLabel)
    {
        Date = date;
        DayLabel = dayLabel;
        CountText = count == 0 ? "—" : count.ToString("N0");
        TooltipText = $"{date:dddd, MMMM d, yyyy}: {count:N0} completion{(count == 1 ? "" : "s")}";
        Background = HeatmapPalette.ToBrush(count, maximum);
        Foreground = count >= Math.Max(1, maximum / 2) ? Brushes.White : Color.FromRgb(45, 74, 102).ToBrush();
        HasData = count > 0;
    }
}

public sealed class WeeklyHeatmapRowView
{
    public string AssignedUser { get; }
    public IReadOnlyList<PeriodDayCellView> Days { get; }

    public WeeklyHeatmapRowView(string assignedUser, IReadOnlyList<PeriodDayCellView> days)
    {
        AssignedUser = assignedUser;
        Days = days;
    }
}

public sealed class CalendarDayCellView
{
    public DateOnly? Date { get; }
    public string DayText { get; }
    public string CountText { get; }
    public string TooltipText { get; }
    public Brush Background { get; }
    public Brush Foreground { get; }
    public bool IsEnabled { get; }
    public bool IsPlaceholder => Date is null;

    public CalendarDayCellView(DateOnly? date, int count, int maximum)
    {
        Date = date;
        DayText = date?.Day.ToString() ?? string.Empty;
        CountText = count == 0 ? string.Empty : count.ToString("N0");
        TooltipText = date is null ? string.Empty : $"{date:dddd, MMMM d, yyyy}: {count:N0} completion{(count == 1 ? "" : "s")}";
        Background = date is null ? Brushes.Transparent : HeatmapPalette.ToBrush(count, maximum);
        Foreground = count >= Math.Max(1, maximum / 2) ? Brushes.White : Color.FromRgb(45, 74, 102).ToBrush();
        IsEnabled = date is not null;
    }
}

public sealed class TrendPointView
{
    public string Label { get; }
    public string ValueText { get; }
    public string TooltipText { get; }
    public double BarHeight { get; }
    public Brush Fill { get; }

    public TrendPointView(string label, int value, int maximum, string tooltipText)
    {
        Label = label;
        ValueText = value.ToString("N0");
        TooltipText = tooltipText;
        BarHeight = maximum == 0 ? 4 : Math.Max(4, 104 * value / (double)maximum);
        Fill = value == 0 ? HeatmapPalette.Empty : HeatmapPalette.Primary;
    }
}

public sealed class ReviewSignalView
{
    public string AssignedUser { get; }
    public string DateText { get; }
    public string Title { get; }
    public string Evidence { get; }
    public Brush Accent { get; }

    public ReviewSignalView(ReviewSignal signal)
    {
        AssignedUser = signal.AssignedUser;
        DateText = signal.Date.ToString("MMM d, yyyy");
        Title = signal.Title;
        Evidence = signal.Evidence;
        Accent = GapPalette.ToBrush(signal.Band);
    }
}

public static class HeatmapPalette
{
    public static Brush Empty { get; } = Create(Color.FromRgb(237, 244, 250));
    public static Brush Primary { get; } = Create(Color.FromRgb(19, 103, 177));

    public static Brush ToBrush(int value, int maximum)
    {
        if (value <= 0 || maximum <= 0) return Empty;
        var ratio = Math.Clamp(value / (double)maximum, 0, 1);
        var red = (byte)(235 - 216 * ratio);
        var green = (byte)(246 - 143 * ratio);
        var blue = (byte)(252 - 75 * ratio);
        return Create(Color.FromRgb(red, green, blue));
    }

    private static SolidColorBrush Create(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

internal static class ColorBrushExtensions
{
    public static SolidColorBrush ToBrush(this Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public static class GapPalette
{
    private static readonly SolidColorBrush Green = Create(Color.FromRgb(27, 135, 76));
    private static readonly SolidColorBrush Amber = Create(Color.FromRgb(180, 117, 15));
    private static readonly SolidColorBrush Red = Create(Color.FromRgb(196, 61, 75));
    private static readonly SolidColorBrush Neutral = Create(Color.FromRgb(116, 137, 153));

    public static Brush NeutralText => Neutral;

    public static Brush ToBrush(GapBand band) => band switch
    {
        GapBand.Green => Green,
        GapBand.Amber => Amber,
        GapBand.Red => Red,
        _ => Neutral
    };

    private static SolidColorBrush Create(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public static class PriorityPalette
{
    private static readonly SolidColorBrush Low = Create(Color.FromRgb(27, 135, 76));
    private static readonly SolidColorBrush Moderate = Create(Color.FromRgb(180, 117, 15));
    private static readonly SolidColorBrush High = Create(Color.FromRgb(196, 61, 75));
    private static readonly SolidColorBrush Neutral = Create(Color.FromRgb(116, 137, 153));

    public static Brush ToBrush(ReviewPriorityBand band) => band switch
    {
        ReviewPriorityBand.Low => Low,
        ReviewPriorityBand.Moderate => Moderate,
        ReviewPriorityBand.High => High,
        _ => Neutral
    };

    private static SolidColorBrush Create(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public sealed class MainWindowState : INotifyPropertyChanged
{
    public ObservableCollection<DateChoice> AvailableDates { get; } = [];
    public ObservableCollection<UserRowView> UserRows { get; } = [];
    public ObservableCollection<UserRowView> TimelineRows { get; } = [];
    private DateChoice? _selectedDate;
    private string _statusText = "No data loaded";
    private string _warningText = string.Empty;
    private bool _hasData;
    public DateChoice? SelectedDate { get => _selectedDate; set { _selectedDate = value; OnPropertyChanged(); } }
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public string WarningText { get => _warningText; set { _warningText = value; OnPropertyChanged(); } }
    public bool HasData { get => _hasData; set { _hasData = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
