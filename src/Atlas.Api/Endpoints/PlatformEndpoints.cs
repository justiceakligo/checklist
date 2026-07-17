using System.Security.Claims;
using System.Text.Json;
using Atlas.Api.Email;
using Atlas.Application.Abstractions;
using Atlas.Application.Billing;
using Atlas.Application.Email;
using Atlas.Application.Settings;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class PlatformEndpoints
{
    public const string ActorKindClaim = "atlas_actor_kind";
    public const string PlatformStaffIdClaim = "atlas_platform_staff_id";
    private const string PlatformActorKind = "platform";

    private static readonly PlatformStaffRole[] CoreRoles = [PlatformStaffRole.Owner, PlatformStaffRole.Admin];
    private static readonly PlatformStaffRole[] SupportRoles = [PlatformStaffRole.Owner, PlatformStaffRole.Admin, PlatformStaffRole.Support];
    private static readonly PlatformStaffRole[] FinanceRoles = [PlatformStaffRole.Owner, PlatformStaffRole.Admin, PlatformStaffRole.Finance];

    public static IEndpointRouteBuilder MapAtlasPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/public/interests", async (
            CreateOrganizationInterestRequest request,
            AtlasDbContext dbContext,
            CancellationToken cancellationToken) =>
            await CreatePublicInterest(request, dbContext, cancellationToken)).WithTags("Public");
        app.MapPost("/v1/public/contact", CreatePublicContact).WithTags("Public");

        var group = app.MapGroup("/v1/platform").WithTags("Platform");

        group.MapPost("/bootstrap", BootstrapOwner);
        group.MapPost("/auth/login", Login);
        group.MapPost("/auth/logout", async (HttpContext httpContext) => await Logout(httpContext));
        group.MapGet("/me", GetMe);

        MapStaff(group);
        MapSettings(group);
        MapTemplates(group);
        MapOrganizations(group);
        MapInterests(group);
        MapRevenue(group);
        MapMetrics(group);
        MapAudit(group);

        return app;
    }

    private static async Task<IResult> BootstrapOwner(
        PlatformBootstrapRequest request,
        AtlasDbContext dbContext,
        IConfiguration configuration,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var configuredKey = configuration["PlatformAdmin:BootstrapKey"] ?? configuration["PLATFORM_BOOTSTRAP_KEY"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return EndpointHelpers.Problem("bootstrap_not_configured", "Platform bootstrap key is not configured.", StatusCodes.Status503ServiceUnavailable);
        }

        if (!string.Equals(request.BootstrapKey, configuredKey, StringComparison.Ordinal))
        {
            return EndpointHelpers.Problem("invalid_bootstrap_key", "Bootstrap key is invalid.", StatusCodes.Status401Unauthorized);
        }

        var staffExists = await dbContext.PlatformStaff.IgnoreQueryFilters().AnyAsync(cancellationToken);
        if (staffExists)
        {
            return EndpointHelpers.Problem("platform_staff_exists", "Platform staff bootstrap has already been completed.", StatusCodes.Status409Conflict);
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.FullName))
        {
            return EndpointHelpers.Problem("validation_failed", "Email, password and full name are required.", StatusCodes.Status422UnprocessableEntity);
        }

        var staff = new PlatformStaff
        {
            Email = NormalizeEmail(request.Email),
            FullName = request.FullName.Trim(),
            Role = PlatformStaffRole.Owner,
            Status = PlatformStaffStatus.Active,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        staff.PasswordHash = new PasswordHasher<PlatformStaff>().HashPassword(staff, request.Password);
        dbContext.PlatformStaff.Add(staff);
        AddPlatformAudit(dbContext, staff.Id, "platform.bootstrap_completed", new { staff.Id, staff.Email }, httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);
        await SignInPlatformStaffAsync(httpContext, staff);

        return Results.Created($"/v1/platform/staff/{staff.Id}", ToStaffResponse(staff));
    }

    private static async Task<IResult> Login(
        PlatformLoginRequest request,
        AtlasDbContext dbContext,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return EndpointHelpers.Problem("validation_failed", "Email and password are required.", StatusCodes.Status422UnprocessableEntity);
        }

        var email = NormalizeEmail(request.Email);
        var staff = await dbContext.PlatformStaff.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Email == email, cancellationToken);
        if (staff?.PasswordHash is null || staff.Status != PlatformStaffStatus.Active)
        {
            return EndpointHelpers.Problem("invalid_credentials", "Invalid email or password.", StatusCodes.Status401Unauthorized);
        }

        var passwordResult = new PasswordHasher<PlatformStaff>().VerifyHashedPassword(staff, staff.PasswordHash, request.Password);
        if (passwordResult == PasswordVerificationResult.Failed)
        {
            return EndpointHelpers.Problem("invalid_credentials", "Invalid email or password.", StatusCodes.Status401Unauthorized);
        }

        staff.LastLoginAt = clock.UtcNow;
        AddPlatformAudit(dbContext, staff.Id, "platform.login", new { staff.Id, staff.Email }, httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);
        await SignInPlatformStaffAsync(httpContext, staff);

        return Results.Ok(ToStaffResponse(staff));
    }

    private static async Task<IResult> Logout(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static async Task<IResult> GetMe(
        AtlasDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken);
        return access.Problem ?? Results.Ok(ToStaffResponse(access.Staff!));
    }

    private static void MapStaff(RouteGroupBuilder group)
    {
        group.MapGet("/staff", async (
            int? page,
            int? pageSize,
            PlatformStaffStatus? status,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var query = dbContext.PlatformStaff.IgnoreQueryFilters().AsNoTracking();
            if (status.HasValue)
            {
                query = query.Where(item => item.Status == status.Value);
            }

            var normalizedPage = EndpointHelpers.NormalizePage(page);
            var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize);
            var total = await query.CountAsync(cancellationToken);
            var staff = await query
                .OrderBy(item => item.FullName)
                .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
                .Take(normalizedPageSize)
                .Select(item => ToStaffResponse(item))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { items = staff, page = normalizedPage, pageSize = normalizedPageSize, total });
        });

        group.MapPost("/staff", async (
            CreatePlatformStaffRequest request,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            if (string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.FullName)
                || string.IsNullOrWhiteSpace(request.Password))
            {
                return EndpointHelpers.Problem("validation_failed", "Email, full name and password are required.", StatusCodes.Status422UnprocessableEntity);
            }

            var email = NormalizeEmail(request.Email);
            var exists = await dbContext.PlatformStaff.IgnoreQueryFilters().AnyAsync(item => item.Email == email, cancellationToken);
            if (exists)
            {
                return EndpointHelpers.Problem("email_in_use", "Platform staff email is already in use.", StatusCodes.Status409Conflict);
            }

            var role = request.Role ?? PlatformStaffRole.Support;
            var status = request.Status ?? PlatformStaffStatus.Active;
            var enumValidation = ValidateEnum(role, "role") ?? ValidateEnum(status, "status");
            if (enumValidation is not null)
            {
                return enumValidation;
            }

            var staff = new PlatformStaff
            {
                Email = email,
                FullName = request.FullName.Trim(),
                Role = role,
                Status = status,
                DisabledAt = status == PlatformStaffStatus.Disabled ? clock.UtcNow : null,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            };
            staff.PasswordHash = new PasswordHasher<PlatformStaff>().HashPassword(staff, request.Password);
            dbContext.PlatformStaff.Add(staff);
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.staff_created", new { staff.Id, staff.Email, staff.Role, staff.Status }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Created($"/v1/platform/staff/{staff.Id}", ToStaffResponse(staff));
        });

        group.MapGet("/staff/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var staff = await dbContext.PlatformStaff.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            return staff is null
                ? EndpointHelpers.Problem("not_found", "Platform staff member was not found.", StatusCodes.Status404NotFound)
                : Results.Ok(ToStaffResponse(staff));
        });

        group.MapPut("/staff/{id:guid}", async (
            Guid id,
            UpdatePlatformStaffRequest request,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var staff = await dbContext.PlatformStaff.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (staff is null)
            {
                return EndpointHelpers.Problem("not_found", "Platform staff member was not found.", StatusCodes.Status404NotFound);
            }

            var nextRole = request.Role ?? staff.Role;
            var nextStatus = request.Status ?? staff.Status;
            var enumValidation = ValidateEnum(nextRole, "role") ?? ValidateEnum(nextStatus, "status");
            if (enumValidation is not null)
            {
                return enumValidation;
            }

            if (staff.Role == PlatformStaffRole.Owner
                && (nextRole != PlatformStaffRole.Owner || nextStatus != PlatformStaffStatus.Active)
                && !await HasAnotherActiveOwnerAsync(dbContext, staff.Id, cancellationToken))
            {
                return EndpointHelpers.Problem("last_owner", "At least one active platform owner is required.", StatusCodes.Status409Conflict);
            }

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                var email = NormalizeEmail(request.Email);
                var exists = await dbContext.PlatformStaff.IgnoreQueryFilters()
                    .AnyAsync(item => item.Email == email && item.Id != id, cancellationToken);
                if (exists)
                {
                    return EndpointHelpers.Problem("email_in_use", "Platform staff email is already in use.", StatusCodes.Status409Conflict);
                }
                staff.Email = email;
            }

            staff.FullName = string.IsNullOrWhiteSpace(request.FullName) ? staff.FullName : request.FullName.Trim();
            staff.Role = nextRole;
            staff.Status = nextStatus;
            staff.DisabledAt = nextStatus == PlatformStaffStatus.Disabled ? staff.DisabledAt ?? clock.UtcNow : null;
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                staff.PasswordHash = new PasswordHasher<PlatformStaff>().HashPassword(staff, request.Password);
            }

            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.staff_updated", new { staff.Id, staff.Email, staff.Role, staff.Status }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToStaffResponse(staff));
        });

        group.MapDelete("/staff/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var staff = await dbContext.PlatformStaff.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (staff is null)
            {
                return EndpointHelpers.Problem("not_found", "Platform staff member was not found.", StatusCodes.Status404NotFound);
            }

            if (staff.Role == PlatformStaffRole.Owner && !await HasAnotherActiveOwnerAsync(dbContext, staff.Id, cancellationToken))
            {
                return EndpointHelpers.Problem("last_owner", "At least one active platform owner is required.", StatusCodes.Status409Conflict);
            }

            staff.Status = PlatformStaffStatus.Disabled;
            staff.DisabledAt ??= clock.UtcNow;
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.staff_disabled", new { staff.Id, staff.Email }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    private static void MapSettings(RouteGroupBuilder group)
    {
        group.MapGet("/settings", async (
            Guid? organizationId,
            string? category,
            bool? includeSecrets,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var canViewSecrets = includeSecrets == true && HasRole(access.Staff!, CoreRoles);
            var query = dbContext.AdminSettings.IgnoreQueryFilters().AsNoTracking();
            if (organizationId.HasValue)
            {
                query = query.Where(item => item.OrganizationId == organizationId.Value);
            }
            if (!string.IsNullOrWhiteSpace(category))
            {
                query = query.Where(item => item.Category == category.Trim());
            }

            var settings = await query
                .OrderBy(item => item.OrganizationId)
                .ThenBy(item => item.Category)
                .ThenBy(item => item.Key)
                .Select(item => ToPlatformSettingResponse(item, canViewSecrets))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { items = settings });
        });

        group.MapPost("/settings", async (
            UpsertPlatformSettingRequest request,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var validation = ValidateSettingRequest(request.Scope, request.OrganizationId, request.Category, request.Key);
            if (validation is not null)
            {
                return validation;
            }

            var organizationId = request.Scope == AdminSettingScope.Organization ? request.OrganizationId : null;
            var organizationValidation = await ValidateOrganizationReferenceAsync(dbContext, organizationId, cancellationToken);
            if (organizationValidation is not null)
            {
                return organizationValidation;
            }

            var setting = await dbContext.AdminSettings.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.OrganizationId == organizationId
                    && item.Category == request.Category.Trim()
                    && item.Key == request.Key.Trim(),
                    cancellationToken);

            if (setting is null)
            {
                setting = new AdminSetting
                {
                    OrganizationId = organizationId,
                    Category = request.Category.Trim(),
                    Key = request.Key.Trim(),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                dbContext.AdminSettings.Add(setting);
            }

            setting.Scope = request.Scope;
            setting.ValueJson = request.Value.GetRawText();
            setting.IsSecret = request.IsSecret;
            setting.UpdatedByUserId = null;
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.setting_upserted", new { setting.Id, setting.OrganizationId, setting.Category, setting.Key, setting.IsSecret }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToPlatformSettingResponse(setting, includeSecret: false));
        });

        group.MapGet("/settings/{id:guid}", async (
            Guid id,
            bool? includeSecret,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var setting = await dbContext.AdminSettings.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            return setting is null
                ? EndpointHelpers.Problem("not_found", "Setting was not found.", StatusCodes.Status404NotFound)
                : Results.Ok(ToPlatformSettingResponse(setting, includeSecret == true && HasRole(access.Staff!, CoreRoles)));
        });

        group.MapPut("/settings/{id:guid}", async (
            Guid id,
            UpdatePlatformSettingRequest request,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var setting = await dbContext.AdminSettings.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (setting is null)
            {
                return EndpointHelpers.Problem("not_found", "Setting was not found.", StatusCodes.Status404NotFound);
            }

            var nextScope = request.Scope ?? setting.Scope;
            var nextOrganizationId = nextScope == AdminSettingScope.Organization
                ? request.OrganizationId ?? setting.OrganizationId
                : null;
            var nextCategory = string.IsNullOrWhiteSpace(request.Category) ? setting.Category : request.Category.Trim();
            var nextKey = string.IsNullOrWhiteSpace(request.Key) ? setting.Key : request.Key.Trim();
            var validation = ValidateSettingRequest(nextScope, nextOrganizationId, nextCategory, nextKey);
            if (validation is not null)
            {
                return validation;
            }

            var organizationValidation = await ValidateOrganizationReferenceAsync(dbContext, nextOrganizationId, cancellationToken);
            if (organizationValidation is not null)
            {
                return organizationValidation;
            }

            var duplicate = await dbContext.AdminSettings.IgnoreQueryFilters()
                .AnyAsync(item => item.Id != id
                    && item.OrganizationId == nextOrganizationId
                    && item.Category == nextCategory
                    && item.Key == nextKey,
                    cancellationToken);
            if (duplicate)
            {
                return EndpointHelpers.Problem("setting_exists", "A setting with this scope, category and key already exists.", StatusCodes.Status409Conflict);
            }

            setting.Scope = nextScope;
            setting.OrganizationId = nextOrganizationId;
            setting.Category = nextCategory;
            setting.Key = nextKey;
            if (request.Value.HasValue)
            {
                setting.ValueJson = request.Value.Value.GetRawText();
            }
            if (request.IsSecret.HasValue)
            {
                setting.IsSecret = request.IsSecret.Value;
            }
            setting.UpdatedByUserId = null;

            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.setting_updated", new { setting.Id, setting.OrganizationId, setting.Category, setting.Key, setting.IsSecret }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToPlatformSettingResponse(setting, includeSecret: false));
        });

        group.MapDelete("/settings/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var setting = await dbContext.AdminSettings.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (setting is null)
            {
                return EndpointHelpers.Problem("not_found", "Setting was not found.", StatusCodes.Status404NotFound);
            }

            dbContext.AdminSettings.Remove(setting);
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.setting_deleted", new { setting.Id, setting.OrganizationId, setting.Category, setting.Key }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    private static void MapTemplates(RouteGroupBuilder group)
    {
        group.MapGet("/templates", async (
            int? page,
            int? pageSize,
            string? q,
            TemplateStatus? status,
            Guid? organizationId,
            bool? globalOnly,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, SupportRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var query = dbContext.Templates.IgnoreQueryFilters().AsNoTracking()
                .Include(item => item.Organization)
                .Include(item => item.CurrentVersion)
                .ThenInclude(item => item!.Requirements)
                .Include(item => item.Versions)
                .ThenInclude(item => item.Requirements)
                .Where(item => item.DeletedAt == null);
            if (status.HasValue)
            {
                query = query.Where(item => item.Status == status.Value);
            }
            if (organizationId.HasValue)
            {
                query = query.Where(item => item.OrganizationId == organizationId.Value);
            }
            if (globalOnly == true)
            {
                query = query.Where(item => item.OrganizationId == null);
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(item =>
                    item.Name.Contains(term)
                    || (item.Category != null && item.Category.Contains(term))
                    || (item.Description != null && item.Description.Contains(term))
                    || (item.CurrentVersion != null
                        && (item.CurrentVersion.Title.Contains(term)
                            || (item.CurrentVersion.Instructions != null && item.CurrentVersion.Instructions.Contains(term))
                            || item.CurrentVersion.SettingsJson.Contains(term)))
                    || item.Versions.Any(version =>
                        version.Title.Contains(term)
                        || (version.Instructions != null && version.Instructions.Contains(term))
                        || version.SettingsJson.Contains(term)));
            }

            var normalizedPage = EndpointHelpers.NormalizePage(page);
            var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize);
            var total = await query.CountAsync(cancellationToken);
            var templates = await query
                .OrderBy(item => item.OrganizationId == null ? 0 : 1)
                .ThenBy(item => item.Category)
                .ThenBy(item => item.Name)
                .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
                .Take(normalizedPageSize)
                .ToListAsync(cancellationToken);
            var items = templates.Select(item => ToPlatformTemplateResponse(item, EffectiveTemplateVersion(item))).ToList();

            return Results.Ok(new { items, page = normalizedPage, pageSize = normalizedPageSize, total });
        });

        group.MapGet("/templates/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, SupportRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var template = await dbContext.Templates.IgnoreQueryFilters().AsNoTracking()
                .Include(item => item.Organization)
                .Include(item => item.CurrentVersion)
                .ThenInclude(item => item!.Requirements)
                .Include(item => item.Versions)
                .ThenInclude(item => item.Requirements)
                .FirstOrDefaultAsync(item => item.Id == id && item.DeletedAt == null, cancellationToken);

            return template is null
                ? EndpointHelpers.Problem("not_found", "Template was not found.", StatusCodes.Status404NotFound)
                : Results.Ok(ToPlatformTemplateResponse(template, EffectiveTemplateVersion(template)));
        });

        group.MapPost("/templates", async (
            CreatePlatformTemplateRequest request,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var validation = await ValidatePlatformTemplateRequestAsync(dbContext, request.OrganizationId, request.Name, request.Title, request.Requirements, cancellationToken);
            if (validation is not null)
            {
                return validation;
            }

            var name = request.Name.Trim();
            var duplicate = await TemplateNameExistsAsync(dbContext, request.OrganizationId, name, excludingTemplateId: null, cancellationToken);
            if (duplicate)
            {
                return EndpointHelpers.Problem("template_exists", "A template with this name already exists for this scope.", StatusCodes.Status409Conflict);
            }

            var now = clock.UtcNow;
            var template = new Template
            {
                OrganizationId = request.OrganizationId,
                Name = name,
                Category = request.Category?.Trim(),
                Description = request.Description?.Trim(),
                Status = request.PublishImmediately ? TemplateStatus.Published : TemplateStatus.Draft,
                CreatedAt = now,
                UpdatedAt = now
            };
            var version = new TemplateVersion
            {
                Template = template,
                VersionNumber = 1,
                Title = request.Title.Trim(),
                Instructions = request.Instructions?.Trim(),
                SettingsJson = EndpointHelpers.JsonOrDefault(request.Settings),
                PublishedAt = request.PublishImmediately ? now : null,
                CreatedAt = now
            };
            foreach (var requirement in request.Requirements.OrderBy(item => item.DisplayOrder))
            {
                version.Requirements.Add(ToTemplateRequirement(requirement));
            }
            if (request.PublishImmediately)
            {
                template.CurrentVersionId = version.Id;
            }

            dbContext.Templates.Add(template);
            dbContext.TemplateVersions.Add(version);
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.template_created", new { template.Id, template.OrganizationId, template.Name, request.PublishImmediately }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/v1/platform/templates/{template.Id}", ToPlatformTemplateResponse(template, version));
        });

        group.MapPut("/templates/{id:guid}", async (
            Guid id,
            UpdatePlatformTemplateRequest request,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var template = await dbContext.Templates.IgnoreQueryFilters()
                .Include(item => item.Organization)
                .Include(item => item.Versions)
                .ThenInclude(item => item.Requirements)
                .FirstOrDefaultAsync(item => item.Id == id && item.DeletedAt == null, cancellationToken);
            if (template is null)
            {
                return EndpointHelpers.Problem("not_found", "Template was not found.", StatusCodes.Status404NotFound);
            }

            var nextOrganizationId = request.OrganizationId.HasValue ? request.OrganizationId : template.OrganizationId;
            var nextName = string.IsNullOrWhiteSpace(request.Name) ? template.Name : request.Name.Trim();
            var latestVersion = template.Versions.OrderByDescending(item => item.VersionNumber).FirstOrDefault();
            var nextTitle = string.IsNullOrWhiteSpace(request.Title) ? latestVersion?.Title ?? template.Name : request.Title.Trim();
            var nextRequirements = request.Requirements;
            var validation = await ValidatePlatformTemplateRequestAsync(dbContext, nextOrganizationId, nextName, nextTitle, nextRequirements, cancellationToken, requireRequirements: false);
            if (validation is not null)
            {
                return validation;
            }

            var duplicate = await TemplateNameExistsAsync(dbContext, nextOrganizationId, nextName, excludingTemplateId: template.Id, cancellationToken);
            if (duplicate)
            {
                return EndpointHelpers.Problem("template_exists", "A template with this name already exists for this scope.", StatusCodes.Status409Conflict);
            }

            template.OrganizationId = nextOrganizationId;
            template.Name = nextName;
            if (request.Category is not null)
            {
                template.Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
            }
            if (request.Description is not null)
            {
                template.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            }
            if (request.Status.HasValue)
            {
                var enumValidation = ValidateEnum(request.Status.Value, "status");
                if (enumValidation is not null)
                {
                    return enumValidation;
                }

                template.Status = request.Status.Value;
            }

            var shouldCreateVersion = request.Title is not null
                || request.Instructions is not null
                || request.Settings.HasValue
                || request.Requirements is not null;
            TemplateVersion? responseVersion = latestVersion;
            if (shouldCreateVersion)
            {
                var nextVersionNumber = template.Versions.Count == 0
                    ? 1
                    : template.Versions.Max(item => item.VersionNumber) + 1;
                responseVersion = new TemplateVersion
                {
                    Template = template,
                    VersionNumber = nextVersionNumber,
                    Title = nextTitle,
                    Instructions = request.Instructions is null ? latestVersion?.Instructions : request.Instructions.Trim(),
                    SettingsJson = request.Settings.HasValue
                        ? EndpointHelpers.JsonOrDefault(request.Settings)
                        : latestVersion?.SettingsJson ?? "{}",
                    CreatedAt = clock.UtcNow
                };

                var requirements = nextRequirements is null
                    ? latestVersion?.Requirements
                        .OrderBy(item => item.DisplayOrder)
                        .Select(CloneTemplateRequirement)
                        .ToList() ?? []
                    : nextRequirements.OrderBy(item => item.DisplayOrder).Select(ToTemplateRequirement).ToList();
                foreach (var requirement in requirements)
                {
                    responseVersion.Requirements.Add(requirement);
                }

                dbContext.TemplateVersions.Add(responseVersion);
            }

            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.template_updated", new { template.Id, template.OrganizationId, template.Name, createdVersion = shouldCreateVersion }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToPlatformTemplateResponse(template, responseVersion));
        });

        group.MapPost("/templates/{id:guid}/publish", async (
            Guid id,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var template = await dbContext.Templates.IgnoreQueryFilters()
                .Include(item => item.Organization)
                .Include(item => item.Versions)
                .ThenInclude(item => item.Requirements)
                .FirstOrDefaultAsync(item => item.Id == id && item.DeletedAt == null, cancellationToken);
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
            AddPlatformAudit(
                dbContext,
                access.Staff!.Id,
                "platform.template_published",
                new { templateId = template.Id, versionId = version.Id, version.VersionNumber },
                httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToPlatformTemplateResponse(template, version));
        });

        group.MapPost("/templates/{id:guid}/archive", async (
            Guid id,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var template = await dbContext.Templates.IgnoreQueryFilters()
                .Include(item => item.Organization)
                .Include(item => item.CurrentVersion)
                .ThenInclude(item => item!.Requirements)
                .Include(item => item.Versions)
                .ThenInclude(item => item.Requirements)
                .FirstOrDefaultAsync(item => item.Id == id && item.DeletedAt == null, cancellationToken);
            if (template is null)
            {
                return EndpointHelpers.Problem("not_found", "Template was not found.", StatusCodes.Status404NotFound);
            }

            template.Status = TemplateStatus.Archived;
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.template_archived", new { template.Id, template.Name }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToPlatformTemplateResponse(template, EffectiveTemplateVersion(template)));
        });

        group.MapDelete("/templates/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var template = await dbContext.Templates.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == id && item.DeletedAt == null, cancellationToken);
            if (template is null)
            {
                return EndpointHelpers.Problem("not_found", "Template was not found.", StatusCodes.Status404NotFound);
            }

            template.Status = TemplateStatus.Archived;
            template.DeletedAt = clock.UtcNow;
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.template_deleted", new { template.Id, template.Name }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    private static void MapOrganizations(RouteGroupBuilder group)
    {
        group.MapGet("/organizations", async (
            int? page,
            int? pageSize,
            string? q,
            OrganizationStatus? status,
            AtlasDbContext dbContext,
            IEntitlementService entitlements,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, SupportRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var query = dbContext.Organizations.IgnoreQueryFilters().AsNoTracking();
            if (status.HasValue)
            {
                query = query.Where(item => item.Status == status.Value);
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                var search = q.Trim().ToLowerInvariant();
                query = query.Where(item => item.Name.ToLower().Contains(search) || item.Slug.ToLower().Contains(search));
            }

            var normalizedPage = EndpointHelpers.NormalizePage(page);
            var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize);
            var total = await query.CountAsync(cancellationToken);
            var organizations = await query
                .OrderByDescending(item => item.CreatedAt)
                .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
                .Take(normalizedPageSize)
                .ToListAsync(cancellationToken);
            var responses = new List<PlatformOrganizationResponse>();
            foreach (var organization in organizations)
            {
                responses.Add(await ToPlatformOrganizationResponseAsync(dbContext, entitlements, clock, organization, cancellationToken));
            }

            return Results.Ok(new { items = responses, page = normalizedPage, pageSize = normalizedPageSize, total });
        });

        group.MapPost("/organizations", async (
            CreatePlatformOrganizationRequest request,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            IEntitlementService entitlements,
            IAdminSettingService settings,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug))
            {
                return EndpointHelpers.Problem("validation_failed", "Organization name and slug are required.", StatusCodes.Status422UnprocessableEntity);
            }

            var slug = request.Slug.Trim().ToLowerInvariant();
            var status = request.Status ?? OrganizationStatus.Active;
            var enumValidation = ValidateEnum(status, "status") ?? ValidateSlug(slug);
            if (enumValidation is not null)
            {
                return enumValidation;
            }

            var exists = await dbContext.Organizations.IgnoreQueryFilters().AnyAsync(item => item.Slug == slug, cancellationToken);
            if (exists)
            {
                return EndpointHelpers.Problem("slug_in_use", "Organization slug is already in use.", StatusCodes.Status409Conflict);
            }

            var defaultRetentionDays = EndpointHelpers.ReadPositiveIntSetting(
                (await settings.GetAsync(null, "retention", "defaultRetentionDays", cancellationToken))?.ValueJson,
                365);
            var organization = new Organization
            {
                Name = request.Name.Trim(),
                Slug = slug,
                Status = status,
                Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "UTC" : request.Timezone.Trim(),
                DefaultLanguage = string.IsNullOrWhiteSpace(request.DefaultLanguage) ? "en" : request.DefaultLanguage.Trim(),
                RetentionDays = request.RetentionDays is > 0 ? request.RetentionDays.Value : defaultRetentionDays,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            };
            dbContext.Organizations.Add(organization);
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.organization_created", new { organization.Id, organization.Name, organization.Slug, organization.Status }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/v1/platform/organizations/{organization.Id}", await ToPlatformOrganizationResponseAsync(dbContext, entitlements, clock, organization, cancellationToken));
        });

        group.MapGet("/organizations/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            IEntitlementService entitlements,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, SupportRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var organization = await dbContext.Organizations.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            return organization is null
                ? EndpointHelpers.Problem("not_found", "Organization was not found.", StatusCodes.Status404NotFound)
                : Results.Ok(await ToPlatformOrganizationResponseAsync(dbContext, entitlements, clock, organization, cancellationToken));
        });

        group.MapGet("/organizations/{id:guid}/api-keys", async (
            Guid id,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var organizationExists = await dbContext.Organizations.IgnoreQueryFilters()
                .AnyAsync(item => item.Id == id, cancellationToken);
            if (!organizationExists)
            {
                return EndpointHelpers.Problem("not_found", "Organization was not found.", StatusCodes.Status404NotFound);
            }

            var keys = await dbContext.ApiKeys.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item => item.OrganizationId == id)
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new PlatformOrganizationApiKeyResponse(
                    item.Id,
                    item.OrganizationId,
                    item.Name,
                    item.KeyPrefix,
                    item.Environment,
                    item.Scopes,
                    item.LastUsedAt,
                    item.ExpiresAt,
                    item.RevokedAt,
                    item.CreatedAt))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { items = keys });
        });

        group.MapPost("/organizations/{id:guid}/api-keys", async (
            Guid id,
            CreatePlatformOrganizationApiKeyRequest request,
            AtlasDbContext dbContext,
            IEntitlementService entitlements,
            IAdminSettingService settings,
            IEmailService emailService,
            IConfiguration configuration,
            ISecretHasher secretHasher,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var organization = await dbContext.Organizations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (organization is null)
            {
                return EndpointHelpers.Problem("not_found", "Organization was not found.", StatusCodes.Status404NotFound);
            }

            var environment = request.Environment ?? ApiKeyEnvironment.Production;
            if (!Enum.IsDefined(environment))
            {
                return EndpointHelpers.Problem("validation_failed", "API key environment is invalid.", StatusCodes.Status422UnprocessableEntity);
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return EndpointHelpers.Problem("validation_failed", "Name is required.", StatusCodes.Status422UnprocessableEntity);
            }

            if (environment == ApiKeyEnvironment.Production)
            {
                var feature = await entitlements.HasFeatureAsync(organization.Id, clock.UtcNow, "api_and_webhooks", cancellationToken);
                if (!feature.Allowed)
                {
                    return EndpointHelpers.EntitlementProblem(feature);
                }

                organization.DeveloperAccessStatus = DeveloperAccessStatus.ProductionApproved;
                organization.DeveloperProductionApprovedAt ??= clock.UtcNow;
                organization.DeveloperProductionRejectedAt = null;
            }

            var scopes = NormalizePlatformApiKeyScopes(request.Scopes, environment);
            var secret = EndpointHelpers.NewApiKey(environment, out var keyPrefix);
            var apiKeyDefaultDays = EndpointHelpers.ReadPositiveIntSetting(
                (await settings.GetAsync(organization.Id, "developer", "apiKeyDefaultDays", cancellationToken))?.ValueJson,
                180);
            var apiKey = new ApiKey
            {
                OrganizationId = id,
                Name = request.Name.Trim(),
                KeyPrefix = keyPrefix,
                SecretHash = secretHasher.HashSecret(secret),
                Environment = environment,
                Scopes = scopes,
                ExpiresAt = request.ExpiresAt ?? clock.UtcNow.AddDays(apiKeyDefaultDays),
                CreatedAt = clock.UtcNow
            };

            dbContext.ApiKeys.Add(apiKey);
            AddPlatformAudit(
                dbContext,
                access.Staff!.Id,
                "platform.organization_api_key_created",
                new
                {
                    OrganizationId = organization.Id,
                    ApiKeyId = apiKey.Id,
                    apiKey.Name,
                    apiKey.KeyPrefix,
                    apiKey.Environment,
                    apiKey.Scopes
                },
                httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);

            EmailSendResult? notification = null;
            if (!string.IsNullOrWhiteSpace(request.NotifyEmail))
            {
                var appBaseUrl = await EndpointHelpers.BuildAppBaseUrlAsync(settings, configuration, organization.Id, httpContext, cancellationToken);
                var email = TransactionalEmailTemplates.ApiKeyCreatedNotification(
                    FirstNameFromEmail(request.NotifyEmail),
                    organization.Name,
                    apiKey.Name,
                    apiKey.KeyPrefix,
                    apiKey.ExpiresAt?.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture),
                    appBaseUrl);
                notification = await emailService.SendAsync(
                    request.NotifyEmail.Trim().ToLowerInvariant(),
                    email.Subject,
                    email.TextBody,
                    email.HtmlBody,
                    "requests@reqara.com",
                    cancellationToken: cancellationToken,
                    headers: new Dictionary<string, string>
                    {
                        ["X-Atlas-Email-Type"] = "api-key-created",
                        ["X-Atlas-Organization-Id"] = organization.Id.ToString(),
                        ["X-Atlas-Api-Key-Id"] = apiKey.Id.ToString()
                    });
            }

            return Results.Created(
                $"/v1/platform/organizations/{organization.Id}/api-keys/{apiKey.Id}",
                new PlatformOrganizationApiKeyCreatedResponse(
                    apiKey.Id,
                    apiKey.OrganizationId,
                    apiKey.Name,
                    apiKey.KeyPrefix,
                    apiKey.Environment,
                    secret,
                    apiKey.Scopes,
                    apiKey.LastUsedAt,
                    apiKey.ExpiresAt,
                    apiKey.RevokedAt,
                    apiKey.CreatedAt,
                    notification?.Sent ?? false,
                    notification?.Error));
        });

        group.MapDelete("/organizations/{organizationId:guid}/api-keys/{apiKeyId:guid}", async (
            Guid organizationId,
            Guid apiKeyId,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var apiKey = await dbContext.ApiKeys.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == apiKeyId && item.OrganizationId == organizationId, cancellationToken);
            if (apiKey is null)
            {
                return EndpointHelpers.Problem("not_found", "API key was not found.", StatusCodes.Status404NotFound);
            }

            apiKey.RevokedAt ??= clock.UtcNow;
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.organization_api_key_revoked", new { organizationId, apiKey.Id, apiKey.Name, apiKey.KeyPrefix }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        group.MapPatch("/organizations/{id:guid}", async (
            Guid id,
            UpdatePlatformOrganizationRequest request,
            AtlasDbContext dbContext,
            IEntitlementService entitlements,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var organization = await dbContext.Organizations.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (organization is null)
            {
                return EndpointHelpers.Problem("not_found", "Organization was not found.", StatusCodes.Status404NotFound);
            }

            organization.Name = string.IsNullOrWhiteSpace(request.Name) ? organization.Name : request.Name.Trim();
            organization.Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? organization.Timezone : request.Timezone.Trim();
            organization.DefaultLanguage = string.IsNullOrWhiteSpace(request.DefaultLanguage) ? organization.DefaultLanguage : request.DefaultLanguage.Trim();
            organization.AccentColor = request.AccentColor ?? organization.AccentColor;
            organization.PrivacyStatement = request.PrivacyStatement ?? organization.PrivacyStatement;
            organization.RetentionDays = request.RetentionDays is > 0 ? request.RetentionDays.Value : organization.RetentionDays;
            if (request.Status.HasValue)
            {
                var enumValidation = ValidateEnum(request.Status.Value, "status");
                if (enumValidation is not null)
                {
                    return enumValidation;
                }

                organization.Status = request.Status.Value;
                if (organization.Status == OrganizationStatus.Active)
                {
                    organization.DeletedAt = null;
                }
            }

            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.organization_updated", new { organization.Id, organization.Name, organization.Status }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(await ToPlatformOrganizationResponseAsync(dbContext, entitlements, clock, organization, cancellationToken));
        });

        group.MapPost("/organizations/{id:guid}/approve", async (
            Guid id,
            AtlasDbContext dbContext,
            IEntitlementService entitlements,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var organization = await dbContext.Organizations.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (organization is null)
            {
                return EndpointHelpers.Problem("not_found", "Organization was not found.", StatusCodes.Status404NotFound);
            }

            organization.Status = OrganizationStatus.Active;
            organization.DeletedAt = null;
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.organization_approved", new { organization.Id, organization.Name }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(await ToPlatformOrganizationResponseAsync(dbContext, entitlements, clock, organization, cancellationToken));
        });

        group.MapPost("/organizations/{id:guid}/developer-access/approve", async (
            Guid id,
            PlatformDeveloperAccessDecisionRequest request,
            AtlasDbContext dbContext,
            IEntitlementService entitlements,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var organization = await dbContext.Organizations.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (organization is null)
            {
                return EndpointHelpers.Problem("not_found", "Organization was not found.", StatusCodes.Status404NotFound);
            }

            var feature = await entitlements.HasFeatureAsync(organization.Id, clock.UtcNow, "api_and_webhooks", cancellationToken);
            if (!feature.Allowed)
            {
                return EndpointHelpers.EntitlementProblem(feature);
            }

            organization.DeveloperAccessStatus = DeveloperAccessStatus.ProductionApproved;
            organization.DeveloperProductionApprovedAt = clock.UtcNow;
            organization.DeveloperProductionRejectedAt = null;
            organization.DeveloperProductionNotes = request.Notes?.Trim();
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.organization_developer_access_approved", new { organization.Id, organization.Name }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(await ToPlatformOrganizationResponseAsync(dbContext, entitlements, clock, organization, cancellationToken));
        });

        group.MapPost("/organizations/{id:guid}/developer-access/reject", async (
            Guid id,
            PlatformDeveloperAccessDecisionRequest request,
            AtlasDbContext dbContext,
            IEntitlementService entitlements,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var organization = await dbContext.Organizations.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (organization is null)
            {
                return EndpointHelpers.Problem("not_found", "Organization was not found.", StatusCodes.Status404NotFound);
            }

            organization.DeveloperAccessStatus = DeveloperAccessStatus.ProductionRejected;
            organization.DeveloperProductionRejectedAt = clock.UtcNow;
            organization.DeveloperProductionApprovedAt = null;
            organization.DeveloperProductionNotes = request.Notes?.Trim();
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.organization_developer_access_rejected", new { organization.Id, organization.Name }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(await ToPlatformOrganizationResponseAsync(dbContext, entitlements, clock, organization, cancellationToken));
        });

        group.MapDelete("/organizations/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var organization = await dbContext.Organizations.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (organization is null)
            {
                return EndpointHelpers.Problem("not_found", "Organization was not found.", StatusCodes.Status404NotFound);
            }

            organization.Status = OrganizationStatus.Closed;
            organization.DeletedAt ??= clock.UtcNow;
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.organization_closed", new { organization.Id, organization.Name }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    private static void MapInterests(RouteGroupBuilder group)
    {
        group.MapGet("/organization-requests", async (
            int? page,
            int? pageSize,
            OrganizationInterestStatus? status,
            Guid? assignedStaffId,
            string? q,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, SupportRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var query = dbContext.PlatformOrganizationInterests.IgnoreQueryFilters().AsNoTracking();
            if (status.HasValue)
            {
                query = query.Where(item => item.Status == status.Value);
            }
            if (assignedStaffId.HasValue)
            {
                query = query.Where(item => item.AssignedStaffId == assignedStaffId.Value);
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                var search = q.Trim().ToLowerInvariant();
                query = query.Where(item => item.OrganizationName.ToLower().Contains(search)
                    || item.ContactEmail.ToLower().Contains(search)
                    || item.ContactName.ToLower().Contains(search));
            }

            var normalizedPage = EndpointHelpers.NormalizePage(page);
            var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize);
            var total = await query.CountAsync(cancellationToken);
            var interests = await query
                .OrderByDescending(item => item.CreatedAt)
                .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
                .Take(normalizedPageSize)
                .Select(item => ToInterestResponse(item))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { items = interests, page = normalizedPage, pageSize = normalizedPageSize, total });
        });

        group.MapGet("/interests", async (
            int? page,
            int? pageSize,
            OrganizationInterestStatus? status,
            Guid? assignedStaffId,
            string? q,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, SupportRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var query = dbContext.PlatformOrganizationInterests.IgnoreQueryFilters().AsNoTracking();
            if (status.HasValue)
            {
                query = query.Where(item => item.Status == status.Value);
            }
            if (assignedStaffId.HasValue)
            {
                query = query.Where(item => item.AssignedStaffId == assignedStaffId.Value);
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                var search = q.Trim().ToLowerInvariant();
                query = query.Where(item => item.OrganizationName.ToLower().Contains(search)
                    || item.ContactEmail.ToLower().Contains(search)
                    || item.ContactName.ToLower().Contains(search));
            }

            var normalizedPage = EndpointHelpers.NormalizePage(page);
            var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize);
            var total = await query.CountAsync(cancellationToken);
            var interests = await query
                .OrderByDescending(item => item.CreatedAt)
                .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
                .Take(normalizedPageSize)
                .Select(item => ToInterestResponse(item))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { items = interests, page = normalizedPage, pageSize = normalizedPageSize, total });
        });

        group.MapPost("/interests", async (
            CreateOrganizationInterestRequest request,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, SupportRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var validation = ValidateInterestRequest(request.OrganizationName, request.ContactName, request.ContactEmail);
            if (validation is not null)
            {
                return validation;
            }

            var status = request.Status ?? OrganizationInterestStatus.New;
            var enumValidation = ValidateEnum(status, "status");
            if (enumValidation is not null)
            {
                return enumValidation;
            }

            var assignmentValidation = await ValidatePlatformStaffAssignmentAsync(dbContext, request.AssignedStaffId, cancellationToken);
            if (assignmentValidation is not null)
            {
                return assignmentValidation;
            }

            var interest = CreateInterestEntity(request, includeInternalFields: true);
            dbContext.PlatformOrganizationInterests.Add(interest);
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.interest_created", new { interest.Id, interest.OrganizationName, interest.ContactEmail }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/v1/platform/interests/{interest.Id}", ToInterestResponse(interest));
        });

        group.MapGet("/interests/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, SupportRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var interest = await dbContext.PlatformOrganizationInterests.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            return interest is null
                ? EndpointHelpers.Problem("not_found", "Organization interest was not found.", StatusCodes.Status404NotFound)
                : Results.Ok(ToInterestResponse(interest));
        });

        group.MapPut("/interests/{id:guid}", async (
            Guid id,
            UpdateOrganizationInterestRequest request,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, SupportRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var interest = await dbContext.PlatformOrganizationInterests.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (interest is null)
            {
                return EndpointHelpers.Problem("not_found", "Organization interest was not found.", StatusCodes.Status404NotFound);
            }

            if (request.Status.HasValue)
            {
                var enumValidation = ValidateEnum(request.Status.Value, "status");
                if (enumValidation is not null)
                {
                    return enumValidation;
                }
            }

            var assignmentValidation = await ValidatePlatformStaffAssignmentAsync(dbContext, request.AssignedStaffId, cancellationToken);
            if (assignmentValidation is not null)
            {
                return assignmentValidation;
            }

            interest.OrganizationName = string.IsNullOrWhiteSpace(request.OrganizationName) ? interest.OrganizationName : request.OrganizationName.Trim();
            interest.ContactName = string.IsNullOrWhiteSpace(request.ContactName) ? interest.ContactName : request.ContactName.Trim();
            interest.ContactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? interest.ContactEmail : NormalizeEmail(request.ContactEmail);
            interest.ContactPhone = request.ContactPhone ?? interest.ContactPhone;
            interest.Source = request.Source ?? interest.Source;
            interest.Region = request.Region ?? interest.Region;
            interest.ExpectedVolume = request.ExpectedVolume ?? interest.ExpectedVolume;
            interest.Message = request.Message ?? interest.Message;
            interest.Notes = request.Notes ?? interest.Notes;
            interest.AssignedStaffId = request.AssignedStaffId ?? interest.AssignedStaffId;
            if (request.Status.HasValue)
            {
                interest.Status = request.Status.Value;
                interest.RejectedAt = interest.Status == OrganizationInterestStatus.Rejected ? clock.UtcNow : interest.RejectedAt;
            }

            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.interest_updated", new { interest.Id, interest.Status, interest.AssignedStaffId }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToInterestResponse(interest));
        });

        group.MapPost("/interests/{id:guid}/approve", async (
            Guid id,
            ApproveOrganizationInterestRequest request,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            IAdminSettingService settings,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var interest = await dbContext.PlatformOrganizationInterests.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (interest is null)
            {
                return EndpointHelpers.Problem("not_found", "Organization interest was not found.", StatusCodes.Status404NotFound);
            }

            Organization? organization = null;
            if (request.CreateOrganization)
            {
                if (string.IsNullOrWhiteSpace(request.OrganizationSlug))
                {
                    return EndpointHelpers.Problem("validation_failed", "Organization slug is required when creating an organization.", StatusCodes.Status422UnprocessableEntity);
                }

                var slug = request.OrganizationSlug.Trim().ToLowerInvariant();
                var slugValidation = ValidateSlug(slug);
                if (slugValidation is not null)
                {
                    return slugValidation;
                }

                var slugExists = await dbContext.Organizations.IgnoreQueryFilters().AnyAsync(item => item.Slug == slug, cancellationToken);
                if (slugExists)
                {
                    return EndpointHelpers.Problem("slug_in_use", "Organization slug is already in use.", StatusCodes.Status409Conflict);
                }

                var ownerEmail = NormalizeEmail(request.OwnerEmail ?? interest.ContactEmail);
                var ownerName = string.IsNullOrWhiteSpace(request.OwnerFullName) ? interest.ContactName : request.OwnerFullName.Trim();
                var owner = await dbContext.Users.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Email == ownerEmail, cancellationToken);
                if (owner is null)
                {
                    if (string.IsNullOrWhiteSpace(request.OwnerPassword))
                    {
                        return EndpointHelpers.Problem("validation_failed", "Owner password is required for a new owner user.", StatusCodes.Status422UnprocessableEntity);
                    }

                    owner = new AppUser
                    {
                        Email = ownerEmail,
                        FullName = ownerName,
                        CreatedAt = clock.UtcNow
                    };
                    owner.PasswordHash = new PasswordHasher<AppUser>().HashPassword(owner, request.OwnerPassword);
                    dbContext.Users.Add(owner);
                }

                var defaultRetentionDays = EndpointHelpers.ReadPositiveIntSetting(
                    (await settings.GetAsync(null, "retention", "defaultRetentionDays", cancellationToken))?.ValueJson,
                    365);
                organization = new Organization
                {
                    Name = string.IsNullOrWhiteSpace(request.OrganizationName) ? interest.OrganizationName : request.OrganizationName.Trim(),
                    Slug = slug,
                    Status = OrganizationStatus.Active,
                    Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "UTC" : request.Timezone.Trim(),
                    DefaultLanguage = string.IsNullOrWhiteSpace(request.DefaultLanguage) ? "en" : request.DefaultLanguage.Trim(),
                    RetentionDays = defaultRetentionDays,
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow
                };
                dbContext.Organizations.Add(organization);
                dbContext.OrganizationUsers.Add(new OrganizationUser
                {
                    Organization = organization,
                    User = owner,
                    Role = OrganizationUserRole.Owner,
                    Status = MembershipStatus.Active,
                    JoinedAt = clock.UtcNow,
                    CreatedAt = clock.UtcNow
                });
            }

            interest.Status = OrganizationInterestStatus.Approved;
            interest.ApprovedAt = clock.UtcNow;
            interest.RejectedAt = null;
            if (organization is not null)
            {
                interest.ApprovedOrganization = organization;
            }

            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.interest_approved", new { interest.Id, interest.OrganizationName, organizationId = organization?.Id }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToInterestResponse(interest));
        });

        group.MapDelete("/interests/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var interest = await dbContext.PlatformOrganizationInterests.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (interest is null)
            {
                return EndpointHelpers.Problem("not_found", "Organization interest was not found.", StatusCodes.Status404NotFound);
            }

            dbContext.PlatformOrganizationInterests.Remove(interest);
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.interest_deleted", new { interest.Id, interest.OrganizationName }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    private static void MapRevenue(RouteGroupBuilder group)
    {
        group.MapGet("/revenue-events", async (
            int? page,
            int? pageSize,
            Guid? organizationId,
            PlatformRevenueEventType? type,
            DateTimeOffset? from,
            DateTimeOffset? to,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, FinanceRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var query = FilterRevenue(dbContext.PlatformRevenueEvents.IgnoreQueryFilters().AsNoTracking(), organizationId, type, from, to);
            var normalizedPage = EndpointHelpers.NormalizePage(page);
            var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize);
            var total = await query.CountAsync(cancellationToken);
            var rawEvents = await query
                .OrderByDescending(item => item.OccurredAt)
                .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
                .Take(normalizedPageSize)
                .ToListAsync(cancellationToken);
            var organizationLookup = await LoadOrganizationLookupAsync(
                dbContext,
                rawEvents.Select(item => item.OrganizationId),
                cancellationToken);
            var events = rawEvents
                .Select(item => ToRevenueResponse(item, FindOrganizationLookup(organizationLookup, item.OrganizationId)))
                .ToList();
            return Results.Ok(new { items = events, page = normalizedPage, pageSize = normalizedPageSize, total });
        });

        group.MapGet("/billing/subscriptions", async (
            int? page,
            int? pageSize,
            string? status,
            string? planCode,
            string? provider,
            AtlasDbContext dbContext,
            IEntitlementService entitlements,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, FinanceRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var organizations = await dbContext.Organizations.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.DeletedAt == null)
                .OrderBy(item => item.Name)
                .ToListAsync(cancellationToken);
            var rows = new List<PlatformBillingSubscriptionResponse>();
            foreach (var organization in organizations)
            {
                var snapshot = await entitlements.GetOrganizationEntitlementsAsync(organization.Id, clock.UtcNow, cancellationToken);
                if (!MatchesSubscriptionFilter(snapshot, status, planCode, provider))
                {
                    continue;
                }

                rows.Add(ToBillingSubscriptionResponse(organization, snapshot, clock.UtcNow));
            }

            var normalizedPage = EndpointHelpers.NormalizePage(page);
            var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize);
            var total = rows.Count;
            return Results.Ok(new
            {
                items = rows
                    .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
                    .Take(normalizedPageSize)
                    .ToList(),
                page = normalizedPage,
                pageSize = normalizedPageSize,
                total
            });
        });

        group.MapPost("/revenue-events", async (
            UpsertRevenueEventRequest request,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, FinanceRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var validation = ValidateRevenueRequest(request.Type, request.Amount, request.Currency, request.Source, request.OccurredAt);
            if (validation is not null)
            {
                return validation;
            }

            var organizationValidation = await ValidateOrganizationReferenceAsync(dbContext, request.OrganizationId, cancellationToken);
            if (organizationValidation is not null)
            {
                return organizationValidation;
            }

            var revenueEvent = new PlatformRevenueEvent
            {
                OrganizationId = request.OrganizationId,
                Type = request.Type!.Value,
                Amount = request.Amount,
                Currency = request.Currency.Trim().ToUpperInvariant(),
                Source = request.Source.Trim(),
                ExternalReference = request.ExternalReference?.Trim(),
                OccurredAt = request.OccurredAt!.Value,
                PeriodStart = request.PeriodStart,
                PeriodEnd = request.PeriodEnd,
                MetadataJson = request.Metadata?.GetRawText() ?? "{}",
                RecordedByStaffId = access.Staff!.Id
            };
            dbContext.PlatformRevenueEvents.Add(revenueEvent);
            AddPlatformAudit(dbContext, access.Staff.Id, "platform.revenue_event_created", new { revenueEvent.Id, revenueEvent.OrganizationId, revenueEvent.Type, revenueEvent.Amount, revenueEvent.Currency }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            var organizationLookup = await LoadOrganizationLookupAsync(dbContext, [revenueEvent.OrganizationId], cancellationToken);
            return Results.Created($"/v1/platform/revenue-events/{revenueEvent.Id}", ToRevenueResponse(revenueEvent, FindOrganizationLookup(organizationLookup, revenueEvent.OrganizationId)));
        });

        group.MapGet("/revenue-events/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, FinanceRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var revenueEvent = await dbContext.PlatformRevenueEvents.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (revenueEvent is null)
            {
                return EndpointHelpers.Problem("not_found", "Revenue event was not found.", StatusCodes.Status404NotFound);
            }

            var organizationLookup = await LoadOrganizationLookupAsync(dbContext, [revenueEvent.OrganizationId], cancellationToken);
            return Results.Ok(ToRevenueResponse(revenueEvent, FindOrganizationLookup(organizationLookup, revenueEvent.OrganizationId)));
        });

        group.MapPut("/revenue-events/{id:guid}", async (
            Guid id,
            UpsertRevenueEventRequest request,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, FinanceRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var revenueEvent = await dbContext.PlatformRevenueEvents.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (revenueEvent is null)
            {
                return EndpointHelpers.Problem("not_found", "Revenue event was not found.", StatusCodes.Status404NotFound);
            }

            var validation = ValidateRevenueRequest(request.Type, request.Amount, request.Currency, request.Source, request.OccurredAt);
            if (validation is not null)
            {
                return validation;
            }

            var organizationValidation = await ValidateOrganizationReferenceAsync(dbContext, request.OrganizationId, cancellationToken);
            if (organizationValidation is not null)
            {
                return organizationValidation;
            }

            revenueEvent.OrganizationId = request.OrganizationId;
            revenueEvent.Type = request.Type!.Value;
            revenueEvent.Amount = request.Amount;
            revenueEvent.Currency = request.Currency.Trim().ToUpperInvariant();
            revenueEvent.Source = request.Source.Trim();
            revenueEvent.ExternalReference = request.ExternalReference?.Trim();
            revenueEvent.OccurredAt = request.OccurredAt!.Value;
            revenueEvent.PeriodStart = request.PeriodStart;
            revenueEvent.PeriodEnd = request.PeriodEnd;
            revenueEvent.MetadataJson = request.Metadata?.GetRawText() ?? "{}";

            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.revenue_event_updated", new { revenueEvent.Id, revenueEvent.OrganizationId, revenueEvent.Type, revenueEvent.Amount, revenueEvent.Currency }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            var organizationLookup = await LoadOrganizationLookupAsync(dbContext, [revenueEvent.OrganizationId], cancellationToken);
            return Results.Ok(ToRevenueResponse(revenueEvent, FindOrganizationLookup(organizationLookup, revenueEvent.OrganizationId)));
        });

        group.MapDelete("/revenue-events/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, FinanceRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var revenueEvent = await dbContext.PlatformRevenueEvents.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (revenueEvent is null)
            {
                return EndpointHelpers.Problem("not_found", "Revenue event was not found.", StatusCodes.Status404NotFound);
            }

            dbContext.PlatformRevenueEvents.Remove(revenueEvent);
            AddPlatformAudit(dbContext, access.Staff!.Id, "platform.revenue_event_deleted", new { revenueEvent.Id, revenueEvent.OrganizationId, revenueEvent.Type, revenueEvent.Amount, revenueEvent.Currency }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    private static void MapMetrics(RouteGroupBuilder group)
    {
        group.MapGet("/metrics", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var effectiveTo = to ?? clock.UtcNow;
            var effectiveFrom = from ?? effectiveTo.AddDays(-30);

            var orgs = dbContext.Organizations.IgnoreQueryFilters();
            var interests = dbContext.PlatformOrganizationInterests.IgnoreQueryFilters();
            var revenue = dbContext.PlatformRevenueEvents.IgnoreQueryFilters();

            var revenueInPeriod = await revenue
                .Where(item => item.OccurredAt >= effectiveFrom && item.OccurredAt <= effectiveTo)
                .GroupBy(item => item.Currency)
                .Select(grouping => new RevenueByCurrency(grouping.Key, grouping.Sum(item => item.Amount)))
                .ToListAsync(cancellationToken);

            var revenueAllTime = await revenue
                .GroupBy(item => item.Currency)
                .Select(grouping => new RevenueByCurrency(grouping.Key, grouping.Sum(item => item.Amount)))
                .ToListAsync(cancellationToken);

            var metrics = new PlatformMetricsResponse(
                effectiveFrom,
                effectiveTo,
                await orgs.CountAsync(cancellationToken),
                await orgs.CountAsync(item => item.Status == OrganizationStatus.Active && item.DeletedAt == null, cancellationToken),
                await orgs.CountAsync(item => item.Status == OrganizationStatus.Suspended, cancellationToken),
                await orgs.CountAsync(item => item.Status == OrganizationStatus.Closed || item.DeletedAt != null, cancellationToken),
                await dbContext.PlatformStaff.IgnoreQueryFilters().CountAsync(item => item.Status == PlatformStaffStatus.Active, cancellationToken),
                await interests.CountAsync(item => item.Status == OrganizationInterestStatus.New, cancellationToken),
                await interests.CountAsync(item => item.Status == OrganizationInterestStatus.Qualified, cancellationToken),
                await interests.CountAsync(item => item.Status == OrganizationInterestStatus.Approved, cancellationToken),
                await interests.CountAsync(item => item.Status == OrganizationInterestStatus.Rejected, cancellationToken),
                await dbContext.Actions.IgnoreQueryFilters().CountAsync(cancellationToken),
                await dbContext.Actions.IgnoreQueryFilters().CountAsync(item => item.Status == ChecklistActionStatus.Sent || item.Status == ChecklistActionStatus.InProgress, cancellationToken),
                await dbContext.Submissions.IgnoreQueryFilters().CountAsync(cancellationToken),
                await dbContext.Submissions.IgnoreQueryFilters().CountAsync(item => item.Status == SubmissionStatus.Accepted, cancellationToken),
                await dbContext.FileAssets.IgnoreQueryFilters().CountAsync(item => item.ScanStatus == FileScanStatus.Pending, cancellationToken),
                await dbContext.NotificationDeliveries.IgnoreQueryFilters().CountAsync(item => item.Status == NotificationDeliveryStatus.Failed, cancellationToken),
                await dbContext.UsageEvents.IgnoreQueryFilters().Where(item => item.OccurredAt >= effectiveFrom && item.OccurredAt <= effectiveTo).SumAsync(item => item.Quantity, cancellationToken),
                revenueInPeriod,
                revenueAllTime);

            return Results.Ok(metrics);
        });
    }

    private static void MapAudit(RouteGroupBuilder group)
    {
        group.MapGet("/audit", async (
            int? page,
            int? pageSize,
            Guid? staffId,
            string? eventType,
            DateTimeOffset? from,
            DateTimeOffset? to,
            AtlasDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, CoreRoles);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var query = dbContext.PlatformAuditEvents.IgnoreQueryFilters().AsNoTracking();
            if (staffId.HasValue)
            {
                query = query.Where(item => item.StaffId == staffId.Value);
            }
            if (!string.IsNullOrWhiteSpace(eventType))
            {
                query = query.Where(item => item.EventType == eventType.Trim());
            }
            if (from.HasValue)
            {
                query = query.Where(item => item.CreatedAt >= from.Value);
            }
            if (to.HasValue)
            {
                query = query.Where(item => item.CreatedAt <= to.Value);
            }

            var normalizedPage = EndpointHelpers.NormalizePage(page);
            var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize, max: 250);
            var total = await query.CountAsync(cancellationToken);
            var events = await query
                .OrderByDescending(item => item.CreatedAt)
                .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
                .Take(normalizedPageSize)
                .Select(item => new PlatformAuditResponse(item.Id, item.StaffId, item.EventType, item.EventData, item.CreatedAt))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { items = events, page = normalizedPage, pageSize = normalizedPageSize, total });
        });
    }

    private static async Task<IResult> CreatePublicInterest(
        CreateOrganizationInterestRequest request,
        AtlasDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validation = ValidateInterestRequest(request.OrganizationName, request.ContactName, request.ContactEmail);
        if (validation is not null)
        {
            return validation;
        }

        var interest = CreateInterestEntity(request, includeInternalFields: false);
        dbContext.PlatformOrganizationInterests.Add(interest);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Accepted($"/v1/public/interests/{interest.Id}", new { interest.Id, interest.Status });
    }

    private static async Task<IResult> CreatePublicContact(
        CreatePublicContactRequest request,
        AtlasDbContext dbContext,
        IEmailService emailService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validation = ValidateContactRequest(request);
        if (validation is not null)
        {
            return validation;
        }

        var captchaToken = httpContext.Request.Headers["X-Reqara-Captcha-Token"].FirstOrDefault()
            ?? request.TurnstileToken;
        if (string.IsNullOrWhiteSpace(captchaToken))
        {
            return EndpointHelpers.Problem("captcha_required", "Captcha token is required.", StatusCodes.Status422UnprocessableEntity);
        }

        var captcha = await VerifyTurnstileAsync(dbContext, httpClientFactory, configuration, captchaToken.Trim(), httpContext, cancellationToken);
        if (captcha is not null)
        {
            return captcha;
        }

        var toEmail = await GetSystemSettingAsync(dbContext, "publicContact", "toEmail", cancellationToken)
            ?? configuration["PublicContact:ToEmail"]
            ?? "hello@nextronyx.com";
        var email = TransactionalEmailTemplates.PublicContact(
            request.Name.Trim(),
            NormalizeEmail(request.Email),
            request.Topic.Trim(),
            request.Message.Trim());
        var send = await emailService.SendAsync(
            toEmail,
            email.Subject,
            email.TextBody,
            email.HtmlBody,
            "requests@reqara.com",
            "Reqara",
            cancellationToken,
            new Dictionary<string, string>
            {
                ["X-Atlas-Email-Type"] = "public-contact"
            },
            NormalizeEmail(request.Email));

        dbContext.PlatformAuditEvents.Add(new PlatformAuditEvent
        {
            EventType = send.Sent ? "public.contact_sent" : "public.contact_send_failed",
            EventData = JsonSerializer.Serialize(
                new
                {
                    name = request.Name.Trim(),
                    email = NormalizeEmail(request.Email),
                    topic = request.Topic.Trim(),
                    toEmail,
                    send.MessageId,
                    send.Error
                },
                EndpointHelpers.JsonOptions),
            IpAddress = httpContext.Connection.RemoteIpAddress,
            UserAgent = httpContext.Request.Headers.UserAgent.FirstOrDefault()
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return send.Sent
            ? Results.Accepted("/v1/public/contact", new PublicContactResponse(true))
            : EndpointHelpers.Problem("email_send_failed", "Contact message could not be sent.", StatusCodes.Status503ServiceUnavailable);
    }

    private static IResult? ValidateContactRequest(CreatePublicContactRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Topic)
            || string.IsNullOrWhiteSpace(request.Message))
        {
            return EndpointHelpers.Problem("validation_failed", "Name, email, topic and message are required.", StatusCodes.Status422UnprocessableEntity);
        }

        if (request.Name.Length > 160)
        {
            return EndpointHelpers.Problem("validation_failed", "Name must be 160 characters or fewer.", StatusCodes.Status422UnprocessableEntity);
        }

        if (request.Email.Length > 320 || !request.Email.Contains('@', StringComparison.Ordinal))
        {
            return EndpointHelpers.Problem("validation_failed", "A valid email address is required.", StatusCodes.Status422UnprocessableEntity);
        }

        if (request.Topic.Length > 120)
        {
            return EndpointHelpers.Problem("validation_failed", "Topic must be 120 characters or fewer.", StatusCodes.Status422UnprocessableEntity);
        }

        if (request.Message.Length > 4000)
        {
            return EndpointHelpers.Problem("validation_failed", "Message must be 4000 characters or fewer.", StatusCodes.Status422UnprocessableEntity);
        }

        return null;
    }

    private static async Task<IResult?> VerifyTurnstileAsync(
        AtlasDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        string token,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var secret = await GetSystemSettingAsync(dbContext, "turnstile", "secretKey", cancellationToken)
            ?? configuration["Turnstile:SecretKey"]
            ?? configuration["TURNSTILE_SECRET_KEY"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            return EndpointHelpers.Problem("captcha_not_configured", "Captcha verification is not configured.", StatusCodes.Status503ServiceUnavailable);
        }

        var remoteIp = httpContext.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
            ?? httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
            ?? httpContext.Connection.RemoteIpAddress?.ToString();
        var form = new Dictionary<string, string>
        {
            ["secret"] = secret,
            ["response"] = token
        };
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            form["remoteip"] = remoteIp;
        }

        using var content = new FormUrlEncodedContent(form);
        var client = httpClientFactory.CreateClient("Turnstile");
        using var response = await client.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return EndpointHelpers.Problem("captcha_unavailable", "Captcha verification is temporarily unavailable.", StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True)
            {
                return null;
            }

            return EndpointHelpers.Problem("captcha_failed", "Captcha verification failed.", StatusCodes.Status403Forbidden);
        }
        catch (JsonException)
        {
            return EndpointHelpers.Problem("captcha_unavailable", "Captcha verification returned an invalid response.", StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<string?> GetSystemSettingAsync(
        AtlasDbContext dbContext,
        string category,
        string key,
        CancellationToken cancellationToken)
    {
        var setting = await dbContext.AdminSettings.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.OrganizationId == null && item.Category == category && item.Key == key)
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var value = EndpointHelpers.ReadStringSetting(setting?.ValueJson);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static PlatformOrganizationInterest CreateInterestEntity(CreateOrganizationInterestRequest request, bool includeInternalFields)
    {
        return new PlatformOrganizationInterest
        {
            OrganizationName = request.OrganizationName.Trim(),
            ContactName = request.ContactName.Trim(),
            ContactEmail = NormalizeEmail(request.ContactEmail),
            ContactPhone = request.ContactPhone?.Trim(),
            Source = request.Source?.Trim(),
            Region = request.Region?.Trim(),
            ExpectedVolume = request.ExpectedVolume?.Trim(),
            Message = request.Message?.Trim(),
            Notes = includeInternalFields ? request.Notes?.Trim() : null,
            Status = includeInternalFields ? request.Status ?? OrganizationInterestStatus.New : OrganizationInterestStatus.New,
            AssignedStaffId = includeInternalFields ? request.AssignedStaffId : null
        };
    }

    private static IResult? ValidateInterestRequest(string? organizationName, string? contactName, string? contactEmail)
    {
        return string.IsNullOrWhiteSpace(organizationName)
            || string.IsNullOrWhiteSpace(contactName)
            || string.IsNullOrWhiteSpace(contactEmail)
                ? EndpointHelpers.Problem("validation_failed", "Organization name, contact name and contact email are required.", StatusCodes.Status422UnprocessableEntity)
                : null;
    }

    private static async Task<PlatformAccess> RequirePlatformStaffAsync(
        AtlasDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        params PlatformStaffRole[] roles)
    {
        if (httpContext.User.Identity?.IsAuthenticated != true
            || httpContext.User.FindFirstValue(ActorKindClaim) != PlatformActorKind
            || !Guid.TryParse(httpContext.User.FindFirstValue(PlatformStaffIdClaim), out var staffId))
        {
            return new PlatformAccess(null, EndpointHelpers.Problem("platform_auth_required", "Platform staff authentication is required.", StatusCodes.Status401Unauthorized));
        }

        var staff = await dbContext.PlatformStaff.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == staffId, cancellationToken);
        if (staff is null || staff.Status != PlatformStaffStatus.Active)
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return new PlatformAccess(null, EndpointHelpers.Problem("platform_auth_required", "Platform staff authentication is required.", StatusCodes.Status401Unauthorized));
        }

        if (roles.Length > 0 && !HasRole(staff, roles))
        {
            return new PlatformAccess(null, EndpointHelpers.Problem("forbidden", "Platform role is not allowed for this action.", StatusCodes.Status403Forbidden));
        }

        return new PlatformAccess(staff, null);
    }

    private static bool HasRole(PlatformStaff staff, IReadOnlyCollection<PlatformStaffRole> roles)
    {
        return roles.Contains(staff.Role);
    }

    private static async Task SignInPlatformStaffAsync(HttpContext httpContext, PlatformStaff staff)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, staff.Id.ToString()),
            new Claim(ClaimTypes.Email, staff.Email),
            new Claim(ClaimTypes.Name, staff.Email),
            new Claim(ActorKindClaim, PlatformActorKind),
            new Claim(PlatformStaffIdClaim, staff.Id.ToString()),
            new Claim(ClaimTypes.Role, staff.Role.ToString())
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    }

    private static async Task<bool> HasAnotherActiveOwnerAsync(
        AtlasDbContext dbContext,
        Guid currentStaffId,
        CancellationToken cancellationToken)
    {
        return await dbContext.PlatformStaff.IgnoreQueryFilters()
            .AnyAsync(item => item.Id != currentStaffId
                && item.Role == PlatformStaffRole.Owner
                && item.Status == PlatformStaffStatus.Active,
                cancellationToken);
    }

    private static void AddPlatformAudit(
        AtlasDbContext dbContext,
        Guid? staffId,
        string eventType,
        object eventData,
        HttpContext httpContext)
    {
        dbContext.PlatformAuditEvents.Add(new PlatformAuditEvent
        {
            StaffId = staffId,
            EventType = eventType,
            EventData = JsonSerializer.Serialize(eventData, EndpointHelpers.JsonOptions),
            IpAddress = httpContext.Connection.RemoteIpAddress,
            UserAgent = httpContext.Request.Headers.UserAgent.FirstOrDefault(),
            CorrelationId = Guid.TryParse(httpContext.TraceIdentifier, out var traceId) ? traceId : null
        });
    }

    private static IResult? ValidateSettingRequest(AdminSettingScope scope, Guid? organizationId, string? category, string? key)
    {
        var enumValidation = ValidateEnum(scope, "scope");
        if (enumValidation is not null)
        {
            return enumValidation;
        }

        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(key))
        {
            return EndpointHelpers.Problem("validation_failed", "Category and key are required.", StatusCodes.Status422UnprocessableEntity);
        }

        if (scope == AdminSettingScope.System && organizationId.HasValue)
        {
            return EndpointHelpers.Problem("validation_failed", "System settings cannot have an organization id.", StatusCodes.Status422UnprocessableEntity);
        }

        if (scope == AdminSettingScope.Organization && !organizationId.HasValue)
        {
            return EndpointHelpers.Problem("validation_failed", "Organization settings require an organization id.", StatusCodes.Status422UnprocessableEntity);
        }

        return null;
    }

    private static IResult? ValidateRevenueRequest(
        PlatformRevenueEventType? type,
        decimal amount,
        string? currency,
        string? source,
        DateTimeOffset? occurredAt)
    {
        if (!type.HasValue)
        {
            return EndpointHelpers.Problem("validation_failed", "Revenue event type is required.", StatusCodes.Status422UnprocessableEntity);
        }

        var enumValidation = ValidateEnum(type.Value, "type");
        if (enumValidation is not null)
        {
            return enumValidation;
        }

        if (amount == 0)
        {
            return EndpointHelpers.Problem("validation_failed", "Amount must be non-zero.", StatusCodes.Status422UnprocessableEntity);
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3 || string.IsNullOrWhiteSpace(source))
        {
            return EndpointHelpers.Problem("validation_failed", "Three-letter currency and source are required.", StatusCodes.Status422UnprocessableEntity);
        }

        if (!occurredAt.HasValue)
        {
            return EndpointHelpers.Problem("validation_failed", "Occurred at is required.", StatusCodes.Status422UnprocessableEntity);
        }

        return null;
    }

    private static IResult? ValidateEnum<TEnum>(TEnum value, string fieldName)
        where TEnum : struct, Enum
    {
        return Enum.IsDefined(typeof(TEnum), value)
            ? null
            : EndpointHelpers.Problem("validation_failed", $"{fieldName} is invalid.", StatusCodes.Status422UnprocessableEntity);
    }

    private static IResult? ValidateSlug(string slug)
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
            var isAllowed = character is >= 'a' and <= 'z'
                || character is >= '0' and <= '9'
                || character == '-';
            if (!isAllowed)
            {
                return EndpointHelpers.Problem("validation_failed", "Organization slug can only contain lowercase letters, numbers, and hyphens.", StatusCodes.Status422UnprocessableEntity);
            }
        }

        return null;
    }

    private static async Task<IResult?> ValidatePlatformStaffAssignmentAsync(
        AtlasDbContext dbContext,
        Guid? assignedStaffId,
        CancellationToken cancellationToken)
    {
        if (!assignedStaffId.HasValue)
        {
            return null;
        }

        var exists = await dbContext.PlatformStaff.IgnoreQueryFilters()
            .AnyAsync(item => item.Id == assignedStaffId.Value && item.Status == PlatformStaffStatus.Active, cancellationToken);
        return exists
            ? null
            : EndpointHelpers.Problem("invalid_assignee", "Assigned platform staff member was not found or is not active.", StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult?> ValidateOrganizationReferenceAsync(
        AtlasDbContext dbContext,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        if (!organizationId.HasValue)
        {
            return null;
        }

        var exists = await dbContext.Organizations.IgnoreQueryFilters()
            .AnyAsync(item => item.Id == organizationId.Value, cancellationToken);
        return exists
            ? null
            : EndpointHelpers.Problem("invalid_organization", "Organization was not found.", StatusCodes.Status422UnprocessableEntity);
    }

    private static IQueryable<PlatformRevenueEvent> FilterRevenue(
        IQueryable<PlatformRevenueEvent> query,
        Guid? organizationId,
        PlatformRevenueEventType? type,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (organizationId.HasValue)
        {
            query = query.Where(item => item.OrganizationId == organizationId.Value);
        }
        if (type.HasValue)
        {
            query = query.Where(item => item.Type == type.Value);
        }
        if (from.HasValue)
        {
            query = query.Where(item => item.OccurredAt >= from.Value);
        }
        if (to.HasValue)
        {
            query = query.Where(item => item.OccurredAt <= to.Value);
        }

        return query;
    }

    private static PlatformStaffResponse ToStaffResponse(PlatformStaff staff)
    {
        return new PlatformStaffResponse(
            staff.Id,
            staff.Email,
            staff.FullName,
            staff.Role,
            staff.Status,
            staff.LastLoginAt,
            staff.DisabledAt,
            staff.CreatedAt,
            staff.UpdatedAt);
    }

    private static PlatformSettingResponse ToPlatformSettingResponse(AdminSetting setting, bool includeSecret)
    {
        return new PlatformSettingResponse(
            setting.Id,
            setting.OrganizationId,
            setting.Scope,
            setting.Category,
            setting.Key,
            setting.IsSecret && !includeSecret ? "\"***\"" : setting.ValueJson,
            setting.IsSecret,
            setting.UpdatedAt);
    }

    private static PlatformTemplateResponse ToPlatformTemplateResponse(Template template, TemplateVersion? version)
    {
        var settings = ParseJsonOrEmpty(version?.SettingsJson);
        return new PlatformTemplateResponse(
            template.Id,
            template.OrganizationId,
            template.Organization?.Name,
            template.OrganizationId is null,
            template.Name,
            template.Category,
            template.Description,
            template.Status,
            template.CurrentVersionId,
            version?.Id,
            version?.VersionNumber,
            version?.Title,
            version?.Instructions,
            settings,
            version?.PublishedAt,
            version?.Requirements
                .OrderBy(item => item.DisplayOrder)
                .Select(ToPlatformTemplateRequirementResponse)
                .ToList() ?? [],
            template.CreatedAt,
            template.UpdatedAt,
            template.DeletedAt);
    }

    private static TemplateVersion? EffectiveTemplateVersion(Template template)
    {
        return template.CurrentVersion
            ?? template.Versions.OrderByDescending(item => item.VersionNumber).FirstOrDefault();
    }

    private static PlatformTemplateRequirementResponse ToPlatformTemplateRequirementResponse(TemplateRequirement requirement)
    {
        return new PlatformTemplateRequirementResponse(
            requirement.Id,
            requirement.Key,
            requirement.Type,
            requirement.Label,
            requirement.Description,
            requirement.IsRequired,
            requirement.DisplayOrder,
            ParseJsonOrEmpty(requirement.ConfigurationJson),
            ParseJsonOrEmpty(requirement.ValidationJson),
            string.IsNullOrWhiteSpace(requirement.ConditionJson) ? null : ParseJsonOrEmpty(requirement.ConditionJson));
    }

    private static TemplateRequirement ToTemplateRequirement(RequirementDefinitionRequest request)
    {
        return new TemplateRequirement
        {
            Key = request.Key.Trim(),
            Type = request.Type,
            Label = request.Label.Trim(),
            Description = request.Description?.Trim(),
            IsRequired = request.Required,
            DisplayOrder = request.DisplayOrder,
            ConfigurationJson = EndpointHelpers.JsonOrDefault(request.Configuration),
            ValidationJson = EndpointHelpers.JsonOrDefault(request.Validation),
            ConditionJson = request.Condition is null ? null : request.Condition.Value.GetRawText()
        };
    }

    private static TemplateRequirement CloneTemplateRequirement(TemplateRequirement requirement)
    {
        return new TemplateRequirement
        {
            Key = requirement.Key,
            Type = requirement.Type,
            Label = requirement.Label,
            Description = requirement.Description,
            IsRequired = requirement.IsRequired,
            DisplayOrder = requirement.DisplayOrder,
            ConfigurationJson = requirement.ConfigurationJson,
            ValidationJson = requirement.ValidationJson,
            ConditionJson = requirement.ConditionJson
        };
    }

    private static async Task<IResult?> ValidatePlatformTemplateRequestAsync(
        AtlasDbContext dbContext,
        Guid? organizationId,
        string? name,
        string? title,
        IReadOnlyList<RequirementDefinitionRequest>? requirements,
        CancellationToken cancellationToken,
        bool requireRequirements = true)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(title))
        {
            return EndpointHelpers.Problem("validation_failed", "Template name and title are required.", StatusCodes.Status422UnprocessableEntity);
        }

        if (organizationId.HasValue)
        {
            var organizationExists = await dbContext.Organizations.IgnoreQueryFilters()
                .AnyAsync(item => item.Id == organizationId.Value && item.DeletedAt == null, cancellationToken);
            if (!organizationExists)
            {
                return EndpointHelpers.Problem("organization_not_found", "Organization was not found.", StatusCodes.Status404NotFound);
            }
        }

        if (requirements is null)
        {
            return requireRequirements
                ? EndpointHelpers.Problem("validation_failed", "At least one requirement is required.", StatusCodes.Status422UnprocessableEntity)
                : null;
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requirement in requirements)
        {
            if (string.IsNullOrWhiteSpace(requirement.Key)
                || string.IsNullOrWhiteSpace(requirement.Label)
                || requirement.DisplayOrder < 0)
            {
                return EndpointHelpers.Problem("validation_failed", "Each requirement needs key, label and a non-negative display order.", StatusCodes.Status422UnprocessableEntity);
            }
            if (!Enum.IsDefined(requirement.Type))
            {
                return EndpointHelpers.Problem("validation_failed", "Requirement type is invalid.", StatusCodes.Status422UnprocessableEntity);
            }
            if (!keys.Add(requirement.Key.Trim()))
            {
                return EndpointHelpers.Problem("validation_failed", "Requirement keys must be unique within a template.", StatusCodes.Status422UnprocessableEntity);
            }
        }

        return null;
    }

    private static async Task<bool> TemplateNameExistsAsync(
        AtlasDbContext dbContext,
        Guid? organizationId,
        string name,
        Guid? excludingTemplateId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Templates.IgnoreQueryFilters()
            .AnyAsync(item => item.DeletedAt == null
                && item.Id != excludingTemplateId
                && item.OrganizationId == organizationId
                && item.Name.ToLower() == name.ToLower(),
                cancellationToken);
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

    private static async Task<PlatformOrganizationResponse> ToPlatformOrganizationResponseAsync(
        AtlasDbContext dbContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        Organization organization,
        CancellationToken cancellationToken)
    {
        var revenue = await dbContext.PlatformRevenueEvents.IgnoreQueryFilters()
            .Where(item => item.OrganizationId == organization.Id)
            .GroupBy(item => item.Currency)
            .Select(grouping => new RevenueByCurrency(grouping.Key, grouping.Sum(item => item.Amount)))
            .ToListAsync(cancellationToken);

        return new PlatformOrganizationResponse(
            organization.Id,
            organization.Name,
            organization.Slug,
            organization.Status,
            organization.Timezone,
            organization.DefaultLanguage,
            organization.RetentionDays,
            organization.DeveloperAccessStatus,
            organization.DeveloperProductionRequestedAt,
            organization.DeveloperProductionApprovedAt,
            organization.DeveloperProductionRejectedAt,
            organization.DeveloperProductionNotes,
            organization.CreatedAt,
            organization.UpdatedAt,
            organization.DeletedAt,
            await dbContext.OrganizationUsers.IgnoreQueryFilters().CountAsync(item => item.OrganizationId == organization.Id, cancellationToken),
            await dbContext.Actions.IgnoreQueryFilters().CountAsync(item => item.OrganizationId == organization.Id, cancellationToken),
            await dbContext.Submissions.IgnoreQueryFilters().CountAsync(item => item.Action != null && item.Action.OrganizationId == organization.Id, cancellationToken),
            revenue,
            await entitlements.GetOrganizationEntitlementsAsync(organization.Id, clock.UtcNow, cancellationToken));
    }

    private static OrganizationInterestResponse ToInterestResponse(PlatformOrganizationInterest interest)
    {
        return new OrganizationInterestResponse(
            interest.Id,
            interest.OrganizationName,
            interest.ContactName,
            interest.ContactEmail,
            interest.ContactPhone,
            interest.Source,
            interest.Region,
            interest.ExpectedVolume,
            interest.Message,
            interest.Status,
            interest.AssignedStaffId,
            interest.ApprovedOrganizationId,
            interest.Notes,
            interest.CreatedAt,
            interest.UpdatedAt,
            interest.ApprovedAt,
            interest.RejectedAt);
    }

    private sealed record PlatformOrganizationLookup(string Name, string Slug);

    private static async Task<IReadOnlyDictionary<Guid, PlatformOrganizationLookup>> LoadOrganizationLookupAsync(
        AtlasDbContext dbContext,
        IEnumerable<Guid?> organizationIds,
        CancellationToken cancellationToken)
    {
        var ids = organizationIds
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, PlatformOrganizationLookup>();
        }

        return await dbContext.Organizations.IgnoreQueryFilters().AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Slug
            })
            .ToDictionaryAsync(
                item => item.Id,
                item => new PlatformOrganizationLookup(item.Name, item.Slug),
                cancellationToken);
    }

    private static PlatformOrganizationLookup? FindOrganizationLookup(
        IReadOnlyDictionary<Guid, PlatformOrganizationLookup> lookup,
        Guid? organizationId)
    {
        return organizationId.HasValue && lookup.TryGetValue(organizationId.Value, out var organization)
            ? organization
            : null;
    }

    private static bool MatchesSubscriptionFilter(
        OrganizationEntitlementSnapshot snapshot,
        string? status,
        string? planCode,
        string? provider)
    {
        var displayStatus = BillingDisplayStatus(snapshot.Billing);
        return (string.IsNullOrWhiteSpace(status)
                || string.Equals(snapshot.Billing.Status, status.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(displayStatus, status.Trim(), StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(planCode)
                || string.Equals(snapshot.Plan.Code, planCode.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(snapshot.Billing.PlanCode, planCode.Trim(), StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(provider)
                || string.Equals(snapshot.Billing.Provider, provider.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static PlatformBillingSubscriptionResponse ToBillingSubscriptionResponse(
        Organization organization,
        OrganizationEntitlementSnapshot snapshot,
        DateTimeOffset asOf)
    {
        var hasRevenue = HasActiveRecurringRevenue(snapshot, asOf);
        return new PlatformBillingSubscriptionResponse(
            organization.Id,
            organization.Name,
            organization.Slug,
            organization.Status,
            snapshot.Billing.PlanCode,
            snapshot.Plan.Name,
            snapshot.Billing.BillingCycle,
            snapshot.Billing.Status,
            BillingDisplayStatus(snapshot.Billing),
            snapshot.Billing.Provider,
            snapshot.Billing.CancelAtPeriodEnd,
            snapshot.Billing.CurrentPeriodStart,
            snapshot.Billing.CurrentPeriodEnd,
            snapshot.Billing.StripeCustomerId,
            snapshot.Billing.StripeSubscriptionId,
            hasRevenue ? Math.Round(MonthlyRecurringRevenue(snapshot.Plan, snapshot.Billing), 2) : null,
            snapshot.Plan.Currency,
            snapshot.Plan.MonthlyChecklistLimit,
            snapshot.Plan.StorageBytes,
            organization.UpdatedAt);
    }

    private static string BillingDisplayStatus(OrganizationBillingState billing)
    {
        return billing.CancelAtPeriodEnd && billing.Status.Trim().ToLowerInvariant() is "active" or "trialing" or "past_due"
            ? "canceling"
            : billing.Status;
    }

    private static bool HasActiveRecurringRevenue(OrganizationEntitlementSnapshot snapshot, DateTimeOffset asOf)
    {
        if (MonthlyRecurringRevenue(snapshot.Plan, snapshot.Billing) <= 0)
        {
            return false;
        }

        if (snapshot.Billing.Status.Trim().ToLowerInvariant() is not ("active" or "trialing" or "past_due"))
        {
            return false;
        }

        return !snapshot.Billing.CurrentPeriodEnd.HasValue || snapshot.Billing.CurrentPeriodEnd.Value > asOf;
    }

    private static decimal MonthlyRecurringRevenue(BillingPlan plan, OrganizationBillingState billing)
    {
        return billing.BillingCycle.Trim().ToLowerInvariant() is "annual" or "yearly" or "year"
            ? (plan.AnnualPriceCents ?? 0) / 1200m
            : (plan.MonthlyPriceCents ?? 0) / 100m;
    }

    private static PlatformRevenueEventResponse ToRevenueResponse(
        PlatformRevenueEvent revenueEvent,
        PlatformOrganizationLookup? organization)
    {
        return new PlatformRevenueEventResponse(
            revenueEvent.Id,
            revenueEvent.OrganizationId,
            revenueEvent.Type,
            revenueEvent.Amount,
            revenueEvent.Currency,
            revenueEvent.Source,
            revenueEvent.ExternalReference,
            revenueEvent.OccurredAt,
            revenueEvent.PeriodStart,
            revenueEvent.PeriodEnd,
            JsonSerializer.Deserialize<JsonElement>(revenueEvent.MetadataJson, EndpointHelpers.JsonOptions),
            revenueEvent.RecordedByStaffId,
            revenueEvent.CreatedAt,
            revenueEvent.UpdatedAt,
            organization?.Name,
            organization?.Slug);
    }

    private static string[] NormalizePlatformApiKeyScopes(
        IReadOnlyList<string>? requestedScopes,
        ApiKeyEnvironment environment)
    {
        string[] defaults = environment == ApiKeyEnvironment.Sandbox
            ? ["sandbox:*"]
            : ["templates:read", "actions:write", "files:write"];
        var source = requestedScopes is { Count: > 0 } ? requestedScopes : defaults;
        var scopes = source
            .Select(scope => scope.Trim())
            .Where(scope => scope.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (environment != ApiKeyEnvironment.Sandbox)
        {
            return scopes.Length == 0 ? defaults : scopes;
        }

        var sandboxScopes = scopes
            .Where(scope => scope.Equals("sandbox:*", StringComparison.OrdinalIgnoreCase)
                || scope.StartsWith("sandbox:", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return sandboxScopes.Length == 0 ? defaults : sandboxScopes;
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static string FirstNameFromEmail(string email)
    {
        var localPart = NormalizeEmail(email).Split('@')[0];
        var separators = new[] { '.', '-', '_' };
        var first = localPart.Split(separators, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? "there" : first;
    }

    private sealed record PlatformAccess(PlatformStaff? Staff, IResult? Problem);
}

public sealed record PlatformBootstrapRequest(string BootstrapKey, string Email, string Password, string FullName);

public sealed record PlatformLoginRequest(string Email, string Password);

public sealed record PlatformStaffResponse(
    Guid Id,
    string Email,
    string FullName,
    PlatformStaffRole Role,
    PlatformStaffStatus Status,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? DisabledAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreatePublicContactRequest(
    string Name,
    string Email,
    string Topic,
    string Message,
    string? TurnstileToken);

public sealed record PublicContactResponse(bool Accepted);

public sealed record CreatePlatformStaffRequest(
    string Email,
    string Password,
    string FullName,
    PlatformStaffRole? Role,
    PlatformStaffStatus? Status);

public sealed record UpdatePlatformStaffRequest(
    string? Email,
    string? Password,
    string? FullName,
    PlatformStaffRole? Role,
    PlatformStaffStatus? Status);

public sealed record UpsertPlatformSettingRequest(
    Guid? OrganizationId,
    AdminSettingScope Scope,
    string Category,
    string Key,
    JsonElement Value,
    bool IsSecret);

public sealed record UpdatePlatformSettingRequest(
    Guid? OrganizationId,
    AdminSettingScope? Scope,
    string? Category,
    string? Key,
    JsonElement? Value,
    bool? IsSecret);

public sealed record PlatformSettingResponse(
    Guid Id,
    Guid? OrganizationId,
    AdminSettingScope Scope,
    string Category,
    string Key,
    string ValueJson,
    bool IsSecret,
    DateTimeOffset UpdatedAt);

public sealed record CreatePlatformTemplateRequest(
    Guid? OrganizationId,
    string Name,
    string? Category,
    string? Description,
    string Title,
    string? Instructions,
    JsonElement? Settings,
    IReadOnlyList<RequirementDefinitionRequest> Requirements,
    bool PublishImmediately);

public sealed record UpdatePlatformTemplateRequest(
    Guid? OrganizationId,
    string? Name,
    string? Category,
    string? Description,
    TemplateStatus? Status,
    string? Title,
    string? Instructions,
    JsonElement? Settings,
    IReadOnlyList<RequirementDefinitionRequest>? Requirements);

public sealed record PlatformTemplateResponse(
    Guid Id,
    Guid? OrganizationId,
    string? OrganizationName,
    bool IsGlobal,
    string Name,
    string? Category,
    string? Description,
    TemplateStatus Status,
    Guid? CurrentVersionId,
    Guid? VersionId,
    int? VersionNumber,
    string? Title,
    string? Instructions,
    JsonElement Settings,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<PlatformTemplateRequirementResponse> Requirements,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);

public sealed record PlatformTemplateRequirementResponse(
    Guid Id,
    string Key,
    RequirementType Type,
    string Label,
    string? Description,
    bool Required,
    int DisplayOrder,
    JsonElement Configuration,
    JsonElement Validation,
    JsonElement? Condition);

public sealed record CreatePlatformOrganizationRequest(
    string Name,
    string Slug,
    OrganizationStatus? Status,
    string? Timezone,
    string? DefaultLanguage,
    int? RetentionDays);

public sealed record UpdatePlatformOrganizationRequest(
    string? Name,
    OrganizationStatus? Status,
    string? Timezone,
    string? DefaultLanguage,
    string? AccentColor,
    string? PrivacyStatement,
    int? RetentionDays);

public sealed record CreatePlatformOrganizationApiKeyRequest(
    string Name,
    IReadOnlyList<string>? Scopes,
    DateTimeOffset? ExpiresAt,
    string? NotifyEmail,
    ApiKeyEnvironment? Environment);

public sealed record PlatformOrganizationApiKeyResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string KeyPrefix,
    ApiKeyEnvironment Environment,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt);

public sealed record PlatformOrganizationApiKeyCreatedResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string KeyPrefix,
    ApiKeyEnvironment Environment,
    string Secret,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt,
    bool NotificationSent,
    string? NotificationError);

public sealed record PlatformOrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    OrganizationStatus Status,
    string Timezone,
    string DefaultLanguage,
    int RetentionDays,
    DeveloperAccessStatus DeveloperAccessStatus,
    DateTimeOffset? DeveloperProductionRequestedAt,
    DateTimeOffset? DeveloperProductionApprovedAt,
    DateTimeOffset? DeveloperProductionRejectedAt,
    string? DeveloperProductionNotes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    int MemberCount,
    int ActionCount,
    int SubmissionCount,
    IReadOnlyList<RevenueByCurrency> RevenueAllTime,
    OrganizationEntitlementSnapshot Entitlements);

public sealed record PlatformDeveloperAccessDecisionRequest(string? Notes);

public sealed record CreateOrganizationInterestRequest(
    string OrganizationName,
    string ContactName,
    string ContactEmail,
    string? ContactPhone,
    string? Source,
    string? Region,
    string? ExpectedVolume,
    string? Message,
    string? Notes,
    OrganizationInterestStatus? Status,
    Guid? AssignedStaffId);

public sealed record UpdateOrganizationInterestRequest(
    string? OrganizationName,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? Source,
    string? Region,
    string? ExpectedVolume,
    string? Message,
    string? Notes,
    OrganizationInterestStatus? Status,
    Guid? AssignedStaffId);

public sealed record ApproveOrganizationInterestRequest(
    bool CreateOrganization,
    string? OrganizationName,
    string? OrganizationSlug,
    string? OwnerEmail,
    string? OwnerFullName,
    string? OwnerPassword,
    string? Timezone,
    string? DefaultLanguage);

public sealed record OrganizationInterestResponse(
    Guid Id,
    string OrganizationName,
    string ContactName,
    string ContactEmail,
    string? ContactPhone,
    string? Source,
    string? Region,
    string? ExpectedVolume,
    string? Message,
    OrganizationInterestStatus Status,
    Guid? AssignedStaffId,
    Guid? ApprovedOrganizationId,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? RejectedAt);

public sealed record UpsertRevenueEventRequest(
    Guid? OrganizationId,
    PlatformRevenueEventType? Type,
    decimal Amount,
    string Currency,
    string Source,
    string? ExternalReference,
    DateTimeOffset? OccurredAt,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    JsonElement? Metadata);

public sealed record PlatformRevenueEventResponse(
    Guid Id,
    Guid? OrganizationId,
    PlatformRevenueEventType Type,
    decimal Amount,
    string Currency,
    string Source,
    string? ExternalReference,
    DateTimeOffset OccurredAt,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    JsonElement Metadata,
    Guid? RecordedByStaffId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? OrganizationName,
    string? OrganizationSlug);

public sealed record PlatformBillingSubscriptionResponse(
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationSlug,
    OrganizationStatus OrganizationStatus,
    string PlanCode,
    string PlanName,
    string BillingCycle,
    string BillingStatus,
    string DisplayStatus,
    string Provider,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    string? StripeCustomerId,
    string? StripeSubscriptionId,
    decimal? MonthlyRecurringRevenue,
    string Currency,
    int? MonthlyChecklistLimit,
    long? StorageBytesLimit,
    DateTimeOffset OrganizationUpdatedAt);

public sealed record RevenueByCurrency(string Currency, decimal Amount);

public sealed record PlatformMetricsResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int OrganizationsTotal,
    int OrganizationsActive,
    int OrganizationsSuspended,
    int OrganizationsClosed,
    int ActivePlatformStaff,
    int InterestsNew,
    int InterestsQualified,
    int InterestsApproved,
    int InterestsRejected,
    int ChecklistsTotal,
    int ChecklistsInFlight,
    int SubmissionsTotal,
    int SubmissionsAccepted,
    int FilesPendingScan,
    int FailedNotifications,
    decimal UsageQuantityInPeriod,
    IReadOnlyList<RevenueByCurrency> RevenueInPeriod,
    IReadOnlyList<RevenueByCurrency> RevenueAllTime);

public sealed record PlatformAuditResponse(
    Guid Id,
    Guid? StaffId,
    string EventType,
    string EventData,
    DateTimeOffset CreatedAt);
