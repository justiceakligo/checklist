using Atlas.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atlas.Infrastructure.Persistence;

public sealed class AtlasDbContextFactory : IDesignTimeDbContextFactory<AtlasDbContext>
{
    public AtlasDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=atlaschecklist;Username=postgres;Password=Fafa@post1";

        var options = new DbContextOptionsBuilder<AtlasDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(AtlasDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AtlasDbContext(options, new EmptyTenantContext(), new DesignTimeClock());
    }

    private sealed class EmptyTenantContext : ITenantContext
    {
        public Guid? OrganizationId => null;
        public string? ActorId => null;
        public string? ActorType => null;
        public Guid? UserId => null;
        public Guid? RecipientId => null;
        public IReadOnlySet<string> Scopes => new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class DesignTimeClock : IAtlasClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
