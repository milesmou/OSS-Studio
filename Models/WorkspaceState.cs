namespace OSSStudio.Models;

public sealed class WorkspaceState
{
    public List<BucketProfile> Buckets { get; init; } = [];

    public List<string> OpenBucketIds { get; init; } = [];

    public string ActiveBucketId { get; set; } = string.Empty;

    public List<DirectoryBookmark> Bookmarks { get; set; } = [];

    public int UploadConcurrency { get; set; } = 3;

    public int DownloadConcurrency { get; set; } = 3;

    public int RequestTimeoutSeconds { get; set; } = 60;

    public int RetryCount { get; set; } = 5;

    public string ThemeMode { get; set; } = "Light";
}
