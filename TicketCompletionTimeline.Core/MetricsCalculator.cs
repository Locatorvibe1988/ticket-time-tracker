namespace TicketCompletionTimeline.Core;

/// <summary>
/// Calculates the dashboard's daily, period, and evidence-based review metrics.
/// The class deliberately keeps aggregation rules in one place so the WPF layer
/// only formats results and does not make business decisions.
/// </summary>
public sealed class MetricsCalculator
{
    /// <summary>
    /// Builds one daily result for the selected date. User events are sorted once
    /// here so first, last, gap, bucket, and drill-down views use the same order.
    /// </summary>
    public DayMetrics Calculate(IEnumerable<CompletionRecord> records, DateOnly date, GapThresholds thresholds)
    {
        var dayRecords = records.Where(record => DateOnly.FromDateTime(record.Completion.DateTime) == date).ToList();
        var users = dayRecords
            .GroupBy(record => record.AssignedUser, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildUser(group.Key, group, thresholds))
            .ToList();
        return new(date, users, BuildUser("All Users", dayRecords, thresholds, aggregate: true));
    }

    private static UserDayMetrics BuildUser(string name, IEnumerable<CompletionRecord> source, GapThresholds thresholds, bool aggregate = false)
    {
        var events = source.OrderBy(record => record.Completion).ThenBy(record => record.SourceRowNumber).ToList();
        var first = events.FirstOrDefault()?.Completion;
        var last = events.LastOrDefault()?.Completion;
        double? longest = null;
        if (!aggregate && events.Count > 1)
        {
            // The All Users row must not calculate a gap from a combined event
            // stream. Its gap is supplied by AverageLongestGap at the caller.
            var maximum = 0d;
            for (var index = 1; index < events.Count; index++)
                maximum = Math.Max(maximum, (events[index].Completion - events[index - 1].Completion).TotalMinutes);
            longest = maximum;
        }
        var buckets = events
            .GroupBy(record => Math.Clamp((record.Completion.Hour * 60 + record.Completion.Minute) / 15, 0, 95))
            .OrderBy(group => group.Key)
            .Select(group => new CompletionBucket(group.Key, group.ToList()))
            .ToList();
        return new(name, events, first, last, longest, thresholds.GetBand(longest), buckets);
    }

    public static double? AverageLongestGap(IEnumerable<UserDayMetrics> users)
    {
        var total = 0d;
        var count = 0;
        foreach (var user in users)
        {
            if (!user.LongestGapMinutes.HasValue) continue;
            total += user.LongestGapMinutes.Value;
            count++;
        }
        return count == 0 ? null : total / count;
    }

    /// <summary>
    /// Calculates weekly or monthly metrics from active dates in the selected
    /// calendar period. Empty dates remain part of the period but do not inflate
    /// the average-per-active-day values.
    /// </summary>
    public PeriodMetrics CalculatePeriod(IEnumerable<CompletionRecord> records, ReportPeriodKind kind, DateOnly startDate, GapThresholds thresholds)
    {
        var endDate = kind == ReportPeriodKind.Week ? startDate.AddDays(6) : startDate.AddMonths(1).AddDays(-1);
        var periodRecords = records
            .Where(record =>
            {
                var date = DateOnly.FromDateTime(record.Completion.DateTime);
                return date >= startDate && date <= endDate;
            })
            .ToList();

        var activeDays = periodRecords
            .Select(record => DateOnly.FromDateTime(record.Completion.DateTime))
            .Distinct()
            .Count();
        var users = periodRecords
            .GroupBy(record => record.AssignedUser, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildPeriodUser(group.Key, group, thresholds))
            .ToList();
        var allUsers = BuildAggregatePeriodUser(periodRecords, users, activeDays, thresholds);
        return new(kind, startDate, endDate, activeDays, users, allUsers);
    }

    public IReadOnlyDictionary<string, UserReviewPriority> CalculateReviewPriorities(
        IEnumerable<CompletionRecord> records,
        DateOnly startDate,
        DateOnly endDate,
        GapThresholds settings)
    {
        var allRecords = records.ToList();
        var periodRecords = allRecords
            .Where(record =>
            {
                var date = DateOnly.FromDateTime(record.Completion.DateTime);
                return date >= startDate && date <= endDate;
            })
            .ToList();
        var workRecords = periodRecords.Where(record => settings.IsWithinWorkHours(record.Completion)).ToList();
        var globalActiveDays = workRecords.Select(record => DateOnly.FromDateTime(record.Completion.DateTime)).Distinct().Count();
        var globalAverageDaily = globalActiveDays == 0 ? 0 : workRecords.Count / (double)globalActiveDays;
        var results = new Dictionary<string, UserReviewPriority>(StringComparer.OrdinalIgnoreCase);

        foreach (var userGroup in periodRecords.GroupBy(record => record.AssignedUser, StringComparer.OrdinalIgnoreCase))
        {
            var user = userGroup.Key;
            var userWork = userGroup.Where(record => settings.IsWithinWorkHours(record.Completion)).ToList();
            var activeDays = userWork.Select(record => DateOnly.FromDateTime(record.Completion.DateTime)).Distinct().Count();
            var evidence = new List<ReviewPriorityEvidence>();

            if (userWork.Count >= settings.ReviewMinCompletions && activeDays >= settings.ReviewMinActiveDays)
            {
                AddLongestGapEvidence(user, userWork, settings, evidence);
                AddDenseBurstEvidence(user, userWork, settings, evidence);
                AddEndOfShiftEvidence(user, userWork, settings, evidence);
                AddBaselineEvidence(user, userWork, allRecords, startDate, settings, evidence);

                if (globalActiveDays >= 5 && globalActiveDays > 0 && activeDays / (double)globalActiveDays < settings.LowConsistencyRatio)
                {
                    evidence.Add(new(
                        "Low active-day consistency",
                        $"Active on {activeDays} of {globalActiveDays} observed workday(s) ({activeDays / (double)globalActiveDays:P0})",
                        userWork.Min(record => DateOnly.FromDateTime(record.Completion.DateTime)),
                        settings.LowConsistencyWeight));
                }

                var userAverage = userWork.Count / (double)activeDays;
                if (globalAverageDaily > 0 && (userAverage >= globalAverageDaily * (1 + settings.VolumeOutlierRatio) || userAverage <= globalAverageDaily * settings.VolumeOutlierRatio))
                {
                    var direction = userAverage >= globalAverageDaily ? "above" : "below";
                    evidence.Add(new(
                        $"Volume outlier — {direction} average",
                        $"Average {userAverage:0.0} work-hour completions/day versus filtered global average {globalAverageDaily:0.0}",
                        userWork.Min(record => DateOnly.FromDateTime(record.Completion.DateTime)),
                        settings.VolumeOutlierWeight));
                }
            }

            var points = evidence.Sum(item => item.Points);
            var band = userWork.Count < settings.ReviewMinCompletions || activeDays < settings.ReviewMinActiveDays
                ? ReviewPriorityBand.InsufficientData
                : points >= settings.HighPriorityThreshold
                    ? ReviewPriorityBand.High
                    : points >= settings.ModeratePriorityThreshold
                        ? ReviewPriorityBand.Moderate
                        : ReviewPriorityBand.Low;
            results[user] = new(user, band, points, userGroup.Count(), activeDays, evidence);
        }

        return results;
    }

    public IReadOnlyList<ReviewSignal> CalculateReviewSignals(
        IEnumerable<CompletionRecord> records,
        PeriodMetrics period,
        GapThresholds thresholds)
    {
        var allRecords = records.ToList();
        var periodEvents = period.AllUsers.Events;
        var signals = new List<ReviewSignal>();

        foreach (var userGroup in periodEvents.GroupBy(record => record.AssignedUser, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var dayGroup in userGroup
                         .Where(record => thresholds.IsWithinWorkHours(record.Completion))
                         .GroupBy(record => DateOnly.FromDateTime(record.Completion.DateTime))
                         .OrderBy(group => group.Key))
            {
                var day = BuildUser(userGroup.Key, dayGroup, thresholds);
                if (day.LongestGapMinutes is > 0 and var gap && gap > thresholds.RedAboveMinutes)
                {
                    signals.Add(new(
                        ReviewSignalKind.LongIdleGap,
                        userGroup.Key,
                        dayGroup.Key,
                        "Long idle gap",
                        $"{gap:0.0} minutes between consecutive work-hour completions",
                        GapBand.Red));
                }

                var burst = day.Events
                    .GroupBy(eventRecord => Math.Clamp((eventRecord.Completion.Hour * 60 + eventRecord.Completion.Minute) / 15, 0, 95))
                    .OrderByDescending(group => group.Count())
                    .FirstOrDefault();
                if (burst is not null && burst.Count() >= 3)
                {
                    signals.Add(new(
                        ReviewSignalKind.DenseCompletionBurst,
                        userGroup.Key,
                        dayGroup.Key,
                        "Dense completion burst",
                        $"{burst.Count()} work-hour completions in one 15-minute interval",
                        GapBand.Amber));
                }

                var endOfShift = day.Events.Where(eventRecord => IsInEndOfShiftWindow(eventRecord, thresholds)).ToList();
                if (endOfShift.Count >= 3)
                {
                    signals.Add(new(
                        ReviewSignalKind.EndOfShiftBatch,
                        userGroup.Key,
                        dayGroup.Key,
                        "End-of-shift batch",
                        $"{endOfShift.Count} work-hour completions in the final 90 minutes of the configured workday",
                        GapBand.Amber));
                }
            }

            AddBaselineSignal(allRecords, period, userGroup.Key, thresholds, signals);
        }

        return signals
            .OrderByDescending(signal => signal.Band)
            .ThenBy(signal => signal.Date)
            .ThenBy(signal => signal.AssignedUser, StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToList();
    }

    private static void AddLongestGapEvidence(string user, IReadOnlyList<CompletionRecord> events, GapThresholds settings, ICollection<ReviewPriorityEvidence> evidence)
    {
        var longest = events
            .GroupBy(record => DateOnly.FromDateTime(record.Completion.DateTime))
            .Select(day => (Date: day.Key, Metrics: BuildUser(user, day, settings)))
            .Where(item => item.Metrics.LongestGapMinutes.HasValue)
            .OrderByDescending(item => item.Metrics.LongestGapMinutes)
            .FirstOrDefault();
        if (longest.Metrics is null || longest.Metrics.LongestGapMinutes is not > 0) return;
        var band = settings.GetBand(longest.Metrics.LongestGapMinutes);
        if (band == GapBand.None) return;
        var points = band == GapBand.Red ? settings.LongGapWeight : band == GapBand.Amber ? Math.Max(1, settings.LongGapWeight / 2) : 0;
        if (points == 0) return;
        evidence.Add(new(
            "Longest work-hour gap",
            $"{longest.Metrics.LongestGapMinutes.Value:0.0} minutes on {longest.Date:MMM d, yyyy} ({band} gap band)",
            longest.Date,
            points));
    }

    private static void AddDenseBurstEvidence(string user, IReadOnlyList<CompletionRecord> events, GapThresholds settings, ICollection<ReviewPriorityEvidence> evidence)
    {
        var burst = events
            .GroupBy(record => (Date: DateOnly.FromDateTime(record.Completion.DateTime), Quarter: (record.Completion.Hour * 60 + record.Completion.Minute) / 15))
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();
        if (burst is not null && burst.Count() >= 3)
            evidence.Add(new("Dense completion burst", $"{burst.Count()} work-hour completions in one 15-minute interval", burst.Key.Date, settings.DenseBurstWeight));
    }

    private static void AddEndOfShiftEvidence(string user, IReadOnlyList<CompletionRecord> events, GapThresholds settings, ICollection<ReviewPriorityEvidence> evidence)
    {
        var batch = events
            .Where(record => IsInEndOfShiftWindow(record, settings))
            .GroupBy(record => DateOnly.FromDateTime(record.Completion.DateTime))
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();
        if (batch is not null && batch.Count() >= 3)
            evidence.Add(new("End-of-shift batch", $"{batch.Count()} completions in the final 90 minutes of the configured workday", batch.Key, settings.EndOfShiftBatchWeight));
    }

    private static void AddBaselineEvidence(string user, IReadOnlyList<CompletionRecord> currentEvents, IReadOnlyList<CompletionRecord> allRecords, DateOnly startDate, GapThresholds settings, ICollection<ReviewPriorityEvidence> evidence)
    {
        var baseline = allRecords
            .Where(record => string.Equals(record.AssignedUser, user, StringComparison.OrdinalIgnoreCase))
            .Where(record =>
            {
                var date = DateOnly.FromDateTime(record.Completion.DateTime);
                return date < startDate && date >= startDate.AddDays(-30) && settings.IsWithinWorkHours(record.Completion);
            })
            .GroupBy(record => DateOnly.FromDateTime(record.Completion.DateTime))
            .Select(group => group.Count())
            .ToArray();
        if (baseline.Length < 3) return;
        var currentActiveDays = currentEvents.Select(record => DateOnly.FromDateTime(record.Completion.DateTime)).Distinct().Count();
        if (currentActiveDays == 0) return;
        var ratio = currentEvents.Count / (double)currentActiveDays / baseline.Average();
        if (ratio >= 1.75 || ratio <= 0.5)
        {
            var direction = ratio >= 1.75 ? "above" : "below";
            evidence.Add(new("Change from baseline", $"Average work-hour volume is {ratio:0.0}× the prior 30-day baseline ({direction} baseline)", startDate, settings.BaselineChangeWeight));
        }
    }

    private static bool IsInEndOfShiftWindow(CompletionRecord record, GapThresholds settings)
    {
        if (!settings.IsWithinWorkHours(record.Completion) || settings.WorkdayStartMinutes >= settings.WorkdayEndMinutes) return false;
        var minutes = record.Completion.TimeOfDay.TotalMinutes;
        return minutes >= settings.WorkdayEndMinutes - 90 && minutes < settings.WorkdayEndMinutes;
    }

    private static PeriodUserMetrics BuildPeriodUser(string name, IEnumerable<CompletionRecord> source, GapThresholds thresholds)
    {
        var events = source.OrderBy(record => record.Completion).ThenBy(record => record.SourceRowNumber).ToList();
        var daily = events
            .GroupBy(record => DateOnly.FromDateTime(record.Completion.DateTime))
            .Select(group => BuildUser(name, group, thresholds))
            .ToList();
        var averageGap = AverageLongestGap(daily);
        return new(
            name,
            events.Count,
            daily.Count == 0 ? 0 : events.Count / (double)daily.Count,
            AverageTimeOfDay(daily.Select(day => day.First)),
            AverageTimeOfDay(daily.Select(day => day.Last)),
            averageGap,
            thresholds.GetBand(averageGap),
            events,
            daily.Count);
    }

    private static PeriodUserMetrics BuildAggregatePeriodUser(IReadOnlyList<CompletionRecord> events, IReadOnlyList<PeriodUserMetrics> users, int activeDays, GapThresholds thresholds)
    {
        // All Users uses the average of each user's period gap. Calculating a
        // gap from the combined stream would measure handoff time between users,
        // which is not the metric this dashboard promises to show.
        var daily = events
            .GroupBy(record => DateOnly.FromDateTime(record.Completion.DateTime))
            .Select(group => BuildUser("All Users", group, thresholds, aggregate: true))
            .ToList();
        var averageGap = users.Select(user => user.AverageLongestGapMinutes).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        double? aggregateAverageGap = averageGap.Length == 0 ? null : averageGap.Average();
        return new(
            "All Users",
            events.Count,
            activeDays == 0 ? 0 : events.Count / (double)activeDays,
            AverageTimeOfDay(daily.Select(day => day.First)),
            AverageTimeOfDay(daily.Select(day => day.Last)),
            aggregateAverageGap,
            thresholds.GetBand(aggregateAverageGap),
            events.OrderBy(record => record.Completion).ThenBy(record => record.SourceRowNumber).ToList(),
            activeDays);
    }

    private static TimeSpan? AverageTimeOfDay(IEnumerable<DateTimeOffset?> values)
    {
        var minutes = values.Where(value => value.HasValue).Select(value => value!.Value.TimeOfDay.TotalMinutes).ToArray();
        return minutes.Length == 0 ? null : TimeSpan.FromMinutes(minutes.Average());
    }

    private static void AddBaselineSignal(IReadOnlyList<CompletionRecord> allRecords, PeriodMetrics period, string user, GapThresholds thresholds, ICollection<ReviewSignal> signals)
    {
        var beforeAndAfter = allRecords
            .Where(record =>
                string.Equals(record.AssignedUser, user, StringComparison.OrdinalIgnoreCase) &&
                (DateOnly.FromDateTime(record.Completion.DateTime) < period.StartDate || DateOnly.FromDateTime(record.Completion.DateTime) > period.EndDate) &&
                thresholds.IsWithinWorkHours(record.Completion))
            .GroupBy(record => DateOnly.FromDateTime(record.Completion.DateTime))
            .Select(group => group.Count())
            .ToArray();
        var current = period.Users.FirstOrDefault(candidate => string.Equals(candidate.AssignedUser, user, StringComparison.OrdinalIgnoreCase));
        if (current is null || current.ActiveDays < 2 || beforeAndAfter.Length < 3) return;
        var baseline = beforeAndAfter.Average();
        if (baseline == 0) return;
        var ratio = current.AverageDailyTotal / baseline;
        if (ratio >= 1.75 || ratio <= 0.5)
        {
            var direction = ratio >= 1.75 ? "above" : "below";
            signals.Add(new(
                ReviewSignalKind.ChangeFromBaseline,
                user,
                period.StartDate,
                "Change from baseline",
                $"Average daily volume is {ratio:0.0}× the prior observed baseline ({direction} baseline)",
                GapBand.Amber));
        }
    }
}
