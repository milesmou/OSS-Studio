using System.Text.Json.Serialization;

namespace OSSStudio.Models;

public sealed record BucketProfile
{
    public BucketProfile(
        string id,
        string name,
        string regionCode,
        string regionName,
        List<ObjectEntry>? objects = null,
        string endpoint = "",
        bool useHttps = true,
        string prefix = "",
        bool requestPayer = false,
        string note = "",
        bool keepLogin = true,
        bool rememberSecret = true,
        string endpointMode = "Default")
    {
        Id = id;
        Name = name;
        RegionCode = regionCode;
        RegionName = regionName;
        Objects = objects ?? [];
        Endpoint = endpoint;
        UseHttps = useHttps;
        Prefix = prefix;
        RequestPayer = requestPayer;
        Note = note;
        KeepLogin = keepLogin;
        RememberSecret = rememberSecret;
        EndpointMode = endpointMode;
    }

    public string Id { get; init; }
    public string Name { get; init; }
    public string RegionCode { get; init; }
    public string RegionName { get; init; }

    [JsonIgnore]
    public List<ObjectEntry> Objects { get; init; }

    public string Endpoint { get; init; }
    public bool UseHttps { get; init; }
    public string Prefix { get; init; }
    public bool RequestPayer { get; init; }
    public string Note { get; init; }
    public bool KeepLogin { get; init; }
    public bool RememberSecret { get; init; }
    public string EndpointMode { get; init; }
}
