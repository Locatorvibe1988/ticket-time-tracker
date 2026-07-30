# Explicit Eastern time normalization

CSV completion timestamps without offsets are interpreted with explicit Windows Eastern Time rules and stored as `DateTimeOffset` values. Ambiguous or nonexistent daylight-saving timestamps are rejected with row-level warnings because the source cannot identify the correct instant; this prevents dashboard results from changing with the workstation timezone.
