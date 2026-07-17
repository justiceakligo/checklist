using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Atlas.Application.Abstractions;
using Atlas.Application.Storage;

namespace Atlas.Infrastructure.Storage;

public sealed class DigitalOceanSpacesStorageService(
    DigitalOceanSpacesOptionsResolver optionsResolver,
    IAtlasClock clock) : IObjectStorageService
{
    public async Task<PresignedUploadResponse> CreateUploadUrlAsync(
        PresignedUploadRequest request,
        CancellationToken cancellationToken)
    {
        var options = await optionsResolver.ResolveAsync(cancellationToken);
        ValidateConfigured(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StorageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContentType);

        using var s3Client = CreateS3Client(options);
        var expiresAt = clock.UtcNow.Add(request.Lifetime);
        var signedRequest = new GetPreSignedUrlRequest
        {
            BucketName = options.BucketName,
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
        var options = await optionsResolver.ResolveAsync(cancellationToken);
        ValidateConfigured(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StorageKey);

        using var s3Client = CreateS3Client(options);
        var expiresAt = clock.UtcNow.Add(request.Lifetime);
        var signedRequest = new GetPreSignedUrlRequest
        {
            BucketName = options.BucketName,
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
        var options = await optionsResolver.ResolveAsync(cancellationToken);
        ValidateConfigured(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        try
        {
            using var s3Client = CreateS3Client(options);
            var response = await s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = options.BucketName,
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
        var options = await optionsResolver.ResolveAsync(cancellationToken);
        ValidateConfigured(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        using var s3Client = CreateS3Client(options);
        await s3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = options.BucketName,
            Key = storageKey
        }, cancellationToken);
    }

    public string BuildQuarantineKey(Guid organizationId, Guid fileId, string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName);
        extension = extension.Length > 20 ? string.Empty : extension.ToLowerInvariant();
        var prefix = optionsResolver.ResolveQuarantinePrefix().Trim().Trim('/');
        return $"{prefix}/org/{organizationId:N}/{fileId:N}{extension}";
    }

    private static IAmazonS3 CreateS3Client(DigitalOceanSpacesOptions options)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl.Trim().TrimEnd('/'),
            ForcePathStyle = options.ForcePathStyle,
            AuthenticationRegion = options.Region
        };

        return new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            config);
    }

    private static void ValidateConfigured(DigitalOceanSpacesOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.BucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ServiceUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AccessKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SecretKey);
    }

    private static string SanitizeHeaderValue(string value)
    {
        return value.Replace("\"", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
    }
}
