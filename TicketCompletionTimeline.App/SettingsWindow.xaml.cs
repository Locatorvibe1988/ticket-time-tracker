using System.Globalization;
using System.Windows;
using TicketCompletionTimeline.Core;

namespace TicketCompletionTimeline.App;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    public GapThresholds Settings { get; private set; }

    public SettingsWindow(SettingsStore store, GapThresholds settings)
    {
        InitializeComponent();
        _store = store;
        Settings = settings;
        Populate(settings);
    }

    private void Populate(GapThresholds settings)
    {
        GreenBox.Text = settings.GreenBelowMinutes.ToString(CultureInfo.InvariantCulture);
        RedBox.Text = settings.RedAboveMinutes.ToString(CultureInfo.InvariantCulture);
        WorkStartBox.Text = TimeSpan.FromMinutes(settings.WorkdayStartMinutes).ToString(@"hh\:mm");
        WorkEndBox.Text = TimeSpan.FromMinutes(settings.WorkdayEndMinutes).ToString(@"hh\:mm");
        ReviewSignalsBox.IsChecked = settings.ShowReviewSignals;
        ReviewPriorityBox.IsChecked = settings.ShowReviewPriority;
        MinActiveDaysBox.Text = settings.ReviewMinActiveDays.ToString(CultureInfo.InvariantCulture);
        MinCompletionsBox.Text = settings.ReviewMinCompletions.ToString(CultureInfo.InvariantCulture);
        LongGapWeightBox.Text = settings.LongGapWeight.ToString(CultureInfo.InvariantCulture);
        DenseBurstWeightBox.Text = settings.DenseBurstWeight.ToString(CultureInfo.InvariantCulture);
        EndShiftWeightBox.Text = settings.EndOfShiftBatchWeight.ToString(CultureInfo.InvariantCulture);
        BaselineWeightBox.Text = settings.BaselineChangeWeight.ToString(CultureInfo.InvariantCulture);
        ConsistencyWeightBox.Text = settings.LowConsistencyWeight.ToString(CultureInfo.InvariantCulture);
        VolumeWeightBox.Text = settings.VolumeOutlierWeight.ToString(CultureInfo.InvariantCulture);
        ModerateThresholdBox.Text = settings.ModeratePriorityThreshold.ToString(CultureInfo.InvariantCulture);
        HighThresholdBox.Text = settings.HighPriorityThreshold.ToString(CultureInfo.InvariantCulture);
        VolumeRatioBox.Text = settings.VolumeOutlierRatio.ToString(CultureInfo.InvariantCulture);
        ConsistencyRatioBox.Text = settings.LowConsistencyRatio.ToString(CultureInfo.InvariantCulture);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e) => Populate(GapThresholds.Default);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadDouble(GreenBox, out var green) || !TryReadDouble(RedBox, out var red) ||
            !TryReadTime(WorkStartBox, out var workStart) || !TryReadTime(WorkEndBox, out var workEnd) ||
            !TryReadInt(MinActiveDaysBox, out var minActiveDays) || !TryReadInt(MinCompletionsBox, out var minCompletions) ||
            !TryReadInt(LongGapWeightBox, out var longGapWeight) || !TryReadInt(DenseBurstWeightBox, out var denseBurstWeight) ||
            !TryReadInt(EndShiftWeightBox, out var endShiftWeight) || !TryReadInt(BaselineWeightBox, out var baselineWeight) ||
            !TryReadInt(ConsistencyWeightBox, out var consistencyWeight) || !TryReadInt(VolumeWeightBox, out var volumeWeight) ||
            !TryReadInt(ModerateThresholdBox, out var moderateThreshold) || !TryReadInt(HighThresholdBox, out var highThreshold) ||
            !TryReadDouble(VolumeRatioBox, out var volumeRatio) || !TryReadDouble(ConsistencyRatioBox, out var consistencyRatio))
        {
            ErrorText.Text = "Enter valid numbers and work times (for example, 06:00 and 18:00).";
            return;
        }

        var settings = new GapThresholds(
            green,
            red,
            ReviewSignalsBox.IsChecked == true,
            (int)workStart.TotalMinutes,
            (int)workEnd.TotalMinutes,
            ReviewPriorityBox.IsChecked == true,
            minActiveDays,
            minCompletions,
            longGapWeight,
            denseBurstWeight,
            endShiftWeight,
            baselineWeight,
            consistencyWeight,
            volumeWeight,
            moderateThreshold,
            highThreshold,
            volumeRatio,
            consistencyRatio);
        if (!settings.IsValid)
        {
            ErrorText.Text = "Check thresholds, work hours, weights, and scoring ratios. High must be greater than Moderate; work start and end cannot match.";
            return;
        }
        try { _store.Save(settings); }
        catch (Exception error) { ErrorText.Text = error.Message; return; }
        Settings = settings;
        DialogResult = true;
    }

    private static bool TryReadDouble(System.Windows.Controls.TextBox box, out double value) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    private static bool TryReadInt(System.Windows.Controls.TextBox box, out int value) => int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryReadTime(System.Windows.Controls.TextBox box, out TimeSpan value)
    {
        if (TimeSpan.TryParseExact(box.Text.Trim(), new[] { @"hh\:mm", @"h\:mm", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out value))
            return value.TotalMinutes >= 0 && value.TotalMinutes < 1440;
        if (DateTime.TryParse(box.Text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
        {
            value = date.TimeOfDay;
            return value.TotalMinutes >= 0 && value.TotalMinutes < 1440;
        }
        value = default;
        return false;
    }
}
