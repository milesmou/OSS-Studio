namespace OSSStudio.Models;

public sealed record BucketProfile(
    string Id,
    string Name,
    string RegionCode,
    string RegionName,
    List<ObjectEntry> Objects,
    string Endpoint = "",
    bool UseHttps = true,
    string Prefix = "",
    bool RequestPayer = false,
    string Note = "",
    bool KeepLogin = true,
    bool RememberSecret = true,
    string EndpointMode = "Default");
