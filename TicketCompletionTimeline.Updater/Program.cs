using System.Diagnostics;
using System.IO.Compression;

namespace TicketCompletionTimeline.Updater;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!args.Contains("--worker", StringComparer.Ordinal))
            return LaunchDetachedWorker(args);

        var options = ParseArguments(args);
        try
        {
            await WaitForParentAsync(options.WaitPid);
            Install(options.PackagePath, options.TargetDirectory, options.RestartFileName);
            return 0;
        }
        catch
        {
            // The original install is restored before this method returns when
            // replacement fails. Leaving the process non-zero also makes the
            // updater diagnosable when it is run manually from PowerShell.
            return 1;
        }
    }

    private static int LaunchDetachedWorker(string[] originalArguments)
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("The updater process path is unavailable.");
        var workerDirectory = Path.Combine(Path.GetTempPath(), "TicketTimeTracker", "updater", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workerDirectory);
        var workerPath = Path.Combine(workerDirectory, Path.GetFileName(source));
        File.Copy(source, workerPath, overwrite: true);

        var startInfo = new ProcessStartInfo(workerPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workerDirectory
        };
        startInfo.ArgumentList.Add("--worker");
        foreach (var argument in originalArguments) startInfo.ArgumentList.Add(argument);
        Process.Start(startInfo);
        return 0;
    }

    private static UpdateArguments ParseArguments(string[] args)
    {
        string? package = null;
        string? target = null;
        string? restart = null;
        var waitPid = 0;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--install": package = NextValue(args, ref index); break;
                case "--target": target = NextValue(args, ref index); break;
                case "--restart": restart = NextValue(args, ref index); break;
                case "--wait-pid": waitPid = int.Parse(NextValue(args, ref index)); break;
                case "--worker": break;
                default: throw new ArgumentException($"Unknown updater argument: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(package) || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(restart) || waitPid <= 0)
            throw new ArgumentException("The updater requires --install, --target, --restart, and --wait-pid.");
        return new UpdateArguments(Path.GetFullPath(package), Path.GetFullPath(target), restart, waitPid);
    }

    private static string NextValue(string[] args, ref int index)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index])) throw new ArgumentException("An updater argument is missing its value.");
        return args[index];
    }

    private static async Task WaitForParentAsync(int processId)
    {
        try
        {
            using var parent = Process.GetProcessById(processId);
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (ArgumentException)
        {
            // The parent already exited, which is the state the updater needs.
        }
    }

    private static void Install(string packagePath, string targetDirectory, string restartFileName)
    {
        if (!File.Exists(packagePath)) throw new FileNotFoundException("The downloaded update package is missing.", packagePath);
        if (Path.GetFileName(restartFileName) != restartFileName) throw new ArgumentException("The restart file name must be a file name, not a path.");

        var workDirectory = Path.Combine(Path.GetTempPath(), "TicketTimeTracker", "install", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        var extractedDirectory = Path.Combine(workDirectory, "extracted");
        Directory.CreateDirectory(extractedDirectory);
        ZipFile.ExtractToDirectory(packagePath, extractedDirectory);

        var sourceExecutable = Directory.EnumerateFiles(extractedDirectory, restartFileName, SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidDataException($"The update package does not contain {restartFileName}.");
        var sourceDirectory = Path.GetDirectoryName(sourceExecutable) ?? throw new InvalidDataException("The update package has no application directory.");
        var targetParent = Directory.GetParent(targetDirectory)?.FullName ?? throw new InvalidOperationException("The application target has no parent directory.");
        Directory.CreateDirectory(targetParent);
        var backupDirectory = targetDirectory + ".backup-" + Guid.NewGuid().ToString("N");
        var failedDirectory = targetDirectory + ".failed-" + Guid.NewGuid().ToString("N");

        try
        {
            // Moving the old directory as one unit keeps the current release
            // recoverable. The app's archive is outside this directory, so it
            // survives regardless of whether this replacement succeeds.
            if (Directory.Exists(targetDirectory)) Directory.Move(targetDirectory, backupDirectory);
            Directory.Move(sourceDirectory, targetDirectory);
            Directory.Delete(backupDirectory, recursive: true);
        }
        catch
        {
            if (Directory.Exists(targetDirectory)) Directory.Move(targetDirectory, failedDirectory);
            if (Directory.Exists(backupDirectory)) Directory.Move(backupDirectory, targetDirectory);
            throw;
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
            TryDeleteFile(packagePath);
        }

        var restartPath = Path.Combine(targetDirectory, restartFileName);
        Process.Start(new ProcessStartInfo(restartPath) { UseShellExecute = true, WorkingDirectory = targetDirectory });
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record UpdateArguments(string PackagePath, string TargetDirectory, string RestartFileName, int WaitPid);
}
