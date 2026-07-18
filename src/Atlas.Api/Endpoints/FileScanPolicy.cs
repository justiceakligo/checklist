using Atlas.Application.Abstractions;
using Atlas.Application.Settings;
using Atlas.Application.Storage;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Atlas.Api.Endpoints;

internal static class FileScanPolicy
{
    public const string TrustedUploadEngine = "trusted-upload";
    private const string ClamAvEngine = "clamav";
    private const string ContentValidationEngine = "content-validation";

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

            file.Sha256Hash = await ComputeSha256Async(tempPath, cancellationToken);
            if (!await ContentMatchesDeclaredTypeAsync(tempPath, file.MimeType, cancellationToken))
            {
                file.ScanStatus = FileScanStatus.Rejected;
                file.ScanEngine = ContentValidationEngine;
                file.ScanCompletedAt = clock.UtcNow;
                return new FileScanApplyResult(FileScanMode.ClamAv, null);
            }

            var scan = await RunClamAvAsync(scannerPath, tempPath, timeoutSeconds, cancellationToken);
            var scannerName = Path.GetFileName(scannerPath);
            if (scan.ExitCode is not (0 or 1)
                && scannerName.Equals("clamdscan", StringComparison.OrdinalIgnoreCase)
                && FindExecutable("clamscan") is { } fallbackScanner
                && !string.Equals(fallbackScanner, scannerPath, StringComparison.OrdinalIgnoreCase))
            {
                scan = await RunClamAvAsync(fallbackScanner, tempPath, timeoutSeconds, cancellationToken);
                scannerName = Path.GetFileName(fallbackScanner);
            }

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

    private static async Task<byte[]> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return await sha256.ComputeHashAsync(stream, cancellationToken);
    }

    private static async Task<bool> ContentMatchesDeclaredTypeAsync(
        string path,
        string declaredMimeType,
        CancellationToken cancellationToken)
    {
        var prefix = await ReadPrefixAsync(path, 4096, cancellationToken);
        if (prefix.Length == 0)
        {
            return false;
        }

        var mimeType = NormalizeMimeType(declaredMimeType);
        return mimeType switch
        {
            "application/pdf" => StartsWithAscii(prefix, "%PDF-"),
            "image/png" => StartsWithBytes(prefix, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
            "image/jpeg" or "image/jpg" => prefix.Length >= 3 && prefix[0] == 0xFF && prefix[1] == 0xD8 && prefix[2] == 0xFF,
            "image/gif" => StartsWithAscii(prefix, "GIF87a") || StartsWithAscii(prefix, "GIF89a"),
            "image/webp" => prefix.Length >= 12 && StartsWithAscii(prefix, "RIFF") && StartsWithAscii(prefix[8..], "WEBP"),
            "application/json" => IsJsonLike(prefix),
            "text/plain" or "text/csv" => IsLikelyText(prefix),
            "application/zip" => IsZipLike(prefix),
            "application/msword" or "application/vnd.ms-excel" or "application/vnd.ms-powerpoint" => IsOleCompoundFile(prefix),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => IsZipLike(prefix),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => IsZipLike(prefix),
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => IsZipLike(prefix),
            _ when mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) => IsLikelyText(prefix),
            _ => true
        };
    }

    private static async Task<byte[]> ReadPrefixAsync(string path, int maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[maxBytes];
        await using var stream = File.OpenRead(path);
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        return buffer[..bytesRead];
    }

    private static string NormalizeMimeType(string value)
    {
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()?
            .ToLowerInvariant() ?? string.Empty;
    }

    private static bool StartsWithAscii(ReadOnlySpan<byte> value, string prefix)
    {
        if (value.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (value[i] != (byte)prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsWithBytes(ReadOnlySpan<byte> value, params byte[] prefix)
    {
        if (value.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (value[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsZipLike(ReadOnlySpan<byte> value)
    {
        return value.Length >= 4
            && value[0] == 0x50
            && value[1] == 0x4B
            && ((value[2] == 0x03 && value[3] == 0x04)
                || (value[2] == 0x05 && value[3] == 0x06)
                || (value[2] == 0x07 && value[3] == 0x08));
    }

    private static bool IsOleCompoundFile(ReadOnlySpan<byte> value)
    {
        return StartsWithBytes(value, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1);
    }

    private static bool IsJsonLike(ReadOnlySpan<byte> value)
    {
        if (!IsLikelyText(value))
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(value).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return text.StartsWith('{') || text.StartsWith('[');
    }

    private static bool IsLikelyText(ReadOnlySpan<byte> value)
    {
        if (LooksLikeKnownBinary(value) || value.Contains((byte)0))
        {
            return false;
        }

        try
        {
            _ = new UTF8Encoding(false, true).GetString(value);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool LooksLikeKnownBinary(ReadOnlySpan<byte> value)
    {
        return StartsWithAscii(value, "%PDF-")
            || StartsWithBytes(value, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
            || (value.Length >= 3 && value[0] == 0xFF && value[1] == 0xD8 && value[2] == 0xFF)
            || StartsWithAscii(value, "GIF87a")
            || StartsWithAscii(value, "GIF89a")
            || (value.Length >= 12 && StartsWithAscii(value, "RIFF") && StartsWithAscii(value[8..], "WEBP"))
            || IsZipLike(value)
            || IsOleCompoundFile(value);
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
