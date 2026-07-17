using Atlas.Application.Abstractions;
using Atlas.Application.Settings;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;

namespace Atlas.Api.Endpoints;

internal static class FileScanPolicy
{
    public const string TrustedUploadEngine = "trusted-upload";

    public static async Task<FileScanMode> ApplyAfterUploadCompleteAsync(
        FileAsset file,
        IAdminSettingService settings,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var mode = await ResolveModeAsync(settings, file.OrganizationId, cancellationToken);
        if (mode == FileScanMode.External)
        {
            file.ScanStatus = FileScanStatus.Pending;
            file.ScanEngine = null;
            file.ScanCompletedAt = null;
            return mode;
        }

        file.ScanStatus = FileScanStatus.Clean;
        file.ScanEngine = TrustedUploadEngine;
        file.ScanCompletedAt = clock.UtcNow;
        return mode;
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
            ?? "trusted";

        return configured.Trim().ToLowerInvariant() is "external" or "scanner" or "strict"
            ? FileScanMode.External
            : FileScanMode.Trusted;
    }
}

internal enum FileScanMode
{
    Trusted,
    External
}
