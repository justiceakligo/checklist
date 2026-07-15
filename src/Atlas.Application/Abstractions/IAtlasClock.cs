namespace Atlas.Application.Abstractions;

public interface IAtlasClock
{
    DateTimeOffset UtcNow { get; }
}
