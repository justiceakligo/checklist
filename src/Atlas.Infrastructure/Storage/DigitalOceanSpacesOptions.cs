namespace Atlas.Infrastructure.Storage;

public sealed class DigitalOceanSpacesOptions
{
    public const string SectionName = "DigitalOceanSpaces";

    public string ServiceUrl { get; set; } = string.Empty;
    public string Region { get; set; } = "nyc3";
    public string BucketName { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string QuarantinePrefix { get; set; } = "quarantine";
    public bool ForcePathStyle { get; set; }
}
