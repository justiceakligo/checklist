using Atlas.Application.Abstractions;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class TeamEndpoints
{
    public static IEndpointRouteBuilder MapAtlasTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/teams").WithTags("Teams");

        group.MapGet("", ListTeams);
        group.MapPost("", CreateTeam);
        group.MapPatch("/{teamId:guid}", UpdateTeam);

        return app;
    }

    private static async Task<IResult> ListTeams(
        bool? activeOnly,
        string? search,
        int? page,
        int? pageSize,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        if (!TryRequireDashboard(tenantContext, out _, out var problem))
        {
            return problem!;
        }

        var normalizedPage = EndpointHelpers.NormalizePage(page);
        var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize, fallback: 100);
        var query = dbContext.OrganizationTeams
            .AsNoTracking()
            .Include(item => item.CreatedByUser)
            .AsQueryable();

        if (activeOnly.GetValueOrDefault(true))
        {
            query = query.Where(item => item.Status == OrganizationTeamStatus.Active);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(item => item.Name.Contains(term)
                || (item.Description != null && item.Description.Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var teams = await query
            .OrderBy(item => item.Name)
            .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            items = teams.Select(ToTeamResponse).ToList(),
            page = normalizedPage,
            pageSize = normalizedPageSize,
            total
        });
    }

    private static async Task<IResult> CreateTeam(
        UpsertTeamRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryRequireTeamAdministration(tenantContext, out var organizationId, out var problem))
        {
            return problem!;
        }

        var validation = ValidateTeamRequest(request);
        if (validation is not null)
        {
            return validation;
        }

        var name = NormalizeTeamName(request.Name);
        if (await dbContext.OrganizationTeams.AnyAsync(item => item.Name == name, cancellationToken))
        {
            return EndpointHelpers.Problem("duplicate_team", "A team with this name already exists.", StatusCodes.Status409Conflict);
        }

        var team = new OrganizationTeam
        {
            OrganizationId = organizationId,
            Name = name,
            Description = Truncate(request.Description?.Trim(), 500),
            Status = request.Status ?? OrganizationTeamStatus.Active,
            CreatedByUserId = tenantContext.UserId,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };

        dbContext.OrganizationTeams.Add(team);
        SecurityEndpoints.AddAudit(
            dbContext,
            organizationId,
            null,
            tenantContext,
            "team.created",
            new { team.Id, team.Name, team.Status },
            httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/v1/teams/{team.Id}", ToTeamResponse(team));
    }

    private static async Task<IResult> UpdateTeam(
        Guid teamId,
        UpsertTeamRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryRequireTeamAdministration(tenantContext, out var organizationId, out var problem))
        {
            return problem!;
        }

        var validation = ValidateTeamRequest(request);
        if (validation is not null)
        {
            return validation;
        }

        var team = await dbContext.OrganizationTeams.FirstOrDefaultAsync(item => item.Id == teamId, cancellationToken);
        if (team is null)
        {
            return EndpointHelpers.Problem("not_found", "Team was not found.", StatusCodes.Status404NotFound);
        }

        var name = NormalizeTeamName(request.Name);
        if (await dbContext.OrganizationTeams.AnyAsync(item => item.Id != team.Id && item.Name == name, cancellationToken))
        {
            return EndpointHelpers.Problem("duplicate_team", "A team with this name already exists.", StatusCodes.Status409Conflict);
        }

        team.Name = name;
        team.Description = Truncate(request.Description?.Trim(), 500);
        team.Status = request.Status ?? team.Status;
        team.UpdatedAt = clock.UtcNow;

        SecurityEndpoints.AddAudit(
            dbContext,
            organizationId,
            null,
            tenantContext,
            "team.updated",
            new { team.Id, team.Name, team.Status },
            httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToTeamResponse(team));
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
            organizationId = Guid.Empty;
            return false;
        }

        return true;
    }

    private static bool TryRequireTeamAdministration(ITenantContext tenantContext, out Guid organizationId, out IResult? problem)
    {
        if (!TryRequireDashboard(tenantContext, out organizationId, out problem))
        {
            return false;
        }

        if (EndpointHelpers.HasScope(tenantContext, "admin:*"))
        {
            return true;
        }

        problem = EndpointHelpers.Problem("forbidden", "Admin scope is required to manage teams.", StatusCodes.Status403Forbidden);
        organizationId = Guid.Empty;
        return false;
    }

    private static IResult? ValidateTeamRequest(UpsertTeamRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return EndpointHelpers.Problem("validation_failed", "Team name is required.", StatusCodes.Status422UnprocessableEntity);
        }

        if (NormalizeTeamName(request.Name).Length > 120)
        {
            return EndpointHelpers.Problem("validation_failed", "Team name must be 120 characters or fewer.", StatusCodes.Status422UnprocessableEntity);
        }

        return null;
    }

    private static TeamResponse ToTeamResponse(OrganizationTeam team)
    {
        return new TeamResponse(
            team.Id,
            team.Name,
            team.Description,
            team.Status,
            team.CreatedByUserId,
            team.CreatedByUser?.FullName,
            team.CreatedAt,
            team.UpdatedAt);
    }

    private static string NormalizeTeamName(string? value)
    {
        return string.Join(' ', (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? Truncate(string? value, int maxLength)
    {
        return value is null || value.Length <= maxLength ? value : value[..maxLength];
    }
}

public sealed record UpsertTeamRequest(
    string? Name,
    string? Description,
    OrganizationTeamStatus? Status);

public sealed record TeamResponse(
    Guid Id,
    string Name,
    string? Description,
    OrganizationTeamStatus Status,
    Guid? CreatedByUserId,
    string? CreatedByUserName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
