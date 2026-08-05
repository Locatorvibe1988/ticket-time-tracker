using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using TicketCompletionTimeline.Core;

namespace TicketCompletionTimeline.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly MainWindowState _state = new();
    private readonly CsvImportService _importer = new();
    private readonly SessionMergeService _merger = new();
    private readonly MetricsCalculator _metrics = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly LocalArchiveStore _archiveStore = new();
    private readonly UpdateService _updateService = new();
    private List<CompletionRecord> _records = [];
    private readonly List<string> _sourceFiles = [];
    private List<ArchiveImportBatch> _archiveImports = [];
    private int _rejectedRows;
    private GapThresholds _thresholds;
    private bool _fullDayTimeline;
    private bool _uiReady;
    private string _selectedTeamName = "All teams";
    private CompletionFilters _filters = new();
    private UserRowView? _dailyAggregateRow;
    private PeriodRowView? _periodAggregateRow;
    private ReportViewKind _activeView = ReportViewKind.Daily;
    private PeriodChoice? _selectedWeek;
    private PeriodChoice? _selectedMonth;

    public ObservableCollection<DateChoice> AvailableDates => _state.AvailableDates;
    public ObservableCollection<PeriodChoice> AvailableWeeks { get; } = [];
    public ObservableCollection<PeriodChoice> AvailableMonths { get; } = [];
    public ObservableCollection<UserRowView> UserRows => _state.UserRows;
    public ObservableCollection<UserRowView> TimelineRows => _state.TimelineRows;
    public ObservableCollection<PeriodRowView> PeriodRows { get; } = [];
    public ObservableCollection<WeeklyHeatmapRowView> WeeklyHeatmapRows { get; } = [];
    public ObservableCollection<CalendarDayCellView> MonthlyCalendarCells { get; } = [];
    public ObservableCollection<TrendPointView> WeeklyTrendPoints { get; } = [];
    public ObservableCollection<TrendPointView> MonthlyTrendPoints { get; } = [];
    public ObservableCollection<ReviewSignalView> ReviewSignalRows { get; } = [];
    public ObservableCollection<FilterUserOption> FilterUsers { get; } = [];
    public ObservableCollection<string> AvailableTeams { get; } = [];
    public UserRowView? DailyAggregateRow => _dailyAggregateRow;
    public PeriodRowView? PeriodAggregateRow => _periodAggregateRow;
    public DateChoice? SelectedDate { get => _state.SelectedDate; set { _state.SelectedDate = value; OnPropertyChanged(); RefreshSelectedDate(); } }
    public PeriodChoice? SelectedWeek { get => _selectedWeek; set { _selectedWeek = value; OnPropertyChanged(); if (_activeView == ReportViewKind.Weekly) RefreshActiveView(); } }
    public PeriodChoice? SelectedMonth { get => _selectedMonth; set { _selectedMonth = value; OnPropertyChanged(); if (_activeView == ReportViewKind.Monthly) RefreshActiveView(); } }
    public string StatusText => _state.StatusText;
    public string WarningText { get => _state.WarningText; private set { _state.WarningText = value; OnPropertyChanged(); } }
    public bool HasData => _state.HasData;
    public int TimelineStartHour => _fullDayTimeline ? 0 : _thresholds.WorkdayStartMinutes / 60;
    public int TimelineEndHour => _fullDayTimeline ? 24 : (int)Math.Ceiling(_thresholds.WorkdayEndMinutes / 60.0);
    public double GreenGapThreshold => _thresholds.GreenBelowMinutes;
    public double RedGapThreshold => _thresholds.RedAboveMinutes;
    public bool HasActiveFilters => _filters.HasValue;
    public string SelectedTeamName
    {
        get => _selectedTeamName;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? "All teams" : value;
            if (string.Equals(_selectedTeamName, next, StringComparison.OrdinalIgnoreCase)) return;
            _selectedTeamName = next;
            OnPropertyChanged();
            ApplyFilterChanges();
        }
    }
    public ReportViewKind ActiveView => _activeView;
    public Visibility DailyViewVisibility => _activeView == ReportViewKind.Daily ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WeeklyViewVisibility => _activeView == ReportViewKind.Weekly ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MonthlyViewVisibility => _activeView == ReportViewKind.Monthly ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasWeeklyDataVisibility => AvailableWeeks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasMonthlyDataVisibility => AvailableMonths.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _thresholds = _settingsStore.Load();
        CdcPalette.Configure(_thresholds.EffectiveCdcColors);
        var preferences = _settingsStore.LoadPreferences();
        _selectedTeamName = preferences.AssignedTeamName ?? "All teams";
        _fullDayTimeline = preferences.FullDayTimeline;
        CollectionViewSource.GetDefaultView(UserRows).GroupDescriptions.Add(new PropertyGroupDescription(nameof(UserRowView.AssignedTeamName)));
        var archive = _archiveStore.Load();
        _records = archive.Records.ToList();
        _archiveImports = archive.Imports.ToList();
        _sourceFiles.AddRange(_archiveImports.Select(batch => batch.SourceFile).Distinct(StringComparer.OrdinalIgnoreCase));
        _rejectedRows = _archiveImports.Sum(batch => batch.RejectedRows);
        RefreshFilterUsers();
        _uiReady = true;
        _filters = ReadFilters();
        OnPropertyChanged(nameof(SelectedTeamName));
        UpdateActiveViewButton();
        if (_records.Count > 0)
        {
            RefreshDates();
            UpdateStatus();
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            foreach (var path in paths.Where(path => string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))) ImportFile(path);
    }

    private void AddCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) == true) foreach (var path in dialog.FileNames) ImportFile(path);
    }

    private void Help_Click(object sender, RoutedEventArgs e) => HelpPopup.IsOpen = !HelpPopup.IsOpen;

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (UpdateConfiguration.TryGetManifestUri(out var configurationError) is not { } manifestUri)
        {
            MessageBox.Show(this, configurationError, "Updates are not configured", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
            var result = await _updateService.CheckAsync(manifestUri, currentVersion);
            if (!result.IsUpdateAvailable)
            {
                MessageBox.Show(this, $"You are running the latest version ({currentVersion}).", "No update available", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new UpdateWindow(result) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            var downloaded = await _updateService.DownloadAndVerifyAsync(dialog.Manifest);
            var updaterPath = Path.Combine(AppContext.BaseDirectory, "TicketCompletionTimeline.Updater.exe");
            var restartPath = Environment.ProcessPath ?? throw new InvalidOperationException("The application process path is unavailable.");
            if (!File.Exists(updaterPath)) throw new FileNotFoundException("The updater is missing from this release.", updaterPath);

            var startInfo = new ProcessStartInfo(updaterPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add("--install");
            startInfo.ArgumentList.Add(downloaded.PackagePath);
            startInfo.ArgumentList.Add("--target");
            startInfo.ArgumentList.Add(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            startInfo.ArgumentList.Add("--restart");
            startInfo.ArgumentList.Add(Path.GetFileName(restartPath));
            startInfo.ArgumentList.Add("--wait-pid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(startInfo);
            Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, $"The update could not be installed. Your current version is still in place.\n\n{error.Message}", "Update failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void ImportFile(string path)
    {
        // Parse before changing the live session. A malformed file must never
        // replace a working dashboard or partially update the local archive.
        CsvImportResult staged;
        try { staged = _importer.Parse(path); }
        catch (Exception error)
        {
            MessageBox.Show(this, $"This CSV could not be read.\n\n{error.Message}", "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (staged.ValidRows.Count == 0)
        {
            _rejectedRows += staged.RejectedRows;
            UpdateStatus();
            MessageBox.Show(this, "No valid completion rows were found. The current dashboard was not changed.", "Nothing imported", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var merge = _merger.Stage(_records, staged.ValidRows);
        if (merge.Conflicts.Count > 0)
        {
            var choice = MessageBox.Show(this, $"{merge.Conflicts.Count} ticket ID conflict(s) were found.\n\nYes: overwrite conflicting records\nNo: clear current data and import this file\nCancel: keep current data", "Resolve import conflicts", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            var resolution = choice switch { MessageBoxResult.Yes => ConflictResolution.OverwriteConflicts, MessageBoxResult.No => ConflictResolution.ClearCurrentDataAndImport, _ => ConflictResolution.CancelImport };
            merge = _merger.Stage(_records, staged.ValidRows, resolution);
        }
        if (merge.WasCancelled) return;
        var nextRecords = merge.Records.ToList();
        var nextImports = _archiveImports
            .Append(new ArchiveImportBatch(path, DateTimeOffset.UtcNow, staged.ValidRows.Count, staged.RejectedRows))
            .ToList();
        try
        {
            _archiveStore.Save(nextRecords, nextImports);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, $"The import was not saved to the local archive. No dashboard changes were made.\n\n{error.Message}", "Archive save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _records = nextRecords;
        _archiveImports = nextImports;
        _rejectedRows += staged.RejectedRows;
        if (!_sourceFiles.Contains(path, StringComparer.OrdinalIgnoreCase)) _sourceFiles.Add(path);
        RefreshFilterUsers();
        WarningText = staged.Warnings.Count == 0 ? string.Empty : $"Import warning: {staged.Warnings.Count} row warning(s). Invalid rows were left out of the dashboard.";
        RefreshDates();
        UpdateStatus();
    }

    private void RefreshDates()
    {
        var dates = _records.Select(row => DateOnly.FromDateTime(row.Completion.DateTime)).Distinct().OrderByDescending(date => date).Select(date => new DateChoice(date)).ToList();
        AvailableDates.Clear();
        foreach (var date in dates) AvailableDates.Add(date);
        if (SelectedDate is null || !dates.Any(date => date.Date == SelectedDate.Date)) SelectedDate = dates.FirstOrDefault();
        RefreshPeriodChoices();
        RefreshActiveView();
    }

    private void RefreshPeriodChoices()
    {
        var recordsByDate = _records
            .GroupBy(row => DateOnly.FromDateTime(row.Completion.DateTime))
            .ToDictionary(group => group.Key, group => group.Count());
        var weeks = recordsByDate.Keys
            .GroupBy(GetStartOfWeek)
            .OrderByDescending(group => group.Key)
            .Select(group => new PeriodChoice(ReportPeriodKind.Week, group.Key, group.Key.AddDays(6), group.Count()))
            .ToList();
        var months = recordsByDate.Keys
            .GroupBy(date => new DateOnly(date.Year, date.Month, 1))
            .OrderByDescending(group => group.Key)
            .Select(group => new PeriodChoice(ReportPeriodKind.Month, group.Key, group.Key.AddMonths(1).AddDays(-1), group.Count()))
            .ToList();
        AvailableWeeks.Clear();
        foreach (var week in weeks) AvailableWeeks.Add(week);
        AvailableMonths.Clear();
        foreach (var month in months) AvailableMonths.Add(month);
        if (_selectedWeek is null || !weeks.Any(week => week.StartDate == _selectedWeek.StartDate)) _selectedWeek = weeks.FirstOrDefault();
        if (_selectedMonth is null || !months.Any(month => month.StartDate == _selectedMonth.StartDate)) _selectedMonth = months.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedWeek));
        OnPropertyChanged(nameof(SelectedMonth));
        OnPropertyChanged(nameof(HasWeeklyDataVisibility));
        OnPropertyChanged(nameof(HasMonthlyDataVisibility));
    }

    private static DateOnly GetStartOfWeek(DateOnly date)
    {
        var daysFromMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysFromMonday);
    }

    private void RefreshActiveView()
    {
        switch (_activeView)
        {
            case ReportViewKind.Weekly when _selectedWeek is not null:
                RefreshPeriod(_selectedWeek);
                break;
            case ReportViewKind.Monthly when _selectedMonth is not null:
                RefreshPeriod(_selectedMonth);
                break;
            default:
                RefreshSelectedDate();
                break;
        }
    }

    private void RefreshSelectedDate()
    {
        ClearDashboardCollections();
        ActivityHeader.Visibility = Visibility.Visible;
        DailyTimelinePanel.Visibility = Visibility.Visible;
        DailySummaryGrid.Visibility = Visibility.Visible;
        PeriodSummaryGrid.Visibility = Visibility.Collapsed;
        DailyAggregateFooter.Visibility = Visibility.Visible;
        PeriodAggregateFooter.Visibility = Visibility.Collapsed;
        DailySummaryTitle.Visibility = Visibility.Visible;
        PeriodSummaryTitle.Visibility = Visibility.Collapsed;
        WeeklyVisualPanel.Visibility = Visibility.Collapsed;
        MonthlyVisualPanel.Visibility = Visibility.Collapsed;
        ReviewSignalPanel.Visibility = Visibility.Collapsed;
        if (_state.SelectedDate is null || _records.Count == 0)
        {
            _state.HasData = _records.Count > 0;
            OnCallNotice.Visibility = Visibility.Collapsed;
            OnCallButton.Visibility = Visibility.Collapsed;
            CompactButton.Visibility = Visibility.Collapsed;
            SetDashboardVisibility(_records.Count > 0);
            return;
        }
        var date = _state.SelectedDate.Date;
        var baseRecords = FilterEngine.FilterRecords(_records, _filters with { ReviewPriority = ReviewPriorityBand.Any }, _thresholds);
        var baseDay = _metrics.Calculate(baseRecords, date, _thresholds);
        // Priority scores are needed only when the feature is enabled or a
        // priority filter is active. Skipping that calculation keeps ordinary
        // daily browsing focused on the metrics the user actually requested.
        var basePriorities = ShouldCalculatePriorities()
            ? _metrics.CalculateReviewPriorities(baseRecords, date.AddDays(-29), date, _thresholds)
            : new Dictionary<string, UserReviewPriority>(StringComparer.OrdinalIgnoreCase);
        var eligibleUsers = baseDay.Users
            .Where(user => FilterEngine.MatchesUser(user, basePriorities.GetValueOrDefault(user.AssignedUser), _filters with { ReviewPriority = ReviewPriorityBand.Any }))
            .Select(user => user.AssignedUser)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filteredRecords = baseRecords.Where(record => eligibleUsers.Contains(record.AssignedUser)).ToList();
        var day = _metrics.Calculate(filteredRecords, date, _thresholds);
        var priorities = _thresholds.ShowReviewPriority
            ? _metrics.CalculateReviewPriorities(filteredRecords, date.AddDays(-29), date, _thresholds)
            : new Dictionary<string, UserReviewPriority>(StringComparer.OrdinalIgnoreCase);
        if (_filters.ReviewPriority != ReviewPriorityBand.Any)
        {
            var priorityUsers = day.Users
                .Where(user => FilterEngine.MatchesUser(user, priorities.GetValueOrDefault(user.AssignedUser), _filters))
                .Select(user => user.AssignedUser)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            filteredRecords = filteredRecords.Where(record => priorityUsers.Contains(record.AssignedUser)).ToList();
            day = _metrics.Calculate(filteredRecords, date, _thresholds);
            priorities = _thresholds.ShowReviewPriority
                ? _metrics.CalculateReviewPriorities(filteredRecords, date.AddDays(-29), date, _thresholds)
                : new Dictionary<string, UserReviewPriority>(StringComparer.OrdinalIgnoreCase);
        }
        var dailyViews = day.Users
            .Select(user => new UserRowView(user, reviewPriority: priorities.GetValueOrDefault(user.AssignedUser)))
            .OrderBy(view => view.AssignedTeamName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(view => view.AssignedUser, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var view in dailyViews) UserRows.Add(view);
        string? currentTeam = null;
        foreach (var view in dailyViews)
        {
            if (!string.Equals(currentTeam, view.AssignedTeamName, StringComparison.OrdinalIgnoreCase))
            {
                currentTeam = view.AssignedTeamName;
                TimelineRows.Add(UserRowView.TeamHeader(currentTeam));
            }
            TimelineRows.Add(view);
        }
        var average = MetricsCalculator.AverageLongestGap(day.Users);
        var aggregateBand = _thresholds.GetBand(average);
        var aggregateView = new UserRowView(day.AllUsers with { LongestGapBand = aggregateBand }, average, aggregate: true);
        _dailyAggregateRow = aggregateView;
        OnPropertyChanged(nameof(DailyAggregateRow));
        TimelineRows.Add(aggregateView);
        _state.HasData = true;
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(TimelineStartHour));
        OnPropertyChanged(nameof(TimelineEndHour));
        var offHours = day.AllUsers.Events.Where(e => !_thresholds.IsWithinWorkHours(e.Completion)).ToList();
        var hasOffHours = offHours.Count > 0;
        OnCallNotice.Text = hasOffHours ? $"{offHours.Count} on-call completion(s) outside {FormatClock(_thresholds.WorkdayStartMinutes)}–{FormatClock(_thresholds.WorkdayEndMinutes)}" : string.Empty;
        OnCallNotice.Visibility = hasOffHours && !_fullDayTimeline ? Visibility.Visible : Visibility.Collapsed;
        OnCallButton.Content = hasOffHours ? "Show on-call hours" : string.Empty;
        OnCallButton.Visibility = hasOffHours && !_fullDayTimeline ? Visibility.Visible : Visibility.Collapsed;
        CompactButton.Visibility = _fullDayTimeline ? Visibility.Visible : Visibility.Collapsed;
        DailyTimelinePanel.HorizontalScrollBarVisibility = _fullDayTimeline ? System.Windows.Controls.ScrollBarVisibility.Auto : System.Windows.Controls.ScrollBarVisibility.Disabled;
        DailyTimelinePanel.ScrollToHorizontalOffset(0);
        UpdateFilterSummary(day.Users.Count);
        SetDashboardVisibility(_records.Count > 0);
        TimelineModeText.Text = _fullDayTimeline ? "  •  full-day on-call view" : $"  •  {FormatClock(_thresholds.WorkdayStartMinutes)}–{FormatClock(_thresholds.WorkdayEndMinutes)} workday view";
    }

    private void RefreshPeriod(PeriodChoice choice)
    {
        ClearDashboardCollections();
        ActivityHeader.Visibility = Visibility.Visible;
        var baseRecords = FilterEngine.FilterRecords(_records, _filters with { ReviewPriority = ReviewPriorityBand.Any }, _thresholds);
        var baseMetrics = _metrics.CalculatePeriod(baseRecords, choice.Kind, choice.StartDate, _thresholds);
        var basePriorities = ShouldCalculatePriorities()
            ? _metrics.CalculateReviewPriorities(baseRecords, choice.StartDate, choice.EndDate, _thresholds)
            : new Dictionary<string, UserReviewPriority>(StringComparer.OrdinalIgnoreCase);
        var eligibleUsers = baseMetrics.Users
            .Where(user => FilterEngine.MatchesUser(user, basePriorities.GetValueOrDefault(user.AssignedUser), _filters with { ReviewPriority = ReviewPriorityBand.Any }))
            .Select(user => user.AssignedUser)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filteredRecords = baseRecords.Where(record => eligibleUsers.Contains(record.AssignedUser)).ToList();
        var metrics = _metrics.CalculatePeriod(filteredRecords, choice.Kind, choice.StartDate, _thresholds);
        var priorities = _thresholds.ShowReviewPriority
            ? _metrics.CalculateReviewPriorities(filteredRecords, choice.StartDate, choice.EndDate, _thresholds)
            : new Dictionary<string, UserReviewPriority>(StringComparer.OrdinalIgnoreCase);
        if (_filters.ReviewPriority != ReviewPriorityBand.Any)
        {
            var priorityUsers = metrics.Users
                .Where(user => FilterEngine.MatchesUser(user, priorities.GetValueOrDefault(user.AssignedUser), _filters))
                .Select(user => user.AssignedUser)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            filteredRecords = filteredRecords.Where(record => priorityUsers.Contains(record.AssignedUser)).ToList();
            metrics = _metrics.CalculatePeriod(filteredRecords, choice.Kind, choice.StartDate, _thresholds);
            priorities = _thresholds.ShowReviewPriority
                ? _metrics.CalculateReviewPriorities(filteredRecords, choice.StartDate, choice.EndDate, _thresholds)
                : new Dictionary<string, UserReviewPriority>(StringComparer.OrdinalIgnoreCase);
        }
        foreach (var user in metrics.Users) PeriodRows.Add(new PeriodRowView(user, priorities.GetValueOrDefault(user.AssignedUser)));
        _periodAggregateRow = new PeriodRowView(metrics.AllUsers);
        OnPropertyChanged(nameof(PeriodAggregateRow));
        DailyTimelinePanel.Visibility = Visibility.Collapsed;
        DailySummaryGrid.Visibility = Visibility.Collapsed;
        PeriodSummaryGrid.Visibility = Visibility.Visible;
        DailyAggregateFooter.Visibility = Visibility.Collapsed;
        PeriodAggregateFooter.Visibility = Visibility.Visible;
        DailySummaryTitle.Visibility = Visibility.Collapsed;
        PeriodSummaryTitle.Visibility = Visibility.Visible;
        PeriodSummaryTitle.Text = choice.Kind == ReportPeriodKind.Week ? "Weekly averages" : "Monthly averages";
        BuildPeriodVisuals(metrics);
        if (_thresholds.ShowReviewSignals)
            foreach (var signal in _metrics.CalculateReviewSignals(filteredRecords, metrics, _thresholds)) ReviewSignalRows.Add(new ReviewSignalView(signal));
        ReviewSignalPanel.Visibility = _thresholds.ShowReviewSignals && ReviewSignalRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TimelineModeText.Text = choice.Kind == ReportPeriodKind.Week
            ? $"  â€¢  {choice.StartDate:MMM d} â€“ {choice.EndDate:MMM d, yyyy}"
            : $"  â€¢  {choice.StartDate:MMMM yyyy}";
        OnCallNotice.Visibility = Visibility.Collapsed;
        OnCallButton.Visibility = Visibility.Collapsed;
        CompactButton.Visibility = Visibility.Collapsed;
        _state.HasData = metrics.HasData;
        OnPropertyChanged(nameof(HasData));
        UpdateFilterSummary(metrics.Users.Count);
        SetDashboardVisibility(_records.Count > 0);
    }

    private void RefreshFilterUsers()
    {
        var selected = FilterUsers.Where(option => option.IsSelected).Select(option => option.AssignedUser).ToHashSet(StringComparer.OrdinalIgnoreCase);
        FilterUsers.Clear();
        foreach (var user in _records.Select(record => record.AssignedUser).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(user => user, StringComparer.OrdinalIgnoreCase))
            FilterUsers.Add(new FilterUserOption(user, selected.Contains(user)));
        RefreshFilterTeams();
        if (_uiReady) { _filters = ReadFilters(); UpdateFilterSummary(); }
    }

    private void RefreshFilterTeams()
    {
        var teams = _records
            .Select(FilterEngine.TeamName)
            .Where(team => !string.Equals(team, "No team", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(team => team, StringComparer.OrdinalIgnoreCase)
            .ToList();
        AvailableTeams.Clear();
        AvailableTeams.Add("All teams");
        foreach (var team in teams) AvailableTeams.Add(team);
        if (!AvailableTeams.Contains(_selectedTeamName, StringComparer.OrdinalIgnoreCase)) _selectedTeamName = "All teams";
        OnPropertyChanged(nameof(SelectedTeamName));
    }

    private void ClearDashboardCollections()
    {
        // Both Daily and period refreshes rebuild the visible collections from
        // the same filtered source. Clearing them together prevents stale rows
        // from one view surviving while the next view is being calculated.
        UserRows.Clear();
        TimelineRows.Clear();
        PeriodRows.Clear();
        WeeklyHeatmapRows.Clear();
        MonthlyCalendarCells.Clear();
        WeeklyTrendPoints.Clear();
        MonthlyTrendPoints.Clear();
        ReviewSignalRows.Clear();
        _dailyAggregateRow = null;
        _periodAggregateRow = null;
        OnPropertyChanged(nameof(DailyAggregateRow));
        OnPropertyChanged(nameof(PeriodAggregateRow));
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e) => FilterPopup.IsOpen = !FilterPopup.IsOpen;

    private void FilterUser_Changed(object sender, RoutedEventArgs e) => ApplyFilterChanges();

    private void FilterSelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilterChanges();

    private void FilterTextChanged(object sender, TextChangedEventArgs e) => ApplyFilterChanges();

    private void ApplyFilterChanges()
    {
        if (!_uiReady) return;
        _filters = ReadFilters();
        UpdateFilterSummary();
        RefreshActiveView();
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _uiReady = false;
        foreach (var option in FilterUsers) option.IsSelected = false;
        _selectedTeamName = "All teams";
        OnPropertyChanged(nameof(SelectedTeamName));
        MinimumCompletionsBox.Text = string.Empty;
        MaximumCompletionsBox.Text = string.Empty;
        MinimumActiveDaysBox.Text = string.Empty;
        MaximumActiveDaysBox.Text = string.Empty;
        GapBandFilterBox.SelectedIndex = 0;
        WorkHoursFilterBox.SelectedIndex = 0;
        ReviewPriorityFilterBox.SelectedIndex = 0;
        _filters = new();
        _uiReady = true;
        UpdateFilterSummary();
        RefreshActiveView();
    }

    private CompletionFilters ReadFilters()
    {
        var users = FilterUsers.Where(option => option.IsSelected).Select(option => option.AssignedUser).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new(
            users.Count == 0 ? null : users,
            ParseNullableInt(MinimumCompletionsBox.Text),
            ParseNullableInt(MaximumCompletionsBox.Text),
            ParseGapBand(GapBandFilterBox.SelectedValue as string),
            ParseWorkHourFilter(WorkHoursFilterBox.SelectedValue as string),
            ParsePriorityBand(ReviewPriorityFilterBox.SelectedValue as string),
            ParseNullableInt(MinimumActiveDaysBox.Text),
            ParseNullableInt(MaximumActiveDaysBox.Text),
            SelectedTeamName.Equals("All teams", StringComparison.OrdinalIgnoreCase) ? null : SelectedTeamName);
    }

    private static int? ParseNullableInt(string text) => int.TryParse(text, out var value) && value >= 0 ? value : null;
    private static GapBand ParseGapBand(string? value) => Enum.TryParse<GapBand>(value, true, out var result) && result != GapBand.None ? result : GapBand.None;
    private static WorkHourFilter ParseWorkHourFilter(string? value) => Enum.TryParse<WorkHourFilter>(value, true, out var result) ? result : WorkHourFilter.Any;
    private static ReviewPriorityBand ParsePriorityBand(string? value) => Enum.TryParse<ReviewPriorityBand>(value, true, out var result) ? result : ReviewPriorityBand.Any;

    private void UpdateFilterSummary(int displayedUsers = -1)
    {
        FilterButton.Content = _filters.HasValue ? "Filters (active)" : "Filters";
        FilterButton.ToolTip = _filters.HasValue ? "One or more dashboard filters are active." : "Choose users or criteria to narrow the dashboard.";
        if (!_filters.HasValue)
        {
            FilterSummaryText.Text = "All users and all activity";
            OnPropertyChanged(nameof(HasActiveFilters));
            return;
        }
        var parts = new List<string>();
        if (_filters.HasTeamFilter) parts.Add($"team: {_filters.AssignedTeamName}");
        if (_filters.HasUserFilter) parts.Add($"{_filters.AssignedUsers!.Count} user(s)");
        if (_filters.WorkHours == WorkHourFilter.WorkHours) parts.Add("work hours");
        if (_filters.WorkHours == WorkHourFilter.OnCall) parts.Add("on-call");
        if (_filters.LongestGapBand != GapBand.None) parts.Add($"{_filters.LongestGapBand} gaps");
        if (_filters.ReviewPriority != ReviewPriorityBand.Any) parts.Add($"{_filters.ReviewPriority} priority");
        if (_filters.MinimumCompletions.HasValue || _filters.MaximumCompletions.HasValue) parts.Add("completion range");
        if (_filters.MinimumActiveDays.HasValue || _filters.MaximumActiveDays.HasValue) parts.Add("active-day range");
        FilterSummaryText.Text = $"{string.Join(" • ", parts)}{(displayedUsers >= 0 ? $" • showing {displayedUsers} user(s)" : string.Empty)}";
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    private static string FormatClock(int minutes) => DateTime.Today.AddMinutes(minutes).ToString("h:mm tt");

    private void ReviewPriority_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: UserReviewPriority priority }) return;
        new ReviewPriorityWindow(priority) { Owner = this }.ShowDialog();
    }

    private void BuildPeriodVisuals(PeriodMetrics metrics)
    {
        if (metrics.Kind == ReportPeriodKind.Week)
        {
            WeeklyVisualPanel.Visibility = Visibility.Visible;
            MonthlyVisualPanel.Visibility = Visibility.Collapsed;
            var dates = Enumerable.Range(0, 7).Select(offset => metrics.StartDate.AddDays(offset)).ToList();
            var maximum = metrics.AllUsers.Events
                .GroupBy(record => DateOnly.FromDateTime(record.Completion.DateTime))
                .Select(group => group.Count())
                .DefaultIfEmpty()
                .Max();
            foreach (var user in metrics.Users.Append(metrics.AllUsers))
            {
                var counts = user.Events.GroupBy(record => DateOnly.FromDateTime(record.Completion.DateTime)).ToDictionary(group => group.Key, group => group.Count());
                var cells = dates.Select(date => new PeriodDayCellView(date, counts.GetValueOrDefault(date), maximum, date.ToString("ddd"))).ToList();
                WeeklyHeatmapRows.Add(new WeeklyHeatmapRowView(user.AssignedUser, cells));
            }
            var dayCounts = metrics.AllUsers.Events.GroupBy(record => DateOnly.FromDateTime(record.Completion.DateTime)).ToDictionary(group => group.Key, group => group.Count());
            var trendMaximum = dayCounts.Values.DefaultIfEmpty().Max();
            foreach (var date in dates)
            {
                var count = dayCounts.GetValueOrDefault(date);
                WeeklyTrendPoints.Add(new TrendPointView(date.ToString("ddd"), count, trendMaximum, $"{date:dddd, MMMM d}: {count:N0} completions"));
            }
        }
        else
        {
            WeeklyVisualPanel.Visibility = Visibility.Collapsed;
            MonthlyVisualPanel.Visibility = Visibility.Visible;
            var firstDay = metrics.StartDate;
            var lastDay = metrics.EndDate;
            var dayCounts = metrics.AllUsers.Events.GroupBy(record => DateOnly.FromDateTime(record.Completion.DateTime)).ToDictionary(group => group.Key, group => group.Count());
            var maximum = dayCounts.Values.DefaultIfEmpty().Max();
            var leadingBlanks = ((int)firstDay.DayOfWeek + 6) % 7;
            for (var index = 0; index < leadingBlanks; index++) MonthlyCalendarCells.Add(new CalendarDayCellView(null, 0, maximum));
            for (var date = firstDay; date <= lastDay; date = date.AddDays(1)) MonthlyCalendarCells.Add(new CalendarDayCellView(date, dayCounts.GetValueOrDefault(date), maximum));
            // A calendar month can span six partial weeks. Building the trend
            // until the final date prevents the last few days from disappearing
            // for months that begin late in the week.
            var monthTrend = new List<(string Label, int Count, DateOnly Start, DateOnly End)>();
            for (var start = firstDay; start <= lastDay; start = start.AddDays(7))
            {
                var end = start.AddDays(6) <= lastDay ? start.AddDays(6) : lastDay;
                var count = dayCounts.Where(pair => pair.Key >= start && pair.Key <= end).Sum(pair => pair.Value);
                monthTrend.Add(($"Week {monthTrend.Count + 1}", count, start, end));
            }
            var trendMaximum = monthTrend.Select(point => point.Count).DefaultIfEmpty().Max();
            foreach (var point in monthTrend) MonthlyTrendPoints.Add(new TrendPointView(point.Label, point.Count, trendMaximum, $"{point.Start:MMM d}–{point.End:MMM d}: {point.Count:N0} completions"));
        }
    }

    private void UpdateStatus()
    {
        _state.StatusText = _records.Count == 0 ? "No local archive loaded" : $"{_sourceFiles.Count} file(s)  •  {_records.Count:N0} valid completions  •  {_rejectedRows:N0} rejected rows  •  saved locally";
        _state.HasData = _records.Count > 0;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasData));
        SetDashboardVisibility(_records.Count > 0);
    }

    private bool ShouldCalculatePriorities() => _thresholds.ShowReviewPriority || _filters.ReviewPriority != ReviewPriorityBand.Any;

    private void ClearData_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Delete the local archive and all imported completion data? This removes the archive and its backup from this computer.", "Delete local archive", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { _archiveStore.Clear(); }
        catch (Exception error)
        {
            MessageBox.Show(this, $"The local archive could not be deleted.\n\n{error.Message}", "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _records.Clear();
        _sourceFiles.Clear();
        _archiveImports.Clear();
        _rejectedRows = 0;
        _filters = new();
        AssignedTeamNameBox.Text = string.Empty;
        _dailyAggregateRow = null;
        _periodAggregateRow = null;
        OnPropertyChanged(nameof(DailyAggregateRow));
        OnPropertyChanged(nameof(PeriodAggregateRow));
        foreach (var option in FilterUsers) option.IsSelected = false;
        FilterUsers.Clear();
        AvailableDates.Clear();
        AvailableWeeks.Clear();
        AvailableMonths.Clear();
        UserRows.Clear();
        TimelineRows.Clear();
        PeriodRows.Clear();
        WeeklyHeatmapRows.Clear();
        MonthlyCalendarCells.Clear();
        WeeklyTrendPoints.Clear();
        MonthlyTrendPoints.Clear();
        ReviewSignalRows.Clear();
        SelectedDate = null;
        SelectedWeek = null;
        SelectedMonth = null;
        WarningText = string.Empty;
        UpdateStatus();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try { _archiveStore.Save(_records, _archiveImports); }
        catch (Exception error)
        {
            var choice = MessageBox.Show(this, $"The local archive could not be saved while closing. Close anyway?\n\n{error.Message}", "Archive save failed", MessageBoxButton.YesNo, MessageBoxImage.Error);
            e.Cancel = choice != MessageBoxResult.Yes;
        }
        if (!e.Cancel)
        {
            try
            {
                _settingsStore.SavePreferences(new AppPreferences(
                    SelectedTeamName.Equals("All teams", StringComparison.OrdinalIgnoreCase) ? null : SelectedTeamName,
                    _fullDayTimeline));
            }
            catch { }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new(propertyName));

    private void ShowOnCall_Click(object sender, RoutedEventArgs e)
    {
        _fullDayTimeline = true;
        OnPropertyChanged(nameof(TimelineStartHour));
        OnPropertyChanged(nameof(TimelineEndHour));
        RefreshSelectedDate();
    }

    private void DailyView_Click(object sender, RoutedEventArgs e) => SetActiveView(ReportViewKind.Daily);

    private void WeeklyView_Click(object sender, RoutedEventArgs e) => SetActiveView(ReportViewKind.Weekly);

    private void MonthlyView_Click(object sender, RoutedEventArgs e) => SetActiveView(ReportViewKind.Monthly);

    private void PeriodDayCell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DateOnly date }) NavigateToDay(date);
    }

    private void PeriodDayCell_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: DateOnly date }) return;
        var records = _records.Where(record => DateOnly.FromDateTime(record.Completion.DateTime) == date).ToList();
        if (records.Count > 0) OpenDrilldown($"Completion details • {date:dddd, MMMM d, yyyy}", records);
        e.Handled = true;
    }

    private void SummaryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        switch (grid.SelectedItem)
        {
            case UserRowView user:
                OpenDrilldown($"Completion details • {user.AssignedUser}", user.Events);
                break;
            case PeriodRowView period:
                OpenDrilldown($"Completion details • {period.AssignedUser}", period.Events);
                break;
        }
    }

    private void SummaryAggregate_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (_activeView == ReportViewKind.Daily && _dailyAggregateRow is not null)
            OpenDrilldown("Completion details • All Users", _dailyAggregateRow.Events);
        else if (_activeView != ReportViewKind.Daily && _periodAggregateRow is not null)
            OpenDrilldown("Completion details • All Users", _periodAggregateRow.Events);
    }

    private void TimelineControl_BucketClicked(object sender, TimelineBucketClickEventArgs e)
    {
        if (e.Events.Count == 0) return;
        var first = e.Events[0];
        OpenDrilldown($"Completion details • {first.AssignedUser} • {first.Completion:MMM d, yyyy h:mm tt}", e.Events);
    }

    private void OpenDrilldown(string title, IEnumerable<CompletionRecord> records)
    {
        var dialog = new DrilldownWindow(title, records) { Owner = this };
        dialog.ShowDialog();
    }

    private void NavigateToDay(DateOnly date)
    {
        var choice = AvailableDates.FirstOrDefault(candidate => candidate.Date == date);
        if (choice is null) return;
        _activeView = ReportViewKind.Daily;
        OnPropertyChanged(nameof(ActiveView));
        OnPropertyChanged(nameof(DailyViewVisibility));
        OnPropertyChanged(nameof(WeeklyViewVisibility));
        OnPropertyChanged(nameof(MonthlyViewVisibility));
        UpdateActiveViewButton();
        SelectedDate = choice;
    }

    private void SetActiveView(ReportViewKind view)
    {
        if (view == ReportViewKind.Weekly && AvailableWeeks.Count == 0) return;
        if (view == ReportViewKind.Monthly && AvailableMonths.Count == 0) return;
        _activeView = view;
        OnPropertyChanged(nameof(ActiveView));
        OnPropertyChanged(nameof(DailyViewVisibility));
        OnPropertyChanged(nameof(WeeklyViewVisibility));
        OnPropertyChanged(nameof(MonthlyViewVisibility));
        UpdateActiveViewButton();
        RefreshActiveView();
    }

    private void UpdateActiveViewButton()
    {
        DailyViewButton.Style = (Style)FindResource(_activeView == ReportViewKind.Daily ? "ActiveTabButton" : "TabButton");
        WeeklyViewButton.Style = (Style)FindResource(_activeView == ReportViewKind.Weekly ? "ActiveTabButton" : "TabButton");
        MonthlyViewButton.Style = (Style)FindResource(_activeView == ReportViewKind.Monthly ? "ActiveTabButton" : "TabButton");
    }

    private void ShowCompact_Click(object sender, RoutedEventArgs e)
    {
        _fullDayTimeline = false;
        OnPropertyChanged(nameof(TimelineStartHour));
        OnPropertyChanged(nameof(TimelineEndHour));
        RefreshSelectedDate();
    }

    private void TimelineScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DailyTimelinePanel.ScrollableHeight <= 0) return;
        var scrollingDown = e.Delta < 0;
        var atBoundary = scrollingDown
            ? DailyTimelinePanel.VerticalOffset >= DailyTimelinePanel.ScrollableHeight - 0.5
            : DailyTimelinePanel.VerticalOffset <= 0.5;
        if (atBoundary) return;
        var nextOffset = DailyTimelinePanel.VerticalOffset - e.Delta / 3.0;
        DailyTimelinePanel.ScrollToVerticalOffset(Math.Clamp(nextOffset, 0, DailyTimelinePanel.ScrollableHeight));
        e.Handled = true;
    }

    private void SetDashboardVisibility(bool showDashboard)
    {
        EmptyState.Visibility = showDashboard ? Visibility.Collapsed : Visibility.Visible;
        DashboardScrollViewer.Visibility = showDashboard ? Visibility.Visible : Visibility.Collapsed;
        Dashboard.Visibility = showDashboard ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settingsStore, _thresholds) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _thresholds = dialog.Settings;
            CdcPalette.Configure(_thresholds.EffectiveCdcColors);
            OnPropertyChanged(nameof(GreenGapThreshold));
            OnPropertyChanged(nameof(RedGapThreshold));
            RefreshActiveView();
        }
    }
}
