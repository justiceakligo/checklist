using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Atlas.Application.Abstractions;
using Atlas.Application.Email;
using Atlas.Application.Settings;
using Atlas.Application.Storage;
using Atlas.Infrastructure.Email;
using Atlas.Infrastructure.Persistence;
using Atlas.Infrastructure.Security;
using Atlas.Infrastructure.Settings;
using Atlas.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Atlas.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAtlasInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is required.");

        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<ITenantContextAccessor>());
        services.AddSingleton<IAtlasClock, SystemClock>();

        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.Configure<DigitalOceanSpacesOptions>(configuration.GetSection(DigitalOceanSpacesOptions.SectionName));
        services.AddHttpClient();

        services.AddDbContext<AtlasDbContext>(options =>
        {
            options.UseNpgsql(
                    connectionString,
                    npgsql => npgsql.MigrationsAssembly(typeof(AtlasDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention();
        });

        services.AddSingleton<ISecretHasher, Sha256SecretHasher>();
        services.AddSingleton<IAmazonS3>(CreateS3Client);
        services.AddScoped<IObjectStorageService, DigitalOceanSpacesStorageService>();
        services.AddScoped<IAdminSettingService, AdminSettingService>();
        services.AddScoped<IEmailService, ResendEmailService>();

        return services;
    }

    private static IAmazonS3 CreateS3Client(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<DigitalOceanSpacesOptions>>().Value;
        var config = new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl,
            ForcePathStyle = options.ForcePathStyle,
            AuthenticationRegion = options.Region,
            RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region)
        };

        return new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            config);
    }
}
