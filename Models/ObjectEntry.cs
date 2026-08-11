namespace OSSClient.Models;

public sealed record ObjectEntry(
    string Name,
    bool IsFolder,
    string Size,
    string StorageClass,
    DateTime LastModified)
{
    public string DisplayName => IsFolder ? $"📁  {Name}" : $"📄  {Name}";

    public string DisplayModified => LastModified.ToString("yyyy-MM-dd HH:mm");
}
