using System.IO;
using System.Text.Json;

namespace TicketCompletionTimeline.App;

internal static class UpdateConfiguration
{
    private const string FileName = "update-config.json";

    public static Uri? TryGetManifestUri(out string? error)
    {
        error = null;
        var path = Path.Combine(AppContext.BaseDirectory, FileName);
        if (!File.Exists(path))
        {
            error = $"Create {FileName} beside the application and set its manifestUrl value.";
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("manifestUrl", out var value))
            {
                error = $"Add a manifestUrl value to {FileName}.";
                return null;
            }

            var text = value.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                error = $"Set manifestUrl in {FileName} before checking for updates.";
                return null;
            }

            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                error = "The update manifest URL must be an HTTPS URL.";
                return null;
            }

            return uri;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            error = $"The update configuration could not be read: {exception.Message}";
            return null;
        }
    }
}
