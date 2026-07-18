using System.Text.Json;
using Atlas.Api.Endpoints;
using Atlas.Application.Abstractions;
using Atlas.Application.Email;
using Atlas.Application.Settings;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Services;

public sealed class DeliveryJobDispatcherService(
    IServiceScopeFactory scopeFactory,
    ILogger<DeliveryJobDispatcherService> logger) : BackgroundService
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
                    (await settings.GetAsync(null, "delivery", "dispatchIntervalSeconds", stoppingToken))?.ValueJson,
                    30);
                delay = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 10, 3600));

                await DispatchDueJobsAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Package delivery dispatch failed.");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private static async Task DispatchDueJobsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<AtlasDbContext>();
        var emailService = services.GetRequiredService<IEmailService>();
        var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
        var dataProtectionProvider = services.GetRequiredService<IDataProtectionProvider>();
        var clock = services.GetRequiredService<IAtlasClock>();
        var now = clock.UtcNow;

        var jobs = await dbContext.DeliveryJobs.IgnoreQueryFilters()
            .Include(item => item.Destination)
            .Include(item => item.Attempts)
            .Include(item => item.SubmissionPackage)
            .ThenInclude(package => package!.Action)
            .ThenInclude(action => action!.Organization)
            .Include(item => item.SubmissionPackage)
            .ThenInclude(package => package!.ActionRecipient)
            .Include(item => item.SubmissionPackage)
            .ThenInclude(package => package!.Submission)
            .ThenInclude(submission => submission!.ReviewedByUser)
            .Include(item => item.SubmissionPackage)
            .ThenInclude(package => package!.Submission)
            .ThenInclude(submission => submission!.Files)
            .ThenInclude(file => file.FileAsset)
            .Where(item => item.Status == DeliveryJobStatus.Queued
                || (item.Status == DeliveryJobStatus.RetryScheduled && item.NextRetryAt <= now))
            .OrderBy(item => item.QueuedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            if (job.SubmissionPackage is null)
            {
                job.Status = DeliveryJobStatus.RequiresAttention;
                job.FailureCode = "package_missing";
                job.FailureMessage = "The submission package for this delivery job no longer exists.";
                job.CompletedAt = now;
                continue;
            }

            if (job.Destination is null || !job.Destination.IsActive || job.Destination.Status != DestinationStatus.Active)
            {
                job.Status = DeliveryJobStatus.RequiresAttention;
                job.FailureCode = "destination_unavailable";
                job.FailureMessage = "The destination is missing, disabled, or invalid.";
                job.CompletedAt = now;
                AddSystemAudit(dbContext, job, "delivery.requires_attention", now);
                await UpdatePackageDeliveryStatusAsync(dbContext, job.SubmissionPackage, clock, cancellationToken);
                continue;
            }

            await PackageEndpoints.ProcessDeliveryJobAsync(
                job,
                job.SubmissionPackage,
                job.Destination,
                emailService,
                httpClientFactory,
                dataProtectionProvider,
                clock,
                cancellationToken);

            dbContext.UsageEvents.Add(new UsageEvent
            {
                OrganizationId = job.OrganizationId,
                ActionId = job.SubmissionPackage.ActionId,
                EventType = "delivery_job_executed",
                Quantity = 1,
                Unit = "delivery_job",
                IdempotencyKey = $"delivery_job_executed:{job.Id:N}:attempt:{job.AttemptCount}",
                OccurredAt = clock.UtcNow,
                CreatedAt = clock.UtcNow
            });
            AddSystemAudit(
                dbContext,
                job,
                job.Status == DeliveryJobStatus.Succeeded ? "delivery.succeeded" : "delivery.failed",
                clock.UtcNow);
            await UpdatePackageDeliveryStatusAsync(dbContext, job.SubmissionPackage, clock, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task UpdatePackageDeliveryStatusAsync(
        AtlasDbContext dbContext,
        SubmissionPackage package,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var statuses = await dbContext.DeliveryJobs.IgnoreQueryFilters()
            .Where(item => item.SubmissionPackageId == package.Id)
            .Select(item => item.Status)
            .ToListAsync(cancellationToken);
        if (statuses.Count == 0)
        {
            return;
        }

        if (statuses.All(item => item == DeliveryJobStatus.Succeeded))
        {
            package.Status = SubmissionPackageStatus.Delivered;
        }
        else if (statuses.Any(item => item == DeliveryJobStatus.Succeeded))
        {
            package.Status = SubmissionPackageStatus.PartiallyDelivered;
        }
        else if (statuses.Any(item => item is DeliveryJobStatus.Failed or DeliveryJobStatus.RequiresAttention))
        {
            package.Status = SubmissionPackageStatus.DeliveryFailed;
        }
        else
        {
            package.Status = SubmissionPackageStatus.RoutingPending;
        }

        package.UpdatedAt = clock.UtcNow;
    }

    private static void AddSystemAudit(
        AtlasDbContext dbContext,
        DeliveryJob job,
        string eventType,
        DateTimeOffset now)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = job.OrganizationId,
            ActionId = job.SubmissionPackage?.ActionId,
            ActorType = ActorType.System,
            EventType = eventType,
            EventData = JsonSerializer.Serialize(new
            {
                job.Id,
                job.SubmissionPackageId,
                job.DestinationId,
                job.Status,
                job.AttemptCount,
                job.FailureCode,
                job.FailureMessage
            }, EndpointHelpers.JsonOptions),
            CreatedAt = now
        });
    }
}
