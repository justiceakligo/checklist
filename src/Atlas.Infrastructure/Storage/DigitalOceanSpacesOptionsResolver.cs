using System.Text.Json;
using Atlas.Domain.Entities;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atlas.Infrastructure.Storage;

public sealed class DigitalOceanSpacesOptionsResolver(
    AtlasDbContext dbContext,
    IOptions<DigitalOceanSpacesOptions> fallbackOptions)
{
    private const string Category = DigitalOceanSpacesOptions.SectionName;
    private readonly DigitalOceanSpacesOptions _fallback = fallbackOptions.Value;

    public async Task<DigitalOceanSpacesOptions> ResolveAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.AdminSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(setting => setting.OrganizationId == null && setting.Category == Category)
            .ToListAsync(cancellationToken);

        return Apply(settings);
    }

    public string ResolveQuarantinePrefix()
    {
        var setting = dbContext.AdminSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefault(setting => setting.OrganizationId == null
                && setting.Category == Category
                && setting.Key == nameof(DigitalOceanSpacesOptions.QuarantinePrefix));

        return ReadString(setting?.ValueJson) ?? _fallback.QuarantinePrefix;
    }

    private DigitalOceanSpacesOptions Apply(IReadOnlyCollection<AdminSetting> settings)
    {
        string? Read(string key)
        {
            var setting = settings.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            return ReadString(setting?.ValueJson);
        }

        bool? ReadBoolValue(string key)
        {
            var setting = settings.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            return ReadBool(setting?.ValueJson);
        }

        return new DigitalOceanSpacesOptions
        {
            ServiceUrl = Read(nameof(DigitalOceanSpacesOptions.ServiceUrl)) ?? _fallback.ServiceUrl,
            Region = Read(nameof(DigitalOceanSpacesOptions.Region)) ?? _fallback.Region,
            BucketName = Read(nameof(DigitalOceanSpacesOptions.BucketName)) ?? _fallback.BucketName,
            AccessKey = Read(nameof(DigitalOceanSpacesOptions.AccessKey)) ?? _fallback.AccessKey,
            SecretKey = Read(nameof(DigitalOceanSpacesOptions.SecretKey)) ?? _fallback.SecretKey,
            QuarantinePrefix = Read(nameof(DigitalOceanSpacesOptions.QuarantinePrefix)) ?? _fallback.QuarantinePrefix,
            ForcePathStyle = ReadBoolValue(nameof(DigitalOceanSpacesOptions.ForcePathStyle)) ?? _fallback.ForcePathStyle
        };
    }

    private static string? ReadString(string? valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return null;
        }

        try
        {
            var value = JsonSerializer.Deserialize<JsonElement>(valueJson);
            var result = value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.GetRawText();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch (JsonException)
        {
            return valueJson;
        }
    }

    private static bool? ReadBool(string? valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return null;
        }

        try
        {
            var value = JsonSerializer.Deserialize<JsonElement>(valueJson);
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
                _ => null
            };
        }
        catch (JsonException)
        {
            return bool.TryParse(valueJson, out var parsed) ? parsed : null;
        }
    }
}
