# Virtualized timeline rendering

The WPF dashboard virtualizes user rows and draws each visible 96-cell timeline row with one custom rendering surface. This keeps the first release responsive for the planned 500-user and 100,000-row session while preserving the spreadsheet-like markers, shading, and count badges without creating a separate WPF control for every cell.
