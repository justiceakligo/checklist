using System.Text.Json;
using Atlas.Api.Endpoints;
using Atlas.Application.Abstractions;
using Atlas.Application.Settings;
using Atlas.Application.Storage;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Services;

public sealed class RetentionPurgeService(
    IServiceScopeFactory scopeFactory,
    ILogger<RetentionPurgeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromHours(1);
            try
            {
                using var scope = scopeFactory.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<IAdminSettingService>();
                var intervalMinutes = EndpointHelpers.ReadPositiveIntSetting(
                    (await settings.GetAsync(null, "retention", "purgeIntervalMinutes", stoppingToken))?.ValueJson,
                    60);
                delay = TimeSpan.FromMinutes(Math.Clamp(intervalMinutes, 5, 1440));

                await PurgeExpiredFilesAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Retention purge failed.");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private static async Task PurgeExpiredFilesAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<AtlasDbContext>();
        var storage = services.GetRequiredService<IObjectStorageService>();
        var clock = services.GetRequiredService<IAtlasClock>();
        var now = clock.UtcNow;
        var expiredFiles = await dbContext.FileAssets.IgnoreQueryFilters()
            .Where(item => item.DeletedAt == null
                && item.RetentionUntil != null
                && item.RetentionUntil <= now)
            .OrderBy(item => item.RetentionUntil)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var file in expiredFiles)
        {
            await storage.DeleteObjectAsync(file.StorageKey, cancellationToken);
            file.DeletedAt = now;
            dbContext.AuditEvents.Add(new()
            {
                OrganizationId = file.OrganizationId,
                ActionId = file.ActionId,
                ActorType = ActorType.System,
                EventType = "file.retention_deleted",
                EventData = JsonSerializer.Serialize(new { file.Id, file.RetentionUntil }, EndpointHelpers.JsonOptions),
                CreatedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
