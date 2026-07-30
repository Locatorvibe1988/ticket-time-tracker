namespace TicketCompletionTimeline.Core;

public sealed class SessionMergeService
{
    /// <summary>
    /// Stages an additive import and reports conflicts before the caller commits
    /// anything to the archive. Repeated IDs within the incoming file remain
    /// separate rows; only differences against the existing session conflict.
    /// </summary>
    public MergeResult Stage(IEnumerable<CompletionRecord> current, IEnumerable<CompletionRecord> incoming, ConflictResolution resolution = ConflictResolution.CancelImport)
    {
        var currentList = current.ToList();
        var incomingList = incoming.ToList();
        var keyed = currentList.Where(record => record.TicketKey is not null).GroupBy(record => record.TicketKey!, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<ImportConflict>();
        foreach (var row in incomingList.Where(record => record.TicketKey is not null))
        {
            if (!keyed.TryGetValue(row.TicketKey!, out var matches)) continue;
            var changed = matches.FirstOrDefault(existing => !string.Equals(existing.Fingerprint, row.Fingerprint, StringComparison.Ordinal));
            if (changed is not null) conflicts.Add(new(row.TicketKey!, changed, row));
        }
        if (conflicts.Count > 0 && resolution == ConflictResolution.CancelImport) return new(currentList, conflicts, true);
        if (resolution == ConflictResolution.ClearCurrentDataAndImport) return new(incomingList, conflicts, false);
        if (resolution == ConflictResolution.OverwriteConflicts)
        {
            var conflictKeys = conflicts.Select(conflict => conflict.TicketKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            currentList.RemoveAll(row => row.TicketKey is not null && conflictKeys.Contains(row.TicketKey));
        }
        currentList.AddRange(incomingList);
        return new(currentList, conflicts, false);
    }
}
