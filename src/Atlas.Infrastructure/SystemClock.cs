using Atlas.Application.Abstractions;

namespace Atlas.Infrastructure;

public sealed class SystemClock : IAtlasClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
