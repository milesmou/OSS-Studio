namespace OSSClient.Models;

public sealed class WorkspaceState
{
    public List<AccountProfile> Accounts { get; init; } = [];

    public string ActiveAccountId { get; set; } = string.Empty;

    public List<string> OpenBucketIds { get; init; } = [];

    public string ActiveBucketId { get; set; } = string.Empty;
}
