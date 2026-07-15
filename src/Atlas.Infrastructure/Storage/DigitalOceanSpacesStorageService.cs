using Amazon.S3;
using Amazon.S3.Model;
using Atlas.Application.Abstractions;
using Atlas.Application.Storage;
using Microsoft.Extensions.Options;

namespace Atlas.Infrastructure.Storage;

public sealed class DigitalOceanSpacesStorageService(
    IAmazonS3 s3Client,
    IOptions<DigitalOceanSpacesOptions> options,
    IAtlasClock clock) : IObjectStorageService
{
    private readonly DigitalOceanSpacesOptions _options = options.Value;

    public async Task<PresignedUploadResponse> CreateUploadUrlAsync(
        PresignedUploadRequest request,
        CancellationToken cancellationToken)
    {
        ValidateConfigured();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StorageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContentType);

        var expiresAt = clock.UtcNow.Add(request.Lifetime);
        var signedRequest = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = request.StorageKey,
            Verb = HttpVerb.PUT,
            Expires = expiresAt.UtcDateTime,
            ContentType = request.ContentType
        };

        var url = await s3Client.GetPreSignedURLAsync(signedRequest);
        return new PresignedUploadResponse(
            new Uri(url),
            new Dictionary<string, string> { ["Content-Type"] = request.ContentType },
            expiresAt);
    }

    public async Task<PresignedDownloadResponse> CreateDownloadUrlAsync(
        PresignedDownloadRequest request,
        CancellationToken cancellationToken)
    {
        ValidateConfigured();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StorageKey);

        var expiresAt = clock.UtcNow.Add(request.Lifetime);
        var signedRequest = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = request.StorageKey,
            Verb = HttpVerb.GET,
            Expires = expiresAt.UtcDateTime,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentType = request.ContentType,
                ContentDisposition = $"attachment; filename=\"{SanitizeHeaderValue(request.FileName)}\""
            }
        };

        var url = await s3Client.GetPreSignedURLAsync(signedRequest);
        return new PresignedDownloadResponse(new Uri(url), expiresAt);
    }

    public async Task<ObjectMetadata?> GetObjectMetadataAsync(string storageKey, CancellationToken cancellationToken)
    {
        ValidateConfigured();
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        try
        {
            var response = await s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = storageKey
            }, cancellationToken);

            return new ObjectMetadata(
                storageKey,
                response.ContentLength,
                response.Headers.ContentType,
                response.ETag);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteObjectAsync(string storageKey, CancellationToken cancellationToken)
    {
        ValidateConfigured();
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        await s3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _options.BucketName,
            Key = storageKey
        }, cancellationToken);
    }

    public string BuildQuarantineKey(Guid organizationId, Guid fileId, string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName);
        extension = extension.Length > 20 ? string.Empty : extension.ToLowerInvariant();
        var prefix = _options.QuarantinePrefix.Trim().Trim('/');
        return $"{prefix}/org/{organizationId:N}/{fileId:N}{extension}";
    }

    private void ValidateConfigured()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.BucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ServiceUrl);
    }

    private static string SanitizeHeaderValue(string value)
    {
        return value.Replace("\"", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
    }
}
