using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Billing;
using Atlas.Application.Email;
using Atlas.Application.Settings;
using Atlas.Application.Storage;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class PackageEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private const int DefaultPackageBundleMaxFileCount = 100;
    private const long DefaultPackageBundleMaxBytes = 250L * 1024 * 1024;

    public static IEndpointRouteBuilder MapAtlasPackageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2").WithTags("Packages");

        MapPackages(group);
        MapDestinations(group);
        MapRoutingRules(group);
        MapDeliveryJobs(group);

        return app;
    }

    public static async Task<SubmissionPackage> EnsurePackageForAcceptedSubmissionAsync(
        Guid submissionId,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out _))
        {
            throw new InvalidOperationException("A dashboard tenant is required to create a package.");
        }

        var existing = await dbContext.SubmissionPackages
            .FirstOrDefaultAsync(item => item.SubmissionId == submissionId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var submission = await dbContext.Submissions
            .Include(item => item.Action)
            .ThenInclude(action => action!.Organization)
            .Include(item => item.ActionRecipient)
            .Include(item => item.Responses)
            .Include(item => item.Files)
            .ThenInclude(file => file.FileAsset)
            .Include(item => item.ReviewedByUser)
            .FirstOrDefaultAsync(item => item.Id == submissionId, cancellationToken);
        if (submission?.Action?.Organization is null || submission.ActionRecipient is null)
        {
            throw new InvalidOperationException("Accepted submission could not be loaded for packaging.");
        }

        if (submission.Status != SubmissionStatus.Accepted)
        {
            throw new InvalidOperationException("Only accepted submissions can be packaged.");
        }

        var previousPackage = await dbContext.SubmissionPackages
            .Where(item => item.ActionId == submission.ActionId
                && item.ActionRecipientId == submission.ActionRecipientId
                && item.Status != SubmissionPackageStatus.Archived)
            .OrderByDescending(item => item.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var contentHash = Convert.ToHexString(submission.ContentHash).ToLowerInvariant();
        var package = new SubmissionPackage
        {
            OrganizationId = organizationId,
            ActionId = submission.ActionId,
            SubmissionId = submission.Id,
            ActionRecipientId = submission.ActionRecipientId,
            PackageReference = await NewPackageReferenceAsync(dbContext, submission.SubmittedAt, contentHash, cancellationToken),
            VersionNumber = (previousPackage?.VersionNumber ?? 0) + 1,
            Status = SubmissionPackageStatus.Ready,
            AcceptedAt = submission.ReviewedAt ?? clock.UtcNow,
            AcceptedByUserId = submission.ReviewedByUserId ?? tenantContext.UserId ?? submission.Action.CreatedByUserId,
            ContentHash = contentHash,
            MetadataJson = BuildPackageMetadataJson(submission, contentHash),
            PreviousPackageId = previousPackage?.Id,
            RevisionReason = previousPackage is null ? null : submission.ReviewComment,
            OwnerUserId = submission.Action.CreatedByUserId,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };

        if (previousPackage is not null)
        {
            previousPackage.Status = SubmissionPackageStatus.Superseded;
            previousPackage.SupersededByPackageId = package.Id;
        }

        dbContext.SubmissionPackages.Add(package);

        var automaticRules = await dbContext.TemplateRoutingRules
            .Include(item => item.Destination)
            .Where(item => item.IsAutomatic
                && item.Trigger == RoutingTrigger.OnAcceptance
                && item.Destination != null
                && item.Destination.IsActive
                && item.Destination.Status == DestinationStatus.Active
                && (item.TemplateId == null || item.TemplateId == submission.Action.TemplateId))
            .OrderBy(item => item.Sequence)
            .ToListAsync(cancellationToken);
        if (automaticRules.Count > 0)
        {
            package.Status = SubmissionPackageStatus.RoutingPending;
            foreach (var rule in automaticRules)
            {
                dbContext.DeliveryJobs.Add(new DeliveryJob
                {
                    OrganizationId = organizationId,
                    SubmissionPackageId = package.Id,
                    DestinationId = rule.DestinationId,
                    Status = DeliveryJobStatus.Queued,
                    RequestedByUserId = tenantContext.UserId,
                    TriggeredBy = "routing_rule:on_acceptance",
                    IdempotencyKey = $"pkg:{package.Id:N}:dest:{rule.DestinationId:N}:v{package.VersionNumber}",
                    QueuedAt = clock.UtcNow,
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow
                });
            }
        }

        dbContext.UsageEvents.Add(new UsageEvent
        {
            OrganizationId = organizationId,
            ActionId = submission.ActionId,
            EventType = "package_created",
            Quantity = 1,
            Unit = "package",
            IdempotencyKey = $"package_created:{package.Id:N}",
            OccurredAt = clock.UtcNow,
            CreatedAt = clock.UtcNow
        });
        SecurityEndpoints.AddAudit(
            dbContext,
            organizationId,
            submission.ActionId,
            tenantContext,
            "package.created",
            new { package.Id, package.PackageReference, package.VersionNumber, package.Status },
            httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);

        return package;
    }

    private static void MapPackages(RouteGroupBuilder group)
    {
        var packages = group.MapGroup("/submission-packages").WithTags("Packages");

        packages.MapGet("", async (
            int? page,
            int? pageSize,
            SubmissionPackageStatus? status,
            Guid? ownerUserId,
            Guid? assignedTeamId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? q,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var normalizedPage = EndpointHelpers.NormalizePage(page);
            var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize);
            var query = dbContext.SubmissionPackages
                .AsNoTracking()
                .Include(item => item.Action)
                .Include(item => item.ActionRecipient)
                .Include(item => item.OwnerUser)
                .Include(item => item.DeliveryJobs)
                .Include(item => item.ManualHandoffs)
                .AsQueryable();
            if (status.HasValue)
            {
                query = query.Where(item => item.Status == status.Value);
            }

            if (ownerUserId.HasValue)
            {
                query = query.Where(item => item.OwnerUserId == ownerUserId.Value);
            }

            if (assignedTeamId.HasValue)
            {
                query = query.Where(item => item.AssignedTeamId == assignedTeamId.Value);
            }

            if (from.HasValue)
            {
                query = query.Where(item => item.AcceptedAt >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(item => item.AcceptedAt <= to.Value);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(item => item.PackageReference.Contains(term)
                    || item.Action!.Title.Contains(term)
                    || (item.ActionRecipient != null && (item.ActionRecipient.Name.Contains(term) || item.ActionRecipient.Email.Contains(term))));
            }

            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(item => item.AcceptedAt)
                .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
                .Take(normalizedPageSize)
                .Select(item => ToPackageListItem(item))
                .ToListAsync(cancellationToken);

            return Results.Ok(new { items, page = normalizedPage, pageSize = normalizedPageSize, total });
        });

        packages.MapGet("/{packageId:guid}", async (
            Guid packageId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var package = await LoadPackageDetailQuery(dbContext)
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == packageId, cancellationToken);
            return package is null
                ? EndpointHelpers.Problem("not_found", "Submission package was not found.", StatusCodes.Status404NotFound)
                : Results.Ok(ToPackageDetail(package));
        });

        group.MapGet("/actions/{actionId:guid}/packages", async (
            Guid actionId,
            bool? latest,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var query = LoadPackageDetailQuery(dbContext)
                .AsNoTracking()
                .Where(item => item.ActionId == actionId)
                .OrderByDescending(item => item.VersionNumber)
                .ThenByDescending(item => item.AcceptedAt);
            var packages = latest.GetValueOrDefault(false)
                ? await query.Take(1).ToListAsync(cancellationToken)
                : await query.ToListAsync(cancellationToken);
            var items = packages.Select(ToPackageDetail).ToList();
            return Results.Ok(new { items, total = items.Count });
        }).WithTags("Packages");

        group.MapGet("/submissions/{submissionId:guid}/package", async (
            Guid submissionId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var package = await LoadPackageDetailQuery(dbContext)
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.SubmissionId == submissionId, cancellationToken);
            return package is null
                ? EndpointHelpers.Problem("not_found", "Submission package was not found.", StatusCodes.Status404NotFound)
                : Results.Ok(ToPackageDetail(package));
        }).WithTags("Packages");

        packages.MapGet("/{packageId:guid}/contents", async (
            Guid packageId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var package = await LoadPackageDetailQuery(dbContext)
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == packageId, cancellationToken);
            return package is null
                ? EndpointHelpers.Problem("not_found", "Submission package was not found.", StatusCodes.Status404NotFound)
                : Results.Ok(ToPackageContents(package));
        });

        packages.MapGet("/{packageId:guid}/export.json", async (
            Guid packageId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            var package = await LoadPackageDetailQuery(dbContext)
                .FirstOrDefaultAsync(item => item.Id == packageId, cancellationToken);
            if (package is null)
            {
                return EndpointHelpers.Problem("not_found", "Submission package was not found.", StatusCodes.Status404NotFound);
            }

            package.GeneratedAt ??= clock.UtcNow;
            SecurityEndpoints.AddAudit(dbContext, organizationId, package.ActionId, tenantContext, "package.downloaded", new { package.Id, package.PackageReference, Type = "json" }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);

            var fileName = $"{SanitizeFileName(package.PackageReference)}-Package-Export.json";
            var json = JsonSerializer.Serialize(ToPackageExport(package), JsonOptions);
            return Results.File(Encoding.UTF8.GetBytes(json), "application/json", fileName);
        });

        packages.MapGet("/{packageId:guid}/report", async (
            Guid packageId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            var package = await LoadPackageDetailQuery(dbContext)
                .FirstOrDefaultAsync(item => item.Id == packageId, cancellationToken);
            if (package is null)
            {
                return EndpointHelpers.Problem("not_found", "Submission package was not found.", StatusCodes.Status404NotFound);
            }

            package.GeneratedAt ??= clock.UtcNow;
            SecurityEndpoints.AddAudit(dbContext, organizationId, package.ActionId, tenantContext, "package.downloaded", new { package.Id, package.PackageReference, Type = "report" }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);

            var fileName = $"{SanitizeFileName(package.PackageReference)}-Submission-Report.pdf";
            return Results.File(BuildPackageReportPdf(package), "application/pdf", fileName);
        });

        packages.MapPost("/{packageId:guid}/generate-report", async (
            Guid packageId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IEntitlementService entitlements,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            var feature = await entitlements.HasFeatureAsync(organizationId, clock.UtcNow, "package_report", cancellationToken);
            if (!feature.Allowed)
            {
                return EndpointHelpers.EntitlementProblem(feature);
            }

            var package = await dbContext.SubmissionPackages.FirstOrDefaultAsync(item => item.Id == packageId, cancellationToken);
            if (package is null)
            {
                return EndpointHelpers.Problem("not_found", "Submission package was not found.", StatusCodes.Status404NotFound);
            }

            package.GeneratedAt = clock.UtcNow;
            if (package.Status == SubmissionPackageStatus.Preparing)
            {
                package.Status = SubmissionPackageStatus.Ready;
            }

            dbContext.UsageEvents.Add(new UsageEvent
            {
                OrganizationId = organizationId,
                ActionId = package.ActionId,
                EventType = "package_report_generated",
                Quantity = 1,
                Unit = "report",
                IdempotencyKey = $"package_report:{package.Id:N}:{clock.UtcNow:yyyyMMddHHmmss}",
                OccurredAt = clock.UtcNow,
                CreatedAt = clock.UtcNow
            });
            SecurityEndpoints.AddAudit(dbContext, organizationId, package.ActionId, tenantContext, "package.report_generated", new { package.Id, package.PackageReference }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Accepted($"/api/v2/submission-packages/{package.Id}/report", ToPackageGeneratedResponse(package, "report"));
        });

        packages.MapGet("/{packageId:guid}/bundle", async (
            Guid packageId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAdminSettingService settings,
            IObjectStorageService storage,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            var package = await LoadPackageDetailQuery(dbContext)
                .FirstOrDefaultAsync(item => item.Id == packageId, cancellationToken);
            if (package is null)
            {
                return EndpointHelpers.Problem("not_found", "Submission package was not found.", StatusCodes.Status404NotFound);
            }

            package.GeneratedAt ??= clock.UtcNow;
            var bundlePlan = await BuildPackageBundlePlanAsync(package, settings, storage, cancellationToken);
            SecurityEndpoints.AddAudit(
                dbContext,
                organizationId,
                package.ActionId,
                tenantContext,
                "package.downloaded",
                new
                {
                    package.Id,
                    package.PackageReference,
                    Type = "bundle",
                    PlannedEmbeddedFileCount = bundlePlan.FilesToEmbed.Count,
                    PlannedEmbeddedBytes = bundlePlan.PlannedEmbeddedBytes,
                    bundlePlan.MaxEmbeddedFileCount,
                    bundlePlan.MaxEmbeddedBytes,
                    ExcludedFiles = bundlePlan.ManifestItems
                        .Where(item => !item.Included)
                        .Select(item => new
                        {
                            item.RequirementId,
                            item.FileAssetId,
                            item.FileName,
                            item.ScanStatus,
                            item.ExcludedReason
                        })
                        .ToList()
                },
                httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);

            var fileName = $"{SanitizeFileName(package.PackageReference)}-Completion-Package.zip";
            return Results.Stream(
                stream => StreamPackageBundleAsync(stream, package, bundlePlan, storage, cancellationToken),
                "application/zip",
                fileName);
        });

        packages.MapPost("/{packageId:guid}/generate-bundle", async (
            Guid packageId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IEntitlementService entitlements,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            var feature = await entitlements.HasFeatureAsync(organizationId, clock.UtcNow, "package_zip_export", cancellationToken);
            if (!feature.Allowed)
            {
                return EndpointHelpers.EntitlementProblem(feature);
            }

            var package = await dbContext.SubmissionPackages.FirstOrDefaultAsync(item => item.Id == packageId, cancellationToken);
            if (package is null)
            {
                return EndpointHelpers.Problem("not_found", "Submission package was not found.", StatusCodes.Status404NotFound);
            }

            package.GeneratedAt = clock.UtcNow;
            dbContext.UsageEvents.Add(new UsageEvent
            {
                OrganizationId = organizationId,
                ActionId = package.ActionId,
                EventType = "package_bundle_generated",
                Quantity = 1,
                Unit = "bundle",
                IdempotencyKey = $"package_bundle:{package.Id:N}:{clock.UtcNow:yyyyMMddHHmmss}",
                OccurredAt = clock.UtcNow,
                CreatedAt = clock.UtcNow
            });
            SecurityEndpoints.AddAudit(dbContext, organizationId, package.ActionId, tenantContext, "package.bundle_generated", new { package.Id, package.PackageReference }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Accepted($"/api/v2/submission-packages/{package.Id}/bundle", ToPackageGeneratedResponse(package, "bundle"));
        });

        packages.MapGet("/{packageId:guid}/summary", async (
            Guid packageId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var package = await LoadPackageDetailQuery(dbContext)
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == packageId, cancellationToken);
            return package is null
                ? EndpointHelpers.Problem("not_found", "Submission package was not found.", StatusCodes.Status404NotFound)
                : Results.Ok(BuildPackageSummary(package));
        });

        packages.MapPost("/{packageId:guid}/archive", async (
            Guid packageId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            var package = await dbContext.SubmissionPackages.FirstOrDefaultAsync(item => item.Id == packageId, cancellationToken);
            if (package is null)
            {
                return EndpointHelpers.Problem("not_found", "Submission package was not found.", StatusCodes.Status404NotFound);
            }

            package.Status = SubmissionPackageStatus.Archived;
            package.ArchivedAt = clock.UtcNow;
            SecurityEndpoints.AddAudit(dbContext, organizationId, package.ActionId, tenantContext, "package.archived", new { package.Id, package.PackageReference }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToPackageGeneratedResponse(package, "archive"));
        });

        packages.MapPost("/{packageId:guid}/assign", async (
            Guid packageId,
            AssignPackageRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            var package = await dbContext.SubmissionPackages.FirstOrDefaultAsync(item => item.Id == packageId, cancellationToken);
            if (package is null)
            {
                return EndpointHelpers.Problem("not_found", "Submission package was not found.", StatusCodes.Status404NotFound);
            }

            package.OwnerUserId = request.OwnerUserId;
            package.AssignedTeamId = request.AssignedTeamId;
            SecurityEndpoints.AddAudit(dbContext, organizationId, package.ActionId, tenantContext, "package.assigned", new { package.Id, package.OwnerUserId, package.AssignedTeamId }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { package.Id, package.OwnerUserId, package.AssignedTeamId });
        });

        packages.MapGet("/{packageId:guid}/deliveries", async (
            Guid packageId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var exists = await dbContext.SubmissionPackages.AnyAsync(item => item.Id == packageId, cancellationToken);
            if (!exists)
            {
                return EndpointHelpers.Problem("not_found", "Submission package was not found.", StatusCodes.Status404NotFound);
            }

            var items = await dbContext.DeliveryJobs.AsNoTracking()
                .Include(item => item.Destination)
                .Where(item => item.SubmissionPackageId == packageId)
                .OrderByDescending(item => item.QueuedAt)
                .Select(item => ToDeliveryJobResponse(item))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { items });
        });

        packages.MapPost("/{packageId:guid}/deliver", DeliverPackage);
        packages.MapPost("/{packageId:guid}/handoffs", CreateManualHandoff);
        packages.MapGet("/{packageId:guid}/handoffs", ListManualHandoffs);

        group.MapPost("/submissions/{submissionId:guid}/package", async (
            Guid submissionId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var status = await dbContext.Submissions
                .AsNoTracking()
                .Where(item => item.Id == submissionId)
                .Select(item => (SubmissionStatus?)item.Status)
                .FirstOrDefaultAsync(cancellationToken);
            if (status is null)
            {
                return EndpointHelpers.Problem("not_found", "Submission was not found.", StatusCodes.Status404NotFound);
            }

            if (status != SubmissionStatus.Accepted)
            {
                return EndpointHelpers.Problem("state_conflict", "Only accepted submissions can be packaged.", StatusCodes.Status409Conflict);
            }

            var package = await EnsurePackageForAcceptedSubmissionAsync(
                submissionId,
                dbContext,
                tenantContext,
                clock,
                httpContext,
                cancellationToken);
            var detail = await LoadPackageDetailQuery(dbContext)
                .AsNoTracking()
                .FirstAsync(item => item.Id == package.Id, cancellationToken);
            return Results.Created($"/api/v2/submission-packages/{package.Id}", ToPackageDetail(detail));
        }).WithTags("Packages");
    }

    private static void MapDestinations(RouteGroupBuilder group)
    {
        var destinations = group.MapGroup("/destinations").WithTags("Destinations");

        destinations.MapGet("", async (
            int? page,
            int? pageSize,
            DestinationType? type,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var normalizedPage = EndpointHelpers.NormalizePage(page);
            var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize);
            var query = dbContext.Destinations.AsNoTracking().AsQueryable();
            if (type.HasValue)
            {
                query = query.Where(item => item.Type == type.Value);
            }

            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(item => item.IsDefault)
                .ThenBy(item => item.Name)
                .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
                .Take(normalizedPageSize)
                .Select(item => ToDestinationResponse(item, false, null))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { items, page = normalizedPage, pageSize = normalizedPageSize, total });
        });

        destinations.MapPost("", async (
            UpsertDestinationRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IEntitlementService entitlements,
            IDataProtectionProvider dataProtectionProvider,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (!EndpointHelpers.HasScope(tenantContext, "admin:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Admin scope is required to manage destinations.", StatusCodes.Status403Forbidden);
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return EndpointHelpers.Problem("validation_failed", "Destination name is required.", StatusCodes.Status422UnprocessableEntity);
            }

            if (await RequireDestinationEntitlementAsync(request.Type, entitlements, organizationId, clock.UtcNow, cancellationToken) is { } entitlementProblem)
            {
                return entitlementProblem;
            }

            var validation = ValidateDestinationConfiguration(request.Type, request.Configuration);
            if (validation is not null)
            {
                return validation;
            }

            var secretToReturn = request.Type == DestinationType.Webhook && request.SecretConfiguration is null
                ? EndpointHelpers.NewOpaqueToken()
                : null;
            var secretJson = secretToReturn is null
                ? request.SecretConfiguration?.GetRawText()
                : JsonSerializer.Serialize(new { signingSecret = secretToReturn }, JsonOptions);

            var destination = new Destination
            {
                OrganizationId = organizationId,
                Name = request.Name.Trim(),
                Type = request.Type,
                Status = request.Status ?? DestinationStatus.Active,
                ConfigurationJson = JsonOrDefault(request.Configuration),
                ConfigurationEncrypted = ProtectSecretConfiguration(dataProtectionProvider, secretJson),
                IsDefault = request.IsDefault ?? false,
                IsActive = request.IsActive ?? true,
                CreatedByUserId = tenantContext.UserId ?? Guid.Empty,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            };

            if (destination.IsDefault)
            {
                await ClearDefaultDestinationAsync(dbContext, organizationId, destination.Type, cancellationToken);
            }

            dbContext.Destinations.Add(destination);
            SecurityEndpoints.AddAudit(dbContext, organizationId, null, tenantContext, "destination.created", new { destination.Id, destination.Type, destination.Name }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v2/destinations/{destination.Id}", ToDestinationResponse(destination, destination.ConfigurationEncrypted is not null, secretToReturn));
        });

        destinations.MapGet("/{destinationId:guid}", async (
            Guid destinationId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var destination = await dbContext.Destinations.AsNoTracking().FirstOrDefaultAsync(item => item.Id == destinationId, cancellationToken);
            return destination is null
                ? EndpointHelpers.Problem("not_found", "Destination was not found.", StatusCodes.Status404NotFound)
                : Results.Ok(ToDestinationResponse(destination, destination.ConfigurationEncrypted is not null, null));
        });

        destinations.MapPatch("/{destinationId:guid}", async (
            Guid destinationId,
            UpsertDestinationRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IEntitlementService entitlements,
            IDataProtectionProvider dataProtectionProvider,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (!EndpointHelpers.HasScope(tenantContext, "admin:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Admin scope is required to manage destinations.", StatusCodes.Status403Forbidden);
            }

            var destination = await dbContext.Destinations.FirstOrDefaultAsync(item => item.Id == destinationId, cancellationToken);
            if (destination is null)
            {
                return EndpointHelpers.Problem("not_found", "Destination was not found.", StatusCodes.Status404NotFound);
            }

            if (await RequireDestinationEntitlementAsync(request.Type, entitlements, organizationId, clock.UtcNow, cancellationToken) is { } entitlementProblem)
            {
                return entitlementProblem;
            }

            var validation = ValidateDestinationConfiguration(request.Type, request.Configuration);
            if (validation is not null)
            {
                return validation;
            }

            destination.Name = string.IsNullOrWhiteSpace(request.Name) ? destination.Name : request.Name.Trim();
            destination.Type = request.Type;
            destination.Status = request.Status ?? destination.Status;
            destination.ConfigurationJson = JsonOrDefault(request.Configuration);
            if (request.SecretConfiguration is not null)
            {
                destination.ConfigurationEncrypted = ProtectSecretConfiguration(dataProtectionProvider, request.SecretConfiguration.Value.GetRawText());
            }

            destination.IsDefault = request.IsDefault ?? destination.IsDefault;
            destination.IsActive = request.IsActive ?? destination.IsActive;
            if (destination.IsDefault)
            {
                await ClearDefaultDestinationAsync(dbContext, organizationId, destination.Type, cancellationToken, destination.Id);
            }

            SecurityEndpoints.AddAudit(dbContext, organizationId, null, tenantContext, "destination.updated", new { destination.Id, destination.Type, destination.Name }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToDestinationResponse(destination, destination.ConfigurationEncrypted is not null, null));
        });

        destinations.MapDelete("/{destinationId:guid}", async (
            Guid destinationId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (!EndpointHelpers.HasScope(tenantContext, "admin:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Admin scope is required to manage destinations.", StatusCodes.Status403Forbidden);
            }

            var destination = await dbContext.Destinations.FirstOrDefaultAsync(item => item.Id == destinationId, cancellationToken);
            if (destination is null)
            {
                return EndpointHelpers.Problem("not_found", "Destination was not found.", StatusCodes.Status404NotFound);
            }

            destination.IsActive = false;
            destination.Status = DestinationStatus.Disabled;
            destination.IsDefault = false;
            SecurityEndpoints.AddAudit(dbContext, organizationId, null, tenantContext, "destination.disabled", new { destination.Id, destination.Type, destination.Name }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        destinations.MapPost("/{destinationId:guid}/validate", ValidateDestination);
        destinations.MapPost("/{destinationId:guid}/test", TestDestination);
    }

    private static void MapRoutingRules(RouteGroupBuilder group)
    {
        var rules = group.MapGroup("/routing-rules").WithTags("Destinations");

        rules.MapGet("", async (
            Guid? templateId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var query = dbContext.TemplateRoutingRules.AsNoTracking().Include(item => item.Destination).AsQueryable();
            if (templateId.HasValue)
            {
                query = query.Where(item => item.TemplateId == templateId.Value);
            }

            var items = await query.OrderBy(item => item.Sequence).Select(item => ToRoutingRuleResponse(item)).ToListAsync(cancellationToken);
            return Results.Ok(new { items });
        });

        rules.MapPost("", async (
            UpsertRoutingRuleRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (!EndpointHelpers.HasScope(tenantContext, "admin:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Admin scope is required to manage routing rules.", StatusCodes.Status403Forbidden);
            }

            if (!await dbContext.Destinations.AnyAsync(item => item.Id == request.DestinationId && item.IsActive, cancellationToken))
            {
                return EndpointHelpers.Problem("not_found", "Destination was not found.", StatusCodes.Status404NotFound);
            }

            var rule = new TemplateRoutingRule
            {
                OrganizationId = organizationId,
                TemplateId = request.TemplateId,
                DestinationId = request.DestinationId,
                Trigger = request.Trigger,
                Sequence = request.Sequence,
                IsRequired = request.IsRequired,
                IsAutomatic = request.IsAutomatic,
                ConfigurationOverridesJson = JsonOrDefault(request.ConfigurationOverrides),
            };
            dbContext.TemplateRoutingRules.Add(rule);
            SecurityEndpoints.AddAudit(dbContext, organizationId, null, tenantContext, "routing_rule.created", new { rule.Id, rule.TemplateId, rule.DestinationId, rule.Trigger, rule.IsAutomatic }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v2/routing-rules/{rule.Id}", ToRoutingRuleResponse(rule));
        });

        rules.MapPatch("/{ruleId:guid}", async (
            Guid ruleId,
            UpsertRoutingRuleRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (!EndpointHelpers.HasScope(tenantContext, "admin:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Admin scope is required to manage routing rules.", StatusCodes.Status403Forbidden);
            }

            var rule = await dbContext.TemplateRoutingRules.FirstOrDefaultAsync(item => item.Id == ruleId, cancellationToken);
            if (rule is null)
            {
                return EndpointHelpers.Problem("not_found", "Routing rule was not found.", StatusCodes.Status404NotFound);
            }

            rule.TemplateId = request.TemplateId;
            rule.DestinationId = request.DestinationId;
            rule.Trigger = request.Trigger;
            rule.Sequence = request.Sequence;
            rule.IsRequired = request.IsRequired;
            rule.IsAutomatic = request.IsAutomatic;
            rule.ConfigurationOverridesJson = JsonOrDefault(request.ConfigurationOverrides);
            SecurityEndpoints.AddAudit(dbContext, organizationId, null, tenantContext, "routing_rule.updated", new { rule.Id, rule.TemplateId, rule.DestinationId, rule.Trigger, rule.IsAutomatic }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToRoutingRuleResponse(rule));
        });

        rules.MapDelete("/{ruleId:guid}", async (
            Guid ruleId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (!EndpointHelpers.HasScope(tenantContext, "admin:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Admin scope is required to manage routing rules.", StatusCodes.Status403Forbidden);
            }

            var rule = await dbContext.TemplateRoutingRules.FirstOrDefaultAsync(item => item.Id == ruleId, cancellationToken);
            if (rule is null)
            {
                return EndpointHelpers.Problem("not_found", "Routing rule was not found.", StatusCodes.Status404NotFound);
            }

            dbContext.TemplateRoutingRules.Remove(rule);
            SecurityEndpoints.AddAudit(dbContext, organizationId, null, tenantContext, "routing_rule.deleted", new { rule.Id, rule.TemplateId, rule.DestinationId }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    private static void MapDeliveryJobs(RouteGroupBuilder group)
    {
        group.MapGet("/delivery-jobs/{jobId:guid}", async (
            Guid jobId,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryRequireDashboard(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var job = await dbContext.DeliveryJobs.AsNoTracking()
                .Include(item => item.Destination)
                .Include(item => item.Attempts)
                .FirstOrDefaultAsync(item => item.Id == jobId, cancellationToken);
            return job is null
                ? EndpointHelpers.Problem("not_found", "Delivery job was not found.", StatusCodes.Status404NotFound)
                : Results.Ok(ToDeliveryJobDetailResponse(job));
        }).WithTags("Destinations");

        group.MapPost("/delivery-jobs/{jobId:guid}/retry", RetryDeliveryJob).WithTags("Destinations");
        group.MapPost("/delivery-jobs/{jobId:guid}/cancel", CancelDeliveryJob).WithTags("Destinations");
    }

    private static async Task<IResult> DeliverPackage(
        Guid packageId,
        DeliverPackageRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IEmailService emailService,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtectionProvider,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
        {
            return problem!;
        }

        var package = await LoadPackageDetailQuery(dbContext)
            .FirstOrDefaultAsync(item => item.Id == packageId, cancellationToken);
        if (package is null)
        {
            return EndpointHelpers.Problem("not_found", "Submission package was not found.", StatusCodes.Status404NotFound);
        }

        var destinations = await ResolveDestinationsAsync(dbContext, request.DestinationIds, cancellationToken);
        if (destinations.Count == 0)
        {
            return EndpointHelpers.Problem("validation_failed", "At least one active destination is required.", StatusCodes.Status422UnprocessableEntity);
        }

        foreach (var destination in destinations)
        {
            if (await RequireDestinationEntitlementAsync(destination.Type, entitlements, organizationId, clock.UtcNow, cancellationToken) is { } entitlementProblem)
            {
                return entitlementProblem;
            }
        }

        var jobs = new List<DeliveryJob>();
        foreach (var destination in destinations)
        {
            var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? $"pkg:{package.Id:N}:dest:{destination.Id:N}:v{package.VersionNumber}"
                : $"{request.IdempotencyKey.Trim()}:{destination.Id:N}";
            var existing = await dbContext.DeliveryJobs
                .Include(item => item.Destination)
                .Include(item => item.Attempts)
                .FirstOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                jobs.Add(existing);
                continue;
            }

            var job = new DeliveryJob
            {
                OrganizationId = organizationId,
                SubmissionPackageId = package.Id,
                DestinationId = destination.Id,
                Status = DeliveryJobStatus.Queued,
                RequestedByUserId = tenantContext.UserId,
                TriggeredBy = "manual",
                IdempotencyKey = idempotencyKey,
                QueuedAt = clock.UtcNow,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            };
            dbContext.DeliveryJobs.Add(job);
            await dbContext.SaveChangesAsync(cancellationToken);
            await ProcessDeliveryJobAsync(job, package, destination, emailService, httpClientFactory, dataProtectionProvider, clock, cancellationToken);
            SecurityEndpoints.AddAudit(
                dbContext,
                organizationId,
                package.ActionId,
                tenantContext,
                "delivery.queued",
                new
                {
                    JobId = job.Id,
                    DestinationId = destination.Id,
                    destination.Type,
                    PackageId = package.Id
                },
                httpContext);
            await UpdatePackageDeliveryStatusAsync(dbContext, package, clock, cancellationToken);
            jobs.Add(job);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Accepted($"/api/v2/submission-packages/{package.Id}/deliveries", new
        {
            items = jobs.Select(ToDeliveryJobResponse).ToList()
        });
    }

    private static async Task<IResult> CreateManualHandoff(
        Guid packageId,
        CreateManualHandoffRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
        {
            return problem!;
        }

        var feature = await entitlements.HasFeatureAsync(organizationId, clock.UtcNow, "manual_handoff", cancellationToken);
        if (!feature.Allowed)
        {
            return EndpointHelpers.EntitlementProblem(feature);
        }

        if (string.IsNullOrWhiteSpace(request.DestinationLabel))
        {
            return EndpointHelpers.Problem("validation_failed", "Destination label is required.", StatusCodes.Status422UnprocessableEntity);
        }

        var package = await dbContext.SubmissionPackages.FirstOrDefaultAsync(item => item.Id == packageId, cancellationToken);
        if (package is null)
        {
            return EndpointHelpers.Problem("not_found", "Submission package was not found.", StatusCodes.Status404NotFound);
        }

        var handoff = new ManualHandoff
        {
            OrganizationId = organizationId,
            SubmissionPackageId = package.Id,
            DestinationLabel = request.DestinationLabel.Trim(),
            CompletedByUserId = tenantContext.UserId ?? Guid.Empty,
            CompletedAt = clock.UtcNow,
            ExternalReference = string.IsNullOrWhiteSpace(request.ExternalReference) ? null : request.ExternalReference.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            EvidenceFileAssetId = request.EvidenceFileAssetId,
            CreatedAt = clock.UtcNow
        };

        package.Status = SubmissionPackageStatus.HandoffConfirmed;
        dbContext.ManualHandoffs.Add(handoff);
        dbContext.UsageEvents.Add(new UsageEvent
        {
            OrganizationId = organizationId,
            ActionId = package.ActionId,
            EventType = "manual_handoff_confirmed",
            Quantity = 1,
            Unit = "handoff",
            IdempotencyKey = $"manual_handoff:{handoff.Id:N}",
            OccurredAt = clock.UtcNow,
            CreatedAt = clock.UtcNow
        });
        SecurityEndpoints.AddAudit(
            dbContext,
            organizationId,
            package.ActionId,
            tenantContext,
            "package.handoff_confirmed",
            new
            {
                HandoffId = handoff.Id,
                PackageId = package.Id,
                handoff.DestinationLabel,
                handoff.ExternalReference
            },
            httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v2/submission-packages/{package.Id}/handoffs/{handoff.Id}", ToManualHandoffResponse(handoff));
    }

    private static async Task<IResult> ListManualHandoffs(
        Guid packageId,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        if (!TryRequireDashboard(tenantContext, out _, out var problem))
        {
            return problem!;
        }

        var exists = await dbContext.SubmissionPackages.AnyAsync(item => item.Id == packageId, cancellationToken);
        if (!exists)
        {
            return EndpointHelpers.Problem("not_found", "Submission package was not found.", StatusCodes.Status404NotFound);
        }

        var items = await dbContext.ManualHandoffs.AsNoTracking()
            .Where(item => item.SubmissionPackageId == packageId)
            .OrderByDescending(item => item.CompletedAt)
            .Select(item => ToManualHandoffResponse(item))
            .ToListAsync(cancellationToken);
        return Results.Ok(new { items });
    }

    private static async Task<IResult> RetryDeliveryJob(
        Guid jobId,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEmailService emailService,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtectionProvider,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
        {
            return problem!;
        }

        var job = await dbContext.DeliveryJobs
            .Include(item => item.SubmissionPackage)
            .ThenInclude(package => package!.Action)
            .ThenInclude(action => action!.Organization)
            .Include(item => item.SubmissionPackage)
            .ThenInclude(package => package!.ActionRecipient)
            .Include(item => item.SubmissionPackage)
            .ThenInclude(package => package!.Submission)
            .ThenInclude(submission => submission!.Files)
            .ThenInclude(file => file.FileAsset)
            .Include(item => item.Destination)
            .Include(item => item.Attempts)
            .FirstOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job?.SubmissionPackage is null || job.Destination is null)
        {
            return EndpointHelpers.Problem("not_found", "Delivery job was not found.", StatusCodes.Status404NotFound);
        }

        if (job.Status is DeliveryJobStatus.Succeeded or DeliveryJobStatus.Cancelled)
        {
            return EndpointHelpers.Problem("state_conflict", "Only failed or pending delivery jobs can be retried.", StatusCodes.Status409Conflict);
        }

        await ProcessDeliveryJobAsync(job, job.SubmissionPackage, job.Destination, emailService, httpClientFactory, dataProtectionProvider, clock, cancellationToken);
        SecurityEndpoints.AddAudit(dbContext, organizationId, job.SubmissionPackage.ActionId, tenantContext, "delivery.retried", new { job.Id, job.DestinationId, job.Status }, httpContext);
        await UpdatePackageDeliveryStatusAsync(dbContext, job.SubmissionPackage, clock, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToDeliveryJobDetailResponse(job));
    }

    private static async Task<IResult> CancelDeliveryJob(
        Guid jobId,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
        {
            return problem!;
        }

        var job = await dbContext.DeliveryJobs.Include(item => item.SubmissionPackage).FirstOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job?.SubmissionPackage is null)
        {
            return EndpointHelpers.Problem("not_found", "Delivery job was not found.", StatusCodes.Status404NotFound);
        }

        if (job.Status == DeliveryJobStatus.Succeeded)
        {
            return EndpointHelpers.Problem("state_conflict", "Succeeded delivery jobs cannot be cancelled.", StatusCodes.Status409Conflict);
        }

        job.Status = DeliveryJobStatus.Cancelled;
        job.CompletedAt = clock.UtcNow;
        SecurityEndpoints.AddAudit(dbContext, organizationId, job.SubmissionPackage.ActionId, tenantContext, "delivery.cancelled", new { job.Id, job.DestinationId }, httpContext);
        await UpdatePackageDeliveryStatusAsync(dbContext, job.SubmissionPackage, clock, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToDeliveryJobResponse(job));
    }

    private static async Task<IResult> ValidateDestination(
        Guid destinationId,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
        {
            return problem!;
        }

        var destination = await dbContext.Destinations.FirstOrDefaultAsync(item => item.Id == destinationId, cancellationToken);
        if (destination is null)
        {
            return EndpointHelpers.Problem("not_found", "Destination was not found.", StatusCodes.Status404NotFound);
        }

        var validation = ValidateDestinationConfiguration(destination.Type, ParseJson(destination.ConfigurationJson));
        destination.LastValidatedAt = clock.UtcNow;
        destination.LastValidationStatus = validation is null ? "valid" : "invalid";
        SecurityEndpoints.AddAudit(dbContext, organizationId, null, tenantContext, "destination.validated", new { destination.Id, destination.LastValidationStatus }, httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);
        return validation is null
            ? Results.Ok(new { destination.Id, valid = true, validatedAt = destination.LastValidatedAt })
            : validation;
    }

    private static async Task<IResult> TestDestination(
        Guid destinationId,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEmailService emailService,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtectionProvider,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryRequireDashboard(tenantContext, out var organizationId, out var problem))
        {
            return problem!;
        }

        var destination = await dbContext.Destinations.FirstOrDefaultAsync(item => item.Id == destinationId, cancellationToken);
        if (destination is null)
        {
            return EndpointHelpers.Problem("not_found", "Destination was not found.", StatusCodes.Status404NotFound);
        }

        var attempt = await SendDestinationTestAsync(destination, emailService, httpClientFactory, dataProtectionProvider, clock, cancellationToken);
        destination.LastValidatedAt = clock.UtcNow;
        destination.LastValidationStatus = attempt.Status == DeliveryAttemptStatus.Succeeded ? "valid" : "failed";
        SecurityEndpoints.AddAudit(dbContext, organizationId, null, tenantContext, "destination.tested", new { destination.Id, destination.Type, attempt.Status, attempt.HttpStatusCode, attempt.FailureCode }, httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);
        return attempt.Status == DeliveryAttemptStatus.Succeeded
            ? Results.Accepted($"/api/v2/destinations/{destination.Id}", new { destination.Id, sent = true, destination.LastValidatedAt })
            : EndpointHelpers.Problem("destination_test_failed", attempt.FailureMessage ?? "Destination test failed.", StatusCodes.Status502BadGateway);
    }

    private static IQueryable<SubmissionPackage> LoadPackageDetailQuery(AtlasDbContext dbContext)
    {
        return dbContext.SubmissionPackages
            .Include(item => item.Action)
            .ThenInclude(action => action!.Organization)
            .Include(item => item.ActionRecipient)
            .Include(item => item.Submission)
            .ThenInclude(submission => submission!.Responses)
            .Include(item => item.Submission)
            .ThenInclude(submission => submission!.Files)
            .ThenInclude(file => file.FileAsset)
            .Include(item => item.Submission)
            .ThenInclude(submission => submission!.ReviewedByUser)
            .Include(item => item.OwnerUser)
            .Include(item => item.DeliveryJobs)
            .ThenInclude(job => job.Destination)
            .Include(item => item.ManualHandoffs);
    }

    internal static async Task ProcessDeliveryJobAsync(
        DeliveryJob job,
        SubmissionPackage package,
        Destination destination,
        IEmailService emailService,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtectionProvider,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var started = clock.UtcNow;
        job.Status = DeliveryJobStatus.Sending;
        job.StartedAt ??= started;
        var attempt = new DeliveryAttempt
        {
            DeliveryJobId = job.Id,
            AttemptNumber = job.AttemptCount + 1,
            StartedAt = started
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            switch (destination.Type)
            {
                case DestinationType.Email:
                    await DeliverEmailDestinationAsync(package, destination, emailService, cancellationToken);
                    attempt.Status = DeliveryAttemptStatus.Succeeded;
                    attempt.ProviderReference = "resend";
                    attempt.ResponseSummary = "Email destination notified.";
                    break;
                case DestinationType.Webhook:
                    var webhookResult = await DeliverWebhookDestinationAsync(package, destination, httpClientFactory, dataProtectionProvider, cancellationToken);
                    attempt.Status = webhookResult.Succeeded ? DeliveryAttemptStatus.Succeeded : DeliveryAttemptStatus.Failed;
                    attempt.HttpStatusCode = webhookResult.StatusCode;
                    attempt.ResponseSummary = webhookResult.ResponseSummary;
                    attempt.FailureCode = webhookResult.Succeeded ? null : "webhook_failed";
                    attempt.FailureMessage = webhookResult.Succeeded ? null : webhookResult.ResponseSummary;
                    break;
                default:
                    attempt.Status = DeliveryAttemptStatus.Succeeded;
                    attempt.ResponseSummary = $"{destination.Type} handoff recorded. Use package exports or manual handoff confirmation for external systems.";
                    break;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            attempt.Status = DeliveryAttemptStatus.Failed;
            attempt.FailureCode = exception.GetType().Name;
            attempt.FailureMessage = Truncate(exception.Message, 1000);
        }

        stopwatch.Stop();
        attempt.CompletedAt = clock.UtcNow;
        attempt.DurationMilliseconds = stopwatch.ElapsedMilliseconds;
        job.AttemptCount++;
        job.CompletedAt = attempt.CompletedAt;
        if (attempt.Status == DeliveryAttemptStatus.Succeeded)
        {
            job.Status = DeliveryJobStatus.Succeeded;
            job.FailureCode = null;
            job.FailureMessage = null;
            job.ExternalReference = attempt.ProviderReference ?? destination.Type.ToString();
        }
        else
        {
            job.FailureCode = attempt.FailureCode;
            job.FailureMessage = attempt.FailureMessage;
            if (job.AttemptCount >= job.MaximumAttempts)
            {
                job.Status = DeliveryJobStatus.RequiresAttention;
                job.NextRetryAt = null;
            }
            else
            {
                job.Status = DeliveryJobStatus.RetryScheduled;
                job.NextRetryAt = clock.UtcNow.Add(DelayForAttempt(job.AttemptCount + 1));
            }
        }

        job.Attempts.Add(attempt);
    }

    private static async Task DeliverEmailDestinationAsync(
        SubmissionPackage package,
        Destination destination,
        IEmailService emailService,
        CancellationToken cancellationToken)
    {
        var config = ParseEmailDestinationConfiguration(destination.ConfigurationJson);
        if (config.Recipients.Count == 0)
        {
            throw new InvalidOperationException("Email destination has no recipients.");
        }

        var summary = BuildPackageSummary(package);
        var subject = $"Reqara package ready: {package.PackageReference}";
        var text =
            $"{summary.PlainText}\n\n" +
            "Open Reqara to download the report, files, and audit trail. This email intentionally does not include submitted files.";
        var html =
            $"<p>{Html(summary.PlainText)}</p>" +
            "<p>Open Reqara to download the report, files, and audit trail. This email intentionally does not include submitted files.</p>";

        foreach (var recipient in config.Recipients)
        {
            var result = await emailService.SendAsync(
                recipient,
                subject,
                text,
                html,
                "requests@reqara.com",
                $"{package.Action?.Organization?.Name ?? "Reqara"} via Reqara",
                cancellationToken,
                new Dictionary<string, string>
                {
                    ["X-Atlas-Email-Type"] = "package-destination-email",
                    ["X-Atlas-Package-Id"] = package.Id.ToString()
                });
            if (!result.Sent)
            {
                throw new InvalidOperationException(result.Error ?? "Email provider did not accept the message.");
            }
        }
    }

    private static async Task<WebhookDeliveryResult> DeliverWebhookDestinationAsync(
        SubmissionPackage package,
        Destination destination,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtectionProvider,
        CancellationToken cancellationToken)
    {
        using var config = JsonDocument.Parse(destination.ConfigurationJson);
        if (!config.RootElement.TryGetProperty("url", out var urlElement)
            || string.IsNullOrWhiteSpace(urlElement.GetString())
            || !Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Webhook destination requires an HTTPS URL.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            id = "evt_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(),
            type = "package.delivery_started",
            occurredAt = DateTimeOffset.UtcNow,
            organizationId = package.OrganizationId,
            data = new
            {
                packageId = package.Id,
                packageReference = package.PackageReference,
                requestId = package.ActionId,
                submissionId = package.SubmissionId
            }
        }, JsonOptions);

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-Reqara-Event", "package.delivery_started");
        request.Headers.TryAddWithoutValidation("X-Reqara-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        if (ReadWebhookSigningSecret(destination, dataProtectionProvider) is { } signingSecret)
        {
            var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingSecret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
            request.Headers.TryAddWithoutValidation("X-Reqara-Signature", $"sha256={signature}");
        }

        var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        var excerpt = Truncate(await response.Content.ReadAsStringAsync(cancellationToken), 2000);
        return new WebhookDeliveryResult((int)response.StatusCode is >= 200 and <= 299, (int)response.StatusCode, excerpt);
    }

    private static async Task<DeliveryAttempt> SendDestinationTestAsync(
        Destination destination,
        IEmailService emailService,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtectionProvider,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var package = new SubmissionPackage
        {
            Id = Guid.NewGuid(),
            OrganizationId = destination.OrganizationId,
            PackageReference = "REQ-TEST",
            VersionNumber = 1,
            AcceptedAt = clock.UtcNow,
            Status = SubmissionPackageStatus.Ready
        };

        var job = new DeliveryJob
        {
            Id = Guid.NewGuid(),
            OrganizationId = destination.OrganizationId,
            SubmissionPackageId = package.Id,
            DestinationId = destination.Id
        };
        await ProcessDeliveryJobAsync(job, package, destination, emailService, httpClientFactory, dataProtectionProvider, clock, cancellationToken);
        return job.Attempts.Single();
    }

    private static async Task UpdatePackageDeliveryStatusAsync(
        AtlasDbContext dbContext,
        SubmissionPackage package,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var jobs = await dbContext.DeliveryJobs
            .Where(item => item.SubmissionPackageId == package.Id)
            .ToListAsync(cancellationToken);
        if (jobs.Count == 0)
        {
            return;
        }

        if (jobs.All(item => item.Status == DeliveryJobStatus.Succeeded))
        {
            package.Status = SubmissionPackageStatus.Delivered;
        }
        else if (jobs.Any(item => item.Status == DeliveryJobStatus.Succeeded))
        {
            package.Status = SubmissionPackageStatus.PartiallyDelivered;
        }
        else if (jobs.Any(item => item.Status is DeliveryJobStatus.Failed or DeliveryJobStatus.RequiresAttention))
        {
            package.Status = SubmissionPackageStatus.DeliveryFailed;
        }
        else
        {
            package.Status = SubmissionPackageStatus.RoutingPending;
        }

        package.UpdatedAt = clock.UtcNow;
    }

    private static async Task<IReadOnlyList<Destination>> ResolveDestinationsAsync(
        AtlasDbContext dbContext,
        IReadOnlyCollection<Guid>? destinationIds,
        CancellationToken cancellationToken)
    {
        if (destinationIds is { Count: > 0 })
        {
            return await dbContext.Destinations
                .Where(item => destinationIds.Contains(item.Id) && item.IsActive && item.Status == DestinationStatus.Active)
                .ToListAsync(cancellationToken);
        }

        return await dbContext.Destinations
            .Where(item => item.IsDefault && item.IsActive && item.Status == DestinationStatus.Active)
            .ToListAsync(cancellationToken);
    }

    private static async Task ClearDefaultDestinationAsync(
        AtlasDbContext dbContext,
        Guid organizationId,
        DestinationType type,
        CancellationToken cancellationToken,
        Guid? exceptId = null)
    {
        var existing = await dbContext.Destinations
            .Where(item => item.OrganizationId == organizationId && item.Type == type && item.IsDefault && item.Id != exceptId)
            .ToListAsync(cancellationToken);
        foreach (var destination in existing)
        {
            destination.IsDefault = false;
        }
    }

    private static IResult? ValidateDestinationConfiguration(DestinationType type, JsonElement? configuration)
    {
        if (configuration is null || configuration.Value.ValueKind is not JsonValueKind.Object)
        {
            return EndpointHelpers.Problem("validation_failed", "Destination configuration must be a JSON object.", StatusCodes.Status422UnprocessableEntity);
        }

        var root = configuration.Value;
        if (type == DestinationType.Email)
        {
            var recipients = ParseEmailDestinationConfiguration(root.GetRawText()).Recipients;
            return recipients.Count == 0
                ? EndpointHelpers.Problem("validation_failed", "Email destination requires at least one recipient.", StatusCodes.Status422UnprocessableEntity)
                : null;
        }

        if (type == DestinationType.Webhook
            && (!root.TryGetProperty("url", out var url)
                || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps))
        {
            return EndpointHelpers.Problem("validation_failed", "Webhook destination requires an HTTPS url.", StatusCodes.Status422UnprocessableEntity);
        }

        return null;
    }

    private static async Task<IResult?> RequireDestinationEntitlementAsync(
        DestinationType type,
        IEntitlementService entitlements,
        Guid organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var feature = type switch
        {
            DestinationType.Email => "email_destination",
            DestinationType.Webhook => "webhook_destination",
            DestinationType.ManualDownload => "manual_handoff",
            DestinationType.CsvExport => "package_zip_export",
            DestinationType.JsonExport => "package_zip_export",
            DestinationType.SharePoint => "sharepoint_destination",
            _ => "custom_destinations"
        };
        var check = await entitlements.HasFeatureAsync(organizationId, now, feature, cancellationToken);
        return check.Allowed ? null : EndpointHelpers.EntitlementProblem(check);
    }

    private static bool TryRequireDashboard(ITenantContext tenantContext, out Guid organizationId, out IResult? problem)
    {
        if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out organizationId, out problem))
        {
            return false;
        }

        if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
        {
            problem = sandboxProblem;
            return false;
        }

        if (!EndpointHelpers.HasScope(tenantContext, "dashboard:*") && !EndpointHelpers.HasScope(tenantContext, "dashboard:read"))
        {
            problem = EndpointHelpers.Problem("forbidden", "Dashboard access is required.", StatusCodes.Status403Forbidden);
            return false;
        }

        return true;
    }

    private static DestinationConfiguration ParseEmailDestinationConfiguration(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var recipients = root.TryGetProperty("recipients", out var recipientsElement) && recipientsElement.ValueKind == JsonValueKind.Array
                ? recipientsElement.EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!.Trim()).ToArray()
                : Array.Empty<string>();
            var cc = root.TryGetProperty("cc", out var ccElement) && ccElement.ValueKind == JsonValueKind.Array
                ? ccElement.EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!.Trim()).ToArray()
                : Array.Empty<string>();
            return new DestinationConfiguration(recipients, cc);
        }
        catch (JsonException)
        {
            return new DestinationConfiguration(Array.Empty<string>(), Array.Empty<string>());
        }
    }

    private static string? ReadWebhookSigningSecret(Destination destination, IDataProtectionProvider dataProtectionProvider)
    {
        if (string.IsNullOrWhiteSpace(destination.ConfigurationEncrypted))
        {
            return null;
        }

        try
        {
            var raw = dataProtectionProvider.CreateProtector("Reqara.Destinations").Unprotect(destination.ConfigurationEncrypted);
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.TryGetProperty("signingSecret", out var secret)
                ? secret.GetString()
                : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or CryptographicException)
        {
            return null;
        }
    }

    private static string? ProtectSecretConfiguration(IDataProtectionProvider dataProtectionProvider, string? secretJson)
    {
        return string.IsNullOrWhiteSpace(secretJson)
            ? null
            : dataProtectionProvider.CreateProtector("Reqara.Destinations").Protect(secretJson);
    }

    private static TimeSpan DelayForAttempt(int attemptNumber)
    {
        return attemptNumber switch
        {
            <= 2 => TimeSpan.FromMinutes(1),
            3 => TimeSpan.FromMinutes(5),
            4 => TimeSpan.FromMinutes(30),
            _ => TimeSpan.FromHours(2)
        };
    }

    private static string BuildPackageMetadataJson(Submission submission, string contentHash)
    {
        return JsonSerializer.Serialize(new
        {
            request = new
            {
                id = submission.ActionId,
                title = submission.Action?.Title,
                publicReference = submission.Action?.PublicReference,
                templateId = submission.Action?.TemplateId
            },
            recipient = new
            {
                id = submission.ActionRecipientId,
                name = submission.ActionRecipient?.Name,
                email = submission.ActionRecipient?.Email
            },
            submission = new
            {
                id = submission.Id,
                versionNumber = submission.VersionNumber,
                submittedAt = submission.SubmittedAt,
                acceptedAt = submission.ReviewedAt,
                reviewComment = submission.ReviewComment,
                contentHash,
                responseCount = submission.Responses.Count,
                fileCount = submission.Files.Count
            }
        }, JsonOptions);
    }

    private static async Task<string> NewPackageReferenceAsync(
        AtlasDbContext dbContext,
        DateTimeOffset submittedAt,
        string contentHash,
        CancellationToken cancellationToken)
    {
        var suffix = contentHash.Length >= 6
            ? contentHash[..6].ToUpperInvariant()
            : Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
        for (var index = 0; index < 20; index++)
        {
            var reference = index == 0
                ? $"REQ-{submittedAt:yyyyMMdd}-{suffix}"
                : $"REQ-{submittedAt:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3))}";
            var exists = await dbContext.SubmissionPackages.IgnoreQueryFilters().AnyAsync(item => item.PackageReference == reference, cancellationToken);
            if (!exists)
            {
                return reference;
            }
        }

        return $"REQ-{submittedAt:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(5))}";
    }

    private static PackageListItemResponse ToPackageListItem(SubmissionPackage package)
    {
        return new PackageListItemResponse(
            package.Id,
            package.PackageReference,
            package.VersionNumber,
            package.Status,
            package.ActionId,
            package.Action?.Title ?? string.Empty,
            package.SubmissionId,
            package.ActionRecipientId,
            package.ActionRecipient?.Name,
            package.ActionRecipient?.Email,
            package.AcceptedAt,
            package.GeneratedAt,
            package.OwnerUserId,
            package.OwnerUser?.FullName,
            package.AssignedTeamId,
            package.DeliveryJobs.Count,
            package.DeliveryJobs.Count(item => item.Status is DeliveryJobStatus.Failed or DeliveryJobStatus.RequiresAttention),
            package.DeliveryJobs.OrderByDescending(item => item.QueuedAt).Select(item => (DeliveryJobStatus?)item.Status).FirstOrDefault(),
            package.ManualHandoffs.Count);
    }

    private static PackageDetailResponse ToPackageDetail(SubmissionPackage package)
    {
        var submission = package.Submission;
        return new PackageDetailResponse(
            package.Id,
            package.PackageReference,
            package.VersionNumber,
            package.Status,
            package.ActionId,
            package.Action?.Title ?? string.Empty,
            package.Action?.PublicReference ?? string.Empty,
            package.SubmissionId,
            package.ActionRecipientId,
            package.ActionRecipient?.Name,
            package.ActionRecipient?.Email,
            package.AcceptedAt,
            package.AcceptedByUserId,
            submission?.ReviewedByUser?.FullName,
            package.GeneratedAt,
            package.ContentHash,
            package.PreviousPackageId,
            package.SupersededByPackageId,
            package.OwnerUserId,
            package.OwnerUser?.FullName,
            package.AssignedTeamId,
            submission?.Responses.Count ?? 0,
            submission?.Files.Count ?? 0,
            submission?.Files.Select(file => new PackageFileResponse(
                file.FileAssetId,
                file.RequirementId,
                file.FileAsset?.OriginalFileName ?? file.DocumentName ?? "Submitted file",
                file.FileAsset?.MimeType ?? string.Empty,
                file.FileAsset?.SizeBytes ?? 0,
                file.FileAsset?.ScanStatus ?? FileScanStatus.Pending,
                file.IsPreviouslySubmitted,
                file.DocumentName,
                file.DocumentExpiresAt)).ToList() ?? [],
            package.DeliveryJobs.Select(ToDeliveryJobResponse).ToList(),
            package.ManualHandoffs.Select(ToManualHandoffResponse).ToList(),
            package.CreatedAt,
            package.UpdatedAt);
    }

    private static PackageContentsResponse ToPackageContents(SubmissionPackage package)
    {
        var submission = package.Submission;
        return new PackageContentsResponse(
            package.Id,
            package.PackageReference,
            package.SubmissionId,
            package.ActionId,
            submission?.Responses.Select(item => new PackageResponseValue(
                item.RequirementId,
                DeserializeJsonElementOrNull(item.ValueJson))).ToList() ?? [],
            submission?.Files.Select(file => new PackageFileResponse(
                file.FileAssetId,
                file.RequirementId,
                file.FileAsset?.OriginalFileName ?? file.DocumentName ?? "Submitted file",
                file.FileAsset?.MimeType ?? string.Empty,
                file.FileAsset?.SizeBytes ?? 0,
                file.FileAsset?.ScanStatus ?? FileScanStatus.Pending,
                file.IsPreviouslySubmitted,
                file.DocumentName,
                file.DocumentExpiresAt)).ToList() ?? [],
            new PackageIntegrityResponse(
                package.ContentHash,
                package.MetadataJson,
                package.VersionNumber,
                package.AcceptedAt,
                package.AcceptedByUserId,
                submission?.DeclarationAcceptedAt,
                submission?.DeclarationIpAddress?.ToString()));
    }

    private static PackageExportResponse ToPackageExport(SubmissionPackage package)
    {
        return new PackageExportResponse(
            ToPackageDetail(package),
            ToPackageContents(package),
            BuildPackageSummary(package),
            package.DeliveryJobs.OrderByDescending(item => item.QueuedAt).Select(ToDeliveryJobResponse).ToList(),
            package.ManualHandoffs.OrderByDescending(item => item.CompletedAt).Select(ToManualHandoffResponse).ToList(),
            DateTimeOffset.UtcNow);
    }

    private static PackageGeneratedResponse ToPackageGeneratedResponse(SubmissionPackage package, string artifact)
    {
        return new PackageGeneratedResponse(
            package.Id,
            package.PackageReference,
            package.Status,
            artifact,
            package.GeneratedAt,
            $"/api/v2/submission-packages/{package.Id}/{(artifact == "bundle" ? "bundle" : artifact == "report" ? "report" : "summary")}");
    }

    private static DestinationResponse ToDestinationResponse(Destination destination, bool hasSecretConfiguration, string? generatedSigningSecret)
    {
        return new DestinationResponse(
            destination.Id,
            destination.Name,
            destination.Type,
            destination.Status,
            ParseJson(destination.ConfigurationJson),
            hasSecretConfiguration,
            generatedSigningSecret,
            destination.IsDefault,
            destination.IsActive,
            destination.CreatedByUserId,
            destination.CreatedAt,
            destination.UpdatedAt,
            destination.LastValidatedAt,
            destination.LastValidationStatus);
    }

    private static RoutingRuleResponse ToRoutingRuleResponse(TemplateRoutingRule rule)
    {
        return new RoutingRuleResponse(
            rule.Id,
            rule.TemplateId,
            rule.DestinationId,
            rule.Destination?.Name,
            rule.Destination?.Type,
            rule.Trigger,
            rule.Sequence,
            rule.IsRequired,
            rule.IsAutomatic,
            ParseJson(rule.ConfigurationOverridesJson),
            rule.CreatedAt,
            rule.UpdatedAt);
    }

    private static DeliveryJobResponse ToDeliveryJobResponse(DeliveryJob job)
    {
        return new DeliveryJobResponse(
            job.Id,
            job.SubmissionPackageId,
            job.DestinationId,
            job.Destination?.Name,
            job.Destination?.Type,
            job.Status,
            job.AttemptCount,
            job.MaximumAttempts,
            job.RequestedByUserId,
            job.TriggeredBy,
            job.QueuedAt,
            job.StartedAt,
            job.CompletedAt,
            job.NextRetryAt,
            job.FailureCode,
            job.FailureMessage,
            job.ExternalReference,
            job.IdempotencyKey);
    }

    private static DeliveryJobDetailResponse ToDeliveryJobDetailResponse(DeliveryJob job)
    {
        return new DeliveryJobDetailResponse(
            ToDeliveryJobResponse(job),
            job.Attempts.OrderBy(item => item.AttemptNumber).Select(item => new DeliveryAttemptResponse(
                item.Id,
                item.AttemptNumber,
                item.StartedAt,
                item.CompletedAt,
                item.Status,
                item.HttpStatusCode,
                item.ProviderReference,
                item.ResponseSummary,
                item.FailureCode,
                item.FailureMessage,
                item.DurationMilliseconds)).ToList());
    }

    private static ManualHandoffResponse ToManualHandoffResponse(ManualHandoff handoff)
    {
        return new ManualHandoffResponse(
            handoff.Id,
            handoff.SubmissionPackageId,
            handoff.DestinationLabel,
            handoff.CompletedByUserId,
            handoff.CompletedAt,
            handoff.ExternalReference,
            handoff.Notes,
            handoff.EvidenceFileAssetId);
    }

    private static PackageSummaryResponse BuildPackageSummary(SubmissionPackage package)
    {
        var recipient = package.ActionRecipient is null
            ? "the recipient"
            : $"{package.ActionRecipient.Name} <{package.ActionRecipient.Email}>";
        var reviewer = package.Submission?.ReviewedByUser?.FullName ?? "the reviewer";
        var title = string.IsNullOrWhiteSpace(package.Action?.Title) ? "a Reqara checklist" : package.Action.Title;
        var plain = $"{recipient} completed {title} on {FormatDate(package.Submission?.SubmittedAt)}. The submission was accepted by {reviewer}. Reference: {package.PackageReference}.";
        return new PackageSummaryResponse(
            plain,
            new
            {
                package.Id,
                package.PackageReference,
                package.VersionNumber,
                package.Status,
                RequestTitle = title,
                RecipientName = package.ActionRecipient?.Name,
                RecipientEmail = package.ActionRecipient?.Email,
                SubmittedAt = package.Submission?.SubmittedAt,
                package.AcceptedAt,
                ReviewerName = reviewer,
                package.ContentHash
            });
    }

    private static async Task<PackageBundlePlan> BuildPackageBundlePlanAsync(
        SubmissionPackage package,
        IAdminSettingService settings,
        IObjectStorageService storage,
        CancellationToken cancellationToken)
    {
        var maxFileCountSetting = await settings.GetAsync(package.OrganizationId, "packageExport", "maxFileCount", cancellationToken);
        var maxEmbeddedFileCount = EndpointHelpers.ReadPositiveIntSetting(maxFileCountSetting?.ValueJson, DefaultPackageBundleMaxFileCount);
        var maxBytesSetting = await settings.GetAsync(package.OrganizationId, "packageExport", "maxZipBytes", cancellationToken);
        var maxEmbeddedBytes = EndpointHelpers.ReadPositiveLongSetting(maxBytesSetting?.ValueJson, DefaultPackageBundleMaxBytes);
        var root = SanitizeFileName(package.PackageReference);
        var filesToEmbed = new List<PackageBundleFilePlan>();
        var manifestItems = new List<PackageBundleFileManifestItem>();
        var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var embeddedFileAssets = new Dictionary<Guid, PackageBundleFilePlan>();
        var plannedEmbeddedBytes = 0L;

        foreach (var file in package.Submission?.Files ?? [])
        {
            var asset = file.FileAsset;
            if (asset is null)
            {
                manifestItems.Add(ToPackageBundleManifestItem(file, false, null, "file_asset_missing"));
                continue;
            }

            if (asset.ScanStatus != FileScanStatus.Clean)
            {
                manifestItems.Add(ToPackageBundleManifestItem(file, false, null, FileScanSkipReason(asset.ScanStatus)));
                continue;
            }

            if (embeddedFileAssets.TryGetValue(file.FileAssetId, out var existingPlan))
            {
                manifestItems.Add(ToPackageBundleManifestItem(file, true, existingPlan.ZipEntryName, null, existingPlan.SizeBytes));
                continue;
            }

            if (filesToEmbed.Count >= maxEmbeddedFileCount)
            {
                manifestItems.Add(ToPackageBundleManifestItem(file, false, null, "max_file_count_exceeded"));
                continue;
            }

            var metadata = await storage.GetObjectMetadataAsync(asset.StorageKey, cancellationToken);
            if (metadata is null)
            {
                manifestItems.Add(ToPackageBundleManifestItem(file, false, null, "storage_object_missing"));
                continue;
            }

            var sizeBytes = metadata.SizeBytes > 0 ? metadata.SizeBytes : asset.SizeBytes;
            if (plannedEmbeddedBytes + sizeBytes > maxEmbeddedBytes)
            {
                manifestItems.Add(ToPackageBundleManifestItem(file, false, null, "max_zip_bytes_exceeded", sizeBytes));
                continue;
            }

            var fileName = string.IsNullOrWhiteSpace(asset.OriginalFileName)
                ? file.DocumentName ?? $"{file.FileAssetId:N}.bin"
                : asset.OriginalFileName;
            var plan = new PackageBundleFilePlan(
                file.RequirementId,
                file.FileAssetId,
                asset.StorageKey,
                fileName,
                string.IsNullOrWhiteSpace(asset.MimeType) ? "application/octet-stream" : asset.MimeType,
                sizeBytes,
                UniqueZipEntryName(usedEntryNames, root, file.FileAssetId, fileName));
            filesToEmbed.Add(plan);
            embeddedFileAssets[file.FileAssetId] = plan;
            plannedEmbeddedBytes += sizeBytes;
            manifestItems.Add(ToPackageBundleManifestItem(file, true, plan.ZipEntryName, null, sizeBytes));
        }

        return new PackageBundlePlan(
            root,
            filesToEmbed,
            manifestItems,
            plannedEmbeddedBytes,
            maxEmbeddedBytes,
            maxEmbeddedFileCount);
    }

    private static async Task StreamPackageBundleAsync(
        Stream output,
        SubmissionPackage package,
        PackageBundlePlan plan,
        IObjectStorageService storage,
        CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"reqara-package-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var temp = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await WritePackageBundleArchiveAsync(temp, package, plan, storage, cancellationToken);
                temp.Position = 0;
                await temp.CopyToAsync(output, 81920, cancellationToken);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The temp file is best-effort cleanup; failure to delete should not break a completed download.
            }
        }
    }

    private static async Task WritePackageBundleArchiveAsync(
        Stream output,
        SubmissionPackage package,
        PackageBundlePlan plan,
        IObjectStorageService storage,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, true);
        AddZipEntry(archive, $"{plan.Root}/Submission Report.txt", BuildPackageReportText(package));
        AddZipEntry(archive, $"{plan.Root}/answers.json", JsonSerializer.Serialize(package.Submission?.Responses.Select(item => new
        {
            item.RequirementId,
            Value = item.ValueJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(item.ValueJson, JsonOptions)
        }) ?? [], JsonOptions));
        AddZipEntry(archive, $"{plan.Root}/metadata.json", package.MetadataJson);
        AddZipEntry(archive, $"{plan.Root}/audit-summary.json", JsonSerializer.Serialize(new
        {
            package.Id,
            package.PackageReference,
            package.VersionNumber,
            package.Status,
            package.AcceptedAt,
            package.AcceptedByUserId,
            GeneratedAt = DateTimeOffset.UtcNow
        }, JsonOptions));

        var unavailableFileAssetIds = new HashSet<Guid>();
        foreach (var file in plan.FilesToEmbed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var objectRead = await storage.OpenReadAsync(file.StorageKey, cancellationToken);
            if (objectRead is null)
            {
                unavailableFileAssetIds.Add(file.FileAssetId);
                continue;
            }

            await using (objectRead)
            {
                var entry = archive.CreateEntry(file.ZipEntryName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                await objectRead.Content.CopyToAsync(entryStream, cancellationToken);
            }
        }

        var manifestItems = plan.ManifestItems
            .Select(item => item.Included && unavailableFileAssetIds.Contains(item.FileAssetId)
                ? item with { Included = false, ZipEntryName = null, ExcludedReason = "storage_object_missing" }
                : item)
            .ToList();
        AddZipEntry(archive, $"{plan.Root}/file-manifest.json", JsonSerializer.Serialize(new
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            ManifestVersion = 2,
            plan.MaxEmbeddedFileCount,
            plan.MaxEmbeddedBytes,
            EmbeddedFileCount = manifestItems.Count(item => item.Included),
            SkippedFileCount = manifestItems.Count(item => !item.Included),
            Files = manifestItems
        }, JsonOptions));
    }

    private static PackageBundleFileManifestItem ToPackageBundleManifestItem(
        SubmissionFile file,
        bool included,
        string? zipEntryName,
        string? excludedReason,
        long? sizeBytes = null)
    {
        return new PackageBundleFileManifestItem(
            file.RequirementId,
            file.FileAssetId,
            file.FileAsset?.OriginalFileName ?? file.DocumentName,
            file.FileAsset?.MimeType,
            sizeBytes ?? file.FileAsset?.SizeBytes,
            file.FileAsset?.ScanStatus,
            file.IsPreviouslySubmitted,
            file.DocumentName,
            file.DocumentExpiresAt,
            included,
            zipEntryName,
            excludedReason);
    }

    private static string FileScanSkipReason(FileScanStatus status)
    {
        return status switch
        {
            FileScanStatus.Pending => "scan_pending",
            FileScanStatus.Rejected => "scan_rejected",
            _ => "file_not_clean"
        };
    }

    private static string UniqueZipEntryName(
        HashSet<string> usedEntryNames,
        string root,
        Guid fileAssetId,
        string fileName)
    {
        var safeFileName = SanitizeZipFileName(fileName, fileAssetId);
        var entryName = $"{root}/files/{safeFileName}";
        if (usedEntryNames.Add(entryName))
        {
            return entryName;
        }

        var suffix = fileAssetId.ToString("N")[..8];
        var stem = SanitizeFileName(Path.GetFileNameWithoutExtension(safeFileName));
        var extension = Path.GetExtension(safeFileName);
        for (var index = 2; index < 1000; index++)
        {
            var candidate = $"{root}/files/{stem}-{suffix}-{index}{extension}";
            if (usedEntryNames.Add(candidate))
            {
                return candidate;
            }
        }

        return $"{root}/files/{suffix}-{Guid.NewGuid():N}{extension}";
    }

    private static string SanitizeZipFileName(string? fileName, Guid fileAssetId)
    {
        var normalized = (fileName ?? string.Empty).Replace('\\', '/');
        var slashIndex = normalized.LastIndexOf('/');
        if (slashIndex >= 0)
        {
            normalized = normalized[(slashIndex + 1)..];
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return $"{fileAssetId:N}.bin";
        }

        var extension = Path.GetExtension(normalized);
        if (extension.Length > 20 || extension.Any(character => character != '.' && !char.IsLetterOrDigit(character)))
        {
            extension = string.Empty;
        }

        var stem = SanitizeFileName(Path.GetFileNameWithoutExtension(normalized));
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = fileAssetId.ToString("N")[..12];
        }

        return $"{stem}{extension.ToLowerInvariant()}";
    }

    private static void AddZipEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }

    private static string BuildPackageReportText(SubmissionPackage package)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Reqara Submission Package Report");
        builder.AppendLine("================================");
        builder.AppendLine();
        builder.AppendLine($"Package reference: {package.PackageReference}");
        builder.AppendLine($"Status: {package.Status}");
        builder.AppendLine($"Checklist: {package.Action?.Title}");
        builder.AppendLine($"Organization: {package.Action?.Organization?.Name}");
        builder.AppendLine($"Recipient: {package.ActionRecipient?.Name} <{package.ActionRecipient?.Email}>");
        builder.AppendLine($"Submitted: {package.Submission?.SubmittedAt:yyyy-MM-dd HH:mm:ss 'UTC'}");
        builder.AppendLine($"Accepted: {package.AcceptedAt:yyyy-MM-dd HH:mm:ss 'UTC'}");
        builder.AppendLine($"Reviewer: {package.Submission?.ReviewedByUser?.FullName ?? package.AcceptedByUserId.ToString()}");
        builder.AppendLine($"Version: {package.VersionNumber}");
        builder.AppendLine();
        builder.AppendLine("Structured answers");
        builder.AppendLine("------------------");
        foreach (var response in package.Submission?.Responses ?? [])
        {
            builder.AppendLine($"{response.RequirementId}: {response.ValueJson}");
        }

        builder.AppendLine();
        builder.AppendLine("File manifest");
        builder.AppendLine("-------------");
        foreach (var file in package.Submission?.Files ?? [])
        {
            builder.AppendLine($"{file.FileAsset?.OriginalFileName ?? file.DocumentName ?? file.FileAssetId.ToString()} ({file.FileAsset?.MimeType}, {file.FileAsset?.SizeBytes} bytes, {file.FileAsset?.ScanStatus})");
        }

        builder.AppendLine();
        builder.AppendLine("This report was generated from the immutable accepted package record. Internal notes are not included unless separately marked delivery-visible.");
        return builder.ToString();
    }

    private static byte[] BuildPackageReportPdf(SubmissionPackage package)
    {
        var commands = new StringBuilder();
        commands.AppendLine("0.99 0.995 1 rg 0 0 612 792 re f");
        commands.AppendLine("0.04 0.04 0.07 rg 0 704 612 88 re f");
        commands.AppendLine("0.50 0.44 1.00 rg 48 724 44 44 re f");
        commands.AppendLine(PdfText("F2", 16, 62, 741, "RQ", "1 1 1"));
        commands.AppendLine(PdfText("F2", 24, 112, 748, "Submission package", "1 1 1"));
        commands.AppendLine(PdfText("F1", 11, 114, 728, "Accepted package report generated by Reqara", "0.78 0.82 0.96"));
        commands.AppendLine("0.02 0.75 0.68 rg 456 736 100 24 re f");
        commands.AppendLine(PdfText("F2", 9, 474, 744, package.Status.ToString().ToUpperInvariant(), "1 1 1"));

        commands.AppendLine("1 1 1 rg 42 404 528 268 re f");
        commands.AppendLine("0.86 0.88 0.92 RG 42 404 528 268 re S");
        commands.AppendLine("0.96 0.98 1 rg 42 620 528 52 re f");
        commands.AppendLine(PdfText("F1", 10, 64, 650, "PACKAGE REFERENCE"));
        commands.AppendLine(PdfText("F2", 20, 64, 626, package.PackageReference));
        commands.AppendLine(PdfText("F1", 10, 360, 650, "ACCEPTED"));
        commands.AppendLine(PdfText("F2", 12, 360, 629, package.AcceptedAt.ToString("MMM d, yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture)));

        DrawLabelValue(commands, 64, 590, "Checklist", package.Action?.Title ?? "Checklist");
        DrawLabelValue(commands, 64, 550, "Organization", package.Action?.Organization?.Name ?? "Organization");
        DrawLabelValue(commands, 64, 510, "Recipient", $"{package.ActionRecipient?.Name} <{package.ActionRecipient?.Email}>");
        DrawLabelValue(commands, 64, 470, "Reviewer", package.Submission?.ReviewedByUser?.FullName ?? package.AcceptedByUserId.ToString());
        DrawLabelValue(commands, 330, 470, "Version", package.VersionNumber.ToString(CultureInfo.InvariantCulture));

        commands.AppendLine("1 1 1 rg 42 220 528 138 re f");
        commands.AppendLine("0.86 0.88 0.92 RG 42 220 528 138 re S");
        commands.AppendLine(PdfText("F2", 15, 64, 330, "Package summary"));
        DrawMetric(commands, 64, 278, "Responses", (package.Submission?.Responses.Count ?? 0).ToString(CultureInfo.InvariantCulture));
        DrawMetric(commands, 190, 278, "Files", (package.Submission?.Files.Count ?? 0).ToString(CultureInfo.InvariantCulture));
        DrawMetric(commands, 316, 278, "Deliveries", package.DeliveryJobs.Count.ToString(CultureInfo.InvariantCulture));
        DrawMetric(commands, 442, 278, "Handoffs", package.ManualHandoffs.Count.ToString(CultureInfo.InvariantCulture));

        commands.AppendLine("0.94 0.98 0.98 rg 42 140 528 52 re f");
        commands.AppendLine("0.78 0.91 0.91 RG 42 140 528 52 re S");
        commands.AppendLine(PdfText("F2", 12, 64, 170, "Audit-ready package"));
        commands.AppendLine(PdfText("F1", 10, 64, 152, "Detailed integrity data is available in the package export for audit teams."));
        commands.AppendLine(PdfText("F1", 9, 42, 74, "Powered by Reqara"));
        commands.AppendLine(PdfText("F1", 9, 372, 74, $"Generated {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm 'UTC'}"));
        return BuildSimplePdf(commands.ToString());
    }

    private static byte[] BuildSimplePdf(string content)
    {
        var contentBytes = Encoding.ASCII.GetBytes(content);
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>",
            $"<< /Length {contentBytes.Length} >>\nstream\n{content}\nendstream"
        };

        var output = new List<byte>();
        AddAscii(output, "%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(output.Count);
            AddAscii(output, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xrefOffset = output.Count;
        AddAscii(output, $"xref\n0 {objects.Length + 1}\n");
        AddAscii(output, "0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            AddAscii(output, $"{offset:0000000000} 00000 n \n");
        }

        AddAscii(output, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    private static void DrawLabelValue(StringBuilder commands, int x, int y, string label, string value)
    {
        commands.AppendLine(PdfText("F1", 9, x, y, label.ToUpperInvariant()));
        var lines = WrapText(value, x > 300 ? 28 : 46).Take(2).ToArray();
        for (var index = 0; index < lines.Length; index++)
        {
            commands.AppendLine(PdfText("F2", 11, x, y - 17 - (index * 13), lines[index]));
        }
    }

    private static void DrawMetric(StringBuilder commands, int x, int y, string label, string value)
    {
        commands.AppendLine("0.97 0.98 1 rg " + x.ToString(CultureInfo.InvariantCulture) + " " + y.ToString(CultureInfo.InvariantCulture) + " 92 46 re f");
        commands.AppendLine("0.88 0.90 0.94 RG " + x.ToString(CultureInfo.InvariantCulture) + " " + y.ToString(CultureInfo.InvariantCulture) + " 92 46 re S");
        commands.AppendLine(PdfText("F2", 17, x + 12, y + 22, value));
        commands.AppendLine(PdfText("F1", 8, x + 12, y + 9, label.ToUpperInvariant()));
    }

    private static string PdfText(string font, int size, int x, int y, string value, string color = "0 0 0")
    {
        return $"BT /{font} {size} Tf {color} rg {x.ToString(CultureInfo.InvariantCulture)} {y.ToString(CultureInfo.InvariantCulture)} Td ({PdfEscape(value)}) Tj ET";
    }

    private static string PdfEscape(string value)
    {
        var safe = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var normalized = character is >= ' ' and <= '~' ? character : '?';
            if (normalized is '\\' or '(' or ')')
            {
                safe.Append('\\');
            }

            safe.Append(normalized);
        }

        return safe.ToString();
    }

    private static IEnumerable<string> WrapText(string value, int maxCharacters)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();
        foreach (var word in words)
        {
            if (line.Length > 0 && line.Length + word.Length + 1 > maxCharacters)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word.Length > maxCharacters ? word[..maxCharacters] : word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    private static JsonElement ParseJson(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
    }

    private static JsonElement? DeserializeJsonElementOrNull(string? json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
    }

    private static string JsonOrDefault(JsonElement? value)
    {
        return value is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null }
            ? value.Value.GetRawText()
            : "{}";
    }

    private static string SanitizeFileName(string value)
    {
        var safe = new string(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray());
        safe = safe.Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "reqara-package" : safe;
    }

    private static string FormatDate(DateTimeOffset? value)
    {
        return value is null
            ? "an unknown date"
            : value.Value.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        return value is null || value.Length <= maxLength ? value : value[..maxLength];
    }

    private static void AddAscii(List<byte> output, string value)
    {
        output.AddRange(Encoding.ASCII.GetBytes(value));
    }

    private static string Html(string value)
    {
        return System.Net.WebUtility.HtmlEncode(value);
    }

    private sealed record DestinationConfiguration(IReadOnlyList<string> Recipients, IReadOnlyList<string> Cc);
    private sealed record WebhookDeliveryResult(bool Succeeded, int? StatusCode, string? ResponseSummary);
    private sealed record PackageBundlePlan(
        string Root,
        IReadOnlyList<PackageBundleFilePlan> FilesToEmbed,
        IReadOnlyList<PackageBundleFileManifestItem> ManifestItems,
        long PlannedEmbeddedBytes,
        long MaxEmbeddedBytes,
        int MaxEmbeddedFileCount);

    private sealed record PackageBundleFilePlan(
        Guid RequirementId,
        Guid FileAssetId,
        string StorageKey,
        string FileName,
        string MimeType,
        long SizeBytes,
        string ZipEntryName);

    private sealed record PackageBundleFileManifestItem(
        Guid RequirementId,
        Guid FileAssetId,
        string? FileName,
        string? MimeType,
        long? SizeBytes,
        FileScanStatus? ScanStatus,
        bool IsPreviouslySubmitted,
        string? DocumentName,
        DateTimeOffset? DocumentExpiresAt,
        bool Included,
        string? ZipEntryName,
        string? ExcludedReason);
}

public sealed record PackageListItemResponse(
    Guid Id,
    string PackageReference,
    int VersionNumber,
    SubmissionPackageStatus Status,
    Guid ActionId,
    string RequestTitle,
    Guid SubmissionId,
    Guid? ActionRecipientId,
    string? RecipientName,
    string? RecipientEmail,
    DateTimeOffset AcceptedAt,
    DateTimeOffset? GeneratedAt,
    Guid? OwnerUserId,
    string? OwnerName,
    Guid? AssignedTeamId,
    int DeliveryCount,
    int FailedDeliveryCount,
    DeliveryJobStatus? LatestDeliveryStatus,
    int HandoffCount);

public sealed record PackageDetailResponse(
    Guid Id,
    string PackageReference,
    int VersionNumber,
    SubmissionPackageStatus Status,
    Guid ActionId,
    string RequestTitle,
    string PublicReference,
    Guid SubmissionId,
    Guid? ActionRecipientId,
    string? RecipientName,
    string? RecipientEmail,
    DateTimeOffset AcceptedAt,
    Guid AcceptedByUserId,
    string? ReviewerName,
    DateTimeOffset? GeneratedAt,
    string? ContentHash,
    Guid? PreviousPackageId,
    Guid? SupersededByPackageId,
    Guid? OwnerUserId,
    string? OwnerName,
    Guid? AssignedTeamId,
    int ResponseCount,
    int FileCount,
    IReadOnlyList<PackageFileResponse> Files,
    IReadOnlyList<DeliveryJobResponse> Deliveries,
    IReadOnlyList<ManualHandoffResponse> ManualHandoffs,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PackageFileResponse(
    Guid FileAssetId,
    Guid RequirementId,
    string FileName,
    string MimeType,
    long SizeBytes,
    FileScanStatus ScanStatus,
    bool IsPreviouslySubmitted,
    string? DocumentName,
    DateTimeOffset? DocumentExpiresAt);

public sealed record PackageContentsResponse(
    Guid PackageId,
    string PackageReference,
    Guid SubmissionId,
    Guid ActionId,
    IReadOnlyList<PackageResponseValue> Responses,
    IReadOnlyList<PackageFileResponse> Files,
    PackageIntegrityResponse Integrity);

public sealed record PackageResponseValue(Guid RequirementId, JsonElement? Value);

public sealed record PackageIntegrityResponse(
    string? ContentHash,
    string MetadataJson,
    int VersionNumber,
    DateTimeOffset AcceptedAt,
    Guid AcceptedByUserId,
    DateTimeOffset? DeclarationAcceptedAt,
    string? DeclarationIpAddress);

public sealed record PackageGeneratedResponse(
    Guid Id,
    string PackageReference,
    SubmissionPackageStatus Status,
    string Artifact,
    DateTimeOffset? GeneratedAt,
    string Url);

public sealed record PackageSummaryResponse(string PlainText, object Json);

public sealed record PackageExportResponse(
    PackageDetailResponse Package,
    PackageContentsResponse Contents,
    PackageSummaryResponse Summary,
    IReadOnlyList<DeliveryJobResponse> Deliveries,
    IReadOnlyList<ManualHandoffResponse> ManualHandoffs,
    DateTimeOffset ExportedAt);

public sealed record AssignPackageRequest(Guid? OwnerUserId, Guid? AssignedTeamId);

public sealed record UpsertDestinationRequest(
    string? Name,
    DestinationType Type,
    JsonElement? Configuration,
    JsonElement? SecretConfiguration,
    DestinationStatus? Status,
    bool? IsDefault,
    bool? IsActive);

public sealed record DestinationResponse(
    Guid Id,
    string Name,
    DestinationType Type,
    DestinationStatus Status,
    JsonElement Configuration,
    bool HasSecretConfiguration,
    string? GeneratedSigningSecret,
    bool IsDefault,
    bool IsActive,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastValidatedAt,
    string? LastValidationStatus);

public sealed record UpsertRoutingRuleRequest(
    Guid? TemplateId,
    Guid DestinationId,
    RoutingTrigger Trigger,
    int Sequence,
    bool IsRequired,
    bool IsAutomatic,
    JsonElement? ConfigurationOverrides);

public sealed record RoutingRuleResponse(
    Guid Id,
    Guid? TemplateId,
    Guid DestinationId,
    string? DestinationName,
    DestinationType? DestinationType,
    RoutingTrigger Trigger,
    int Sequence,
    bool IsRequired,
    bool IsAutomatic,
    JsonElement ConfigurationOverrides,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DeliverPackageRequest(
    IReadOnlyList<Guid>? DestinationIds,
    bool IncludeReport,
    bool IncludeFiles,
    string? IdempotencyKey);

public sealed record DeliveryJobResponse(
    Guid Id,
    Guid SubmissionPackageId,
    Guid? DestinationId,
    string? DestinationName,
    DestinationType? DestinationType,
    DeliveryJobStatus Status,
    int AttemptCount,
    int MaximumAttempts,
    Guid? RequestedByUserId,
    string TriggeredBy,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? NextRetryAt,
    string? FailureCode,
    string? FailureMessage,
    string? ExternalReference,
    string IdempotencyKey);

public sealed record DeliveryJobDetailResponse(
    DeliveryJobResponse Job,
    IReadOnlyList<DeliveryAttemptResponse> Attempts);

public sealed record DeliveryAttemptResponse(
    Guid Id,
    int AttemptNumber,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DeliveryAttemptStatus Status,
    int? HttpStatusCode,
    string? ProviderReference,
    string? ResponseSummary,
    string? FailureCode,
    string? FailureMessage,
    long? DurationMilliseconds);

public sealed record CreateManualHandoffRequest(
    string? DestinationLabel,
    string? ExternalReference,
    string? Notes,
    Guid? EvidenceFileAssetId);

public sealed record ManualHandoffResponse(
    Guid Id,
    Guid SubmissionPackageId,
    string DestinationLabel,
    Guid CompletedByUserId,
    DateTimeOffset CompletedAt,
    string? ExternalReference,
    string? Notes,
    Guid? EvidenceFileAssetId);
