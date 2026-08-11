using System.Text.Json;
using OSSClient.Models;

namespace OSSClient.Services;

public sealed class WorkspaceStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _statePath;

    public WorkspaceStateStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _statePath = Path.Combine(appData, "OSSClient", "workspace.json");
    }

    public WorkspaceState Load()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                var state = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(_statePath), JsonOptions);
                if (state is { Accounts.Count: > 0 })
                {
                    Normalize(state);
                    return state;
                }
            }
        }
        catch (JsonException)
        {
            // Invalid local state falls back to a safe seed workspace.
        }
        catch (IOException)
        {
            // The UI remains usable even if local state cannot be read.
        }

        return CreateSeedState();
    }

    public void Save(WorkspaceState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(_statePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(state, JsonOptions));
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
        if (state.Accounts.All(account => account.Id != state.ActiveAccountId))
        {
            state.ActiveAccountId = state.Accounts[0].Id;
        }

        var bucketIds = state.Accounts.SelectMany(account => account.Buckets).Select(bucket => bucket.Id).ToHashSet();
        state.OpenBucketIds.RemoveAll(id => !bucketIds.Contains(id));

        if (state.OpenBucketIds.Count == 0)
        {
            var firstBucket = state.Accounts.First(account => account.Id == state.ActiveAccountId).Buckets.FirstOrDefault();
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

    private static WorkspaceState CreateSeedState()
    {
        var now = new DateTime(2026, 8, 11, 10, 18, 0, DateTimeKind.Local);

        IReadOnlyList<ObjectEntry> ImageObjects =
        [
            new("campaign/", true, "—", "—", now.AddMinutes(-46)),
            new("product/", true, "—", "—", now.AddHours(-16)),
            new("homepage-banner.webp", false, "1.82 MB", "标准存储", now),
            new("manifest.json", false, "12.4 KB", "标准存储", now.AddMinutes(-1)),
            new("assets-release-v24.zip", false, "348.6 MB", "低频访问", now.AddDays(-2))
        ];

        IReadOnlyList<ObjectEntry> StaticObjects =
        [
            new("css/", true, "—", "—", now.AddDays(-1)),
            new("js/", true, "—", "—", now.AddDays(-1)),
            new("index.html", false, "8.6 KB", "标准存储", now.AddMinutes(-23)),
            new("favicon.ico", false, "15.1 KB", "标准存储", now.AddDays(-4))
        ];

        var production = new AccountProfile(
            "account-production",
            "生产环境 · 阿里云主账号",
            "cn-hangzhou",
            [
                new("bucket-prod-images", "prod-images", "cn-hangzhou", "华东 1（杭州）", ImageObjects),
                new("bucket-web-static", "web-static", "cn-beijing", "华北 2（北京）", StaticObjects),
                new("bucket-app-backups", "app-backups", "cn-shenzhen", "华南 1（深圳）", ImageObjects.Take(3).ToArray()),
                new("bucket-audit-archive", "audit-archive", "cn-shanghai", "华东 2（上海）", StaticObjects.Take(2).ToArray())
            ]);

        var testing = new AccountProfile(
            "account-testing",
            "测试环境 · RAM 用户",
            "cn-hangzhou",
            [
                new("bucket-test-assets", "test-assets", "cn-hangzhou", "华东 1（杭州）", StaticObjects),
                new("bucket-test-logs", "test-logs", "cn-shanghai", "华东 2（上海）", ImageObjects.Take(2).ToArray())
            ]);

        return new WorkspaceState
        {
            Accounts = [production, testing],
            ActiveAccountId = production.Id,
            OpenBucketIds = [production.Buckets[0].Id, production.Buckets[1].Id],
            ActiveBucketId = production.Buckets[0].Id
        };
    }
}
