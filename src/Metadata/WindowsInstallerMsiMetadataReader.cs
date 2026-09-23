using System.Runtime.CompilerServices;

#if WINDOWS
using WixToolset.Dtf.WindowsInstaller;
#endif

namespace AppVeyorArtifactsReceiver.Metadata;

internal sealed class WindowsInstallerMsiMetadataReader : IMsiMetadataReader
{
    public Task<MsiMetadataReadResult> ReadAsync(string packagePath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(MsiMetadataReadResult.Failed(
                "Windows Installer is unavailable on this operating system."));
        }

        return ReadWithWindowsInstallerAsync(packagePath, cancellationToken);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<MsiMetadataReadResult> ReadWithWindowsInstallerAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
#if WINDOWS
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Dictionary<string, string> properties = new(StringComparer.Ordinal);
            using Database database = new(packagePath, DatabaseOpenMode.ReadOnly);
            using View view = database.OpenView("SELECT `Property`, `Value` FROM `Property`");
            view.Execute();
            while (true)
            {
                Record record = view.Fetch();
                if (record == null)
                {
                    break;
                }

                using (record)
                {
                    string name = record.GetString(1);
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    properties[name] = record.GetString(2) ?? string.Empty;
                }
            }

            return Task.FromResult(MsiMetadataReadResult.Success(MsiArtifactMetadata.FromProperties(properties)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(MsiMetadataReadResult.Failed(ex.Message));
        }
#else
        _ = packagePath;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MsiMetadataReadResult.Failed(
            "Windows Installer is unavailable on this operating system."));
#endif
    }
}
