namespace TicketCompletionTimeline.Core;

public static class FilterEngine
{
    /// <summary>
    /// Applies record-level filters before metrics are calculated. Keeping this
    /// step separate means totals, charts, and evidence all see the same session
    /// slice instead of each view quietly applying different rules.
    /// </summary>
    public static IReadOnlyList<CompletionRecord> FilterRecords(
        IEnumerable<CompletionRecord> records,
        CompletionFilters filters,
        GapThresholds settings)
    {
        var selected = filters.AssignedUsers;
        return records
            .Where(record => selected is not { Count: > 0 } || selected.Contains(record.AssignedUser, StringComparer.OrdinalIgnoreCase))
            .Where(record => !filters.HasTeamFilter || string.Equals(TeamName(record), filters.AssignedTeamName!.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(record => filters.WorkHours switch
            {
                WorkHourFilter.WorkHours => settings.IsWithinWorkHours(record.Completion),
                WorkHourFilter.OnCall => !settings.IsWithinWorkHours(record.Completion),
                _ => true
            })
            .ToList();
    }

    public static string TeamName(CompletionRecord record)
    {
        foreach (var pair in record.SourceValues)
        {
            if (string.Equals(pair.Key, "Assigned User's Team Name", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pair.Key, "Assigned User Team Name", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pair.Key, "Assigned User Team", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(pair.Value) ? "No team" : pair.Value.Trim();
            }
        }

        return "No team";
    }

    public static bool MatchesUser(
        UserDayMetrics metrics,
        UserReviewPriority? priority,
        CompletionFilters filters)
    {
        return MatchesCommon(metrics.AssignedUser, metrics.Total, metrics.LongestGapBand, metrics.Total > 0 ? 1 : 0, priority, filters);
    }

    public static bool MatchesUser(
        PeriodUserMetrics metrics,
        UserReviewPriority? priority,
        CompletionFilters filters)
    {
        return MatchesCommon(metrics.AssignedUser, metrics.Total, metrics.AverageLongestGapBand, metrics.ActiveDays, priority, filters);
    }

    private static bool MatchesCommon(
        string user,
        int total,
        GapBand gapBand,
        int activeDays,
        UserReviewPriority? priority,
        CompletionFilters filters)
    {
        if (filters.MinimumCompletions.HasValue && total < filters.MinimumCompletions.Value) return false;
        if (filters.MaximumCompletions.HasValue && total > filters.MaximumCompletions.Value) return false;
        if (filters.LongestGapBand != GapBand.None && gapBand != filters.LongestGapBand) return false;
        if (filters.MinimumActiveDays.HasValue && activeDays < filters.MinimumActiveDays.Value) return false;
        if (filters.MaximumActiveDays.HasValue && activeDays > filters.MaximumActiveDays.Value) return false;
        if (filters.ReviewPriority != ReviewPriorityBand.Any && priority?.Band != filters.ReviewPriority) return false;
        return true;
    }
}
