# Contributing to Ticket Time Tracker

Copyright © 2026 Matthew Massena Jr.

Thank you for helping improve Ticket Completion Timeline. Matthew Massena Jr. is the original author of the existing project code. The project is licensed under the MIT License. Contributors retain copyright to their own original contributions and grant users the rights required by the MIT License when those contributions are included in the project.

## Before making a change

1. Read the project behavior and boundaries in [CONTEXT.md](<./CONTEXT.md>).
2. Read the implementation specification in [docs/specs/ticket-completion-timeline.md](<./docs/specs/ticket-completion-timeline.md>).
3. Keep the application local-only. Do not add telemetry, hosted storage, authentication, or network calls without an explicit product decision.
4. Do not commit real customer CSV files, local archives, credentials, or screenshots containing business data.

## Development setup

The project targets Windows and .NET 10. From the repository root:

~~~powershell
dotnet restore .\TicketCompletionTimeline.slnx
dotnet build .\TicketCompletionTimeline.slnx --configuration Release
dotnet test .\TicketCompletionTimeline.Tests\TicketCompletionTimeline.Tests.csproj --configuration Release
~~~

Run the desktop app with:

~~~powershell
dotnet run --project .\TicketCompletionTimeline.App
~~~

## Code expectations

- Keep business rules in TicketCompletionTimeline.Core so they can be tested without WPF.
- Preserve the All Users longest-gap rule: it is the average of individual user longest gaps, not a combined event-stream gap.
- Treat activity outside configured work hours as on-call activity and do not score it as a risk signal.
- Preserve original CSV fields for drill-down.
- Add or update a focused test for changed calculation, import, archive, filtering, or conflict behavior.
- Explain non-obvious decisions in comments, especially timezone normalization, archive recovery, and timeline rendering.
- Keep UI copy factual and avoid claims that cannot be demonstrated by the imported data.

## Pull requests

A pull request should describe:

- What user-visible behavior changed.
- Which tests were added or run.
- Any documentation or release notes that were updated.
- Any known limitations or manual verification still needed.

The final portable build must not contain source files, PDB files, customer data, or local archive files unless they are intentionally included for a documented development reason.
