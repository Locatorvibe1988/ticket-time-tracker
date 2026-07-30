using System.Windows;
using TicketCompletionTimeline.Core;

namespace TicketCompletionTimeline.App;

public partial class UpdateWindow : Window
{
    public UpdateManifest Manifest { get; }

    public UpdateWindow(UpdateCheckResult result)
    {
        InitializeComponent();
        Manifest = result.Manifest;
        VersionText.Text = $"Version {result.CurrentVersion} → {result.AvailableVersion}";
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(Manifest.ReleaseNotes) ? "No release notes were provided." : Manifest.ReleaseNotes;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Install_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
