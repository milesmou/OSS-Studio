namespace OSSStudio.Models;

public sealed class WorkspaceState
{
    public List<BucketProfile> Buckets { get; init; } = [];

    public List<string> OpenBucketIds { get; init; } = [];

    public string ActiveBucketId { get; set; } = string.Empty;
}
