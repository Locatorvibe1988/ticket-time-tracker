# Ticket Completion Timeline Test Plan

Generated from the engineering review of `docs/specs/ticket-completion-timeline.md`.

## Affected surface

- WPF main window: drag/drop import, empty state, selected date, summary rows, timeline, clear action, and settings.
- Import session: additive merge, validation warnings, conflict dialog, and cancel safety.
- Portable publish: self-contained Windows x64 launch without network access.

## Key interactions

- Drop the supplied CSV and verify 164 valid rows, one date, 12 users, and baseline metrics.
- Drop a malformed or invalid-row CSV and verify warnings while the current session remains unchanged.
- Add a second CSV with new IDs, identical rows, changed IDs, repeated IDs, and missing IDs.
- Choose overwrite, clear-and-import, and cancel for conflicts.
- Switch between dates in a multi-day fixture.
- Edit thresholds, restart the app, and verify persistence and validation.
- Clear data and verify the empty drop zone returns.

## Edge cases

- Empty file
- Header-only file
- Missing required headers
- Quoted commas
- Embedded line breaks
- Invalid timestamp
- Missing assigned user
- Ambiguous or nonexistent Eastern DST timestamp
- One completion for a user
- Many completions in one 15-minute cell
- 500 users and 100,000 rows
- Invalid settings file

## Critical paths

1. Drop valid CSV -> parse -> validate -> commit -> calculate -> render.
2. Drop second CSV -> stage -> compare fingerprints -> resolve conflict -> commit or preserve prior state.
3. Select date -> retrieve cached day model -> render virtualized rows.
4. Publish -> copy portable output -> launch Windows x64 build offline.
