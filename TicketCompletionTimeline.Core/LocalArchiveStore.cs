using System.Text.Json;

namespace TicketCompletionTimeline.Core;

public sealed record ArchiveImportBatch(
    string SourceFile,
    DateTimeOffset ImportedAt,
    int ValidRows,
    int RejectedRows);

public sealed record ArchiveSnapshot(
    IReadOnlyList<CompletionRecord> Records,
    IReadOnlyList<ArchiveImportBatch> Imports)
{
    public static ArchiveSnapshot Empty { get; } = new([], []);
}

public sealed class LocalArchiveStore
{
    private readonly string _path;
    private readonly string _backupPath;
    private readonly string _temporaryPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string Path => _path;

    public LocalArchiveStore(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TicketCompletionTimeline",
            "archive.json");
        _backupPath = _path + ".bak";
        _temporaryPath = _path + ".tmp";
    }

    public ArchiveSnapshot Load()
    {
        // The primary file is preferred. A failed or interrupted write falls
        // back to the previous backup instead of opening the dashboard empty.
        if (TryLoad(_path, out var snapshot)) return snapshot;
        if (TryLoad(_backupPath, out snapshot)) return snapshot;
        return ArchiveSnapshot.Empty;
    }

    public void Save(IEnumerable<CompletionRecord> records, IEnumerable<ArchiveImportBatch> imports)
    {
        var snapshot = new ArchiveSnapshot(records.ToList(), imports.ToList());
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("The archive path has no directory.");
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
        // Write the complete JSON before touching the current archive. The move
        // is the commit point; the temporary file is removed on every exit path.
        File.WriteAllText(_temporaryPath, json);
        try
        {
            if (File.Exists(_path)) File.Copy(_path, _backupPath, overwrite: true);
            File.Move(_temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(_temporaryPath)) File.Delete(_temporaryPath);
        }
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
        if (File.Exists(_backupPath)) File.Delete(_backupPath);
        if (File.Exists(_temporaryPath)) File.Delete(_temporaryPath);
    }

    private bool TryLoad(string path, out ArchiveSnapshot snapshot)
    {
        snapshot = ArchiveSnapshot.Empty;
        try
        {
            if (!File.Exists(path)) return false;
            snapshot = JsonSerializer.Deserialize<ArchiveSnapshot>(File.ReadAllText(path), _jsonOptions) ?? ArchiveSnapshot.Empty;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
