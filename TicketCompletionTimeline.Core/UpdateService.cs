using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TicketCompletionTimeline.Core;

/// <summary>
/// The small piece of release metadata that the application needs before it
/// can offer an update. The ZIP is never trusted because of its filename.
/// DownloadUrl and Sha256 are validated before a package is returned.
/// </summary>
public sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("releaseNotes")] string ReleaseNotes,
    [property: JsonPropertyName("minimumSupportedVersion")] string? MinimumSupportedVersion = null);

public sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version AvailableVersion,
    UpdateManifest Manifest)
{
    public bool IsUpdateAvailable => AvailableVersion > CurrentVersion;
}

public sealed record DownloadedUpdate(string PackagePath, string WorkingDirectory);

/// <summary>
/// Fetches and verifies update metadata and packages. Keeping this in Core
/// makes version comparison and checksum behavior testable without starting
/// the WPF application or launching an updater process.
/// </summary>
public sealed class UpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public UpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<UpdateCheckResult> CheckAsync(Uri manifestUri, Version currentVersion, CancellationToken cancellationToken = default)
    {
        EnsureHttps(manifestUri, "The update manifest must use HTTPS.");
        using var response = await _httpClient.GetAsync(manifestUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var manifest = ParseManifest(json);
        var availableVersion = ParseVersion(manifest.Version, "version");
        return new UpdateCheckResult(currentVersion, availableVersion, manifest);
    }

    public async Task<DownloadedUpdate> DownloadAndVerifyAsync(UpdateManifest manifest, CancellationToken cancellationToken = default)
    {
        var downloadUri = new Uri(manifest.DownloadUrl, UriKind.Absolute);
        EnsureHttps(downloadUri, "The update package must use HTTPS.");
        var expectedHash = NormalizeHash(manifest.Sha256);

        var workingDirectory = Path.Combine(Path.GetTempPath(), "TicketTimeTracker", "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        var packagePath = Path.Combine(workingDirectory, "update-package.zip");

        try
        {
            using var response = await _httpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(packagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            await using var package = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(package, cancellationToken));
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded update failed its SHA-256 checksum verification.");

            return new DownloadedUpdate(packagePath, workingDirectory);
        }
        catch
        {
            TryDeleteDirectory(workingDirectory);
            throw;
        }
    }

    public static UpdateManifest ParseManifest(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new FormatException("The update manifest is empty.");
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions)
            ?? throw new FormatException("The update manifest is not valid JSON.");

        _ = ParseVersion(manifest.Version, "version");
        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri))
            throw new FormatException("The update manifest contains an invalid download URL.");
        EnsureHttps(downloadUri, "The update package must use HTTPS.");
        _ = NormalizeHash(manifest.Sha256);
        if (!string.IsNullOrWhiteSpace(manifest.MinimumSupportedVersion))
            _ = ParseVersion(manifest.MinimumSupportedVersion, "minimumSupportedVersion");
        return manifest;
    }

    public static Version ParseVersion(string? value, string fieldName)
    {
        if (!Version.TryParse(value, out var version) || version < new Version(0, 0))
            throw new FormatException($"The update manifest contains an invalid {fieldName}.");
        return version;
    }

    private static string NormalizeHash(string? hash)
    {
        var normalized = hash?.Trim() ?? string.Empty;
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new FormatException("The update manifest must contain a 64-character SHA-256 checksum.");
        return normalized.ToUpperInvariant();
    }

    private static void EnsureHttps(Uri uri, string message)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new FormatException(message);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // A failed cleanup must not hide the original network or checksum error.
        }
    }
}
