using System.Globalization;
using System.Text.Json;
using Atlas.Api.Email;
using Atlas.Api.Endpoints;
using Atlas.Application.Abstractions;
using Atlas.Application.Email;
using Atlas.Application.Settings;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Services;

public sealed class ReminderDispatcherService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReminderDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromMinutes(1);
            try
            {
                using var scope = scopeFactory.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<IAdminSettingService>();
                var intervalSeconds = EndpointHelpers.ReadPositiveIntSetting(
                    (await settings.GetAsync(null, "reminders", "dispatchIntervalSeconds", stoppingToken))?.ValueJson,
                    60);
                delay = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 15, 3600));

                await DispatchDueRemindersAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automatic reminder dispatch failed.");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private static async Task DispatchDueRemindersAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<AtlasDbContext>();
        var emailService = services.GetRequiredService<IEmailService>();
        var settings = services.GetRequiredService<IAdminSettingService>();
        var secretHasher = services.GetRequiredService<ISecretHasher>();
        var clock = services.GetRequiredService<IAtlasClock>();
        var configuration = services.GetRequiredService<IConfiguration>();

        var now = clock.UtcNow;
        var schedules = await dbContext.ReminderSchedules.IgnoreQueryFilters()
            .Include(item => item.ActionRecipient)
            .ThenInclude(item => item!.Action)
            .ThenInclude(item => item!.Organization)
            .Include(item => item.ActionRecipient)
            .ThenInclude(item => item!.Action)
            .ThenInclude(item => item!.CreatedByUser)
            .Where(item => item.Status == ReminderStatus.Pending && item.TriggerAt <= now)
            .OrderBy(item => item.TriggerAt)
            .Take(25)
            .ToListAsync(cancellationToken);

        foreach (var schedule in schedules)
        {
            var recipient = schedule.ActionRecipient;
            var action = recipient?.Action;
            if (recipient is null || action?.Organization is null || action.CreatedByUser is null)
            {
                schedule.Status = ReminderStatus.Failed;
                schedule.AttemptCount += 1;
                continue;
            }

            if (recipient.Status == ActionRecipientStatus.Submitted
                || action.Status is ChecklistActionStatus.Cancelled or ChecklistActionStatus.Completed or ChecklistActionStatus.Expired)
            {
                schedule.Status = ReminderStatus.Cancelled;
                continue;
            }

            var graceDaysSetting = await settings.GetAsync(action.OrganizationId, "security", "recipientTokenGraceDays", cancellationToken)
                ?? await settings.GetAsync(action.OrganizationId, "security", "recipientTokenDays", cancellationToken);
            var graceDays = EndpointHelpers.ReadPositiveIntSetting(graceDaysSetting?.ValueJson, 30);
            action.ExpiresAt ??= (action.DueAt ?? now).AddDays(graceDays);
            if (action.ExpiresAt <= now)
            {
                schedule.Status = ReminderStatus.Cancelled;
                action.Status = ChecklistActionStatus.Expired;
                recipient.Status = ActionRecipientStatus.Expired;
                continue;
            }

            var rawToken = EndpointHelpers.NewOpaqueToken();
            recipient.AccessTokenHash = secretHasher.HashSecret(rawToken);
            recipient.TokenExpiresAt = action.ExpiresAt;
            recipient.LastActivityAt = now;
            var link = await BuildRecipientLinkAsync(settings, configuration, action.OrganizationId, rawToken, cancellationToken);
            var templateKind = schedule.Type == ReminderType.Overdue
                ? ChecklistEmailTemplateKind.Overdue
                : ChecklistEmailTemplateKind.Reminder;
            var email = TransactionalEmailTemplates.Checklist(
                FirstName(recipient.Name),
                action.CreatedByUser.FullName,
                action.Organization.Name,
                action.Title,
                FormatDate(action.DueAt),
                FormatDate(recipient.TokenExpiresAt ?? action.ExpiresAt),
                link,
                templateKind);

            var sendResult = await emailService.SendAsync(
                recipient.Email,
                email.Subject,
                email.TextBody,
                email.HtmlBody,
                "requests@reqara.com",
                $"{action.Organization.Name} via Reqara",
                cancellationToken,
                new Dictionary<string, string>
                {
                    ["X-Atlas-Email-Type"] = schedule.Type == ReminderType.Overdue ? "recipient-overdue" : "recipient-reminder",
                    ["X-Atlas-Action-Id"] = action.Id.ToString(),
                    ["X-Atlas-Recipient-Id"] = recipient.Id.ToString()
                },
                action.CreatedByUser.Email);

            schedule.AttemptCount += 1;
            schedule.Status = sendResult.Sent ? ReminderStatus.Sent : ReminderStatus.Failed;
            schedule.SentAt = sendResult.Sent ? now : null;
            dbContext.NotificationDeliveries.Add(new NotificationDelivery
            {
                OrganizationId = action.OrganizationId,
                ActionRecipientId = recipient.Id,
                Channel = DeliveryChannel.Email,
                TemplateKey = schedule.Type == ReminderType.Overdue ? "recipient_overdue" : "recipient_reminder",
                ProviderMessageId = sendResult.MessageId,
                Status = sendResult.Sent ? NotificationDeliveryStatus.Delivered : NotificationDeliveryStatus.Failed,
                ErrorCode = Truncate(sendResult.Error, 100),
                SentAt = sendResult.Sent ? now : null,
                DeliveredAt = sendResult.Sent ? now : null,
                CreatedAt = now
            });
            dbContext.AuditEvents.Add(new AuditEvent
            {
                OrganizationId = action.OrganizationId,
                ActionId = action.Id,
                ActorType = ActorType.System,
                EventType = sendResult.Sent ? "reminder.auto_sent" : "reminder.auto_send_failed",
                EventData = JsonSerializer.Serialize(new
                {
                    ScheduleId = schedule.Id,
                    RecipientId = recipient.Id,
                    schedule.Type,
                    TokenPrefix = EndpointHelpers.TokenLogPrefix(rawToken),
                    sendResult.Error
                }, EndpointHelpers.JsonOptions),
                CreatedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<string> BuildRecipientLinkAsync(
        IAdminSettingService settings,
        IConfiguration configuration,
        Guid organizationId,
        string rawToken,
        CancellationToken cancellationToken)
    {
        var configuredBaseUrl = EndpointHelpers.ReadStringSetting(
                (await settings.GetAsync(organizationId, "app", "baseUrl", cancellationToken))?.ValueJson)
            ?? configuration["App:BaseUrl"]
            ?? configuration["APP_BASE_URL"]
            ?? "https://reqara.com";
        return $"{configuredBaseUrl.TrimEnd('/')}/c/{rawToken}";
    }

    private static string FirstName(string fullName)
    {
        var trimmed = fullName.Trim();
        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? trimmed[..space] : trimmed;
    }

    private static string FormatDate(DateTimeOffset? value)
    {
        return value is null
            ? "Not set"
            : value.Value.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        return value is null || value.Length <= maxLength ? value : value[..maxLength];
    }
}
