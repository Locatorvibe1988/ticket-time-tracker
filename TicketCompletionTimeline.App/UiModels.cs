using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
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
    public string AssignedUser { get; }
    public int Total { get; }
    public string FirstText { get; }
    public string LastText { get; }
    public string LongestGapText { get; }
    public Brush LongestGapBrush { get; }
    public Brush LongestGapTextBrush { get; }
    public GapBand LongestGapBand { get; }
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

    public UserRowView(UserDayMetrics metrics, double? displayedGap = null, bool aggregate = false, UserReviewPriority? reviewPriority = null)
    {
        AssignedUser = metrics.AssignedUser;
        Total = metrics.Total;
        FirstText = metrics.First?.ToString("h:mm:ss tt") ?? "—";
        LastText = metrics.Last?.ToString("h:mm:ss tt") ?? "—";
        LongestGapText = displayedGap.HasValue ? FormatGap(displayedGap.Value) : FormatGap(metrics.LongestGapMinutes);
        LongestGapBand = displayedGap.HasValue ? metrics.LongestGapBand : metrics.LongestGapBand;
        LongestGapBrush = GapPalette.ToBrush(LongestGapBand);
        LongestGapTextBrush = LongestGapBand == GapBand.None ? GapPalette.NeutralText : Brushes.White;
        Events = metrics.Events;
        ReviewPriority = aggregate ? null : reviewPriority;
    }

    private static string FormatGap(double? minutes) => minutes is null ? "—" : $"{minutes:0.0} min";
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
