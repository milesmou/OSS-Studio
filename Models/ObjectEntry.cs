namespace OSSStudio.Models;

public sealed record ObjectEntry(
    string Name,
    bool IsFolder,
    string Size,
    string StorageClass,
    DateTime LastModified,
    string Key = "",
    long SizeBytes = 0)
{
    public string DisplayName => IsFolder ? $"📁  {Name}" : $"📄  {Name}";

    public string DisplayModified => IsFolder || LastModified == DateTime.MinValue
        ? "—"
        : LastModified.ToString("yyyy-MM-dd HH:mm");
}
