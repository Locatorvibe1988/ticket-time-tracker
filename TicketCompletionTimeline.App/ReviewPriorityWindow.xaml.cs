using System.Windows;
using System.Windows.Media;
using TicketCompletionTimeline.Core;

namespace TicketCompletionTimeline.App;

public partial class ReviewPriorityWindow : Window
{
    public string BandText { get; }
    public Brush BandBrush { get; }
    public IReadOnlyList<ReviewPriorityEvidenceView> Evidence { get; }

    public ReviewPriorityWindow(UserReviewPriority priority)
    {
        InitializeComponent();
        BandText = priority.Band switch
        {
            ReviewPriorityBand.Low => "Low",
            ReviewPriorityBand.Moderate => "Moderate",
            ReviewPriorityBand.High => "High",
            _ => "Insufficient data"
        };
        BandBrush = PriorityPalette.ToBrush(priority.Band);
        Evidence = priority.Evidence.Select(item => new ReviewPriorityEvidenceView(item)).ToList();
        DataContext = this;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class ReviewPriorityEvidenceView
{
    public string Title { get; }
    public string Detail { get; }
    public string PointsText { get; }

    public ReviewPriorityEvidenceView(ReviewPriorityEvidence evidence)
    {
        Title = evidence.Title;
        Detail = evidence.Date.HasValue ? $"{evidence.Detail}  •  {evidence.Date:MMM d, yyyy}" : evidence.Detail;
        PointsText = evidence.Points > 0 ? $"+{evidence.Points} review point(s)" : string.Empty;
    }
}
