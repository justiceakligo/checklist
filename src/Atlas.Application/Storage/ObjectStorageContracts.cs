namespace Atlas.Application.Storage;

public sealed record PresignedUploadRequest(
    string StorageKey,
    string ContentType,
    long SizeBytes,
    TimeSpan Lifetime);

public sealed record PresignedUploadResponse(
    Uri UploadUrl,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset ExpiresAt);

public sealed record PresignedDownloadRequest(
    string StorageKey,
    string FileName,
    string ContentType,
    TimeSpan Lifetime);

public sealed record PresignedDownloadResponse(
    Uri DownloadUrl,
    DateTimeOffset ExpiresAt);

public sealed record ObjectMetadata(
    string StorageKey,
    long SizeBytes,
    string? ContentType,
    string? ETag);

public interface IObjectStorageService
{
    Task<PresignedUploadResponse> CreateUploadUrlAsync(PresignedUploadRequest request, CancellationToken cancellationToken);
    Task<PresignedDownloadResponse> CreateDownloadUrlAsync(PresignedDownloadRequest request, CancellationToken cancellationToken);
    Task<ObjectMetadata?> GetObjectMetadataAsync(string storageKey, CancellationToken cancellationToken);
    Task DeleteObjectAsync(string storageKey, CancellationToken cancellationToken);
    string BuildQuarantineKey(Guid organizationId, Guid fileId, string originalFileName);
}
