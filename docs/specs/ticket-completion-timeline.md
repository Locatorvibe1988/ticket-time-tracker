# Ticket Completion Timeline Desktop Dashboard

## Context

Supervisors currently rely on a spreadsheet to review ticket completion activity by assigned user. The supplied CSV export proves the source format: 164 rows, 12 assigned users, one date, quoted comma-delimited fields, multiline values, `Assigned User`, `Last Completion`, and `ID` columns.

The goal is a portable offline Windows app that accepts CSV files through drag-and-drop and produces daily detail plus automatic weekly and monthly summaries without manual spreadsheet preparation.

## Current State

- The native C#/.NET WPF application is implemented and builds as a self-contained Windows desktop app.
- Product language and boundaries are documented in `CONTEXT.md`.
- Architecture is documented in `docs/adr/0001-portable-local-wpf-dashboard.md`.
- The supplied CSV contains 164 rows, 12 assigned users, one available date, `yyyy-MM-dd HH:mm:ss` timestamps, `ID` as the ticket identifier, embedded line breaks inside fields, and three repeated IDs within the file.

## Proposed Change

Build a self-contained C#/.NET WPF Windows application with drag-and-drop CSV import, additive multi-file loading, validation warnings, conflict detection, automatic Daily/Weekly/Monthly period selection, a persistent local archive, a daily 15-minute timeline, per-user metrics, weekly and monthly averages, source-row drill-down, a pinned `All Users` summary row, configurable gap colors, and local settings persistence without hosted data.

## Implementation Details

### Data model

Each valid CSV row becomes a completion record containing the original source columns, source file name, source row number, normalized assigned user, parsed local completion timestamp, and an optional ticket key from `Ticket ID` or `ID`. Records are persisted in one local atomic JSON archive and loaded automatically at startup.

Required fields are `Assigned User` and `Last Completion`. Unknown columns are preserved for future drill-down.

### Import behavior

- Support quoted commas, multiline quoted fields, and UTF-8 CSV.
- Trim assigned-user whitespace for grouping.
- Interpret timestamps without offsets as Eastern local time.
- Reject rows with missing or invalid required fields and report counts and reasons.
- Preserve valid duplicate rows.
- Repeated identifiers within one file remain separate valid rows.
- When adding another file, new IDs append, identical existing rows append, and the same ID with changed values creates a conflict.
- Conflict choices are `Overwrite Conflicts`, `Clear Current Data and Import`, and `Cancel Import`.

### Dashboard

The main window contains a blue Microsoft-style header, selected date and date selector, file/valid/rejected status, a red `Clear Data` action, an empty-state drop zone, summary columns (`Assigned User`, `Total`, `First`, `Last`, `Longest Gap`), and 96 timeline cells from 12:00 AM through 11:45 PM.

Each user row shades that user's first-to-last active window. The `All Users` row uses the global active window. Each valid row creates one completion marker. Multiple events in one 15-minute cell are stacked and countable. Users are alphabetically ordered and the `All Users` row is pinned at the bottom.

For the selected day, `All Users` uses the total count, earliest completion, latest completion, and the average of users' longest-gap values. A user with one completion displays `—` and is excluded from that average.

The view selector exposes Daily, Weekly, and Monthly when imported data contains periods for those views. Daily retains the timeline and exact first/last values. Weekly and Monthly show total completions, average completions per active day, average first completion time, average last completion time, and average longest gap. A partial calendar period is valid and displays its active-day count.

Double-clicking a summary row opens the preserved original source rows. Clicking a Daily timeline count opens the records in that 15-minute bucket. Double-clicking a populated weekly or monthly day cell opens that date's source rows. The drill-down uses one row per completion and one original CSV field per column, with spreadsheet-style sorting, resizing, reordering, horizontal scrolling, and clipboard copying. Source filename and CSV row metadata remain stored but are not displayed.

Weekly also shows a user-by-day completion heatmap and daily trend. Monthly shows a calendar heatmap and weekly trend. Populated day cells navigate to Daily view. Period views can optionally show observable review signals for long idle gaps, after-hours completions, dense 15-minute bursts, end-of-shift batches, and large changes from an established baseline. The setting defaults to off and persists locally. These signals are review prompts, not automatic conclusions about cause or intent.

### Gap colors and settings

Defaults are green under 1 hour, amber from 1 to 2 hours, and red over 2 hours. Thresholds are editable in Settings and persist locally. Imported data is never persisted.

## Acceptance Criteria

1. Dropping the supplied CSV loads 164 valid rows with zero rejected rows.
2. The supplied CSV displays one available date and 12 assigned-user rows.
3. The supplied CSV shows Total 164, First 6:57:36 AM, Last 5:46:45 PM, and average longest gap 159.1 minutes.
4. The timeline renders 96 15-minute cells.
5. Each valid row creates one completion event.
6. Multiple events in one cell remain countable and do not disappear.
7. Users sort alphabetically and `All Users` remains pinned at the bottom.
8. A single-completion user shows `—` for longest gap.
9. Invalid required fields are excluded and reported.
10. Repeated IDs within one file remain separate events.
11. Adding a second file appends non-conflicting rows.
12. Changed values for an existing ID trigger the conflict dialog.
13. `Cancel Import` preserves the current session and `Clear Data` removes it.
14. Date, week, and month selectors switch between automatically discovered periods.
15. Weekly and monthly views show totals and averages based on active days.
16. Empty week/month selectors are hidden when no periods exist for that view.
17. Weekly view renders a user-by-day heatmap and daily trend.
18. Monthly view renders a calendar heatmap and weekly trend.
19. Clicking a populated period day opens the Daily view for that date.
20. The local archive loads on startup and saves after successful imports.
21. A damaged primary archive recovers from its backup when available.
22. Double-clicking a summary row opens original source rows and fields.
23. Clicking a Daily timeline count opens its source rows.
24. Review signals are hidden by default and can be enabled in Settings.
25. Enabled review signals show affected user, date, and evidence.
26. Gap thresholds and the review-signal setting can be edited and survive app restart.
27. The app publishes as a self-contained portable Windows build.
28. Unit and integration tests pass.

## Testing Plan

| Layer | Coverage |
|---|---|
| Unit | CSV parsing, multiline fields, timestamp parsing, validation, grouping, longest gaps, averages, threshold colors |
| Integration | Supplied CSV import, additive imports, conflict handling, clear behavior, multi-day selection |
| UI smoke | Drag/drop, empty state, date selector, pinned summary row, settings persistence |
| Publish smoke | Launch self-contained build on Windows without installation or network access |

## Rollback Plan

Revert the application files or replace the portable application folder with the previous build. No database migration or hosted-state rollback is required.

## Effort Estimate

Estimated total: 2 to 4 focused development days. The work divides into project scaffolding and publishing, CSV parsing and validation, metrics and timeline calculation, WPF dashboard, import conflicts, tests, and packaging polish.

## Out of Scope

- Raw-data drill-down beyond the implemented summary/timeline/date interactions
- Month summary charts beyond the implemented monthly averages
- Sliding-scale timeline
- Duplicate-management UI
- Hosted deployment, accounts, authentication, and network sync
- Hosted or synchronized data storage
- Alternate sorting
- CSV export

## Engineering Review

### What already exists

| Artifact | Current state | Review action |
|---|---|---|
| `CONTEXT.md` | Product language, scope, metrics, import rules, and source CSV facts | Reuse as the domain source of truth |
| `docs/adr/0001-portable-local-wpf-dashboard.md` | Local WPF and portable distribution decision | Extend with timezone and rendering decisions |
| Supplied CSV export | 164 valid rows, 12 users, one date, quoted multiline CSV, `ID` key | Use as the primary integration fixture |
| Application source | Does not exist | Create greenfield solution |
| Test infrastructure | Does not exist | Create xUnit test project and Windows smoke script |

### NOT in scope

- Hosted deployment or network services. This would change the privacy and deployment boundary.
- Raw-row drill-down. The full source row is preserved in memory, but the UI is deferred.
- Month charts and sliding timeline navigation. The date-aware session model supports them later without expanding version one.
- Duplicate management. Repeated valid rows remain visible as events; conflict checks apply only when merging a new file into an existing session.
- CSV export. It is unrelated to the first supervisor review loop.

### Architecture decisions

#### 1. Explicit Eastern time normalization

The parser stores timestamps as `DateTimeOffset` values normalized with Windows Eastern Time rules. Source timestamps without offsets are parsed as `DateTimeKind.Unspecified` and converted with `TimeZoneInfo` rather than the machine-local timezone.

Ambiguous or nonexistent daylight-saving timestamps are rejected with a row-level warning because the source does not provide enough information to choose the correct instant. All date grouping and 15-minute bucketing use the normalized Eastern date and local clock.

#### 2. Staged import transaction

An import is parsed and validated into a staging result before changing the active session.

```text
CSV drop
  -> CsvImportService: parse rows and preserve source columns
  -> Validation: required fields, timestamps, identifier aliases
  -> SessionMergeService: compare fingerprints and collect conflicts
  -> User choice: overwrite, clear-and-import, or cancel
  -> Commit: replace the immutable in-memory session atomically
  -> MetricsCalculator: calculate selected-day summaries
  -> MainViewModel: publish one UI state
  -> Virtualized timeline: render visible user rows
```

Cancel, parse failure, or conflict dismissal leaves the prior session unchanged. The UI never renders a partially merged dataset.

#### 3. Deterministic conflict fingerprints

`Ticket ID` and `ID` are accepted aliases for the ticket key. A repeated identifier in one file is appended as a separate row. During a merge, an incoming row is identical when its normalized key and canonicalized values for all shared columns match an existing row. A same-key row with different canonical values is a conflict. Missing identifiers never conflict.

#### 4. Virtualized timeline rendering

The user list uses WPF row virtualization. Each visible row uses one custom drawing surface for the 96 time cells, active-window shading, markers, and count badges. This avoids creating thousands of independent WPF controls while keeping the visual behavior explicit and testable.

The implementation target is 100,000 rows, 500 assigned users, and a responsive first render and date switch on a normal supervisor Windows workstation. The supplied 164-row file remains the acceptance fixture.

#### 5. Distribution

The first release targets `win-x64` and uses `dotnet publish --self-contained true`. A checked-in `publish.ps1` creates the portable output folder. The first release does not require CI publishing or auto-update; the manual release artifact is a versioned zip or folder that can be copied to another Windows workstation.

### Architecture failure modes

| Failure | Detection | User result | Test |
|---|---|---|---|
| Malformed quoted or multiline CSV | Parser exception or row error | Import warning with rejected-row count; prior session remains | Multiline and malformed-row tests |
| Ambiguous or nonexistent Eastern timestamp | Timezone validation | Row rejected with a specific timestamp warning | DST transition tests |
| Conflicting ID during merge | Fingerprint comparison | Choice dialog; no mutation before choice | Merge conflict tests |
| Settings file missing or invalid | Settings load validation | Defaults restored and warning shown | Settings recovery tests |
| Large user count slows layout | Virtualized row count and publish smoke | Same data remains usable; no silent truncation | 500-user performance fixture |
| Publish missing runtime files | Launch smoke test | Release rejected before handoff | Self-contained publish test |

### Code quality review

No application code exists yet, so there are no existing code-quality findings. The implementation must keep parsing, session merging, metric calculation, settings, and WPF presentation separate. Normalization and timestamp parsing must have one shared implementation, not copies in import and metrics code.

### Test review

The spec currently names broad test categories at `ticket-completion-timeline.md:72-79`, but it does not enumerate every branch. The implementation plan adds explicit coverage before UI polish.

```text
CODE PATHS                                      USER FLOWS
[+] CsvImportService                            [+] Drop valid supplied CSV
  |- quoted and multiline fields                 |- [E2E] 164 rows -> 12 users -> daily view
  |- missing required field                      |- Drop malformed file -> warning, old data kept
  |- invalid timestamp                           |- Add second file -> additive merge
  |- ambiguous/nonexistent DST time              |- Conflict -> overwrite / clear / cancel
  `- empty file                                  |- Clear Data -> empty state

[+] SessionMergeService                         [+] Date selector
  |- new identifier                              |- [E2E] switch dates without re-import
  |- identical fingerprint                       `- Settings -> edit thresholds -> restart
  |- changed fingerprint
  |- repeated identifier in one file
  `- no identifier

[+] MetricsCalculator                            [+] Timeline rendering
  |- zero events                                 |- 96 cells and active-window shading
  |- one event -> no gap                         |- stacked markers and count badge
  |- multiple events -> max gap
  `- All Users average excludes no-gap users

[+] SettingsService                              [+] Publish smoke
  |- missing file -> defaults                    |- launch without install or network
  |- valid settings                               `- no CSV written to disk
  `- invalid threshold ordering

Coverage target: all listed branches tested; UI flows covered by smoke or integration tests.
```

Planned test inventory:

| Suite | Planned cases |
|---|---:|
| CSV parser and validation | 14 |
| Merge and conflict behavior | 10 |
| Metrics and timezone rules | 14 |
| Settings recovery and thresholds | 5 |
| WPF interaction smoke | 8 |
| Publish and privacy smoke | 3 |
| Total | 54 |

### Performance review

The app has no database or network path, so there are no query or N+1 concerns. The main risks are CSV parsing, repeated metric recalculation, and WPF layout cost. Parse once into an immutable session, group and sort once per selected date, cache day summaries by date, and virtualize user rows. Do not write CSV data to disk or rebuild the full visual tree on every marker.

### Implementation Tasks

- [ ] **T1 (P1, human: ~3h / CC: ~20min)** Create the WPF solution, domain records, xUnit project, and `win-x64` publish profile. Verify a self-contained empty app launches.
- [ ] **T2 (P1, human: ~5h / CC: ~30min)** Implement RFC-compliant CSV parsing with multiline fields, required-field validation, `ID`/`Ticket ID` aliases, row warnings, and source-column preservation. Verify the supplied CSV imports as 164 valid rows.
- [ ] **T3 (P1, human: ~4h / CC: ~25min)** Implement Eastern `DateTimeOffset` normalization, DST rejection rules, date grouping, 15-minute bucket calculation, per-user metrics, and `All Users` averaging. Verify the supplied baseline metrics.
- [ ] **T4 (P1, human: ~4h / CC: ~25min)** Implement staged additive merging, canonical fingerprints, conflict choices, cancel safety, and clear behavior. Verify repeated IDs in one file remain separate.
- [ ] **T5 (P1, human: ~8h / CC: ~45min)** Implement the WPF empty state, header, date selector, summary table, virtualized custom timeline, markers, active windows, pinned aggregate row, and status counts.
- [ ] **T6 (P2, human: ~3h / CC: ~15min)** Implement local settings persistence with default recovery, threshold ordering validation, and green/amber/red rendering.
- [ ] **T7 (P1, human: ~6h / CC: ~30min)** Add the 54-case test inventory across unit, integration, UI smoke, publish, and privacy tests. Verify all acceptance criteria.
- [ ] **T8 (P1, human: ~2h / CC: ~10min)** Add `publish.ps1`, README launch instructions, and a clean Windows publish smoke test.

### Parallelization

Sequential implementation, no safe parallelization opportunity for the first pass. The timeline UI depends on the domain and metrics contracts, and the merge tests depend on the import model. After T2-T4 stabilize, T5 and T6 can be developed in separate worktrees if needed, then joined before T7 and T8.

## Review Readiness Dashboard

| Run | Status | Result |
|---|---|---|
| Scope challenge | Complete | Modular structure accepted; no application code existed to reuse |
| Architecture | Complete | Eastern normalization, staged imports, virtualized rendering, deterministic conflicts, and publish target locked |
| Code quality | Complete | Greenfield; separation and single-normalizer rules added |
| Tests | Complete | 54 planned cases and code/user-flow coverage diagram added |
| Performance | Complete | In-memory, cached day summaries and virtualized rows selected |
| Outside voice | Not run | No external reviewer available in this workspace |

VERDICT: READY FOR IMPLEMENTATION

## GSTACK REVIEW REPORT

| Run | Status | Findings |
|---|---|---|
| Architecture | PASS | 5 design risks addressed with recommended decisions |
| Code quality | PASS | No existing code; explicit module boundaries added |
| Tests | PASS | All planned code paths and user flows have test coverage requirements |
| Performance | PASS | 100,000-row and 500-user target addressed with virtualization and caching |
| Quality gate | CONCERN | Automated Codex gate unavailable because the Windows executable was blocked |
| Redaction gate | CONCERN | Bun launcher unavailable; manual sensitive-data scan passed |

NO UNRESOLVED DECISIONS

## Review-signal foundation

The timeline can later support configurable operational review signals. Signals must describe observable patterns and evidence, not label a person as dishonest or predict misconduct. Each signal should show the affected user, date, time range, threshold used, and source events so a supervisor can review the underlying records.

Initial candidate signals are: unusually long idle gaps, repeated off-hours completions, unusually dense completion bursts, and end-of-shift batch completion. These are review prompts only; they do not establish cause or intent. Signal scoring and labels remain deferred until the user defines which patterns should be considered operationally concerning.
