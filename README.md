# Ticket Time Tracker

Ticket Time Tracker is a local Windows supervisor dashboard for reviewing ticket completion activity by assigned user. It imports one or more CSV exports, keeps the data on the local computer, and presents daily timelines plus weekly and monthly summary views.

The application is a native WPF desktop tool. It is not hosted, does not require an account, and does not upload imported data.

## Author and license

Copyright © 2026 Matthew Massena Jr.

This project is open source under the [MIT License](<./LICENSE>). You may use, copy, modify, merge, publish, distribute, sublicense, and sell copies of the software as permitted by that license. The copyright notice and license text must remain with copies or substantial portions of the software.

Matthew Massena Jr. is the original author and copyright holder of the existing project code. Contributors retain copyright to their own original contributions unless a separate written agreement says otherwise; contributions are accepted under the MIT terms described in [CONTRIBUTING.md](<./CONTRIBUTING.md>).

## What the application does

- Accepts CSV files through drag and drop or the **Add CSV** button.
- Groups completion events by the **Assigned User** column.
- Uses **Last Completion** as the completion timestamp.
- Displays one daily timeline row per assigned user.
- Shows completion counts in 15-minute timeline buckets.
- Colors the gaps between completions green, amber, or red according to Settings.
- Shows total, first completion, last completion, and longest gap for each user.
- Calculates the **All Users** longest gap as the average of the individual users' longest gaps. It does not calculate a gap from the combined user stream.
- Automatically exposes Daily, Weekly, and Monthly views when the imported data contains those periods.
- Shows weekly user-by-day activity and monthly calendar heatmaps.
- Supports user, count, active-day, gap-band, work-hour, on-call, and review-priority filters.
- Opens original CSV values in an Excel-style drill-down grid.
- Saves the canonical imported dataset locally before the application closes and after every successful import.

## CSV contract

Two columns are required. Header matching is case-insensitive and ignores surrounding whitespace.

| Column | Required | Use |
| --- | --- | --- |
| **Assigned User** | Yes | Groups rows into user timeline and summary rows. |
| **Last Completion** | Yes | Supplies the date and time of each completion event. |
| **Ticket ID** or **ID** | No | Enables conflict detection when another CSV is added. |

All other columns are preserved and shown in the drill-down window. A row is rejected when **Assigned User** is blank or **Last Completion** cannot be parsed.

### Timestamp rules

- A timestamp with an explicit offset is converted to Eastern Time for display and grouping.
- A timestamp without an offset is interpreted as an Eastern Time wall-clock value.
- Ambiguous or nonexistent daylight-saving times are rejected rather than silently guessed.
- UTF-8 CSV is recommended.
- Quoted commas, quoted multiline fields, and escaped quotes are supported.

Example minimum file:

~~~csv
Assigned User,Last Completion,ID,Notes
alex@example.com,2026-07-29 09:15:00,1001,Completed ticket
alex@example.com,2026-07-29 10:05:00,1002,"Completed, customer confirmed"
~~~

## Import behavior

Additional files are additive. Valid rows are appended to the current session and duplicates are preserved because duplicate management is intentionally a future feature.

When a ticket identifier matches an existing row but the values differ, the application asks whether to:

1. Overwrite conflicting records.
2. Clear the current dataset and import the new file.
3. Cancel the import.

An import is staged before the archive is changed. If parsing fails, a conflict is cancelled, or the archive cannot be saved, the existing dashboard remains intact.

## Views and interaction

### Daily

Daily is the detailed view. It shows a timeline from the configured workday start through the configured workday end. If completions exist outside that range, the application offers a full-day on-call view with horizontal scrolling.

The timeline uses one count badge for each user and 15-minute bucket. Select a badge, double-click a summary row, or double-click the **All Users** footer to open the original rows behind that result.

### Weekly

Weekly shows averages across active days in the selected calendar week. It also includes a user-by-day heatmap and a daily completion trend. Selecting a populated day cell opens that day's Daily view.

### Monthly

Monthly shows averages across active days in the selected calendar month. It includes a calendar heatmap and a week-by-week completion trend. Months that span six calendar weeks retain all six trend segments.

### Filters

Filters are session-only and do not change the archive. They recalculate the displayed metrics and visualizations together. **Clear filters** restores the full loaded dataset; **Clear Data** is the separate destructive action that deletes the local archive after confirmation.

## Settings

Settings are stored locally and include:

- Workday start and end time. Everything outside this range is treated as on-call activity.
- Green and red gap thresholds. Amber is the range between them.
- Optional weekly/monthly operational review signals.
- Optional user-level Operational Review Priority.
- Evidence weights and minimum-history thresholds for review priority.
- CDC List colors for 001, 002, 004, 005/006, 009, and other codes.
- Assigned-team selection with team-first grouping in the Daily view and summary.

On-call activity is not treated as a risk signal. Review Priority is an evidence summary for supervisor review, not a misconduct conclusion. It uses observable patterns such as long work-hour gaps, dense bursts, end-of-shift batching, changes from a user's prior baseline, low active-day consistency, and volume outliers against the filtered global average.

## Local storage and privacy

The default local storage directory is:

~~~text
%LOCALAPPDATA%\TicketCompletionTimeline
~~~

Files written there:

- archive.json - the canonical imported rows and import history.
- archive.json.bak - the previous archive kept for recovery.
- settings.json - saved application settings.

The archive is written through a temporary file and then moved into place. The application never sends these files to a server. Do not include this directory, customer data, or exported CSV files in a release package.

## Run from source

Requirements:

- Windows 10 or Windows 11.
- .NET 10 SDK.

From PowerShell at the repository root:

~~~powershell
dotnet restore .\TicketCompletionTimeline.slnx
dotnet run --project .\TicketCompletionTimeline.App
~~~

The app loads the existing local archive automatically when it starts.

## Build the portable release

Run:

~~~powershell
.\publish.ps1
~~~

The script creates a self-contained win-x64 folder at:

~~~text
artifacts\portable-win-x64
~~~

Copy the entire folder to another Windows computer and launch TicketCompletionTimeline.App.exe. Do not copy only the executable; the runtime and WPF support files are part of the folder.

For distribution, zip the complete folder after testing it on a clean Windows profile. The folder also contains `TicketCompletionTimeline.Updater.exe` and `update-config.json` for the optional manual update flow. A code-signing certificate is recommended before shareholder or company-wide distribution because unsigned Windows executables may receive a SmartScreen warning.

## Updating an installed copy

The application includes a visible **Updates** button. Update checks are manual by default; the application does not call home at startup and it never uploads imported ticket data.

Before publishing a release, edit `TicketCompletionTimeline.App\update-config.json` and set `manifestUrl` to an HTTPS URL that managers can read. GitHub Releases is the recommended public option. A company network share or SharePoint-hosted HTTPS file is appropriate when the distribution must stay internal. The manifest itself is a small JSON file; `update-manifest.example.json` shows the format:

~~~json
{
  "version": "1.0.1",
  "downloadUrl": "https://downloads.example.com/ticket-time-tracker/Ticket-Time-Tracker-1.0.1.zip",
  "sha256": "64_CHARACTER_SHA256_OF_THE_ZIP",
  "releaseNotes": "Short description of the changes.",
  "minimumSupportedVersion": "1.0.0"
}
~~~

Release workflow:

1. Change the `<Version>`, `<AssemblyVersion>`, and `<FileVersion>` values in `TicketCompletionTimeline.App\TicketCompletionTimeline.App.csproj`.
2. Run the Release tests and `publish.ps1`.
3. Test the complete `artifacts\portable-win-x64` folder on a clean Windows profile.
4. Zip the complete publish folder. Do not include `%LOCALAPPDATA%\TicketCompletionTimeline` or customer CSV files.
5. Calculate the ZIP SHA-256 value with `Get-FileHash -Algorithm SHA256`.
6. Upload the ZIP to the chosen HTTPS location and publish a manifest with that exact version, URL, checksum, and release notes.
7. Open the older installed copy, select **Updates**, review the notes, and choose **Download and install**.

The app downloads into a temporary directory, verifies SHA-256, then starts the separate updater. The updater waits for the app to exit, replaces the portable install folder as one unit, retains a rollback folder until replacement succeeds, and restarts the same executable name. The archive and settings are stored under `%LOCALAPPDATA%\TicketCompletionTimeline`, so they are preserved across updates. A checksum protects against accidental or incomplete downloads; Authenticode signing is still recommended for production distribution and is not silently assumed by this open-source build.

To test the updater without a hosted server, run `TicketCompletionTimeline.Updater.exe` manually with a verified package and the documented `--install`, `--target`, `--restart`, and `--wait-pid` arguments. It returns a non-zero exit code when the package is missing, the target executable is absent, or replacement fails.

## Tests

Run the full test suite with:

~~~powershell
dotnet test .\TicketCompletionTimeline.Tests\TicketCompletionTimeline.Tests.csproj --configuration Release
~~~

The tests cover:

- CSV parsing, quoted multiline fields, required columns, and rejected rows.
- Eastern Time normalization and daylight-saving edge cases.
- Daily, weekly, and monthly metrics.
- The average-longest-gap rule for All Users.
- Gap bands and configurable work-hour boundaries.
- Filters and on-call treatment.
- Review signals and review-priority minimum history.
- Additive imports and conflict decisions.
- Local archive round trips and backup recovery.

## Repository map

~~~text
TicketCompletionTimeline.App/       WPF windows, timeline drawing, and view models
TicketCompletionTimeline.Core/      CSV, archive, merge, filtering, and metric rules
TicketCompletionTimeline.Tests/     xUnit tests for core behavior
docs/specs/                         Product specification and agreed behavior
docs/adr/                           Architecture decisions
docs/test-plan.md                   Manual test plan
publish.ps1                         Self-contained Windows publish script
~~~

The core project contains the rules that affect numbers. The WPF project formats those results and handles interaction. Keeping those responsibilities separate makes it possible to test the calculations without starting a desktop window.

## Release checklist

Before presenting or distributing a build:

1. Run dotnet test with the Release configuration and confirm every test passes.
2. Run publish.ps1 and copy the full output folder to a clean test directory.
3. Launch the portable executable without the .NET SDK installed on the test machine.
4. Import a small sample CSV, then import a second file and exercise the conflict dialog.
5. Check Daily, Weekly, and Monthly views, including a month that spans six calendar weeks.
6. Resize the window and verify the summary remains reachable through the outer scroll bar.
7. Open Settings and confirm work hours, colors, and optional review features persist after restart.
8. Test a file containing quoted commas, multiline fields, blank users, invalid timestamps, and after-hours completions.
9. Confirm the drill-down shows every original column and does not alter the archive.
10. Remove any local archive and test CSVs from the distribution folder before zipping it.
11. Add a version number and release notes before sending the package to shareholders.

## Known boundaries

- Duplicate detection and deduplication controls are not included yet.
- CSV export is not included yet.
- The detailed 15-minute timeline is Daily-only; Weekly and Monthly are summary views with drill-down navigation.
- The application is local-only and has no hosted synchronization or multi-user access.
