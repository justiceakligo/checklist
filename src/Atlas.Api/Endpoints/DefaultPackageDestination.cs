using System.Text.Json;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

internal static class DefaultPackageDestination
{
    public const string Name = "Organization inbox";

    public static async Task AddEmailIfMissingAsync(
        AtlasDbContext dbContext,
        Guid organizationId,
        Guid createdByUserId,
        string email,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var exists = await dbContext.Destinations.IgnoreQueryFilters()
            .AnyAsync(item => item.OrganizationId == organizationId
                && item.Type == DestinationType.Email
                && item.IsDefault
                && item.IsActive
                && item.Status == DestinationStatus.Active,
                cancellationToken);
        if (exists)
        {
            return;
        }

        dbContext.Destinations.Add(CreateEmail(organizationId, createdByUserId, email, now));
    }

    public static Destination CreateEmail(Guid organizationId, Guid createdByUserId, string email, DateTimeOffset now)
    {
        return new Destination
        {
            OrganizationId = organizationId,
            Name = Name,
            Type = DestinationType.Email,
            Status = DestinationStatus.Active,
            ConfigurationJson = JsonSerializer.Serialize(
                new
                {
                    recipients = new[] { email.Trim().ToLowerInvariant() },
                    cc = Array.Empty<string>()
                },
                EndpointHelpers.JsonOptions),
            IsDefault = true,
            IsActive = true,
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
            UpdatedAt = now,
            LastValidatedAt = now,
            LastValidationStatus = "valid"
        };
    }
}
