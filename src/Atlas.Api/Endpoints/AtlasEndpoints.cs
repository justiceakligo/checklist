using System.Text.Json;
using Atlas.Api.Email;
using Atlas.Application.Abstractions;
using Atlas.Application.Billing;
using Atlas.Application.Email;
using Atlas.Application.Settings;
using Atlas.Application.Storage;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Atlas.Api.Endpoints;

public static class AtlasEndpoints
{
    public static IEndpointRouteBuilder MapAtlasEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        v1.MapGet("/health", () => Results.Ok(new { status = "ok", service = "atlas-api" }))
            .WithTags("Health");

        MapAuthAndOrganization(v1);
        MapBilling(v1);
        MapDashboard(v1);
        MapOrganizationMembers(v1);
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
            IAdminSettingService settings,
            IEmailService emailService,
            IConfiguration configuration,
            ISecretHasher secretHasher,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var fullName = request.ResolvedFullName();
            var organizationName = request.ResolvedOrganizationName();
            var requestedSlug = request.ResolvedOrganizationSlug();
            if (string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.Password)
                || string.IsNullOrWhiteSpace(fullName)
                || string.IsNullOrWhiteSpace(organizationName))
            {
                return EndpointHelpers.Problem(
                    "validation_failed",
                    "Email, password, full name and organization name are required.",
                    StatusCodes.Status422UnprocessableEntity);
            }

            var email = request.Email.Trim().ToLowerInvariant();
            var slugWasProvided = !string.IsNullOrWhiteSpace(requestedSlug);
            var slug = ToOrganizationSlug(slugWasProvided ? requestedSlug! : organizationName);
            var slugProblem = ValidateOrganizationSlug(slug);
            if (slugProblem is not null)
            {
                return slugProblem;
            }

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
                if (slugWasProvided)
                {
                    return EndpointHelpers.Problem("slug_in_use", "Organization slug is already in use.", StatusCodes.Status409Conflict);
                }

                slug = await NextAvailableOrganizationSlugAsync(dbContext, slug, cancellationToken);
            }

            var now = clock.UtcNow;
            var user = new AppUser
            {
                Email = email,
                FullName = fullName.Trim(),
                CreatedAt = now
            };
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, request.Password);

            var defaultRetentionDays = EndpointHelpers.ReadPositiveIntSetting(
                (await settings.GetAsync(null, "retention", "defaultRetentionDays", cancellationToken))?.ValueJson,
                365);
            var organization = new Organization
            {
                Name = organizationName.Trim(),
                Slug = slug,
                Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "UTC" : request.Timezone.Trim(),
                DefaultLanguage = string.IsNullOrWhiteSpace(request.DefaultLanguage) ? "en" : request.DefaultLanguage.Trim(),
                RetentionDays = defaultRetentionDays,
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

            var verificationResult = await SecurityEndpoints.SendEmailVerificationAsync(
                user,
                organization.Id,
                organization.Name,
                dbContext,
                settings,
                emailService,
                configuration,
                secretHasher,
                clock,
                httpContext,
                cancellationToken);
            await SecurityEndpoints.SignInDashboardUserAsync(httpContext, user);

            return Results.Created($"/v1/organizations/{organization.Id}", new SignupResponse(
                user.Id,
                organization.Id,
                organization.Slug,
                membership.Role,
                user.EmailVerifiedAt.HasValue,
                verificationResult.Sent));
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
            IEntitlementService entitlements,
            ITenantContext tenantContext,
            IAtlasClock clock,
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

            if (request.AccentColor is not null && request.AccentColor != organization.AccentColor)
            {
                var feature = await entitlements.HasFeatureAsync(organizationId, clock.UtcNow, "custom_branding", cancellationToken);
                if (!feature.Allowed)
                {
                    return EndpointHelpers.EntitlementProblem(feature);
                }
            }

            if (request.RetentionDays is > 0 && request.RetentionDays.Value != organization.RetentionDays)
            {
                var feature = await entitlements.HasFeatureAsync(organizationId, clock.UtcNow, "custom_retention", cancellationToken);
                if (!feature.Allowed)
                {
                    return EndpointHelpers.EntitlementProblem(feature);
                }
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

    private static void MapBilling(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/billing").WithTags("Billing");

        group.MapGet("/plans", async (
            IEntitlementService entitlements,
            CancellationToken cancellationToken) =>
        {
            var plans = await entitlements.ListPlansAsync(cancellationToken);
            return Results.Ok(new { items = plans });
        });

        v1.MapGet("/organizations/{id:guid}/entitlements", async (
            Guid id,
            IEntitlementService entitlements,
            ITenantContext tenantContext,
            IAtlasClock clock,
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

            return Results.Ok(await entitlements.GetOrganizationEntitlementsAsync(id, clock.UtcNow, cancellationToken));
        }).WithTags("Billing");
    }

    private static void MapDashboard(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/dashboard").WithTags("Dashboard");

        group.MapGet("/summary", async (
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IEntitlementService entitlements,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            var now = clock.UtcNow;
            var tomorrow = now.AddDays(1);
            var actions = await dbContext.Actions
                .AsNoTracking()
                .Include(item => item.Recipients)
                .OrderByDescending(item => item.CreatedAt)
                .ToListAsync(cancellationToken);
            var submissions = await dbContext.Submissions
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            var pendingFiles = await dbContext.FileAssets
                .AsNoTracking()
                .CountAsync(item => item.ScanStatus == FileScanStatus.Pending, cancellationToken);
            var rejectedFiles = await dbContext.FileAssets
                .AsNoTracking()
                .CountAsync(item => item.ScanStatus == FileScanStatus.Rejected, cancellationToken);
            var waitingRecipients = actions
                .SelectMany(action => action.Recipients.Select(recipient => new { action, recipient }))
                .Where(item => item.recipient.Status != ActionRecipientStatus.Submitted
                    && item.action.Status is not ChecklistActionStatus.Cancelled and not ChecklistActionStatus.Completed and not ChecklistActionStatus.Expired)
                .ToList();

            var attention = waitingRecipients
                .OrderByDescending(item => item.action.DueAt is not null && item.action.DueAt < now)
                .ThenBy(item => item.action.DueAt ?? DateTimeOffset.MaxValue)
                .ThenByDescending(item => item.recipient.LastActivityAt ?? item.recipient.CreatedAt)
                .Take(10)
                .Select(item => new DashboardAttentionItem(
                    item.action.Id,
                    item.action.Title,
                    item.action.DueAt,
                    item.recipient.Id,
                    item.recipient.Name,
                    item.recipient.Email,
                    item.recipient.Status,
                    item.recipient.LastActivityAt,
                    item.action.DueAt is not null && item.action.DueAt < now,
                    item.action.DueAt is not null && item.action.DueAt >= now && item.action.DueAt <= tomorrow))
                .ToList();

            return Results.Ok(new DashboardSummaryResponse(
                organizationId,
                now,
                new DashboardNeedsAttention(
                    waitingRecipients.Count,
                    waitingRecipients.Count(item => item.action.DueAt is not null && item.action.DueAt < now),
                    waitingRecipients.Count(item => item.action.DueAt is not null && item.action.DueAt >= now && item.action.DueAt <= tomorrow)),
                new DashboardChecklistCounts(
                    actions.Count,
                    actions.Count(item => item.Status == ChecklistActionStatus.Draft),
                    actions.Count(item => item.Status is ChecklistActionStatus.Sent or ChecklistActionStatus.InProgress),
                    actions.Count(item => item.Status == ChecklistActionStatus.Submitted),
                    actions.Count(item => item.Status == ChecklistActionStatus.Completed),
                    actions.Count(item => item.Status == ChecklistActionStatus.Cancelled),
                    actions.Count(item => item.Status == ChecklistActionStatus.Expired)),
                new DashboardSubmissionCounts(
                    submissions.Count,
                    submissions.Count(item => item.Status == SubmissionStatus.Submitted),
                    submissions.Count(item => item.Status == SubmissionStatus.Accepted),
                    submissions.Count(item => item.Status == SubmissionStatus.ChangesRequested)),
                new DashboardFileCounts(pendingFiles, rejectedFiles),
                attention,
                await entitlements.GetOrganizationEntitlementsAsync(organizationId, now, cancellationToken)));
        });
    }

    private static void MapOrganizationMembers(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/organization-members").WithTags("Organization members");

        group.MapGet("", async (
            int? page,
            int? pageSize,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var members = await dbContext.OrganizationUsers
                .AsNoTracking()
                .Include(item => item.User)
                .OrderBy(item => item.User!.FullName)
                .ToListAsync(cancellationToken);
            return Results.Ok(new { items = members.Select(ToMemberResponse).ToList() });
        });

        group.MapPost("", async (
            CreateOrganizationMemberRequest request,
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

            if (!EndpointHelpers.HasScope(tenantContext, "admin:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Admin scope is required.", StatusCodes.Status403Forbidden);
            }

            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.FullName))
            {
                return EndpointHelpers.Problem("validation_failed", "Email and full name are required.", StatusCodes.Status422UnprocessableEntity);
            }

            var role = request.Role ?? OrganizationUserRole.Member;
            var status = request.Status ?? MembershipStatus.Active;
            if (!Enum.IsDefined(role) || !Enum.IsDefined(status))
            {
                return EndpointHelpers.Problem("validation_failed", "Role or status is invalid.", StatusCodes.Status422UnprocessableEntity);
            }

            var email = request.Email.Trim().ToLowerInvariant();
            var existingMembership = await dbContext.OrganizationUsers.IgnoreQueryFilters()
                .AnyAsync(item => item.OrganizationId == organizationId
                    && item.User != null
                    && item.User.Email == email,
                    cancellationToken);
            if (existingMembership)
            {
                return EndpointHelpers.Problem("member_exists", "This email is already a member of the organization.", StatusCodes.Status409Conflict);
            }

            var user = await dbContext.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Email == email, cancellationToken);
            if (user is null)
            {
                if (string.IsNullOrWhiteSpace(request.Password))
                {
                    return EndpointHelpers.Problem("validation_failed", "Password is required when creating a new user directly.", StatusCodes.Status422UnprocessableEntity);
                }

                user = new AppUser
                {
                    Email = email,
                    FullName = request.FullName.Trim(),
                    CreatedAt = clock.UtcNow
                };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, request.Password);
                dbContext.Users.Add(user);
            }
            else
            {
                user.FullName = string.IsNullOrWhiteSpace(request.FullName) ? user.FullName : request.FullName.Trim();
            }

            var membership = new OrganizationUser
            {
                OrganizationId = organizationId,
                User = user,
                Role = role,
                Status = status,
                InvitedAt = status == MembershipStatus.Invited ? clock.UtcNow : null,
                JoinedAt = status == MembershipStatus.Active ? clock.UtcNow : null,
                CreatedAt = clock.UtcNow
            };

            dbContext.OrganizationUsers.Add(membership);
            SecurityEndpoints.AddAudit(dbContext, organizationId, null, tenantContext, "organization.member_created", new { user.Email, membership.Role, membership.Status }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/v1/organization-members/{membership.Id}", ToMemberResponse(membership));
        });

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateOrganizationMemberRequest request,
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

            if (!EndpointHelpers.HasScope(tenantContext, "admin:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Admin scope is required.", StatusCodes.Status403Forbidden);
            }

            var membership = await dbContext.OrganizationUsers
                .Include(item => item.User)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (membership?.User is null)
            {
                return EndpointHelpers.Problem("not_found", "Organization member was not found.", StatusCodes.Status404NotFound);
            }

            var nextRole = request.Role ?? membership.Role;
            var nextStatus = request.Status ?? membership.Status;
            if (!Enum.IsDefined(nextRole) || !Enum.IsDefined(nextStatus))
            {
                return EndpointHelpers.Problem("validation_failed", "Role or status is invalid.", StatusCodes.Status422UnprocessableEntity);
            }

            if (membership.Role == OrganizationUserRole.Owner
                && (nextRole != OrganizationUserRole.Owner || nextStatus != MembershipStatus.Active)
                && !await HasAnotherActiveOrganizationOwnerAsync(dbContext, organizationId, membership.Id, cancellationToken))
            {
                return EndpointHelpers.Problem("last_owner", "At least one active organization owner is required.", StatusCodes.Status409Conflict);
            }

            membership.Role = nextRole;
            membership.Status = nextStatus;
            membership.JoinedAt = nextStatus == MembershipStatus.Active ? membership.JoinedAt ?? clock.UtcNow : membership.JoinedAt;
            membership.InvitedAt = nextStatus == MembershipStatus.Invited ? membership.InvitedAt ?? clock.UtcNow : membership.InvitedAt;
            if (!string.IsNullOrWhiteSpace(request.FullName))
            {
                membership.User.FullName = request.FullName.Trim();
            }
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                membership.User.PasswordHash = new PasswordHasher<AppUser>().HashPassword(membership.User, request.Password);
            }

            SecurityEndpoints.AddAudit(dbContext, organizationId, null, tenantContext, "organization.member_updated", new { membership.Id, membership.User.Email, membership.Role, membership.Status }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToMemberResponse(membership));
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (!EndpointHelpers.HasScope(tenantContext, "admin:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Admin scope is required.", StatusCodes.Status403Forbidden);
            }

            var membership = await dbContext.OrganizationUsers
                .Include(item => item.User)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (membership?.User is null)
            {
                return EndpointHelpers.Problem("not_found", "Organization member was not found.", StatusCodes.Status404NotFound);
            }

            if (membership.Role == OrganizationUserRole.Owner
                && !await HasAnotherActiveOrganizationOwnerAsync(dbContext, organizationId, membership.Id, cancellationToken))
            {
                return EndpointHelpers.Problem("last_owner", "At least one active organization owner is required.", StatusCodes.Status409Conflict);
            }

            membership.Status = MembershipStatus.Disabled;
            SecurityEndpoints.AddAudit(dbContext, organizationId, null, tenantContext, "organization.member_disabled", new { membership.Id, membership.User.Email }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
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

        group.MapGet("", async (
            string? query,
            string? category,
            string? country,
            string? region,
            int? limit,
            AtlasDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var templateRows = await dbContext.Templates
                .AsNoTracking()
                .Include(template => template.CurrentVersion)
                .OrderBy(template => template.Category)
                .ThenBy(template => template.Name)
                .ToListAsync(cancellationToken);

            var terms = SplitSearchTerms(query);
            var take = limit is > 0 and <= 100 ? limit.Value : 50;
            var templates = templateRows
                .Select(template => new TemplateSearchRow(
                    template,
                    ToTemplateResponse(template),
                    TemplateSearchText(template)))
                .Where(item => MatchesTemplateSearch(item, terms, category, country, region))
                .OrderByDescending(item => terms.Count == 0 ? 0 : TemplateSearchScore(item, terms))
                .ThenByDescending(item => item.Response.Rating ?? 0)
                .ThenBy(item => item.Response.Category)
                .ThenBy(item => item.Response.Name)
                .Take(take)
                .Select(item => item.Response)
                .ToList();

            return Results.Ok(new { items = templates });
        });

        group.MapPost("", async (
            CreateTemplateRequest request,
            AtlasDbContext dbContext,
            IEntitlementService entitlements,
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

            var feature = await entitlements.HasFeatureAsync(organizationId, clock.UtcNow, "custom_workflows", cancellationToken);
            if (!feature.Allowed)
            {
                return EndpointHelpers.EntitlementProblem(feature);
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

            return Results.Created($"/v1/templates/{template.Id}", ToTemplateResponse(template, version));
        });

        group.MapPost("/{id:guid}/publish", async (
            Guid id,
            AtlasDbContext dbContext,
            IEntitlementService entitlements,
            ITenantContext tenantContext,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            var feature = await entitlements.HasFeatureAsync(organizationId, clock.UtcNow, "custom_workflows", cancellationToken);
            if (!feature.Allowed)
            {
                return EndpointHelpers.EntitlementProblem(feature);
            }

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
            return Results.Ok(ToTemplateResponse(template, version));
        });
    }

    private static void MapActions(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/actions").WithTags("Actions");

        group.MapGet("", async (
            int? page,
            int? pageSize,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out _, out var problem))
            {
                return problem!;
            }
            if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
            {
                return sandboxProblem;
            }

            var normalizedPage = EndpointHelpers.NormalizePage(page);
            var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize);
            var query = dbContext.Actions.AsNoTracking();
            var total = await query.CountAsync(cancellationToken);
            var actionRows = await query
                .Include(action => action.Template)
                .Include(action => action.Recipients)
                .Include(action => action.Requirements)
                .Include(action => action.Submissions)
                .ThenInclude(submission => submission.Responses)
                .Include(action => action.Submissions)
                .ThenInclude(submission => submission.Files)
                .OrderByDescending(action => action.UpdatedAt)
                .ThenByDescending(action => action.CreatedAt)
                .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
                .Take(normalizedPageSize)
                .ToListAsync(cancellationToken);

            var actions = actionRows.Select(ToActionListItemResponse).ToList();

            return Results.Ok(new
            {
                items = actions,
                page = normalizedPage,
                pageSize = normalizedPageSize,
                total
            });
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
            if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
            {
                return sandboxProblem;
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
                action.ClientReference,
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
            IEntitlementService entitlements,
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
            if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
            {
                return sandboxProblem;
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

            if (request.SendImmediately)
            {
                if (await SecurityEndpoints.RequireVerifiedDashboardEmailAsync(dbContext, tenantContext, cancellationToken) is { } verificationProblem)
                {
                    return verificationProblem;
                }

                var limit = await entitlements.CanSendChecklistAsync(organizationId, clock.UtcNow, 1, cancellationToken);
                if (!limit.Allowed)
                {
                    return EndpointHelpers.EntitlementProblem(limit);
                }
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

            var recipientEmail = request.Recipient.Email.Trim().ToLowerInvariant();
            var clientReference = string.IsNullOrWhiteSpace(request.ClientReference)
                ? null
                : request.ClientReference.Trim();
            if (clientReference is { Length: > 160 })
            {
                return EndpointHelpers.Problem("validation_failed", "Client reference must be 160 characters or fewer.", StatusCodes.Status422UnprocessableEntity);
            }

            if (!string.IsNullOrWhiteSpace(clientReference))
            {
                var existingClientReference = await dbContext.Actions
                    .AsNoTracking()
                    .Where(action => action.ClientReference == clientReference)
                    .OrderByDescending(action => action.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
                if (existingClientReference is not null)
                {
                    return DuplicateActionProblem(
                        "duplicate_client_reference",
                        "An action with this client reference already exists.",
                        existingClientReference);
                }
            }

            if (!request.AllowDuplicate)
            {
                var duplicateWindowMinutes = EndpointHelpers.ReadPositiveIntSetting(
                    (await settings.GetAsync(organizationId, "actions", "duplicateWindowMinutes", cancellationToken))?.ValueJson,
                    10);
                var duplicateCreatedAfter = clock.UtcNow.AddMinutes(-duplicateWindowMinutes);
                var possibleDuplicate = await dbContext.Actions
                    .AsNoTracking()
                    .Include(action => action.Recipients)
                    .Where(action => action.CreatedAt >= duplicateCreatedAfter
                        && action.Status != ChecklistActionStatus.Cancelled
                        && action.TemplateId == request.TemplateId
                        && action.Title == request.Title.Trim()
                        && action.DueAt == request.DueAt
                        && action.Recipients.Any(recipient => recipient.Email == recipientEmail))
                    .OrderByDescending(action => action.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
                if (possibleDuplicate is not null)
                {
                    return DuplicateActionProblem(
                        "possible_duplicate_action",
                        "A very similar checklist was just created. Confirm before creating it again.",
                        possibleDuplicate);
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
                ClientReference = clientReference,
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

            var otpRequired = await ResolveRecipientOtpRequiredAsync(request, dbContext, settings, organizationId, cancellationToken);
            var recipient = new ActionRecipient
            {
                Action = action,
                Name = request.Recipient.Name.Trim(),
                Email = recipientEmail,
                Phone = request.Recipient.Phone,
                OtpRequired = otpRequired,
                AccessTokenHash = secretHasher.HashSecret(rawToken),
                TokenExpiresAt = action.ExpiresAt,
                CreatedAt = now
            };

            action.Recipients.Add(recipient);
            await AddRequirementSnapshotsAsync(action, request, dbContext, cancellationToken);

            dbContext.Actions.Add(action);
            SecurityEndpoints.AddAudit(dbContext, organizationId, action.Id, tenantContext, "action.created", new { action.Id, action.Title }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);

            var recipientUrl = await EndpointHelpers.BuildRecipientLinkAsync(settings, configuration, organizationId, httpContext, rawToken, cancellationToken);
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
                await ScheduleAutomaticRemindersAsync(action, organizationId, entitlements, settings, clock, cancellationToken);
                SecurityEndpoints.AddAudit(dbContext, organizationId, action.Id, tenantContext, "action.sent", new { actionId = action.Id, recipientId = recipient.Id, send.TokenPrefix }, httpContext);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var response = new ActionResponse(
                action.Id,
                action.PublicReference,
                action.ClientReference,
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
            IEntitlementService entitlements,
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
            if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
            {
                return sandboxProblem;
            }

            if (await SecurityEndpoints.RequireVerifiedDashboardEmailAsync(dbContext, tenantContext, cancellationToken) is { } verificationProblem)
            {
                return verificationProblem;
            }

            var action = await dbContext.Actions
                .Include(item => item.Organization)
                .Include(item => item.CreatedByUser)
                .Include(item => item.Recipients)
                .ThenInclude(item => item.ReminderSchedules)
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
            if (!wasAlreadySent)
            {
                var limit = await entitlements.CanSendChecklistAsync(organizationId, clock.UtcNow, 1, cancellationToken);
                if (!limit.Allowed)
                {
                    return EndpointHelpers.EntitlementProblem(limit);
                }
            }

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
                var recipientLink = await EndpointHelpers.BuildRecipientLinkAsync(settings, configuration, organizationId, httpContext, rawToken, cancellationToken);
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
                await ScheduleAutomaticRemindersAsync(action, organizationId, entitlements, settings, clock, cancellationToken);
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
            if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
            {
                return sandboxProblem;
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
            if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
            {
                return sandboxProblem;
            }

            if (await SecurityEndpoints.RequireVerifiedDashboardEmailAsync(dbContext, tenantContext, cancellationToken) is { } verificationProblem)
            {
                return verificationProblem;
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
                var recipientLink = await EndpointHelpers.BuildRecipientLinkAsync(settings, configuration, organizationId, httpContext, rawToken, cancellationToken);
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
            if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
            {
                return sandboxProblem;
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
            IEntitlementService entitlements,
            IObjectStorageService storage,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }
            if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
            {
                return sandboxProblem;
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

            var storageLimit = await entitlements.CanStoreFileAsync(organizationId, clock.UtcNow, request.SizeBytes, cancellationToken);
            if (!storageLimit.Allowed)
            {
                return EndpointHelpers.EntitlementProblem(storageLimit);
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
                RetentionUntil = clock.UtcNow.AddDays(await GetRetentionDaysAsync(dbContext, organizationId, cancellationToken)),
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
                signedUrl.ExpiresAt,
                fileAsset.RetentionUntil));
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
            if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
            {
                return sandboxProblem;
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
            if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
            {
                return sandboxProblem;
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
            if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
            {
                return sandboxProblem;
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

    private static TemplateResponse ToTemplateResponse(Template template, TemplateVersion? version = null)
    {
        var effectiveVersion = version ?? template.CurrentVersion;
        var settings = ParseJsonOrEmpty(effectiveVersion?.SettingsJson);
        return new TemplateResponse(
            template.Id,
            template.Name,
            template.Category,
            template.Description,
            template.Status,
            template.CurrentVersionId,
            effectiveVersion?.Title,
            effectiveVersion?.Instructions,
            settings,
            ReadJsonString(settings, "industry"),
            ReadJsonString(settings, "example"),
            ReadJsonInt(settings, "rating"),
            ReadJsonString(settings, "highlight"),
            ReadJsonString(settings, "country"),
            ReadJsonString(settings, "region"),
            ReadJsonStringArray(settings, "tags"),
            ReadJsonStringArray(settings, "searchTerms"),
            template.CreatedAt,
            template.UpdatedAt);
    }

    private static IReadOnlyList<string> SplitSearchTerms(string? query)
    {
        return string.IsNullOrWhiteSpace(query)
            ? Array.Empty<string>()
            : query.Trim()
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();
    }

    private static bool MatchesTemplateSearch(
        TemplateSearchRow row,
        IReadOnlyList<string> terms,
        string? category,
        string? country,
        string? region)
    {
        if (!string.IsNullOrWhiteSpace(category)
            && !ContainsNormalized(row.Response.Category, category))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(country)
            && !ContainsNormalized(row.Response.Country, country))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(region)
            && !ContainsNormalized(row.Response.Region, region))
        {
            return false;
        }

        return terms.Count == 0 || terms.All(term => row.SearchText.Contains(term, StringComparison.Ordinal));
    }

    private static int TemplateSearchScore(TemplateSearchRow row, IReadOnlyList<string> terms)
    {
        var name = row.Response.Name.ToLowerInvariant();
        var category = row.Response.Category?.ToLowerInvariant() ?? string.Empty;
        var tags = row.Response.Tags.Concat(row.Response.SearchTerms)
            .Select(item => item.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var score = 0;
        foreach (var term in terms)
        {
            if (name.Equals(term, StringComparison.Ordinal))
            {
                score += 100;
            }
            if (name.Contains(term, StringComparison.Ordinal))
            {
                score += 40;
            }
            if (tags.Contains(term))
            {
                score += 35;
            }
            if (category.Contains(term, StringComparison.Ordinal))
            {
                score += 20;
            }
            if (row.SearchText.Contains(term, StringComparison.Ordinal))
            {
                score += 10;
            }
        }

        return score;
    }

    private static string TemplateSearchText(Template template)
    {
        var version = template.CurrentVersion;
        return string.Join(' ', new[]
            {
                template.Name,
                template.Category,
                template.Description,
                version?.Title,
                version?.Instructions,
                version?.SettingsJson
            }
            .Where(item => !string.IsNullOrWhiteSpace(item)))
            .ToLowerInvariant();
    }

    private static bool ContainsNormalized(string? value, string expected)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement ParseJsonOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return JsonSerializer.Deserialize<JsonElement>("{}");
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json, EndpointHelpers.JsonOptions);
        }
        catch (JsonException)
        {
            return JsonSerializer.Deserialize<JsonElement>("{}");
        }
    }

    private static string? ReadJsonString(JsonElement json, string propertyName)
    {
        return json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static int? ReadJsonInt(JsonElement json, string propertyName)
    {
        return json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty(propertyName, out var value)
            && value.TryGetInt32(out var intValue)
                ? intValue
                : null;
    }

    private static IReadOnlyList<string> ReadJsonStringArray(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static OrganizationMemberResponse ToMemberResponse(OrganizationUser membership)
    {
        return new OrganizationMemberResponse(
            membership.Id,
            membership.OrganizationId,
            membership.UserId,
            membership.User?.Email ?? string.Empty,
            membership.User?.FullName ?? string.Empty,
            membership.Role,
            membership.Status,
            membership.InvitedAt,
            membership.JoinedAt,
            membership.CreatedAt);
    }

    private static async Task<bool> HasAnotherActiveOrganizationOwnerAsync(
        AtlasDbContext dbContext,
        Guid organizationId,
        Guid currentMembershipId,
        CancellationToken cancellationToken)
    {
        return await dbContext.OrganizationUsers.IgnoreQueryFilters()
            .AnyAsync(item => item.OrganizationId == organizationId
                && item.Id != currentMembershipId
                && item.Role == OrganizationUserRole.Owner
                && item.Status == MembershipStatus.Active,
                cancellationToken);
    }

    private static async Task<int> GetRetentionDaysAsync(
        AtlasDbContext dbContext,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var retentionDays = await dbContext.Organizations.IgnoreQueryFilters()
            .Where(item => item.Id == organizationId)
            .Select(item => item.RetentionDays)
            .FirstOrDefaultAsync(cancellationToken);
        return retentionDays > 0 ? retentionDays : 365;
    }

    private static async Task<bool> ResolveRecipientOtpRequiredAsync(
        CreateActionRequest request,
        AtlasDbContext dbContext,
        IAdminSettingService settings,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (request.Recipient.OtpRequired.HasValue)
        {
            return request.Recipient.OtpRequired.Value;
        }

        if (TryReadRecipientOtpRequired(request.Settings, out var actionOtpRequired))
        {
            return actionOtpRequired;
        }

        if (request.TemplateId.HasValue)
        {
            var templateSettingsJson = await dbContext.Templates
                .Where(item => item.Id == request.TemplateId.Value && item.CurrentVersion != null)
                .Select(item => item.CurrentVersion!.SettingsJson)
                .FirstOrDefaultAsync(cancellationToken);
            if (TryReadRecipientOtpRequired(ParseJsonOrEmpty(templateSettingsJson), out var templateOtpRequired))
            {
                return templateOtpRequired;
            }
        }

        return EndpointHelpers.ReadBoolSetting(
            (await settings.GetAsync(organizationId, "recipientOtp", "defaultRequired", cancellationToken))?.ValueJson,
            false);
    }

    private static bool TryReadRecipientOtpRequired(JsonElement? settings, out bool required)
    {
        required = false;
        return settings.HasValue && TryReadRecipientOtpRequired(settings.Value, out required);
    }

    private static bool TryReadRecipientOtpRequired(JsonElement settings, out bool required)
    {
        required = false;
        if (settings.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryReadJsonBool(settings, "otpRequired", out required)
            || TryReadJsonBool(settings, "recipientOtpRequired", out required)
            || TryReadJsonBool(settings, "requireRecipientOtp", out required))
        {
            return true;
        }

        if (settings.TryGetProperty("recipientOtp", out var recipientOtp)
            && recipientOtp.ValueKind == JsonValueKind.Object
            && (TryReadJsonBool(recipientOtp, "required", out required)
                || TryReadJsonBool(recipientOtp, "defaultRequired", out required)))
        {
            return true;
        }

        return false;
    }

    private static bool TryReadJsonBool(JsonElement json, string propertyName, out bool value)
    {
        value = false;
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        switch (property.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                value = false;
                return true;
            case JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
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

    private static async Task ScheduleAutomaticRemindersAsync(
        ChecklistAction action,
        Guid organizationId,
        IEntitlementService entitlements,
        IAdminSettingService settings,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        if (action.DueAt is null || action.Recipients.Count == 0)
        {
            return;
        }

        var feature = await entitlements.HasFeatureAsync(organizationId, clock.UtcNow, "automatic_reminders", cancellationToken);
        if (!feature.Allowed)
        {
            return;
        }

        var beforeDueDays = EndpointHelpers.ReadPositiveIntSetting(
            (await settings.GetAsync(organizationId, "reminders", "beforeDueDays", cancellationToken))?.ValueJson,
            3);
        var overdueDays = EndpointHelpers.ReadPositiveIntSetting(
            (await settings.GetAsync(organizationId, "reminders", "overdueDays", cancellationToken))?.ValueJson,
            1);
        var beforeDueAt = action.DueAt.Value.AddDays(-beforeDueDays);
        var overdueAt = action.DueAt.Value.AddDays(overdueDays);

        foreach (var recipient in action.Recipients)
        {
            if (recipient.Status == ActionRecipientStatus.Submitted)
            {
                continue;
            }

            if (beforeDueAt > clock.UtcNow
                && recipient.ReminderSchedules.All(item => item.Type != ReminderType.BeforeDue))
            {
                recipient.ReminderSchedules.Add(new ReminderSchedule
                {
                    ActionRecipientId = recipient.Id,
                    TriggerAt = beforeDueAt,
                    Type = ReminderType.BeforeDue,
                    Status = ReminderStatus.Pending,
                    CreatedAt = clock.UtcNow
                });
            }

            if (overdueAt > clock.UtcNow
                && recipient.ReminderSchedules.All(item => item.Type != ReminderType.Overdue))
            {
                recipient.ReminderSchedules.Add(new ReminderSchedule
                {
                    ActionRecipientId = recipient.Id,
                    TriggerAt = overdueAt,
                    Type = ReminderType.Overdue,
                    Status = ReminderStatus.Pending,
                    CreatedAt = clock.UtcNow
                });
            }
        }
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
        var recipientFirstName = FirstName(recipient.Name);
        var dueDate = FormatDate(action.DueAt);
        var expiresAt = FormatDate(recipient.TokenExpiresAt ?? action.ExpiresAt);
        var email = TransactionalEmailTemplates.Checklist(
            recipientFirstName,
            sender.FullName,
            organization.Name,
            action.Title,
            dueDate,
            expiresAt,
            recipientLink,
            kind switch
            {
                ChecklistEmailKind.Reminder => ChecklistEmailTemplateKind.Reminder,
                ChecklistEmailKind.Overdue => ChecklistEmailTemplateKind.Overdue,
                _ => ChecklistEmailTemplateKind.Invitation
            });

        var result = await emailService.SendAsync(
            recipient.Email,
            email.Subject,
            email.TextBody,
            email.HtmlBody,
            "requests@reqara.com",
            $"{organization.Name} via Reqara",
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

    private static string FirstName(string fullName)
    {
        var trimmed = fullName.Trim();
        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? trimmed[..space] : trimmed;
    }

    private static ActionListItemResponse ToActionListItemResponse(ChecklistAction action)
    {
        var recipient = action.Recipients
            .OrderBy(item => item.CreatedAt)
            .FirstOrDefault();
        var latestSubmission = action.Submissions
            .OrderByDescending(item => item.SubmittedAt)
            .ThenByDescending(item => item.VersionNumber)
            .FirstOrDefault();
        var completedRequirementCount = latestSubmission is null
            ? 0
            : latestSubmission.Responses.Select(item => item.RequirementId)
                .Concat(latestSubmission.Files.Select(item => item.RequirementId))
                .Distinct()
                .Count();
        var lastActivityAt = new[]
            {
                action.UpdatedAt,
                action.SentAt,
                action.CompletedAt,
                recipient?.LastActivityAt,
                recipient?.SubmittedAt,
                latestSubmission?.SubmittedAt,
                latestSubmission?.ReviewedAt
            }
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .DefaultIfEmpty(action.CreatedAt)
            .Max();

        return new ActionListItemResponse(
            action.Id,
            action.PublicReference,
            action.ClientReference,
            action.Title,
            action.Status,
            action.DueAt,
            action.ExpiresAt,
            action.SentAt,
            action.CompletedAt,
            action.TemplateId,
            action.Template?.Name,
            recipient is null
                ? null
                : new ActionListRecipientResponse(
                    recipient.Id,
                    recipient.Name,
                    recipient.Email,
                    recipient.Status,
                    recipient.LastActivityAt,
                    recipient.SubmittedAt),
            completedRequirementCount,
            action.Requirements.Count,
            action.CreatedAt,
            action.UpdatedAt,
            lastActivityAt);
    }

    private static IResult DuplicateActionProblem(string code, string title, ChecklistAction action)
    {
        return Results.Problem(
            title: title,
            statusCode: StatusCodes.Status409Conflict,
            type: $"https://docs.atlas.example/errors/{code}",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["existingActionId"] = action.Id,
                ["publicReference"] = action.PublicReference,
                ["clientReference"] = action.ClientReference,
                ["existingActionStatus"] = action.Status.ToString()
            });
    }

    private static string ToOrganizationSlug(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        var lastWasHyphen = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            var next = (character is >= 'a' and <= 'z') || (character is >= '0' and <= '9')
                ? character
                : '-';

            if (next == '-')
            {
                if (lastWasHyphen || builder.Length == 0)
                {
                    continue;
                }

                lastWasHyphen = true;
                builder.Append(next);
                continue;
            }

            lastWasHyphen = false;
            builder.Append(next);
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > 100)
        {
            slug = slug[..100].Trim('-');
        }

        return string.IsNullOrWhiteSpace(slug) ? "organization" : slug;
    }

    private static IResult? ValidateOrganizationSlug(string slug)
    {
        if (slug.Length is < 2 or > 100)
        {
            return EndpointHelpers.Problem("validation_failed", "Organization slug must be between 2 and 100 characters.", StatusCodes.Status422UnprocessableEntity);
        }

        if (slug.StartsWith('-') || slug.EndsWith('-') || slug.Contains("--", StringComparison.Ordinal))
        {
            return EndpointHelpers.Problem("validation_failed", "Organization slug cannot start, end, or contain consecutive hyphens.", StatusCodes.Status422UnprocessableEntity);
        }

        foreach (var character in slug)
        {
            if ((character is >= 'a' and <= 'z') || (character is >= '0' and <= '9') || character == '-')
            {
                continue;
            }

            return EndpointHelpers.Problem("validation_failed", "Organization slug can only contain lowercase letters, numbers, and hyphens.", StatusCodes.Status422UnprocessableEntity);
        }

        return null;
    }

    private static async Task<string> NextAvailableOrganizationSlugAsync(
        AtlasDbContext dbContext,
        string baseSlug,
        CancellationToken cancellationToken)
    {
        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var suffixText = "-" + suffix.ToString(CultureInfo.InvariantCulture);
            var stemMaxLength = 100 - suffixText.Length;
            var stem = baseSlug.Length > stemMaxLength
                ? baseSlug[..stemMaxLength].Trim('-')
                : baseSlug;
            var candidate = stem + suffixText;

            var exists = await dbContext.Organizations.IgnoreQueryFilters()
                .AnyAsync(organization => organization.Slug == candidate, cancellationToken);
            if (!exists)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unable to allocate an organization slug.");
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

public sealed class SignupRequest
{
    public string? Email { get; init; }
    public string? Password { get; init; }
    public string? FullName { get; init; }
    public string? Name { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? OrganizationName { get; init; }
    public string? OrgName { get; init; }
    public string? CompanyName { get; init; }
    public string? Company { get; init; }
    public string? OrganizationSlug { get; init; }
    public string? OrgSlug { get; init; }
    public string? Slug { get; init; }
    public string? Timezone { get; init; }
    public string? DefaultLanguage { get; init; }

    public string? ResolvedFullName()
    {
        if (!string.IsNullOrWhiteSpace(FullName))
        {
            return FullName;
        }

        if (!string.IsNullOrWhiteSpace(Name))
        {
            return Name;
        }

        var combined = string.Join(' ', new[] { FirstName, LastName }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
        return string.IsNullOrWhiteSpace(combined) ? null : combined;
    }

    public string? ResolvedOrganizationName()
    {
        if (!string.IsNullOrWhiteSpace(OrganizationName))
        {
            return OrganizationName;
        }

        if (!string.IsNullOrWhiteSpace(OrgName))
        {
            return OrgName;
        }

        if (!string.IsNullOrWhiteSpace(CompanyName))
        {
            return CompanyName;
        }

        return string.IsNullOrWhiteSpace(Company) ? null : Company;
    }

    public string? ResolvedOrganizationSlug()
    {
        if (!string.IsNullOrWhiteSpace(OrganizationSlug))
        {
            return OrganizationSlug;
        }

        if (!string.IsNullOrWhiteSpace(OrgSlug))
        {
            return OrgSlug;
        }

        return string.IsNullOrWhiteSpace(Slug) ? null : Slug;
    }
}

public sealed record SignupResponse(
    Guid UserId,
    Guid OrganizationId,
    string OrganizationSlug,
    OrganizationUserRole Role,
    bool EmailVerified,
    bool EmailVerificationSent);

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

public sealed record DashboardSummaryResponse(
    Guid OrganizationId,
    DateTimeOffset GeneratedAt,
    DashboardNeedsAttention NeedsAttention,
    DashboardChecklistCounts Checklists,
    DashboardSubmissionCounts Submissions,
    DashboardFileCounts Files,
    IReadOnlyList<DashboardAttentionItem> AttentionItems,
    OrganizationEntitlementSnapshot Entitlements);

public sealed record DashboardNeedsAttention(
    int WaitingRecipients,
    int OverdueRecipients,
    int DueSoonRecipients);

public sealed record DashboardChecklistCounts(
    int Total,
    int Draft,
    int InFlight,
    int Submitted,
    int Completed,
    int Cancelled,
    int Expired);

public sealed record DashboardSubmissionCounts(
    int Total,
    int Submitted,
    int Accepted,
    int ChangesRequested);

public sealed record DashboardFileCounts(
    int PendingScan,
    int Rejected);

public sealed record DashboardAttentionItem(
    Guid ActionId,
    string ChecklistTitle,
    DateTimeOffset? DueAt,
    Guid RecipientId,
    string RecipientName,
    string RecipientEmail,
    ActionRecipientStatus RecipientStatus,
    DateTimeOffset? LastActivityAt,
    bool Overdue,
    bool DueSoon);

public sealed record OrganizationMemberResponse(
    Guid Id,
    Guid OrganizationId,
    Guid UserId,
    string Email,
    string FullName,
    OrganizationUserRole Role,
    MembershipStatus Status,
    DateTimeOffset? InvitedAt,
    DateTimeOffset? JoinedAt,
    DateTimeOffset CreatedAt);

public sealed record CreateOrganizationMemberRequest(
    string Email,
    string FullName,
    string? Password,
    OrganizationUserRole? Role,
    MembershipStatus? Status);

public sealed record UpdateOrganizationMemberRequest(
    string? FullName,
    string? Password,
    OrganizationUserRole? Role,
    MembershipStatus? Status);

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
    string? Title,
    string? Instructions,
    JsonElement Settings,
    string? Industry,
    string? Example,
    int? Rating,
    string? Highlight,
    string? Country,
    string? Region,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> SearchTerms,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record TemplateSearchRow(Template Template, TemplateResponse Response, string SearchText);

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
    IReadOnlyList<RequirementDefinitionRequest> Requirements,
    string? ClientReference,
    bool AllowDuplicate);

public sealed record RecipientRequest(
    string Name,
    string Email,
    string? Phone,
    bool? OtpRequired);

public sealed record ActionResponse(
    Guid Id,
    string PublicReference,
    string? ClientReference,
    string Title,
    ChecklistActionStatus Status,
    DateTimeOffset? DueAt,
    DateTimeOffset? ExpiresAt,
    string? RecipientUrl,
    DateTimeOffset CreatedAt);

public sealed record ActionListItemResponse(
    Guid Id,
    string PublicReference,
    string? ClientReference,
    string Title,
    ChecklistActionStatus Status,
    DateTimeOffset? DueAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? CompletedAt,
    Guid? TemplateId,
    string? TemplateName,
    ActionListRecipientResponse? Recipient,
    int CompletedRequirementCount,
    int TotalRequirementCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastActivityAt);

public sealed record ActionListRecipientResponse(
    Guid Id,
    string Name,
    string Email,
    ActionRecipientStatus Status,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset? SubmittedAt);

public sealed record ActionDetailResponse(
    Guid Id,
    string PublicReference,
    string? ClientReference,
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
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RetentionUntil);

public sealed record FileScanResultRequest(FileScanStatus ScanStatus, string? ScanEngine);
