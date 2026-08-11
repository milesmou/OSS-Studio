namespace OSSStudio.Models;

public sealed record DirectoryBookmark(string Id, string BucketId, string Prefix, string DisplayName = "");
