using Atlas.Application.Abstractions;
using Atlas.Application.Settings;
using Atlas.Application.Storage;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using System.Diagnostics;

namespace Atlas.Api.Endpoints;

internal static class FileScanPolicy
{
    public const string TrustedUploadEngine = "trusted-upload";
    private const string ClamAvEngine = "clamav";

    public static async Task<FileScanApplyResult> ApplyAfterUploadCompleteAsync(
        FileAsset file,
        IAdminSettingService settings,
        IObjectStorageService storage,
        IHttpClientFactory httpClientFactory,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var mode = await ResolveModeAsync(settings, file.OrganizationId, cancellationToken);
        if (mode == FileScanMode.External)
        {
            file.ScanStatus = FileScanStatus.Pending;
            file.ScanEngine = null;
            file.ScanCompletedAt = null;
            return new FileScanApplyResult(mode, null);
        }

        if (mode == FileScanMode.ClamAv)
        {
            var scanResult = await ScanWithClamAvAsync(
                file,
                settings,
                storage,
                httpClientFactory,
                clock,
                cancellationToken);
            if (scanResult.Problem is not null)
            {
                return new FileScanApplyResult(mode, scanResult.Problem);
            }

            return new FileScanApplyResult(mode, null);
        }

        file.ScanStatus = FileScanStatus.Clean;
        file.ScanEngine = TrustedUploadEngine;
        file.ScanCompletedAt = clock.UtcNow;
        return new FileScanApplyResult(mode, null);
    }

    private static async Task<FileScanMode> ResolveModeAsync(
        IAdminSettingService settings,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var configured = EndpointHelpers.ReadStringSetting(
                (await settings.GetAsync(organizationId, "fileScanning", "mode", cancellationToken))?.ValueJson)
            ?? EndpointHelpers.ReadStringSetting(
                (await settings.GetAsync(null, "fileScanning", "mode", cancellationToken))?.ValueJson)
            ?? "clamav";

        return configured.Trim().ToLowerInvariant() switch
        {
            "external" or "scanner" or "strict" => FileScanMode.External,
            "trusted" or "trust" or "none" or "disabled" => FileScanMode.Trusted,
            _ => FileScanMode.ClamAv
        };
    }

    private static async Task<FileScanApplyResult> ScanWithClamAvAsync(
        FileAsset file,
        IAdminSettingService settings,
        IObjectStorageService storage,
        IHttpClientFactory httpClientFactory,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var scannerPath = EndpointHelpers.ReadStringSetting(
                (await settings.GetAsync(file.OrganizationId, "fileScanning", "clamAvCommand", cancellationToken))?.ValueJson)
            ?? EndpointHelpers.ReadStringSetting(
                (await settings.GetAsync(null, "fileScanning", "clamAvCommand", cancellationToken))?.ValueJson)
            ?? FindExecutable("clamdscan")
            ?? FindExecutable("clamscan");

        if (string.IsNullOrWhiteSpace(scannerPath))
        {
            file.ScanStatus = FileScanStatus.Pending;
            file.ScanEngine = ClamAvEngine;
            file.ScanCompletedAt = null;
            return new FileScanApplyResult(
                FileScanMode.ClamAv,
                EndpointHelpers.Problem(
                    "file_scanner_unavailable",
                    "ClamAV scanner is not available on the API host.",
                    StatusCodes.Status503ServiceUnavailable));
        }

        var timeoutSeconds = EndpointHelpers.ReadPositiveIntSetting(
            (await settings.GetAsync(file.OrganizationId, "fileScanning", "timeoutSeconds", cancellationToken))?.ValueJson
                ?? (await settings.GetAsync(null, "fileScanning", "timeoutSeconds", cancellationToken))?.ValueJson,
            30);

        var tempDirectory = Path.Combine(Path.GetTempPath(), "reqara-file-scan");
        Directory.CreateDirectory(tempDirectory);
        var tempPath = Path.Combine(tempDirectory, $"{file.Id:N}{SafeExtension(file.Extension)}");

        try
        {
            var signedDownload = await storage.CreateDownloadUrlAsync(
                new PresignedDownloadRequest(
                    file.StorageKey,
                    file.OriginalFileName,
                    file.MimeType,
                    TimeSpan.FromMinutes(2)),
                cancellationToken);

            using var client = httpClientFactory.CreateClient();
            using var response = await client.GetAsync(
                signedDownload.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new FileScanApplyResult(
                    FileScanMode.ClamAv,
                    EndpointHelpers.Problem(
                        "file_scan_download_failed",
                        "Uploaded file could not be downloaded for scanning.",
                        StatusCodes.Status503ServiceUnavailable));
            }

            await using (var destination = File.Create(tempPath))
            {
                await response.Content.CopyToAsync(destination, cancellationToken);
            }

            var scan = await RunClamAvAsync(scannerPath, tempPath, timeoutSeconds, cancellationToken);
            file.ScanEngine = ClamAvEngine;
            file.ScanCompletedAt = clock.UtcNow;
            if (scan.ExitCode == 0)
            {
                file.ScanStatus = FileScanStatus.Clean;
                return new FileScanApplyResult(FileScanMode.ClamAv, null);
            }

            if (scan.ExitCode == 1)
            {
                file.ScanStatus = FileScanStatus.Rejected;
                return new FileScanApplyResult(FileScanMode.ClamAv, null);
            }

            file.ScanStatus = FileScanStatus.Pending;
            file.ScanCompletedAt = null;
            return new FileScanApplyResult(
                FileScanMode.ClamAv,
                EndpointHelpers.Problem(
                    "file_scan_failed",
                    "ClamAV could not complete the file scan.",
                    StatusCodes.Status503ServiceUnavailable,
                    scan.Output));
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private static async Task<ClamAvProcessResult> RunClamAvAsync(
        string scannerPath,
        string filePath,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var executableName = Path.GetFileName(scannerPath);
        var arguments = executableName.Equals("clamdscan", StringComparison.OrdinalIgnoreCase)
            ? $"--no-summary --fdpass {Quote(filePath)}"
            : $"--no-summary {Quote(filePath)}";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = scannerPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new ClamAvProcessResult(2, "ClamAV scan timed out.");
        }

        var output = string.Join(
            Environment.NewLine,
            new[] { await stdout, await stderr }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new ClamAvProcessResult(process.ExitCode, output);
    }

    private static string? FindExecutable(string name)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];

        foreach (var path in paths)
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(path, name + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string SafeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 20)
        {
            return ".bin";
        }

        return extension.StartsWith('.') ? extension : "." + extension;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
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
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal enum FileScanMode
{
    Trusted,
    External,
    ClamAv
}

internal sealed record FileScanApplyResult(FileScanMode Mode, IResult? Problem);

internal sealed record ClamAvProcessResult(int ExitCode, string Output);
