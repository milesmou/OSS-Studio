namespace OSSClient.Models;

public sealed record BucketProfile(
    string Id,
    string Name,
    string RegionCode,
    string RegionName,
    IReadOnlyList<ObjectEntry> Objects);
