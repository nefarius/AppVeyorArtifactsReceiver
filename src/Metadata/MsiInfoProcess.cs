using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace AppVeyorArtifactsReceiver.Metadata;

/// <summary>
///     Exports an MSI Property table by running <c>msiinfo</c> without a shell.
/// </summary>
internal sealed class MsiInfoProcess(string executableName = "msiinfo") : IMsiPropertyTableExporter
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<MsiPropertyTableExport> ExportAsync(string packagePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = new()
        {
            FileName = executableName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("export");
        startInfo.ArgumentList.Add(packagePath);
        startInfo.ArgumentList.Add("Property");

        using Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return MsiPropertyTableExport.Failed("Failed to start msiinfo.");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            return MsiPropertyTableExport.Failed($"msiinfo is not available: {ex.Message}");
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeoutCts = new(Timeout);
        using CancellationTokenSource linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await DrainAsync(stdoutTask, stderrTask);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await DrainAsync(stdoutTask, stderrTask);
            return MsiPropertyTableExport.Failed("msiinfo timed out.");
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(stderr)
                ? $"msiinfo exited with code {process.ExitCode}."
                : stderr.Trim();
            return MsiPropertyTableExport.Failed(detail);
        }

        return MsiPropertyTableExport.Success(stdout);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process may already have exited.
        }
    }

    private static async Task DrainAsync(Task<string> stdoutTask, Task<string> stderrTask)
    {
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch
        {
            // Output is irrelevant once the process has been stopped.
        }
    }
}
