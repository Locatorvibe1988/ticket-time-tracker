using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TicketCompletionTimeline.Core;

namespace TicketCompletionTimeline.App;

public partial class DrilldownWindow : Window
{
    public ObservableCollection<DrilldownRowView> Rows { get; } = [];

    public DrilldownWindow(string title, IEnumerable<CompletionRecord> records)
    {
        InitializeComponent();
        DataContext = this;
        TitleText.Text = title;
        var ordered = records.OrderBy(record => record.Completion).ThenBy(record => record.SourceRowNumber).ToList();
        foreach (var record in ordered) Rows.Add(new DrilldownRowView(record));
        ConfigureColumns(ordered);
        CountText.Text = $"{Rows.Count:N0} completion row{(Rows.Count == 1 ? "" : "s")}  •  Each row is one original CSV completion event.";
    }

    private void ConfigureColumns(IReadOnlyList<CompletionRecord> records)
    {
        var headers = records
            .SelectMany(record => record.SourceValues.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var header in headers)
        {
            DetailsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Width = new DataGridLength(GetColumnWidth(header)),
                Binding = new Binding($"Fields[{header}]")
                {
                    Mode = BindingMode.OneWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.Default
                }
            });
        }
    }

    private static double GetColumnWidth(string header)
    {
        if (header.Equals("Assigned User", StringComparison.OrdinalIgnoreCase)) return 210;
        if (header.Equals("Last Completion", StringComparison.OrdinalIgnoreCase)) return 180;
        if (header.Equals("ID", StringComparison.OrdinalIgnoreCase) || header.Equals("Ticket ID", StringComparison.OrdinalIgnoreCase)) return 140;
        return 180;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
