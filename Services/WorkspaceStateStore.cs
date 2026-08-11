using System.Text.Json;
using OSSStudio.Models;

namespace OSSStudio.Services;

public sealed class WorkspaceStateStore
{
    private readonly string _statePath;

    public WorkspaceStateStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _statePath = Path.Combine(appData, "OSS-Studio", "workspace.json");
    }

    public WorkspaceState Load()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                var state = JsonSerializer.Deserialize(
                    File.ReadAllText(_statePath),
                    WorkspaceJsonContext.Default.WorkspaceState);
                if (state is not null)
                {
                    Normalize(state);
                    return state;
                }
            }
        }
        catch (JsonException)
        {
            // Invalid local state falls back to an empty workspace.
        }
        catch (IOException)
        {
            // The UI remains usable even if local state cannot be read.
        }

        return CreateEmptyState();
    }

    public void Save(WorkspaceState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(_statePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                _statePath,
                JsonSerializer.Serialize(state, WorkspaceJsonContext.Default.WorkspaceState));
        }
        catch (IOException)
        {
            // Persistence is best-effort and must not terminate the desktop client.
        }
        catch (UnauthorizedAccessException)
        {
            // A locked-down workstation can still use the current in-memory session.
        }
    }

    private static void Normalize(WorkspaceState state)
    {
        state.Buckets.RemoveAll(bucket => bucket.Id is
            "bucket-prod-images" or
            "bucket-web-static" or
            "bucket-app-backups" or
            "bucket-audit-archive" or
            "bucket-test-assets" or
            "bucket-test-logs");

        var bucketIds = state.Buckets.Select(bucket => bucket.Id).ToHashSet();
        state.OpenBucketIds.RemoveAll(id => !bucketIds.Contains(id));

        if (state.OpenBucketIds.Count == 0)
        {
            var firstBucket = state.Buckets.FirstOrDefault();
            if (firstBucket is not null)
            {
                state.OpenBucketIds.Add(firstBucket.Id);
            }
        }

        if (!state.OpenBucketIds.Contains(state.ActiveBucketId))
        {
            state.ActiveBucketId = state.OpenBucketIds.FirstOrDefault() ?? string.Empty;
        }
    }

    private static WorkspaceState CreateEmptyState() => new();
}
