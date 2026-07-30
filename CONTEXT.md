# Ticket Completion Timeline

This context defines the language and agreed product boundaries for the local Windows supervisor dashboard that turns completion CSV files into a daily assigned-user timeline.

## Glossary

### Completion event

A completed ticket represented by one CSV row and its `Last Completion` timestamp.

### Last Completion

The CSV field that supplies the timestamp for a completion event. Values are interpreted as local Eastern Time by default unless the value includes an explicit timezone offset.

### Assigned user

The person associated with a completion event; the dashboard groups completion events by the exact CSV field `Assigned User`.

### Initial dashboard scope

The first view includes all assigned users, one row per user, and an aggregate `All Users` summary. Individual-user drill-down is a later feature.

### Timeline

The timeline spans the full local day from 12:00 AM through 11:45 PM in 15-minute intervals. The active window from the first to the last completion is visually emphasized.

### Completion marker

Each CSV row produces one timeline marker. Multiple markers in the same user and 15-minute cell are stacked or tightly arranged, with a count shown when overlap makes individual markers difficult to distinguish.

### Longest gap

For an individual assigned user, the longest gap is the largest elapsed time between consecutive completion events for that user. The `All Users` value is the arithmetic average of the individual users' longest-gap values, rather than a gap calculated from the combined event stream.

If a user has only one completion, that user's `Longest Gap` is displayed as `—` and the user is excluded from the `All Users` longest-gap average.

### Import validation

Rows missing or containing invalid values for `Last Completion` or `Assigned User` are excluded from the dashboard. The import completes with a warning that reports the number of rejected rows and the reasons.

### Duplicate rows

Duplicate valid CSV rows count as separate completion events in the initial version. Duplicate detection, warning, and optional deduplication are deferred to a future feature.

### Deployment boundary

The product is a local Windows desktop supervisor tool only. It is not hosted and does not upload imported data or require an account or network service in the initial version.

### Date scope

The import model supports any multi-day or whole-month CSV file. The dashboard automatically exposes Daily, Weekly, and Monthly views for periods represented in the imported data. Daily shows one selected date; Weekly and Monthly show totals plus averages across active days in the selected period. Incomplete calendar weeks or months are labeled with their number of active days.

### Import interaction

The primary version-one import flow is dragging a CSV file onto the desktop app.

### User row order

Assigned-user rows are ordered alphabetically by the normalized `Assigned User` value. Alternate sorting, such as by total completions, is future UI functionality.

### Loaded source row

Each valid imported row retains all original CSV columns in the loaded session for future drill-down. Version one uses `Assigned User` and `Last Completion` for the dashboard and does not persist or display the other columns.

### Distribution

The initial release is a portable, self-contained Windows application that can be copied and launched without installation or administrator access.

### Implementation platform

Version one is a native C#/.NET WPF Windows desktop application, published self-contained.

### Visual direction

The dashboard should feel polished, professional, modern, and Microsoft-like. It should use a pleasant blue visual system with strong color coding. Longest-gap values use green for short gaps and red for long gaps, with amber as the middle state.

### Gap color thresholds

The initial `Longest Gap` color thresholds are green for under 1 hour, amber for 1–2 hours, and red for over 2 hours. These values are editable through a Settings menu.

### Preference and archive persistence

User-configured gap thresholds and review-signal settings persist locally between app runs. The canonical imported dataset is also saved locally in an atomic JSON archive under the user's local application-data folder, with a backup copy for recovery. No imported data is uploaded or hosted.

### Summary time display

The selected date appears in the dashboard title. `First` and `Last` values use a 12-hour local-time format such as `9:11 AM`.

### Aggregate row

The `All Users` aggregate row remains visually pinned at the bottom of the user timeline so its totals and metrics remain easy to find as the user list grows.

### Date navigation

When a multi-day CSV is loaded, the dashboard provides selectors for available days, weeks, and months. Each selector is populated from the imported dates and is hidden when that view has no available period. Weekly and monthly views are summary views; the detailed timeline remains on the Daily view.

### All Users metrics

For the selected day, `All Users → Total` is the count of all valid completions, `First` is the earliest completion, and `Last` is the latest completion. `All Users → Longest Gap` is the average of the individual users' longest-gap values.

For Weekly and Monthly views, `Total` is the period total, `Avg/day` divides each user's total by that user's active days (and divides `All Users` by all active days), `Avg first` and `Avg last` average time-of-day values across active days, and `Avg longest gap` averages each user's daily longest gaps. Users with no calculable gap are excluded from the gap average.

### Period visualizations and review signals

Weekly view includes a user-by-day completion heatmap and daily completion trend. Monthly view includes a calendar heatmap and weekly completion trend. Clicking a populated day cell switches to that day's Daily timeline.

Weekly and Monthly views can optionally show evidence-based review signals for long idle gaps above the red threshold, after-hours completions outside 6 AM–6 PM, three or more completions in one 15-minute interval, end-of-shift batches of three or more completions between 4:30 PM and 6 PM, and large changes from a user's prior observed daily baseline when enough history exists. The setting defaults to off and persists locally. Signals identify observable events and thresholds only; they do not establish intent or misconduct.

### Empty and reset states

Before import, the app shows a clear empty state with a large `Drop CSV here` area and a short instruction. On startup, the saved local archive is loaded automatically. After data is loaded, a red `Clear Data` action deletes the local archive and its backup after confirmation.

### Filters and Operational Review Priority

The dashboard has session-only filters for Assigned User, minimum and maximum completions, minimum and maximum active days, longest-gap band, work-hours/on-call status, and Operational Review Priority. Filters recalculate metrics and visuals from the filtered records, and `Clear filters` restores the full dataset.

Settings can define normal work hours; all activity outside that window is treated as on-call activity and is not penalized. Operational Review Priority is a separate opt-in setting that defaults off. When enabled, it requires at least 3 active workdays and 10 work-hour completions, uses daily trailing 30-day context, and uses Low/Moderate/High bands rather than a displayed numeric ranking. Evidence includes long gaps, dense bursts, end-of-shift batching, baseline change, low active-day consistency, and high/low volume outliers versus the filtered global average. Weights, thresholds, and ratios are configurable with recommended-default restore. The aggregate `All Users` row is never assigned a priority band.

### Additional imports

Additional CSV files are additive by default: valid rows are appended to the current in-memory dataset, duplicates are preserved, and available dates are combined. If a data conflict is detected, the app asks whether to overwrite the conflicting data or fully clear the current dataset.

### Conflict identity

When a ticket identifier exists, the app uses `Ticket ID` or the observed source alias `ID` to identify a record for conflict detection. A matching identifier with different values is a conflict only when comparing a new file with the already-loaded session. Repeated identifiers within one file remain separate valid rows in version one. If no identifier exists, rows are treated as independent and appended.

### Conflict choices

When conflicts are found, the import dialog offers `Overwrite Conflicts`, `Clear Current Data and Import`, and `Cancel Import`.

### Import status

The loaded dashboard shows a compact status summary with the number of source files, valid rows, and rejected rows. This status supports additive imports without exposing the raw table in version one.

### Raw-data drill-down

Double-clicking a daily or period summary row opens the original completion records for that user. Clicking a timeline count opens the records in that 15-minute bucket. Double-clicking a populated weekly or monthly day cell opens all records for that date. The drill-down displays one original completion per row and one original CSV field per column in an Excel-style grid, including ticket ID, timestamp, assigned user, and all other source fields. Source filename and CSV row metadata remain stored but are not displayed because they are not useful to the supervisor workflow.

### Version-one boundary

Version one includes drag-and-drop import, additive loading for any CSV timeframe, automatic Daily/Weekly/Monthly period selection, a persistent local archive, daily user timelines, weekly and monthly summary averages, source-row drill-down, validation warnings, conflict choices, configurable gap colors, and a polished empty state. Duplicate management, CSV export, and sliding-scale timeline navigation remain future features.

### Observed source CSV

The supplied export contains 164 rows, 12 assigned users, and one date. It is quoted comma-delimited CSV with embedded line breaks in fields, uses `yyyy-MM-dd HH:mm:ss` timestamps without offsets, and provides `ID` as the ticket identifier. The initial parser must support this shape directly.

### Implementation specification

The backlog-ready implementation specification is maintained at `docs/specs/ticket-completion-timeline.md`.

### Engineering review outputs

The engineering review is recorded at the end of the implementation specification. Its test plan is `docs/test-plan.md`; timezone and timeline-rendering decisions are recorded in `docs/adr/0002-explicit-eastern-time-normalization.md` and `docs/adr/0003-virtualized-timeline-rendering.md`.
