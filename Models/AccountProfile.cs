namespace OSSClient.Models;

public sealed record AccountProfile(
    string Id,
    string DisplayName,
    string DefaultRegion,
    IReadOnlyList<BucketProfile> Buckets);
