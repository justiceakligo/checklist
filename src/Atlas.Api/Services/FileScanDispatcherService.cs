using System.Text.Json;
using Atlas.Api.Endpoints;
using Atlas.Application.Abstractions;
using Atlas.Application.Settings;
using Atlas.Application.Storage;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Services;

public sealed class FileScanDispatcherService(
    IServiceScopeFactory scopeFactory,
    ILogger<FileScanDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(30);
            try
            {
                using var scope = scopeFactory.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<IAdminSettingService>();
                var intervalSeconds = EndpointHelpers.ReadPositiveIntSetting(
                    (await settings.GetAsync(null, "fileScanning", "dispatchIntervalSeconds", stoppingToken))?.ValueJson,
                    30);
                delay = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 10, 3600));

                await ScanPendingFilesAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Pending file scan dispatch failed.");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task ScanPendingFilesAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<AtlasDbContext>();
        var settings = services.GetRequiredService<IAdminSettingService>();
        var storage = services.GetRequiredService<IObjectStorageService>();
        var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
        var clock = services.GetRequiredService<IAtlasClock>();
        var pendingFiles = await dbContext.FileAssets.IgnoreQueryFilters()
            .Where(item => item.DeletedAt == null && item.ScanStatus == FileScanStatus.Pending)
            .OrderBy(item => item.CreatedAt)
            .Take(25)
            .ToListAsync(cancellationToken);

        foreach (var file in pendingFiles)
        {
            var metadata = await storage.GetObjectMetadataAsync(file.StorageKey, cancellationToken);
            if (metadata is null)
            {
                var missingUploadGraceMinutes = EndpointHelpers.ReadPositiveIntSetting(
                    (await settings.GetAsync(file.OrganizationId, "fileScanning", "missingUploadGraceMinutes", cancellationToken))?.ValueJson
                        ?? (await settings.GetAsync(null, "fileScanning", "missingUploadGraceMinutes", cancellationToken))?.ValueJson,
                    15);
                if (file.CreatedAt <= clock.UtcNow.AddMinutes(-missingUploadGraceMinutes))
                {
                    file.ScanStatus = FileScanStatus.Rejected;
                    file.ScanEngine = "upload-missing";
                    file.ScanCompletedAt = clock.UtcNow;
                    AddScanAudit(dbContext, file, clock);
                }

                continue;
            }

            if (metadata.SizeBytes != file.SizeBytes)
            {
                file.ScanStatus = FileScanStatus.Rejected;
                file.ScanEngine = "size-mismatch";
                file.ScanCompletedAt = clock.UtcNow;
                AddScanAudit(dbContext, file, clock);
                continue;
            }

            var scan = await FileScanPolicy.ApplyAfterUploadCompleteAsync(
                file,
                settings,
                storage,
                httpClientFactory,
                clock,
                cancellationToken);
            if (scan.Problem is not null)
            {
                logger.LogWarning(
                    "Pending file scan failed for file {FileId} in organization {OrganizationId}.",
                    file.Id,
                    file.OrganizationId);
                continue;
            }

            if (file.ScanStatus != FileScanStatus.Pending)
            {
                AddScanAudit(dbContext, file, clock);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void AddScanAudit(
        AtlasDbContext dbContext,
        FileAsset file,
        IAtlasClock clock)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = file.OrganizationId,
            ActionId = file.ActionId,
            ActorType = ActorType.System,
            EventType = "file.scan_completed",
            EventData = JsonSerializer.Serialize(new
            {
                file.Id,
                file.ScanStatus,
                file.ScanEngine
            }, EndpointHelpers.JsonOptions),
            CreatedAt = clock.UtcNow
        });
    }
}
