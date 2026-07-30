using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using TicketCompletionTimeline.Core;

namespace TicketCompletionTimeline.Tests;

public sealed class CoreTests
{
    [Fact]
    public void ParsesQuotedMultilineFieldsAndRequiredColumns()
    {
        const string csv = "ID,Assigned User,Last Completion,Notes\n1,Alice,2026-07-29 06:57:36,\"line one\nline two\"\n";
        var result = new CsvImportService().Parse(new StringReader(csv));
        Assert.Single(result.ValidRows);
        Assert.Equal("line one\nline two", result.ValidRows[0].SourceValues["Notes"]);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void UsesExplicitEasternTimeForOffsetlessSourceValues()
    {
        const string csv = "ID,Assigned User,Last Completion\n1,Alice,2026-07-29 06:57:36\n";
        var row = Assert.Single(new CsvImportService().Parse(new StringReader(csv)).ValidRows);
        Assert.Equal(new DateOnly(2026, 7, 29), DateOnly.FromDateTime(row.Completion.DateTime));
        Assert.Equal(TimeSpan.FromHours(-4), row.Completion.Offset);
    }

    [Fact]
    public void CalculatesAverageLongestGapFromUsersNotCombinedStream()
    {
        var importer = new CsvImportService();
        const string csv = "ID,Assigned User,Last Completion\n1,Alice,2026-07-29 08:00:00\n2,Alice,2026-07-29 09:00:00\n3,Alice,2026-07-29 10:00:00\n4,Bob,2026-07-29 08:30:00\n5,Bob,2026-07-29 11:30:00\n";
        var rows = importer.Parse(new StringReader(csv)).ValidRows;
        var day = new MetricsCalculator().Calculate(rows, new DateOnly(2026, 7, 29), GapThresholds.Default);
        Assert.Equal(60, day.Users.Single(user => user.AssignedUser == "Alice").LongestGapMinutes);
        Assert.Equal(180, day.Users.Single(user => user.AssignedUser == "Bob").LongestGapMinutes);
        Assert.Equal(120, MetricsCalculator.AverageLongestGap(day.Users));
    }

    [Fact]
    public void SingleCompletionHasNoLongestGap()
    {
        const string csv = "ID,Assigned User,Last Completion\n1,Alice,2026-07-29 08:00:00\n";
        var rows = new CsvImportService().Parse(new StringReader(csv)).ValidRows;
        var day = new MetricsCalculator().Calculate(rows, new DateOnly(2026, 7, 29), GapThresholds.Default);
        Assert.Null(day.Users.Single().LongestGapMinutes);
        Assert.Null(MetricsCalculator.AverageLongestGap(day.Users));
    }

    [Fact]
    public void UpdateManifestRequiresHttpsAndSha256()
    {
        Assert.Throws<FormatException>(() => UpdateService.ParseManifest("{\"version\":\"1.0.1\",\"downloadUrl\":\"http://example.test/update.zip\",\"sha256\":\"" + new string('A', 64) + "\"}"));
        Assert.Throws<FormatException>(() => UpdateService.ParseManifest("{\"version\":\"1.0.1\",\"downloadUrl\":\"https://example.test/update.zip\",\"sha256\":\"not-a-hash\"}"));
    }

    [Fact]
    public async Task UpdateCheckReportsOnlyNewerVersions()
    {
        var hash = new string('A', 64);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"version\":\"1.0.1\",\"downloadUrl\":\"https://example.test/update.zip\",\"sha256\":\"" + hash + "\"}")
        });
        var result = await new UpdateService(new HttpClient(handler)).CheckAsync(new Uri("https://example.test/manifest.json"), new Version(1, 0, 0));
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(new Version(1, 0, 1), result.AvailableVersion);
    }

    [Fact]
    public async Task UpdateDownloadRejectsChecksumMismatch()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("not the expected package"))
        });
        var manifest = new UpdateManifest("1.0.1", "https://example.test/update.zip", new string('B', 64), "test");
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateService(new HttpClient(handler)).DownloadAndVerifyAsync(manifest));
    }

    [Fact]
    public void PreservesRepeatedIdsWithinOneImport()
    {
        const string csv = "ID,Assigned User,Last Completion\nA,Alice,2026-07-29 08:00:00\nA,Alice,2026-07-29 09:00:00\n";
        var rows = new CsvImportService().Parse(new StringReader(csv)).ValidRows;
        Assert.Equal(2, rows.Count);
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }

    [Fact]
    public void ChangedExistingIdProducesConflictAndCancelPreservesSession()
    {
        const string original = "ID,Assigned User,Last Completion\nA,Alice,2026-07-29 08:00:00\n";
        const string changed = "ID,Assigned User,Last Completion\nA,Bob,2026-07-29 08:00:00\n";
        var service = new SessionMergeService();
        var existing = new CsvImportService().Parse(new StringReader(original)).ValidRows;
        var incoming = new CsvImportService().Parse(new StringReader(changed)).ValidRows;
        var result = service.Stage(existing, incoming);
        Assert.Single(result.Conflicts);
        Assert.True(result.WasCancelled);
        Assert.Equal("Alice", Assert.Single(result.Records).AssignedUser);
    }

    [Fact]
    public void ThresholdsClassifyGapBands()
    {
        var thresholds = new GapThresholds(60, 120);
        Assert.Equal(GapBand.Green, thresholds.GetBand(59.9));
        Assert.Equal(GapBand.Amber, thresholds.GetBand(120));
        Assert.Equal(GapBand.Red, thresholds.GetBand(120.1));
    }

    [Fact]
    public void CalculatesWeeklyAveragesAcrossActiveDays()
    {
        const string csv = "ID,Assigned User,Last Completion\n" +
                           "1,Alice,2026-07-27 08:00:00\n" +
                           "2,Alice,2026-07-27 10:00:00\n" +
                           "3,Alice,2026-07-28 09:00:00\n" +
                           "4,Bob,2026-07-28 11:00:00\n";
        var rows = new CsvImportService().Parse(new StringReader(csv)).ValidRows;
        var week = new MetricsCalculator().CalculatePeriod(rows, ReportPeriodKind.Week, new DateOnly(2026, 7, 27), GapThresholds.Default);

        Assert.Equal(2, week.ActiveDays);
        Assert.Equal(4, week.AllUsers.Total);
        Assert.Equal(2.0, week.AllUsers.AverageDailyTotal);
        Assert.Equal(3, week.Users.Single(user => user.AssignedUser == "Alice").Total);
        Assert.Equal(1.5, week.Users.Single(user => user.AssignedUser == "Alice").AverageDailyTotal);
        Assert.Equal(TimeSpan.FromMinutes(8 * 60 + 30), week.Users.Single(user => user.AssignedUser == "Alice").AverageFirstTime);
    }

    [Fact]
    public void CalculatesMonthlyPeriodAndExcludesEmptyDaysFromAverages()
    {
        const string csv = "ID,Assigned User,Last Completion\n" +
                           "1,Alice,2026-07-01 08:00:00\n" +
                           "2,Alice,2026-07-15 10:00:00\n";
        var rows = new CsvImportService().Parse(new StringReader(csv)).ValidRows;
        var month = new MetricsCalculator().CalculatePeriod(rows, ReportPeriodKind.Month, new DateOnly(2026, 7, 1), GapThresholds.Default);

        Assert.Equal(new DateOnly(2026, 7, 31), month.EndDate);
        Assert.Equal(2, month.ActiveDays);
        Assert.Equal(1.0, month.Users.Single().AverageDailyTotal);
    }

    [Fact]
    public void ProducesEvidenceBasedPeriodReviewSignals()
    {
        const string csv = "ID,Assigned User,Last Completion\n" +
                           "1,Alice,2026-07-27 08:00:00\n" +
                           "2,Alice,2026-07-27 11:30:00\n" +
                           "3,Alice,2026-07-27 17:00:00\n" +
                           "4,Alice,2026-07-27 17:05:00\n" +
                           "5,Alice,2026-07-27 17:10:00\n" +
                           "6,Alice,2026-07-27 17:20:00\n" +
                           "7,Alice,2026-07-28 19:00:00\n";
        var rows = new CsvImportService().Parse(new StringReader(csv)).ValidRows;
        var calculator = new MetricsCalculator();
        var week = calculator.CalculatePeriod(rows, ReportPeriodKind.Week, new DateOnly(2026, 7, 27), GapThresholds.Default);
        var signals = calculator.CalculateReviewSignals(rows, week, GapThresholds.Default);

        Assert.Contains(signals, signal => signal.Kind == ReviewSignalKind.LongIdleGap);
        Assert.Contains(signals, signal => signal.Kind == ReviewSignalKind.DenseCompletionBurst);
        Assert.Contains(signals, signal => signal.Kind == ReviewSignalKind.EndOfShiftBatch);
        Assert.DoesNotContain(signals, signal => signal.Kind == ReviewSignalKind.AfterHoursCompletion);
    }

    [Fact]
    public void ReviewSignalsDefaultOffAndPersistAsASetting()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ticket-timeline-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            Assert.False(store.Load().ShowReviewSignals);
            store.Save(new GapThresholds(60, 120, true, 480, 1020, true));
            var saved = store.Load();
            Assert.True(saved.ShowReviewSignals);
            Assert.True(saved.ShowReviewPriority);
            Assert.Equal(480, saved.WorkdayStartMinutes);
            Assert.Equal(1020, saved.WorkdayEndMinutes);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WorkdayBoundariesTreatEverythingOutsideAsOnCall()
    {
        var settings = new GapThresholds(60, 120, false, 480, 1020);
        Assert.False(settings.IsWithinWorkHours(new DateTimeOffset(2026, 7, 29, 7, 59, 0, TimeSpan.FromHours(-4))));
        Assert.True(settings.IsWithinWorkHours(new DateTimeOffset(2026, 7, 29, 8, 0, 0, TimeSpan.FromHours(-4))));
        Assert.True(settings.IsWithinWorkHours(new DateTimeOffset(2026, 7, 29, 16, 59, 0, TimeSpan.FromHours(-4))));
        Assert.False(settings.IsWithinWorkHours(new DateTimeOffset(2026, 7, 29, 17, 0, 0, TimeSpan.FromHours(-4))));
    }

    [Fact]
    public void FiltersCanSelectUsersAndWorkHourStatus()
    {
        const string csv = "ID,Assigned User,Last Completion\n" +
                           "1,Alice,2026-07-29 08:00:00\n" +
                           "2,Alice,2026-07-29 19:00:00\n" +
                           "3,Bob,2026-07-29 09:00:00\n";
        var rows = new CsvImportService().Parse(new StringReader(csv)).ValidRows;
        var filters = new CompletionFilters(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Alice" }, WorkHours: WorkHourFilter.WorkHours);
        var result = FilterEngine.FilterRecords(rows, filters, GapThresholds.Default);
        Assert.Single(result);
        Assert.Equal("Alice", result[0].AssignedUser);
    }

    [Fact]
    public void ReviewPriorityUsesEvidenceAndNeverScoresOnCallEvents()
    {
        const string csv = "ID,Assigned User,Last Completion\n" +
                           "1,Alice,2026-07-27 08:00:00\n" +
                           "2,Alice,2026-07-27 11:30:00\n" +
                           "3,Alice,2026-07-27 17:00:00\n" +
                           "4,Alice,2026-07-27 17:05:00\n" +
                           "5,Alice,2026-07-27 17:10:00\n" +
                           "6,Alice,2026-07-28 08:00:00\n" +
                           "7,Alice,2026-07-28 17:00:00\n" +
                           "8,Alice,2026-07-29 08:00:00\n" +
                           "9,Alice,2026-07-29 17:00:00\n" +
                           "10,Alice,2026-07-29 17:05:00\n" +
                           "11,Alice,2026-07-29 19:00:00\n";
        var rows = new CsvImportService().Parse(new StringReader(csv)).ValidRows;
        var settings = GapThresholds.Default with { ShowReviewPriority = true };
        var priority = new MetricsCalculator().CalculateReviewPriorities(rows, new DateOnly(2026, 7, 27), new DateOnly(2026, 7, 29), settings)["Alice"];
        Assert.Equal(ReviewPriorityBand.Moderate, priority.Band);
        Assert.Contains(priority.Evidence, item => item.Title == "Longest work-hour gap");
        Assert.Contains(priority.Evidence, item => item.Title == "Dense completion burst");
        Assert.Contains(priority.Evidence, item => item.Title == "End-of-shift batch");
        Assert.DoesNotContain(priority.Evidence, item => item.Detail.Contains("19:00", StringComparison.Ordinal));
    }

    [Fact]
    public void ReviewPriorityRequiresMinimumHistory()
    {
        const string csv = "ID,Assigned User,Last Completion\n1,Alice,2026-07-29 08:00:00\n";
        var rows = new CsvImportService().Parse(new StringReader(csv)).ValidRows;
        var priority = new MetricsCalculator().CalculateReviewPriorities(rows, new DateOnly(2026, 7, 29), new DateOnly(2026, 7, 29), GapThresholds.Default)["Alice"];
        Assert.Equal(ReviewPriorityBand.InsufficientData, priority.Band);
        Assert.Empty(priority.Evidence);
    }

    [Fact]
    public void LocalArchiveRoundTripsOriginalRowsAndRecoversFromBackup()
    {
        const string csv = "ID,Assigned User,Last Completion,Notes\n" +
                           "A,Alice,2026-07-29 08:00:00,first note\n";
        var rows = new CsvImportService().Parse(new StringReader(csv)).ValidRows;
        var path = Path.Combine(Path.GetTempPath(), $"ticket-timeline-archive-{Guid.NewGuid():N}.json");
        try
        {
            var store = new LocalArchiveStore(path);
            var batch = new ArchiveImportBatch("first.csv", DateTimeOffset.UtcNow, rows.Count, 0);
            store.Save(rows, [batch]);
            var first = store.Load();
            Assert.Single(first.Records);
            Assert.Equal("first note", first.Records[0].SourceValues["Notes"]);
            Assert.Single(first.Imports);

            store.Save([], []);
            File.WriteAllText(path, "not valid json");
            var recovered = store.Load();
            Assert.Single(recovered.Records);
            Assert.Equal("first note", recovered.Records[0].SourceValues["Notes"]);
        }
        finally
        {
            foreach (var file in new[] { path, path + ".bak", path + ".tmp" })
                if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void SuppliedCsvMatchesKnownBaselineWhenProvided()
    {
        var path = Environment.GetEnvironmentVariable("TICKET_TIMELINE_SAMPLE");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var import = new CsvImportService().Parse(path);
        Assert.Equal(164, import.ValidRows.Count);
        Assert.Empty(import.Warnings);
        var day = new MetricsCalculator().Calculate(import.ValidRows, new DateOnly(2026, 7, 29), GapThresholds.Default);
        Assert.Equal(12, day.Users.Count);
        Assert.Equal(164, day.AllUsers.Total);
        Assert.Equal("6:57:36 AM", day.AllUsers.First!.Value.ToString("h:mm:ss tt"));
        Assert.Equal("5:46:45 PM", day.AllUsers.Last!.Value.ToString("h:mm:ss tt"));
        Assert.InRange(MetricsCalculator.AverageLongestGap(day.Users)!.Value, 159.0, 159.2);
    }
}
