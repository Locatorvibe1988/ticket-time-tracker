using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TicketCompletionTimeline.Core;

namespace TicketCompletionTimeline.App;

public sealed class TimelineHeaderControl : FrameworkElement
{
    private static readonly Pen GridPen = CreatePen(Color.FromRgb(198, 216, 231));
    private static readonly Typeface AxisTypeface = new("Segoe UI");
    private static readonly SolidColorBrush AxisTextBrush = CreateBrush(Color.FromRgb(81, 111, 137));

    public static readonly DependencyProperty StartHourProperty = DependencyProperty.Register(
        nameof(StartHour), typeof(int), typeof(TimelineHeaderControl),
        new FrameworkPropertyMetadata(6, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EndHourProperty = DependencyProperty.Register(
        nameof(EndHour), typeof(int), typeof(TimelineHeaderControl),
        new FrameworkPropertyMetadata(18, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public int StartHour { get => (int)GetValue(StartHourProperty); set => SetValue(StartHourProperty, value); }
    public int EndHour { get => (int)GetValue(EndHourProperty); set => SetValue(EndHourProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        // A workday view fills the available column. The full-day view keeps a
        // wider natural width so the horizontal scrollbar has room to separate
        // hourly labels instead of forcing them into the same pixels.
        var slots = Math.Max(1, (EndHour - StartHour) * 4);
        var compact = StartHour != 0 || EndHour != 24;
        var width = compact && !double.IsInfinity(availableSize.Width) ? Math.Max(1, availableSize.Width) : slots * 26;
        return new Size(width, 40);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var slots = Math.Max(1, (EndHour - StartHour) * 4);
        var cellWidth = ActualWidth > 0 ? ActualWidth / slots : 26;
        var labelStepHours = SelectLabelStepHours(cellWidth * 4);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var index = 0; index <= slots; index++)
        {
            var x = index * cellWidth;
            drawingContext.DrawLine(GridPen, new Point(x, 17), new Point(x, ActualHeight));
            if (index < slots && index % (labelStepHours * 4) == 0)
            {
                var label = FormatHour(StartHour + index / 4);
                var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AxisTypeface, 10, AxisTextBrush, pixelsPerDip);
                drawingContext.DrawText(text, new Point(x + 3, 2));
            }
        }
    }

    private static int SelectLabelStepHours(double pixelsPerHour)
    {
        // Labels are useful only when the next label has enough room to be read.
        // Round the interval to a clock-friendly value so the axis remains easy
        // to scan at narrow window sizes and with custom workday settings.
        if (pixelsPerHour >= 72) return 1;
        if (pixelsPerHour >= 36) return 2;
        if (pixelsPerHour >= 24) return 3;
        return 4;
    }

    private static Pen CreatePen(Color color)
    {
        var pen = new Pen(CreateBrush(color), 1);
        pen.Freeze();
        return pen;
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static string FormatHour(int hour)
    {
        if (hour == 0 || hour == 24) return "12 AM";
        if (hour == 12) return "12 PM";
        return hour < 12 ? $"{hour} AM" : $"{hour - 12} PM";
    }
}

public sealed class TimelineBucketClickEventArgs : EventArgs
{
    public IReadOnlyList<CompletionRecord> Events { get; }
    public TimelineBucketClickEventArgs(IReadOnlyList<CompletionRecord> events) => Events = events;
}

public sealed class TimelineControl : FrameworkElement
{
    private static readonly Pen GridPen = CreatePen(Color.FromRgb(223, 233, 241));
    private static readonly SolidColorBrush ActiveBrush = CreateBrush(Color.FromRgb(237, 246, 252));
    private static readonly SolidColorBrush GreenGapBrush = CreateBrush(Color.FromArgb(165, 27, 135, 76));
    private static readonly SolidColorBrush AmberGapBrush = CreateBrush(Color.FromArgb(165, 180, 117, 15));
    private static readonly SolidColorBrush RedGapBrush = CreateBrush(Color.FromArgb(165, 196, 61, 75));
    private static readonly Typeface BadgeTypeface = new("Segoe UI");

    public static readonly DependencyProperty EventsProperty = DependencyProperty.Register(
        nameof(Events), typeof(IReadOnlyList<CompletionRecord>), typeof(TimelineControl),
        new FrameworkPropertyMetadata(Array.Empty<CompletionRecord>(), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GapBandProperty = DependencyProperty.Register(
        nameof(GapBand), typeof(GapBand), typeof(TimelineControl),
        new FrameworkPropertyMetadata(GapBand.None, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StartHourProperty = DependencyProperty.Register(
        nameof(StartHour), typeof(int), typeof(TimelineControl),
        new FrameworkPropertyMetadata(6, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EndHourProperty = DependencyProperty.Register(
        nameof(EndHour), typeof(int), typeof(TimelineControl),
        new FrameworkPropertyMetadata(18, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GreenBelowMinutesProperty = DependencyProperty.Register(
        nameof(GreenBelowMinutes), typeof(double), typeof(TimelineControl),
        new FrameworkPropertyMetadata(60d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RedAboveMinutesProperty = DependencyProperty.Register(
        nameof(RedAboveMinutes), typeof(double), typeof(TimelineControl),
        new FrameworkPropertyMetadata(120d, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<CompletionRecord> Events { get => (IReadOnlyList<CompletionRecord>)GetValue(EventsProperty); set => SetValue(EventsProperty, value); }
    public GapBand GapBand { get => (GapBand)GetValue(GapBandProperty); set => SetValue(GapBandProperty, value); }
    public int StartHour { get => (int)GetValue(StartHourProperty); set => SetValue(StartHourProperty, value); }
    public int EndHour { get => (int)GetValue(EndHourProperty); set => SetValue(EndHourProperty, value); }
    public double GreenBelowMinutes { get => (double)GetValue(GreenBelowMinutesProperty); set => SetValue(GreenBelowMinutesProperty, value); }
    public double RedAboveMinutes { get => (double)GetValue(RedAboveMinutesProperty); set => SetValue(RedAboveMinutesProperty, value); }
    public event EventHandler<TimelineBucketClickEventArgs>? BucketClicked;

    protected override Size MeasureOverride(Size availableSize)
    {
        // Each cell represents one 15-minute bucket. Compact workday views
        // stretch across the window; the full-day view keeps a fixed cell width
        // so its horizontal scroll position stays aligned with the time header.
        var slots = Math.Max(1, (EndHour - StartHour) * 4);
        var width = StartHour == 0 && EndHour == 24 ? slots * 26 : (double.IsInfinity(availableSize.Width) ? slots * 20 : Math.Max(1, availableSize.Width));
        return new Size(width, 48);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var slots = Math.Max(1, (EndHour - StartHour) * 4);
        var cellWidth = StartHour == 0 && EndHour == 24 ? 26 : Math.Max(1, ActualWidth / slots);
        var clickedSlot = Math.Clamp((int)(e.GetPosition(this).X / cellWidth) + StartHour * 4, StartHour * 4, EndHour * 4 - 1);
        var bucket = Events.Where(record => record.Completion.Hour * 4 + record.Completion.Minute / 15 == clickedSlot).OrderBy(record => record.Completion).ThenBy(record => record.SourceRowNumber).ToList();
        if (bucket.Count > 0)
        {
            BucketClicked?.Invoke(this, new TimelineBucketClickEventArgs(bucket));
            e.Handled = true;
        }
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var slots = Math.Max(1, (EndHour - StartHour) * 4);
        var compact = StartHour != 0 || EndHour != 24;
        var cellWidth = compact ? Math.Max(1, ActualWidth / slots) : 26;
        const double height = 48;
        var firstVisibleSlot = StartHour * 4;
        var lastVisibleSlot = EndHour * 4;
        var orderedEvents = Events
            .Where(record =>
            {
                var slot = record.Completion.Hour * 4 + record.Completion.Minute / 15;
                return slot >= firstVisibleSlot && slot < lastVisibleSlot;
            })
            .OrderBy(record => record.Completion)
            .ThenBy(record => record.SourceRowNumber)
            .ToList();

        if (orderedEvents.Count > 0)
        {
            var activeStart = Math.Max(0, (orderedEvents.First().Completion.TimeOfDay.TotalMinutes - StartHour * 60) / 15);
            var activeEnd = Math.Min(slots, (orderedEvents.Last().Completion.TimeOfDay.TotalMinutes - StartHour * 60) / 15 + 1);
            drawingContext.DrawRectangle(ActiveBrush, null, new Rect(activeStart * cellWidth, 0, Math.Max(0, activeEnd - activeStart) * cellWidth, height));

            // Draw one colored segment for each consecutive pair. Keeping this
            // as an indexed loop avoids the temporary Zip iterator on every
            // virtualized row and makes the gap boundaries explicit.
            for (var index = 1; index < orderedEvents.Count; index++)
            {
                var first = orderedEvents[index - 1];
                var second = orderedEvents[index];
                var gapMinutes = (second.Completion - first.Completion).TotalMinutes;
                var band = gapMinutes < GreenBelowMinutes ? GapBand.Green : gapMinutes <= RedAboveMinutes ? GapBand.Amber : GapBand.Red;
                var start = (first.Completion.TimeOfDay.TotalMinutes - StartHour * 60) / 15;
                var end = (second.Completion.TimeOfDay.TotalMinutes - StartHour * 60) / 15;
                var visibleStart = Math.Max(0, start);
                var visibleEnd = Math.Min(slots, end);
                if (visibleEnd > visibleStart)
                {
                    drawingContext.DrawRoundedRectangle(GetGapBrush(band), null, new Rect(visibleStart * cellWidth, 38, (visibleEnd - visibleStart) * cellWidth, 6), 3, 3);
                }
            }
        }

        for (var index = 0; index < slots; index++)
        {
            var x = index * cellWidth;
            drawingContext.DrawLine(GridPen, new Point(x, 0), new Point(x, height));
        }
        drawingContext.DrawLine(GridPen, new Point(slots * cellWidth, 0), new Point(slots * cellWidth, height));
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var bucket in orderedEvents.GroupBy(record => record.Completion.Hour * 4 + record.Completion.Minute / 15 - firstVisibleSlot))
        {
            var count = bucket.Count();
            var badgeWidth = Math.Min(Math.Max(20, cellWidth - 4), count > 99 ? 30 : 24);
            var badge = new Rect(bucket.Key * cellWidth + (cellWidth - badgeWidth) / 2, 8, badgeWidth, 22);
            var colors = bucket
                .Select(record => CdcListClassifier.Classify(GetCdcList(record)).Color)
                .Distinct()
                .OrderBy(color => color)
                .ToList();
            if (colors.Count == 1)
            {
                drawingContext.DrawRoundedRectangle(CdcPalette.Brush(colors[0]), null, badge, 8, 8);
            }
            else
            {
                DrawDiagonalBadge(drawingContext, badge, colors[0], colors[1]);
            }
            var badgeTextBrush = colors.All(color => CdcPalette.TextBrush(color) == Brushes.Black) ? Brushes.Black : Brushes.White;
            var text = new FormattedText(count.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BadgeTypeface, 11, badgeTextBrush, pixelsPerDip);
            drawingContext.DrawText(text, new Point(badge.X + (badge.Width - text.Width) / 2, badge.Y + (badge.Height - text.Height) / 2));
        }
    }

    private static string GetCdcList(CompletionRecord record)
    {
        var match = record.SourceValues.FirstOrDefault(pair => string.Equals(pair.Key, "CDC List", StringComparison.OrdinalIgnoreCase));
        return match.Value ?? string.Empty;
    }

    private static void DrawDiagonalBadge(DrawingContext drawingContext, Rect badge, CdcColor first, CdcColor second)
    {
        drawingContext.PushClip(new RectangleGeometry(badge, 8, 8));
        drawingContext.DrawRectangle(CdcPalette.Brush(first), null, badge);
        var diagonal = new StreamGeometry();
        using (var context = diagonal.Open())
        {
            context.BeginFigure(new Point(badge.Left, badge.Bottom), true, true);
            context.LineTo(new Point(badge.Left, badge.Top), true, false);
            context.LineTo(new Point(badge.Right, badge.Top), true, false);
        }
        diagonal.Freeze();
        drawingContext.DrawGeometry(CdcPalette.Brush(second), null, diagonal);
        drawingContext.DrawLine(CreatePen(Color.FromArgb(180, 255, 255, 255)), new Point(badge.Left, badge.Bottom), new Point(badge.Right, badge.Top));
        drawingContext.Pop();
    }

    private static SolidColorBrush GetGapBrush(GapBand band) => band switch
    {
        GapBand.Green => GreenGapBrush,
        GapBand.Amber => AmberGapBrush,
        GapBand.Red => RedGapBrush,
        _ => GreenGapBrush
    };

    private static Pen CreatePen(Color color)
    {
        var pen = new Pen(CreateBrush(color), 1);
        pen.Freeze();
        return pen;
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
