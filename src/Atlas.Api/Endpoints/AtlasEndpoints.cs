using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Email;
using Atlas.Application.Settings;
using Atlas.Application.Storage;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net;

namespace Atlas.Api.Endpoints;

public static class AtlasEndpoints
{
    public static IEndpointRouteBuilder MapAtlasEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        v1.MapGet("/health", () => Results.Ok(new { status = "ok", service = "atlas-api" }))
            .WithTags("Health");

        MapAuthAndOrganization(v1);
        MapAdminSettings(v1);
        MapTemplates(v1);
        MapActions(v1);
        MapFiles(v1);

        return app;
    }

    private static void MapAuthAndOrganization(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("").WithTags("Auth & org");

        group.MapPost("/auth/signup", async (
            SignupRequest request,
            AtlasDbContext dbContext,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.Password)
                || string.IsNullOrWhiteSpace(request.FullName)
                || string.IsNullOrWhiteSpace(request.OrganizationName)
                || string.IsNullOrWhiteSpace(request.OrganizationSlug))
            {
                return EndpointHelpers.Problem(
                    "validation_failed",
                    "Email, password, full name, organization name and slug are required.",
                    StatusCodes.Status422UnprocessableEntity);
            }

            var email = request.Email.Trim().ToLowerInvariant();
            var slug = request.OrganizationSlug.Trim().ToLowerInvariant();

            var emailExists = await dbContext.Users.IgnoreQueryFilters()
                .AnyAsync(user => user.Email == email, cancellationToken);
            if (emailExists)
            {
                return EndpointHelpers.Problem("email_in_use", "Email is already in use.", StatusCodes.Status409Conflict);
            }

            var slugExists = await dbContext.Organizations.IgnoreQueryFilters()
                .AnyAsync(organization => organization.Slug == slug, cancellationToken);
            if (slugExists)
            {
                return EndpointHelpers.Problem("slug_in_use", "Organization slug is already in use.", StatusCodes.Status409Conflict);
            }

            var now = clock.UtcNow;
            var user = new AppUser
            {
                Email = email,
                FullName = request.FullName.Trim(),
                CreatedAt = now
            };
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, request.Password);

            var organization = new Organization
            {
                Name = request.OrganizationName.Trim(),
                Slug = slug,
                Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "UTC" : request.Timezone.Trim(),
                DefaultLanguage = string.IsNullOrWhiteSpace(request.DefaultLanguage) ? "en" : request.DefaultLanguage.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            };

            var membership = new OrganizationUser
            {
                Organization = organization,
                User = user,
                Role = OrganizationUserRole.Owner,
                Status = MembershipStatus.Active,
                JoinedAt = now,
                CreatedAt = now
            };

            dbContext.AddRange(user, organization, membership);
            await dbContext.SaveChangesAsync(cancellationToken);
            await SecurityEndpoints.SignInDashboardUserAsync(httpContext, user);

            return Results.Created($"/v1/organizations/{organization.Id}", new SignupResponse(
                user.Id,
                organization.Id,
                organization.Slug,
                membership.Role));
        });

        group.MapGet("/organizations/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (organizationId != id)
            {
                return EndpointHelpers.Problem("forbidden", "The organization context does not match the route.", StatusCodes.Status403Forbidden);
            }

            var organization = await dbContext.Organizations
                .AsNoTracking()
                .Where(item => item.Id == id)
                .Select(item => new OrganizationResponse(
                    item.Id,
                    item.Name,
                    item.Slug,
                    item.Status,
                    item.Timezone,
                    item.DefaultLanguage,
                    item.AccentColor,
                    item.PrivacyStatement,
                    item.RetentionDays,
                    item.CreatedAt,
                    item.UpdatedAt))
                .FirstOrDefaultAsync(cancellationToken);

            return organization is null
                ? EndpointHelpers.Problem("not_found", "Organization was not found.", StatusCodes.Status404NotFound)
                : Results.Ok(organization);
        });

        group.MapPatch("/organizations/{id:guid}", async (
            Guid id,
            UpdateOrganizationRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (organizationId != id)
            {
                return EndpointHelpers.Problem("forbidden", "The organization context does not match the route.", StatusCodes.Status403Forbidden);
            }

            var organization = await dbContext.Organizations.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (organization is null)
            {
                return EndpointHelpers.Problem("not_found", "Organization was not found.", StatusCodes.Status404NotFound);
            }

            organization.Name = string.IsNullOrWhiteSpace(request.Name) ? organization.Name : request.Name.Trim();
            organization.Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? organization.Timezone : request.Timezone.Trim();
            organization.DefaultLanguage = string.IsNullOrWhiteSpace(request.DefaultLanguage)
                ? organization.DefaultLanguage
                : request.DefaultLanguage.Trim();
            organization.AccentColor = request.AccentColor ?? organization.AccentColor;
            organization.PrivacyStatement = request.PrivacyStatement ?? organization.PrivacyStatement;
            organization.RetentionDays = request.RetentionDays is > 0 ? request.RetentionDays.Value : organization.RetentionDays;

            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new OrganizationResponse(
                organization.Id,
                organization.Name,
                organization.Slug,
                organization.Status,
                organization.Timezone,
                organization.DefaultLanguage,
                organization.AccentColor,
                organization.PrivacyStatement,
                organization.RetentionDays,
                organization.CreatedAt,
                organization.UpdatedAt));
        });
    }

    private static void MapAdminSettings(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/admin/settings").WithTags("Admin settings");

        group.MapGet("", async (
            Guid? organizationId,
            IAdminSettingService settings,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var tenantOrganizationId, out var problem))
            {
                return problem!;
            }

            if (!EndpointHelpers.HasScope(tenantContext, "admin:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Admin scope is required.", StatusCodes.Status403Forbidden);
            }

            var effectiveOrganizationId = organizationId ?? tenantOrganizationId;
            var values = await settings.ListAsync(effectiveOrganizationId, cancellationToken);
            return Results.Ok(new { items = values });
        });

        group.MapPut("/{category}/{key}", async (
            string category,
            string key,
            UpsertAdminSettingHttpRequest request,
            IAdminSettingService settings,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var tenantOrganizationId, out var problem))
            {
                return problem!;
            }

            if (!EndpointHelpers.HasScope(tenantContext, "admin:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Admin scope is required.", StatusCodes.Status403Forbidden);
            }

            var organizationId = request.Scope == AdminSettingScope.Organization
                ? request.OrganizationId ?? tenantOrganizationId
                : (Guid?)null;

            var value = await settings.UpsertAsync(
                new UpsertAdminSettingRequest(
                    organizationId,
                    request.Scope,
                    category,
                    key,
                    request.Value.GetRawText(),
                    request.IsSecret,
                    request.UpdatedByUserId),
                cancellationToken);

            return Results.Ok(value);
        });
    }

    private static void MapTemplates(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/templates").WithTags("Templates");

        group.MapGet("", async (AtlasDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var templates = await dbContext.Templates
                .AsNoTracking()
                .OrderByDescending(template => template.CreatedAt)
                .Select(template => new TemplateResponse(
                    template.Id,
                    template.Name,
                    template.Category,
                    template.Description,
                    template.Status,
                    template.CurrentVersionId,
                    template.CreatedAt,
                    template.UpdatedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(new { items = templates });
        });

        group.MapPost("", async (
            CreateTemplateRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Title))
            {
                return EndpointHelpers.Problem(
                    "validation_failed",
                    "Template name and title are required.",
                    StatusCodes.Status422UnprocessableEntity);
            }

            var now = clock.UtcNow;
            var template = new Template
            {
                OrganizationId = organizationId,
                Name = request.Name.Trim(),
                Category = request.Category?.Trim(),
                Description = request.Description,
                Status = TemplateStatus.Draft,
                CreatedByUserId = request.CreatedByUserId,
                CreatedAt = now,
                UpdatedAt = now
            };

            var version = new TemplateVersion
            {
                Template = template,
                VersionNumber = 1,
                Title = request.Title.Trim(),
                Instructions = request.Instructions,
                SettingsJson = EndpointHelpers.JsonOrDefault(request.Settings),
                CreatedByUserId = request.CreatedByUserId,
                CreatedAt = now
            };

            foreach (var requirement in request.Requirements.OrderBy(item => item.DisplayOrder))
            {
                version.Requirements.Add(ToTemplateRequirement(requirement));
            }

            dbContext.Templates.Add(template);
            dbContext.TemplateVersions.Add(version);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Created($"/v1/templates/{template.Id}", new TemplateResponse(
                template.Id,
                template.Name,
                template.Category,
                template.Description,
                template.Status,
                template.CurrentVersionId,
                template.CreatedAt,
                template.UpdatedAt));
        });

        group.MapPost("/{id:guid}/publish", async (
            Guid id,
            AtlasDbContext dbContext,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            var template = await dbContext.Templates
                .Include(item => item.Versions)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (template is null)
            {
                return EndpointHelpers.Problem("not_found", "Template was not found.", StatusCodes.Status404NotFound);
            }

            var version = template.Versions.OrderByDescending(item => item.VersionNumber).FirstOrDefault();
            if (version is null)
            {
                return EndpointHelpers.Problem("validation_failed", "Template has no version to publish.", StatusCodes.Status422UnprocessableEntity);
            }

            version.PublishedAt ??= clock.UtcNow;
            template.CurrentVersionId = version.Id;
            template.Status = TemplateStatus.Published;

            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new TemplateResponse(
                template.Id,
                template.Name,
                template.Category,
                template.Description,
                template.Status,
                template.CurrentVersionId,
                template.CreatedAt,
                template.UpdatedAt));
        });
    }

    private static void MapActions(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/actions").WithTags("Actions");

        group.MapGet("", async (AtlasDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var actions = await dbContext.Actions
                .AsNoTracking()
                .OrderByDescending(action => action.CreatedAt)
                .Select(action => new ActionResponse(
                    action.Id,
                    action.PublicReference,
                    action.Title,
                    action.Status,
                    action.DueAt,
                    action.ExpiresAt,
                    null,
                    action.CreatedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(new { items = actions });
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var action = await dbContext.Actions
                .AsNoTracking()
                .Include(item => item.Recipients)
                .Include(item => item.Requirements)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (action is null)
            {
                return EndpointHelpers.Problem("not_found", "Action was not found.", StatusCodes.Status404NotFound);
            }

            return Results.Ok(new ActionDetailResponse(
                action.Id,
                action.PublicReference,
                action.Title,
                action.Description,
                action.Status,
                action.DueAt,
                action.ExpiresAt,
                action.SentAt,
                action.CompletedAt,
                action.Recipients.Select(item => new ActionRecipientResponse(item.Id, item.Name, item.Email, item.Phone, item.Status, item.OtpRequired, item.FirstViewedAt, item.StartedAt, item.SubmittedAt)).ToList(),
                action.Requirements.OrderBy(item => item.DisplayOrder).Select(item => new ActionRequirementResponse(item.Id, item.Key, item.Type, item.Label, item.IsRequired, item.DisplayOrder)).ToList(),
                action.CreatedAt,
                action.UpdatedAt));
        });

        group.MapPost("", async (
            CreateActionRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAdminSettingService settings,
            IEmailService emailService,
            IConfiguration configuration,
            ISecretHasher secretHasher,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (request.CreatedByUserId == Guid.Empty
                || string.IsNullOrWhiteSpace(request.Title)
                || request.Recipient is null
                || string.IsNullOrWhiteSpace(request.Recipient.Email)
                || string.IsNullOrWhiteSpace(request.Recipient.Name))
            {
                return EndpointHelpers.Problem(
                    "validation_failed",
                    "Title, creator and recipient name/email are required.",
                    StatusCodes.Status422UnprocessableEntity);
            }

            var sender = await dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(user => user.Id == request.CreatedByUserId, cancellationToken);
            if (sender is null)
            {
                return EndpointHelpers.Problem("sender_not_found", "Checklist sender was not found.", StatusCodes.Status422UnprocessableEntity);
            }

            var senderIsMember = await dbContext.OrganizationUsers
                .AnyAsync(item => item.OrganizationId == organizationId
                    && item.UserId == request.CreatedByUserId
                    && item.Status == MembershipStatus.Active,
                    cancellationToken);
            if (!senderIsMember)
            {
                return EndpointHelpers.Problem("forbidden", "Checklist sender must be an active organization member.", StatusCodes.Status403Forbidden);
            }

            var organization = await dbContext.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == organizationId, cancellationToken);
            if (organization is null)
            {
                return EndpointHelpers.Problem("not_found", "Organization was not found.", StatusCodes.Status404NotFound);
            }

            var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
            var requestHash = EndpointHelpers.ComputeRequestHash(request);
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var existing = await dbContext.IdempotencyRecords
                    .FirstOrDefaultAsync(item => item.Key == idempotencyKey, cancellationToken);
                if (existing is not null)
                {
                    if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                    {
                        return EndpointHelpers.Problem("idempotency_conflict", "Idempotency key was already used with a different request.", StatusCodes.Status409Conflict);
                    }

                    var replay = JsonSerializer.Deserialize<JsonElement>(existing.ResponseJson, EndpointHelpers.JsonOptions);
                    return Results.Json(replay, statusCode: existing.StatusCode);
                }
            }

            var graceDaysSetting = await settings.GetAsync(organizationId, "security", "recipientTokenGraceDays", cancellationToken)
                ?? await settings.GetAsync(organizationId, "security", "recipientTokenDays", cancellationToken);
            var graceDays = EndpointHelpers.ReadPositiveIntSetting(graceDaysSetting?.ValueJson, 30);
            var now = clock.UtcNow;
            var rawToken = EndpointHelpers.NewOpaqueToken();
            var expiresAt = request.ExpiresAt ?? (request.DueAt ?? now).AddDays(graceDays);

            var action = new ChecklistAction
            {
                OrganizationId = organizationId,
                TemplateId = request.TemplateId,
                Title = request.Title.Trim(),
                Description = request.Description,
                PublicReference = EndpointHelpers.NewPublicReference(),
                Status = ChecklistActionStatus.Draft,
                DueAt = request.DueAt,
                ExpiresAt = expiresAt,
                SentAt = null,
                CreatedByUserId = request.CreatedByUserId,
                SettingsJson = EndpointHelpers.JsonOrDefault(request.Settings),
                CreatedAt = now,
                UpdatedAt = now
            };

            var recipient = new ActionRecipient
            {
                Action = action,
                Name = request.Recipient.Name.Trim(),
                Email = request.Recipient.Email.Trim().ToLowerInvariant(),
                Phone = request.Recipient.Phone,
                OtpRequired = request.Recipient.OtpRequired,
                AccessTokenHash = secretHasher.HashSecret(rawToken),
                TokenExpiresAt = action.ExpiresAt,
                CreatedAt = now
            };

            action.Recipients.Add(recipient);
            await AddRequirementSnapshotsAsync(action, request, dbContext, cancellationToken);

            dbContext.Actions.Add(action);
            SecurityEndpoints.AddAudit(dbContext, organizationId, action.Id, tenantContext, "action.created", new { action.Id, action.Title }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);

            var recipientUrl = await BuildRecipientLinkAsync(settings, configuration, organizationId, httpContext, rawToken, cancellationToken);
            if (request.SendImmediately)
            {
                var send = await SendChecklistEmailAsync(
                    action,
                    recipient,
                    sender,
                    organization,
                    recipientUrl,
                    rawToken,
                    ChecklistEmailKind.Invitation,
                    emailService,
                    clock,
                    cancellationToken);
                AddNotificationDelivery(dbContext, organizationId, recipient.Id, "recipient_invitation", send.Result, clock);
                if (!send.Result.Sent)
                {
                    SecurityEndpoints.AddAudit(dbContext, organizationId, action.Id, tenantContext, "action.send_failed", new { actionId = action.Id, recipientId = recipient.Id, send.TokenPrefix, send.Result.Error }, httpContext);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return EndpointHelpers.Problem("email_send_failed", "Checklist invitation email could not be sent.", StatusCodes.Status503ServiceUnavailable);
                }

                action.Status = ChecklistActionStatus.Sent;
                action.SentAt = clock.UtcNow;
                dbContext.UsageEvents.Add(new UsageEvent
                {
                    OrganizationId = organizationId,
                    ActionId = action.Id,
                    EventType = "action_sent",
                    Quantity = 1,
                    Unit = "action",
                    IdempotencyKey = $"action_sent:{action.Id:N}",
                    OccurredAt = clock.UtcNow,
                    CreatedAt = clock.UtcNow
                });
                SecurityEndpoints.AddAudit(dbContext, organizationId, action.Id, tenantContext, "action.sent", new { actionId = action.Id, recipientId = recipient.Id, send.TokenPrefix }, httpContext);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var response = new ActionResponse(
                action.Id,
                action.PublicReference,
                action.Title,
                action.Status,
                action.DueAt,
                action.ExpiresAt,
                recipientUrl,
                action.CreatedAt);

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                dbContext.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    OrganizationId = organizationId,
                    Key = idempotencyKey,
                    RequestHash = requestHash,
                    StatusCode = StatusCodes.Status201Created,
                    ResponseJson = JsonSerializer.Serialize(response, EndpointHelpers.JsonOptions),
                    CreatedAt = clock.UtcNow,
                    ExpiresAt = clock.UtcNow.AddHours(24)
                });
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return Results.Created($"/v1/actions/{action.Id}", response);
        });

        group.MapPost("/{id:guid}/send", async (
            Guid id,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAdminSettingService settings,
            IEmailService emailService,
            IConfiguration configuration,
            ISecretHasher secretHasher,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            var action = await dbContext.Actions
                .Include(item => item.Organization)
                .Include(item => item.CreatedByUser)
                .Include(item => item.Recipients)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (action?.Organization is null || action.CreatedByUser is null)
            {
                return EndpointHelpers.Problem("not_found", "Action was not found.", StatusCodes.Status404NotFound);
            }

            if (action.Status is not (ChecklistActionStatus.Draft or ChecklistActionStatus.Sent))
            {
                return EndpointHelpers.Problem("state_conflict", "Only draft or sent actions can be sent.", StatusCodes.Status409Conflict);
            }

            var wasAlreadySent = action.SentAt.HasValue;
            var graceDaysSetting = await settings.GetAsync(organizationId, "security", "recipientTokenGraceDays", cancellationToken)
                ?? await settings.GetAsync(organizationId, "security", "recipientTokenDays", cancellationToken);
            var graceDays = EndpointHelpers.ReadPositiveIntSetting(graceDaysSetting?.ValueJson, 30);
            action.ExpiresAt ??= (action.DueAt ?? clock.UtcNow).AddDays(graceDays);
            var recipientTokens = new List<(ActionRecipient Recipient, string RawToken, string Link)>();
            foreach (var recipient in action.Recipients)
            {
                var rawToken = EndpointHelpers.NewOpaqueToken();
                recipient.AccessTokenHash = secretHasher.HashSecret(rawToken);
                recipient.TokenExpiresAt = action.ExpiresAt;
                var recipientLink = await BuildRecipientLinkAsync(settings, configuration, organizationId, httpContext, rawToken, cancellationToken);
                recipientTokens.Add((recipient, rawToken, recipientLink));
            }
            await dbContext.SaveChangesAsync(cancellationToken);

            var sendResults = new List<ChecklistEmailSendResult>();
            foreach (var item in recipientTokens)
            {
                var send = await SendChecklistEmailAsync(
                    action,
                    item.Recipient,
                    action.CreatedByUser,
                    action.Organization,
                    item.Link,
                    item.RawToken,
                    ChecklistEmailKind.Invitation,
                    emailService,
                    clock,
                    cancellationToken);
                sendResults.Add(send);
                AddNotificationDelivery(dbContext, organizationId, item.Recipient.Id, "recipient_invitation", send.Result, clock);
            }

            if (sendResults.Any(item => !item.Result.Sent))
            {
                SecurityEndpoints.AddAudit(
                    dbContext,
                    organizationId,
                    action.Id,
                    tenantContext,
                    "action.send_failed",
                    new { action.Id, failures = sendResults.Where(item => !item.Result.Sent).Select(item => new { item.TokenPrefix, item.Result.Error }) },
                    httpContext);
                await dbContext.SaveChangesAsync(cancellationToken);
                return EndpointHelpers.Problem("email_send_failed", "One or more checklist invitation emails could not be sent.", StatusCodes.Status503ServiceUnavailable);
            }

            action.Status = ChecklistActionStatus.Sent;
            action.SentAt ??= clock.UtcNow;

            if (!wasAlreadySent)
            {
                dbContext.UsageEvents.Add(new UsageEvent
                {
                    OrganizationId = organizationId,
                    ActionId = action.Id,
                    EventType = "action_sent",
                    Quantity = 1,
                    Unit = "action",
                    IdempotencyKey = $"action_sent:{action.Id:N}",
                    OccurredAt = clock.UtcNow,
                    CreatedAt = clock.UtcNow
                });
            }
            SecurityEndpoints.AddAudit(dbContext, organizationId, action.Id, tenantContext, "action.sent", new { action.Id, recipients = sendResults.Select(item => new { item.RecipientId, item.TokenPrefix }) }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Accepted($"/v1/actions/{action.Id}", new { sent = sendResults.Count });
        });

        group.MapPost("/{id:guid}/cancel", async (
            Guid id,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            var action = await dbContext.Actions.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (action is null)
            {
                return EndpointHelpers.Problem("not_found", "Action was not found.", StatusCodes.Status404NotFound);
            }

            if (action.Status is ChecklistActionStatus.Completed or ChecklistActionStatus.Cancelled)
            {
                return EndpointHelpers.Problem("state_conflict", "Action cannot be cancelled in its current state.", StatusCodes.Status409Conflict);
            }

            action.Status = ChecklistActionStatus.Cancelled;
            action.CancelledAt = clock.UtcNow;
            SecurityEndpoints.AddAudit(dbContext, organizationId, action.Id, tenantContext, "action.cancelled", new { action.Id }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { action.Id, action.Status, action.CancelledAt });
        });

        group.MapPost("/{id:guid}/reminders", async (
            Guid id,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAdminSettingService settings,
            IEmailService emailService,
            IConfiguration configuration,
            ISecretHasher secretHasher,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            var action = await dbContext.Actions
                .Include(item => item.Organization)
                .Include(item => item.CreatedByUser)
                .Include(item => item.Recipients)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (action?.Organization is null || action.CreatedByUser is null)
            {
                return EndpointHelpers.Problem("not_found", "Action was not found.", StatusCodes.Status404NotFound);
            }

            var pendingRecipients = action.Recipients
                .Where(item => item.Status != ActionRecipientStatus.Submitted)
                .ToList();
            if (pendingRecipients.Count == 0)
            {
                return EndpointHelpers.Problem("state_conflict", "There are no pending recipients to remind.", StatusCodes.Status409Conflict);
            }

            var graceDaysSetting = await settings.GetAsync(organizationId, "security", "recipientTokenGraceDays", cancellationToken)
                ?? await settings.GetAsync(organizationId, "security", "recipientTokenDays", cancellationToken);
            var graceDays = EndpointHelpers.ReadPositiveIntSetting(graceDaysSetting?.ValueJson, 30);
            action.ExpiresAt ??= (action.DueAt ?? clock.UtcNow).AddDays(graceDays);
            var recipientTokens = new List<(ActionRecipient Recipient, string RawToken, string Link, ReminderSchedule Schedule)>();
            foreach (var recipient in pendingRecipients)
            {
                var rawToken = EndpointHelpers.NewOpaqueToken();
                recipient.AccessTokenHash = secretHasher.HashSecret(rawToken);
                recipient.TokenExpiresAt = action.ExpiresAt;
                var recipientLink = await BuildRecipientLinkAsync(settings, configuration, organizationId, httpContext, rawToken, cancellationToken);
                var schedule = new ReminderSchedule
                {
                    ActionRecipientId = recipient.Id,
                    Type = ReminderType.Manual,
                    Status = ReminderStatus.Pending,
                    TriggerAt = clock.UtcNow,
                    CreatedAt = clock.UtcNow
                };
                dbContext.ReminderSchedules.Add(schedule);
                recipientTokens.Add((recipient, rawToken, recipientLink, schedule));
            }
            await dbContext.SaveChangesAsync(cancellationToken);

            var kind = action.DueAt is not null && action.DueAt.Value < clock.UtcNow
                ? ChecklistEmailKind.Overdue
                : ChecklistEmailKind.Reminder;
            var sendResults = new List<ChecklistEmailSendResult>();
            foreach (var item in recipientTokens)
            {
                var send = await SendChecklistEmailAsync(
                    action,
                    item.Recipient,
                    action.CreatedByUser,
                    action.Organization,
                    item.Link,
                    item.RawToken,
                    kind,
                    emailService,
                    clock,
                    cancellationToken);
                sendResults.Add(send);
                AddNotificationDelivery(dbContext, organizationId, item.Recipient.Id, kind == ChecklistEmailKind.Overdue ? "recipient_overdue" : "recipient_reminder", send.Result, clock);
                item.Schedule.Status = send.Result.Sent ? ReminderStatus.Sent : ReminderStatus.Failed;
                item.Schedule.SentAt = send.Result.Sent ? clock.UtcNow : null;
                item.Schedule.AttemptCount = 1;
            }

            var failed = sendResults.Where(item => !item.Result.Sent).ToList();
            SecurityEndpoints.AddAudit(
                dbContext,
                organizationId,
                action.Id,
                tenantContext,
                failed.Count == 0 ? "reminder.sent" : "reminder.send_failed",
                new
                {
                    action.Id,
                    sent = sendResults.Count - failed.Count,
                    failed = failed.Count,
                    recipients = sendResults.Select(item => new { item.RecipientId, item.TokenPrefix })
                },
                httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return failed.Count == 0
                ? Results.Accepted($"/v1/actions/{action.Id}/timeline", new { sent = sendResults.Count })
                : EndpointHelpers.Problem("email_send_failed", "One or more reminder emails could not be sent.", StatusCodes.Status503ServiceUnavailable);
        });

        group.MapGet("/{id:guid}/timeline", async (
            Guid id,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var events = await dbContext.AuditEvents
                .Where(item => item.ActionId == id)
                .OrderBy(item => item.CreatedAt)
                .Select(item => new AuditEventResponse(item.Id, item.EventType, item.ActorType, item.ActorId, item.EventData, item.CreatedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(new { items = events });
        });
    }

    private static void MapFiles(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/files").WithTags("Files");

        group.MapPost("/upload-intents", async (
            UploadIntentRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAdminSettingService settings,
            IObjectStorageService storage,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (string.IsNullOrWhiteSpace(request.FileName)
                || string.IsNullOrWhiteSpace(request.MimeType)
                || request.SizeBytes <= 0)
            {
                return EndpointHelpers.Problem(
                    "validation_failed",
                    "File name, MIME type and positive size are required.",
                    StatusCodes.Status422UnprocessableEntity);
            }

            var maxUploadSetting = await settings.GetAsync(organizationId, "files", "maxUploadBytes", cancellationToken);
            var maxUploadBytes = EndpointHelpers.ReadPositiveIntSetting(maxUploadSetting?.ValueJson, 10 * 1024 * 1024);
            if (request.SizeBytes > maxUploadBytes)
            {
                return EndpointHelpers.Problem("upload_too_large", "Upload exceeds configured size limit.", StatusCodes.Status413PayloadTooLarge);
            }

            var uploadMinutesSetting = await settings.GetAsync(organizationId, "files", "uploadUrlMinutes", cancellationToken);
            var uploadMinutes = EndpointHelpers.ReadPositiveIntSetting(uploadMinutesSetting?.ValueJson, 10);
            var fileId = Guid.NewGuid();
            var storageKey = storage.BuildQuarantineKey(organizationId, fileId, request.FileName);
            var now = clock.UtcNow;

            var fileAsset = new FileAsset
            {
                Id = fileId,
                OrganizationId = organizationId,
                ActionId = request.ActionId,
                ActionRecipientId = request.ActionRecipientId,
                StorageKey = storageKey,
                OriginalFileName = request.FileName,
                MimeType = request.MimeType,
                Extension = Path.GetExtension(request.FileName),
                SizeBytes = request.SizeBytes,
                ScanStatus = FileScanStatus.Pending,
                CreatedAt = now
            };

            dbContext.FileAssets.Add(fileAsset);
            await dbContext.SaveChangesAsync(cancellationToken);

            var signedUrl = await storage.CreateUploadUrlAsync(
                new PresignedUploadRequest(
                    storageKey,
                    request.MimeType,
                    request.SizeBytes,
                    TimeSpan.FromMinutes(uploadMinutes)),
                cancellationToken);

            return Results.Created($"/v1/files/{fileId}", new UploadIntentResponse(
                fileId,
                storageKey,
                signedUrl.UploadUrl,
                signedUrl.Headers,
                signedUrl.ExpiresAt));
        });

        group.MapGet("/{id:guid}/download-url", async (
            Guid id,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAdminSettingService settings,
            IObjectStorageService storage,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var file = await dbContext.FileAssets.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (file is null)
            {
                return EndpointHelpers.Problem("not_found", "File was not found.", StatusCodes.Status404NotFound);
            }

            if (file.ScanStatus != FileScanStatus.Clean)
            {
                return EndpointHelpers.Problem(
                    "file_not_available",
                    "File is not available for download until it passes scanning.",
                    StatusCodes.Status409Conflict);
            }

            var downloadMinutesSetting = await settings.GetAsync(file.OrganizationId, "files", "downloadUrlMinutes", cancellationToken);
            var downloadMinutes = EndpointHelpers.ReadPositiveIntSetting(downloadMinutesSetting?.ValueJson, 5);
            var signedUrl = await storage.CreateDownloadUrlAsync(
                new PresignedDownloadRequest(
                    file.StorageKey,
                    file.OriginalFileName,
                    file.MimeType,
                    TimeSpan.FromMinutes(downloadMinutes)),
                cancellationToken);

            return Results.Ok(signedUrl);
        });

        group.MapPost("/{id:guid}/complete", async (
            Guid id,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IObjectStorageService storage,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (!EndpointHelpers.HasScope(tenantContext, "files:*"))
            {
                return EndpointHelpers.Problem("forbidden", "File scope is required.", StatusCodes.Status403Forbidden);
            }

            var file = await dbContext.FileAssets.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (file is null)
            {
                return EndpointHelpers.Problem("not_found", "File was not found.", StatusCodes.Status404NotFound);
            }

            var metadata = await storage.GetObjectMetadataAsync(file.StorageKey, cancellationToken);
            if (metadata is null)
            {
                return EndpointHelpers.Problem("upload_not_found", "Uploaded object was not found in storage.", StatusCodes.Status409Conflict);
            }

            if (metadata.SizeBytes != file.SizeBytes)
            {
                return EndpointHelpers.Problem("upload_mismatch", "Uploaded object size does not match the declared size.", StatusCodes.Status409Conflict);
            }

            file.ScanStatus = FileScanStatus.Pending;
            SecurityEndpoints.AddAudit(dbContext, organizationId, file.ActionId, tenantContext, "file.uploaded", new { file.Id, file.OriginalFileName, file.SizeBytes }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new FileStatusResponse(file.Id, file.ScanStatus, file.ScanCompletedAt));
        });

        group.MapPost("/{id:guid}/scan-result", async (
            Guid id,
            FileScanResultRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (!EndpointHelpers.HasScope(tenantContext, "files:*"))
            {
                return EndpointHelpers.Problem("forbidden", "File scope is required.", StatusCodes.Status403Forbidden);
            }

            if (request.ScanStatus is not (FileScanStatus.Clean or FileScanStatus.Rejected))
            {
                return EndpointHelpers.Problem("validation_failed", "Scan result must be Clean or Rejected.", StatusCodes.Status422UnprocessableEntity);
            }

            var file = await dbContext.FileAssets.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (file is null)
            {
                return EndpointHelpers.Problem("not_found", "File was not found.", StatusCodes.Status404NotFound);
            }

            file.ScanStatus = request.ScanStatus;
            file.ScanEngine = string.IsNullOrWhiteSpace(request.ScanEngine) ? "external" : request.ScanEngine.Trim();
            file.ScanCompletedAt = clock.UtcNow;
            SecurityEndpoints.AddAudit(dbContext, organizationId, file.ActionId, tenantContext, "file.scan_completed", new { file.Id, file.ScanStatus, file.ScanEngine }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new FileStatusResponse(file.Id, file.ScanStatus, file.ScanCompletedAt));
        });
    }

    private static TemplateRequirement ToTemplateRequirement(RequirementDefinitionRequest request)
    {
        return new TemplateRequirement
        {
            Key = request.Key.Trim(),
            Type = request.Type,
            Label = request.Label.Trim(),
            Description = request.Description,
            IsRequired = request.Required,
            DisplayOrder = request.DisplayOrder,
            ConfigurationJson = EndpointHelpers.JsonOrDefault(request.Configuration),
            ValidationJson = EndpointHelpers.JsonOrDefault(request.Validation),
            ConditionJson = request.Condition is null ? null : request.Condition.Value.GetRawText()
        };
    }

    private static async Task AddRequirementSnapshotsAsync(
        ChecklistAction action,
        CreateActionRequest request,
        AtlasDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (request.TemplateId.HasValue)
        {
            var template = await dbContext.Templates
                .Include(item => item.CurrentVersion)
                .ThenInclude(version => version!.Requirements)
                .FirstOrDefaultAsync(item => item.Id == request.TemplateId.Value, cancellationToken);

            if (template?.CurrentVersion is not null)
            {
                action.TemplateVersionId = template.CurrentVersion.Id;
                foreach (var requirement in template.CurrentVersion.Requirements.OrderBy(item => item.DisplayOrder))
                {
                    action.Requirements.Add(new Requirement
                    {
                        SourceTemplateRequirementId = requirement.Id,
                        Key = requirement.Key,
                        Type = requirement.Type,
                        Label = requirement.Label,
                        Description = requirement.Description,
                        IsRequired = requirement.IsRequired,
                        DisplayOrder = requirement.DisplayOrder,
                        ConfigurationJson = requirement.ConfigurationJson,
                        ValidationJson = requirement.ValidationJson,
                        ConditionJson = requirement.ConditionJson
                    });
                }
                return;
            }
        }

        foreach (var requirement in request.Requirements.OrderBy(item => item.DisplayOrder))
        {
            action.Requirements.Add(new Requirement
            {
                Key = requirement.Key.Trim(),
                Type = requirement.Type,
                Label = requirement.Label.Trim(),
                Description = requirement.Description,
                IsRequired = requirement.Required,
                DisplayOrder = requirement.DisplayOrder,
                ConfigurationJson = EndpointHelpers.JsonOrDefault(requirement.Configuration),
                ValidationJson = EndpointHelpers.JsonOrDefault(requirement.Validation),
                ConditionJson = requirement.Condition is null ? null : requirement.Condition.Value.GetRawText()
            });
        }
    }

    private static async Task<string> BuildRecipientLinkAsync(
        IAdminSettingService settings,
        IConfiguration configuration,
        Guid organizationId,
        HttpContext httpContext,
        string rawToken,
        CancellationToken cancellationToken)
    {
        var configuredBaseUrl = ReadStringSetting((await settings.GetAsync(organizationId, "app", "baseUrl", cancellationToken))?.ValueJson)
            ?? configuration["App:BaseUrl"]
            ?? configuration["APP_BASE_URL"];
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}"
            : configuredBaseUrl.Trim();

        return $"{baseUrl.TrimEnd('/')}/c/{rawToken}";
    }

    private static async Task<ChecklistEmailSendResult> SendChecklistEmailAsync(
        ChecklistAction action,
        ActionRecipient recipient,
        AppUser sender,
        Organization organization,
        string recipientLink,
        string rawToken,
        ChecklistEmailKind kind,
        IEmailService emailService,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var subject = kind switch
        {
            ChecklistEmailKind.Reminder => $"Reminder: {organization.Name} is waiting on a few items",
            ChecklistEmailKind.Overdue => $"Your checklist from {organization.Name} is past due",
            _ => $"{organization.Name} needs a few things from you"
        };
        var recipientFirstName = FirstName(recipient.Name);
        var dueDate = FormatDate(action.DueAt);
        var expiresAt = FormatDate(recipient.TokenExpiresAt ?? action.ExpiresAt);
        var encodedLink = WebUtility.HtmlEncode(recipientLink);
        var encodedRecipient = WebUtility.HtmlEncode(recipientFirstName);
        var encodedSender = WebUtility.HtmlEncode(sender.FullName);
        var encodedOrganization = WebUtility.HtmlEncode(organization.Name);
        var encodedTitle = WebUtility.HtmlEncode(action.Title);
        var encodedDueDate = WebUtility.HtmlEncode(dueDate);
        var encodedExpiresAt = WebUtility.HtmlEncode(expiresAt);

        var textBody =
            $"Hi {recipientFirstName},\n\n" +
            $"{sender.FullName} at {organization.Name} has requested some information\n" +
            "and documents from you.\n\n" +
            $"Request: {action.Title}\n" +
            $"Due: {dueDate}\n\n" +
            "Open your secure checklist:\n" +
            $"{recipientLink}\n\n" +
            "You don't need an account. The link is private to you - please don't forward it.\n" +
            $"It expires on {expiresAt}.\n\n" +
            $"If you have questions, reply to this email to reach {sender.FullName} directly.";

        var htmlBody =
            $"""
            <!doctype html>
            <html>
            <body style="font-family:Arial,sans-serif;color:#1f2937;line-height:1.5">
              <p>Hi {encodedRecipient},</p>
              <p>{encodedSender} at {encodedOrganization} has requested some information and documents from you.</p>
              <p><strong>Request:</strong> {encodedTitle}<br><strong>Due:</strong> {encodedDueDate}</p>
              <p>
                <a href="{encodedLink}" style="display:inline-block;background:#111827;color:#ffffff;text-decoration:none;padding:12px 18px;border-radius:6px;font-weight:700">Open your checklist</a>
              </p>
              <p>If the button does not work, open this link:<br><a href="{encodedLink}">{encodedLink}</a></p>
              <p>You don't need an account. This link is private to you - please don't forward it. It expires on {encodedExpiresAt}.</p>
              <p>If you have questions, reply to this email to reach {encodedSender} directly.</p>
              <p style="font-size:12px;color:#6b7280">This link is private to you. If you didn't expect this email, you can safely ignore it.</p>
            </body>
            </html>
            """;

        var result = await emailService.SendAsync(
            recipient.Email,
            subject,
            textBody,
            htmlBody,
            "requests@projectatlas.app",
            $"{organization.Name} via Project Atlas",
            cancellationToken,
            new Dictionary<string, string>
            {
                ["X-Atlas-Email-Type"] = kind switch
                {
                    ChecklistEmailKind.Reminder => "recipient-reminder",
                    ChecklistEmailKind.Overdue => "recipient-overdue",
                    _ => "recipient-invitation"
                },
                ["X-Atlas-Action-Id"] = action.Id.ToString(),
                ["X-Atlas-Recipient-Id"] = recipient.Id.ToString()
            },
            sender.Email);

        return new ChecklistEmailSendResult(recipient.Id, EndpointHelpers.TokenLogPrefix(rawToken), result);
    }

    private static void AddNotificationDelivery(
        AtlasDbContext dbContext,
        Guid organizationId,
        Guid recipientId,
        string templateKey,
        EmailSendResult result,
        IAtlasClock clock)
    {
        dbContext.NotificationDeliveries.Add(new NotificationDelivery
        {
            OrganizationId = organizationId,
            ActionRecipientId = recipientId,
            Channel = DeliveryChannel.Email,
            TemplateKey = templateKey,
            ProviderMessageId = result.MessageId,
            Status = result.Sent ? NotificationDeliveryStatus.Delivered : NotificationDeliveryStatus.Failed,
            ErrorCode = Truncate(result.Error, 100),
            SentAt = result.Sent ? clock.UtcNow : null,
            DeliveredAt = result.Sent ? clock.UtcNow : null,
            CreatedAt = clock.UtcNow
        });
    }

    private static string? ReadStringSetting(string? valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return null;
        }

        try
        {
            var value = JsonSerializer.Deserialize<JsonElement>(valueJson, EndpointHelpers.JsonOptions);
            return value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.GetRawText();
        }
        catch (JsonException)
        {
            return valueJson;
        }
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

    private enum ChecklistEmailKind
    {
        Invitation,
        Reminder,
        Overdue
    }

    private sealed record ChecklistEmailSendResult(Guid RecipientId, string TokenPrefix, EmailSendResult Result);
}

public sealed record SignupRequest(
    string Email,
    string Password,
    string FullName,
    string OrganizationName,
    string OrganizationSlug,
    string? Timezone,
    string? DefaultLanguage);

public sealed record SignupResponse(
    Guid UserId,
    Guid OrganizationId,
    string OrganizationSlug,
    OrganizationUserRole Role);

public sealed record OrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    OrganizationStatus Status,
    string Timezone,
    string DefaultLanguage,
    string? AccentColor,
    string? PrivacyStatement,
    int RetentionDays,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpdateOrganizationRequest(
    string? Name,
    string? Timezone,
    string? DefaultLanguage,
    string? AccentColor,
    string? PrivacyStatement,
    int? RetentionDays);

public sealed record UpsertAdminSettingHttpRequest(
    AdminSettingScope Scope,
    JsonElement Value,
    bool IsSecret,
    Guid? OrganizationId,
    Guid? UpdatedByUserId);

public sealed record CreateTemplateRequest(
    string Name,
    string? Category,
    string? Description,
    string Title,
    string? Instructions,
    JsonElement? Settings,
    Guid? CreatedByUserId,
    IReadOnlyList<RequirementDefinitionRequest> Requirements);

public sealed record RequirementDefinitionRequest(
    string Key,
    RequirementType Type,
    string Label,
    string? Description,
    bool Required,
    int DisplayOrder,
    JsonElement? Configuration,
    JsonElement? Validation,
    JsonElement? Condition);

public sealed record TemplateResponse(
    Guid Id,
    string Name,
    string? Category,
    string? Description,
    TemplateStatus Status,
    Guid? CurrentVersionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateActionRequest(
    Guid? TemplateId,
    string Title,
    string? Description,
    RecipientRequest Recipient,
    DateTimeOffset? DueAt,
    DateTimeOffset? ExpiresAt,
    JsonElement? Settings,
    bool SendImmediately,
    Guid CreatedByUserId,
    IReadOnlyList<RequirementDefinitionRequest> Requirements);

public sealed record RecipientRequest(
    string Name,
    string Email,
    string? Phone,
    bool OtpRequired);

public sealed record ActionResponse(
    Guid Id,
    string PublicReference,
    string Title,
    ChecklistActionStatus Status,
    DateTimeOffset? DueAt,
    DateTimeOffset? ExpiresAt,
    string? RecipientUrl,
    DateTimeOffset CreatedAt);

public sealed record ActionDetailResponse(
    Guid Id,
    string PublicReference,
    string Title,
    string? Description,
    ChecklistActionStatus Status,
    DateTimeOffset? DueAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ActionRecipientResponse> Recipients,
    IReadOnlyList<ActionRequirementResponse> Requirements,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ActionRecipientResponse(
    Guid Id,
    string Name,
    string Email,
    string? Phone,
    ActionRecipientStatus Status,
    bool OtpRequired,
    DateTimeOffset? FirstViewedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? SubmittedAt);

public sealed record ActionRequirementResponse(
    Guid Id,
    string Key,
    RequirementType Type,
    string Label,
    bool Required,
    int DisplayOrder);

public sealed record AuditEventResponse(
    Guid Id,
    string EventType,
    ActorType ActorType,
    string? ActorId,
    string EventData,
    DateTimeOffset CreatedAt);

public sealed record UploadIntentRequest(
    Guid RequirementId,
    Guid? ActionId,
    Guid? ActionRecipientId,
    string FileName,
    long SizeBytes,
    string MimeType);

public sealed record UploadIntentResponse(
    Guid FileId,
    string StorageKey,
    Uri UploadUrl,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset ExpiresAt);

public sealed record FileScanResultRequest(FileScanStatus ScanStatus, string? ScanEngine);
