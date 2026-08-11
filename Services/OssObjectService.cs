using OSS = AlibabaCloud.OSS.V2;
using OSSStudio.Models;
using System.Text;

namespace OSSStudio.Services;

public sealed class OssObjectService
{
    public OssObjectService(int uploadConcurrency = 3, int downloadConcurrency = 3)
    {
        UploadConcurrency = Math.Clamp(uploadConcurrency, 1, 8);
        DownloadConcurrency = Math.Clamp(downloadConcurrency, 1, 8);
    }

    public int UploadConcurrency { get; set; }

    public int DownloadConcurrency { get; set; }

    public async Task<int> UploadPathsAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string prefix,
        IReadOnlyList<string> paths,
        IProgress<(int Current, int Total, string Name)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var uploads = await Task.Run(() => CollectUploads(prefix, paths, cancellationToken), cancellationToken);

        using var client = CreateClient(bucket, credential);
        var completed = 0;
        await Parallel.ForEachAsync(uploads, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = UploadConcurrency
        }, async (upload, token) =>
        {
            var request = new OSS.Models.PutObjectRequest
            {
                Bucket = bucket.Name,
                Key = upload.Key
            };
            AddRequesterPaysHeader(request, bucket);
            await client.PutObjectFromFileAsync(
                request,
                upload.FilePath,
                cancellationToken: token);
            var current = Interlocked.Increment(ref completed);
            progress?.Report((current, uploads.Count, upload.Key));
        });

        return uploads.Count;
    }

    public async Task CreateFolderAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string key,
        CancellationToken cancellationToken = default)
    {
        var folderKey = key.EndsWith('/') ? key : $"{key}/";
        using var client = CreateClient(bucket, credential);
        using var body = new MemoryStream();
        var request = new OSS.Models.PutObjectRequest
        {
            Bucket = bucket.Name,
            Key = folderKey,
            Body = body,
            ContentLength = 0
        };
        AddRequesterPaysHeader(request, bucket);
        await client.PutObjectAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<int> CopyEntriesAsync(
        BucketProfile bucket,
        BucketCredential credential,
        IReadOnlyList<ObjectEntry> entries,
        string destinationPrefix,
        IProgress<(int Current, int Total, string Name)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedDestination = string.IsNullOrWhiteSpace(destinationPrefix)
            ? string.Empty
            : destinationPrefix.EndsWith('/') ? destinationPrefix : $"{destinationPrefix}/";
        using var client = CreateClient(bucket, credential);
        var copies = new List<(string SourceKey, string TargetKey)>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.IsFolder)
            {
                copies.Add((entry.Key, normalizedDestination + entry.Name));
                continue;
            }

            var objects = await ListObjectSummariesAsync(client, bucket, entry.Key, cancellationToken);
            copies.AddRange(objects
                .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                .Select(item =>
                {
                    var sourceKey = item.Key!;
                    var relativeKey = sourceKey.StartsWith(entry.Key, StringComparison.Ordinal)
                        ? sourceKey[entry.Key.Length..]
                        : sourceKey;
                    return (sourceKey, normalizedDestination + entry.Name + relativeKey);
                }));
        }

        if (copies.Any(copy => string.Equals(copy.SourceKey, copy.TargetKey, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("目标目录与源目录相同，请填写其它 OSS 目录");
        }

        var completed = 0;
        await Parallel.ForEachAsync(copies, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = 3
        }, async (copy, token) =>
        {
            var request = new OSS.Models.CopyObjectRequest
            {
                Bucket = bucket.Name,
                Key = copy.TargetKey,
                SourceBucket = bucket.Name,
                SourceKey = copy.SourceKey,
                ForbidOverwrite = true
            };
            AddRequesterPaysHeader(request, bucket);
            await client.CopyObjectAsync(request, cancellationToken: token);
            var current = Interlocked.Increment(ref completed);
            progress?.Report((current, copies.Count, copy.TargetKey));
        });
        return copies.Count;
    }

    public async Task<int> CopyEntryAsync(
        BucketProfile bucket,
        BucketCredential credential,
        ObjectEntry entry,
        string targetKey,
        IProgress<(int Current, int Total, string Name)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(bucket, credential);
        var copies = new List<(string SourceKey, string TargetKey)>();
        if (!entry.IsFolder)
        {
            copies.Add((entry.Key, targetKey.TrimEnd('/')));
        }
        else
        {
            var targetPrefix = targetKey.EndsWith('/') ? targetKey : $"{targetKey}/";
            var objects = await ListObjectSummariesAsync(client, bucket, entry.Key, cancellationToken);
            copies.AddRange(objects
                .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                .Select(item =>
                {
                    var sourceKey = item.Key!;
                    var relativeKey = sourceKey.StartsWith(entry.Key, StringComparison.Ordinal)
                        ? sourceKey[entry.Key.Length..]
                        : sourceKey;
                    return (sourceKey, targetPrefix + relativeKey);
                }));
        }

        if (copies.Any(copy => string.Equals(copy.SourceKey, copy.TargetKey, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("目标路径与源对象相同，请修改名称或目标目录");
        }

        var completed = 0;
        await Parallel.ForEachAsync(copies, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = 3
        }, async (copy, token) =>
        {
            var request = new OSS.Models.CopyObjectRequest
            {
                Bucket = bucket.Name,
                Key = copy.TargetKey,
                SourceBucket = bucket.Name,
                SourceKey = copy.SourceKey,
                ForbidOverwrite = true
            };
            AddRequesterPaysHeader(request, bucket);
            await client.CopyObjectAsync(request, cancellationToken: token);
            var current = Interlocked.Increment(ref completed);
            progress?.Report((current, copies.Count, copy.TargetKey));
        });
        return copies.Count;
    }

    public string GetObjectAddress(
        BucketProfile bucket,
        BucketCredential credential,
        string key,
        DateTime expiresAt)
    {
        using var client = CreateClient(bucket, credential);
        var request = new OSS.Models.GetObjectRequest
        {
            Bucket = bucket.Name,
            Key = key
        };
        AddRequesterPaysHeader(request, bucket);
        var result = client.Presign(request, expiresAt);
        return string.IsNullOrWhiteSpace(result.Url)
            ? throw new InvalidOperationException("OSS 未返回对象访问地址")
            : result.Url;
    }

    public async Task<string> GetTextObjectAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string key,
        int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(bucket, credential);
        var request = new OSS.Models.GetObjectRequest { Bucket = bucket.Name, Key = key };
        AddRequesterPaysHeader(request, bucket);
        var result = await client.GetObjectAsync(request, cancellationToken: cancellationToken);
        if (result.ContentLength is > 0 && result.ContentLength > maximumBytes)
        {
            result.Body?.Dispose();
            throw new InvalidOperationException($"文本文件不能超过 {maximumBytes / 1024 / 1024} MB");
        }

        using var body = result.Body ?? throw new IOException("OSS 未返回文件内容");
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await body.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (memory.Length + read > maximumBytes)
            {
                throw new InvalidOperationException($"文本文件不能超过 {maximumBytes / 1024 / 1024} MB");
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        var bytes = memory.ToArray();
        if (bytes.Any(value => value == 0))
        {
            throw new InvalidOperationException("文件内容包含二进制数据，无法使用文本编辑器打开");
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes)
                .TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidOperationException("文件不是有效的 UTF-8 文本，暂不支持在线编辑");
        }
    }

    public async Task PutTextObjectAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string key,
        string content,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(bucket, credential);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
        using var body = new MemoryStream(bytes, writable: false);
        var request = new OSS.Models.PutObjectRequest
        {
            Bucket = bucket.Name,
            Key = key,
            ContentType = GetTextContentType(key),
            Body = body
        };
        AddRequesterPaysHeader(request, bucket);
        await client.PutObjectAsync(request, cancellationToken: cancellationToken);
    }

    public async Task DownloadObjectAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string key,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(bucket, credential);
        var request = new OSS.Models.GetObjectRequest { Bucket = bucket.Name, Key = key };
        AddRequesterPaysHeader(request, bucket);
        await client.GetObjectToFileAsync(request, filePath, cancellationToken: cancellationToken);
    }

    public async Task<int> DownloadFolderAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string prefix,
        string destinationDirectory,
        IProgress<(int Current, int Total, string Name)>? progress = null,
        CancellationToken cancellationToken = default)
        => await DownloadFolderCoreAsync(
            bucket,
            credential,
            prefix,
            destinationDirectory,
            DownloadConcurrency,
            progress,
            cancellationToken);

    private async Task<int> DownloadFolderCoreAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string prefix,
        string destinationDirectory,
        int concurrency,
        IProgress<(int Current, int Total, string Name)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(bucket, credential);
        var objects = await ListObjectSummariesAsync(client, bucket, prefix, cancellationToken);
        var folderName = SanitizePathSegment(prefix.TrimEnd('/').Split('/').LastOrDefault() ?? bucket.Name);
        var targetRoot = Path.GetFullPath(Path.Combine(destinationDirectory, folderName));
        Directory.CreateDirectory(targetRoot);
        var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var downloads = new List<(string Key, string FilePath, string RelativeKey)>();

        foreach (var item in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = item.Key ?? string.Empty;
            if (key.Length == 0 || key.EndsWith('/'))
            {
                continue;
            }

            var relativeKey = key.StartsWith(prefix, StringComparison.Ordinal) ? key[prefix.Length..] : key;
            var relativePath = string.Join(Path.DirectorySeparatorChar, relativeKey
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(SanitizePathSegment));
            if (relativePath.Length == 0)
            {
                continue;
            }

            var filePath = Path.GetFullPath(Path.Combine(targetRoot, relativePath));
            var rootWithSeparator = targetRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!filePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"对象路径超出下载目录：{key}");
            }

            filePath = GetUniqueDownloadPath(filePath, reservedPaths);
            downloads.Add((key, filePath, relativeKey));
        }

        var downloaded = 0;
        await Parallel.ForEachAsync(
            downloads,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(concurrency, 1, 8),
                CancellationToken = cancellationToken
            },
            async (download, token) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(download.FilePath)!);
                var request = new OSS.Models.GetObjectRequest { Bucket = bucket.Name, Key = download.Key };
                AddRequesterPaysHeader(request, bucket);
                await client.GetObjectToFileAsync(request, download.FilePath, cancellationToken: token);
                var current = Interlocked.Increment(ref downloaded);
                progress?.Report((current, downloads.Count, download.RelativeKey));
            });

        return downloaded;
    }

    public async Task<int> DownloadEntriesAsync(
        BucketProfile bucket,
        BucketCredential credential,
        IReadOnlyList<ObjectEntry> entries,
        string destinationDirectory,
        IProgress<(int Current, int Total, string Name)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var completed = 0;
        var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reservedPathsLock = new object();
        await Parallel.ForEachAsync(
            entries,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = DownloadConcurrency,
                CancellationToken = cancellationToken
            },
            async (entry, token) =>
            {
                if (entry.IsFolder)
                {
                    await DownloadFolderCoreAsync(
                        bucket,
                        credential,
                        entry.Key,
                        destinationDirectory,
                        1,
                        cancellationToken: token);
                }
                else
                {
                    var requestedPath = Path.GetFullPath(Path.Combine(
                        destinationDirectory,
                        SanitizePathSegment(entry.Name)));
                    string filePath;
                    lock (reservedPathsLock)
                    {
                        filePath = GetUniqueDownloadPath(requestedPath, reservedPaths);
                    }

                    await DownloadObjectAsync(bucket, credential, entry.Key, filePath, token);
                }

                var current = Interlocked.Increment(ref completed);
                progress?.Report((current, entries.Count, entry.Name));
            });

        return completed;
    }

    public async Task<int> DeleteEntriesAsync(
        BucketProfile bucket,
        BucketCredential credential,
        IReadOnlyList<ObjectEntry> entries,
        IProgress<(int Current, int Total, string Name)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var deleted = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            deleted += entry.IsFolder
                ? await DeleteFolderAsync(bucket, credential, entry.Key, cancellationToken)
                : await DeleteSingleAsync(bucket, credential, entry.Key, cancellationToken);
            progress?.Report((deleted, entries.Count, entry.Name));
        }

        return deleted;
    }

    public async Task DeleteObjectAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string key,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(bucket, credential);
        var request = new OSS.Models.DeleteObjectRequest { Bucket = bucket.Name, Key = key };
        AddRequesterPaysHeader(request, bucket);
        await client.DeleteObjectAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<int> DeleteFolderAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string prefix,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(bucket, credential);
        var objects = await ListObjectSummariesAsync(client, bucket, prefix, cancellationToken);
        var keys = objects
            .Select(item => item.Key)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var batch in keys.Chunk(1000))
        {
            var request = new OSS.Models.DeleteMultipleObjectsRequest
            {
                Bucket = bucket.Name,
                Objects = batch.Select(key => new OSS.Models.DeleteObject { Key = key }).ToList()
            };
            AddRequesterPaysHeader(request, bucket);
            await client.DeleteMultipleObjectsAsync(request, cancellationToken: cancellationToken);
        }

        return keys.Count;
    }

    public async Task<OssObjectListResult> ListObjectsAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        var requestedPrefix = prefix ?? bucket.Prefix;
        try
        {
            var objects = await ListObjectsCoreAsync(
                bucket,
                credential,
                bucket.Endpoint,
                bucket.RegionCode,
                requestedPrefix,
                cancellationToken);
            return new OssObjectListResult(objects, bucket.Endpoint, bucket.RegionCode, false);
        }
        catch (OSS.OperationException exception) when (exception.InnerException is OSS.ServiceException)
        {
            var serviceException = (OSS.ServiceException)exception.InnerException;
            if (!TryGetRedirectEndpoint(serviceException, bucket.Endpoint, out var endpoint, out var regionCode))
            {
                throw;
            }

            var objects = await ListObjectsCoreAsync(
                bucket,
                credential,
                endpoint,
                regionCode,
                requestedPrefix,
                cancellationToken);
            return new OssObjectListResult(objects, endpoint, regionCode, true);
        }
    }

    public async Task<IReadOnlyList<string>> ListChildFolderPrefixesAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string prefix,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ListChildFolderPrefixesCoreAsync(
                bucket,
                credential,
                bucket.Endpoint,
                bucket.RegionCode,
                prefix,
                cancellationToken);
        }
        catch (OSS.OperationException exception) when (exception.InnerException is OSS.ServiceException)
        {
            var serviceException = (OSS.ServiceException)exception.InnerException;
            if (!TryGetRedirectEndpoint(serviceException, bucket.Endpoint, out var endpoint, out var regionCode))
            {
                throw;
            }

            return await ListChildFolderPrefixesCoreAsync(
                bucket,
                credential,
                endpoint,
                regionCode,
                prefix,
                cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<string>> ListChildFolderPrefixesCoreAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string endpoint,
        string regionCode,
        string prefix,
        CancellationToken cancellationToken)
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? string.Empty
            : prefix.Trim().TrimStart('/').TrimEnd('/') + "/";
        using var client = CreateClient(bucket, credential, endpoint, regionCode);
        var request = new OSS.Models.ListObjectsV2Request
        {
            Bucket = bucket.Name,
            Prefix = normalizedPrefix,
            Delimiter = "/",
            MaxKeys = 999
        };
        AddRequesterPaysHeader(request, bucket);
        var folders = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var page in client.ListObjectsV2Paginator(request).IterPageAsync(cancellationToken))
        {
            foreach (var commonPrefix in page.CommonPrefixes ?? [])
            {
                if (!string.IsNullOrWhiteSpace(commonPrefix.Prefix))
                {
                    folders.Add(commonPrefix.Prefix!);
                }
            }
        }

        return folders.OrderBy(prefix => prefix, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<List<ObjectEntry>> ListObjectsCoreAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string endpoint,
        string regionCode,
        string prefix,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(bucket, credential, endpoint, regionCode);
        var request = new OSS.Models.ListObjectsV2Request
        {
            Bucket = bucket.Name,
            Prefix = prefix,
            Delimiter = "/",
            MaxKeys = 999
        };

        if (bucket.RequestPayer)
        {
            request.Headers["x-oss-request-payer"] = "requester";
        }

        var entries = new List<ObjectEntry>();
        var paginator = client.ListObjectsV2Paginator(request);
        await foreach (var page in paginator.IterPageAsync(cancellationToken))
        {
            foreach (var commonPrefix in page.CommonPrefixes ?? [])
            {
                var key = commonPrefix.Prefix ?? string.Empty;
                var name = GetRelativeName(key, prefix);
                if (name.Length > 0)
                {
                    entries.Add(new ObjectEntry(name, true, "—", "—", DateTime.MinValue, key));
                }
            }

            foreach (var item in page.Contents ?? [])
            {
                var key = item.Key ?? string.Empty;
                if (key.Length == 0 || string.Equals(key, prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var name = GetRelativeName(key, prefix);
                if (name.Length > 0)
                {
                    entries.Add(new ObjectEntry(
                        name,
                        false,
                        FormatSize(item.Size ?? 0),
                        item.StorageClass ?? "Standard",
                        item.LastModified?.ToLocalTime() ?? DateTime.MinValue,
                        key,
                        item.Size ?? 0));
                }
            }
        }

        return entries
            .OrderByDescending(entry => entry.IsFolder)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<List<OSS.Models.ObjectSummary>> ListObjectSummariesAsync(
        OSS.Client client,
        BucketProfile bucket,
        string prefix,
        CancellationToken cancellationToken)
    {
        var request = new OSS.Models.ListObjectsV2Request
        {
            Bucket = bucket.Name,
            Prefix = prefix,
            MaxKeys = 999
        };
        AddRequesterPaysHeader(request, bucket);

        var objects = new List<OSS.Models.ObjectSummary>();
        await foreach (var page in client.ListObjectsV2Paginator(request).IterPageAsync(cancellationToken))
        {
            objects.AddRange(page.Contents ?? []);
        }

        return objects;
    }

    private static OSS.Client CreateClient(
        BucketProfile bucket,
        BucketCredential credential,
        string? endpoint = null,
        string? regionCode = null)
    {
        var configuration = OSS.Configuration.LoadDefault();
        configuration.CredentialsProvider = new OSS.Credentials.StaticCredentialsProvider(
            credential.AccessKeyId,
            credential.AccessKeySecret);
        configuration.Region = regionCode ?? bucket.RegionCode;
        configuration.Endpoint = endpoint ?? bucket.Endpoint;
        configuration.DisableSsl = !bucket.UseHttps;
        configuration.UseCName = string.Equals(bucket.EndpointMode, "Cname", StringComparison.OrdinalIgnoreCase);
        return new OSS.Client(configuration);
    }

    private static void AddRequesterPaysHeader(OSS.Models.RequestModel request, BucketProfile bucket)
    {
        if (bucket.RequestPayer)
        {
            request.Headers["x-oss-request-payer"] = "requester";
        }
    }

    private static string SanitizePathSegment(string segment)
    {
        var value = segment is "." or ".." ? $"_{segment}" : segment;
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidChar, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "_" : value;
    }

    private static List<(string FilePath, string Key)> CollectUploads(
        string prefix,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        var normalizedPrefix = string.IsNullOrEmpty(prefix) || prefix.EndsWith('/') ? prefix : $"{prefix}/";
        var uploads = new List<(string FilePath, string Key)>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                uploads.Add((path, normalizedPrefix + Path.GetFileName(path)));
                continue;
            }

            if (!Directory.Exists(path))
            {
                continue;
            }

            var directory = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var directoryName = Path.GetFileName(directory);
            foreach (var filePath in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(directory, filePath).Replace(Path.DirectorySeparatorChar, '/');
                uploads.Add((filePath, $"{normalizedPrefix}{directoryName}/{relativePath}"));
            }
        }

        return uploads;
    }

    private async Task<int> DeleteSingleAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string key,
        CancellationToken cancellationToken)
    {
        await DeleteObjectAsync(bucket, credential, key, cancellationToken);
        return 1;
    }

    private static string GetUniqueDownloadPath(string requestedPath, HashSet<string> reservedPaths)
    {
        if (!File.Exists(requestedPath) && !Directory.Exists(requestedPath) && reservedPaths.Add(requestedPath))
        {
            return requestedPath;
        }

        var directory = Path.GetDirectoryName(requestedPath)!;
        var extension = Path.GetExtension(requestedPath);
        var name = Path.GetFileNameWithoutExtension(requestedPath);
        for (var suffix = 2; ; suffix++)
        {
            var candidate = Path.Combine(directory, $"{name} ({suffix}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate) && reservedPaths.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool TryGetRedirectEndpoint(
        OSS.ServiceException exception,
        string currentEndpoint,
        out string endpoint,
        out string regionCode)
    {
        endpoint = string.Empty;
        regionCode = string.Empty;
        if (!exception.ErrorFields.TryGetValue("Endpoint", out var suggestedEndpoint))
        {
            return false;
        }

        endpoint = suggestedEndpoint.Trim();
        const string prefix = "oss-";
        const string suffix = ".aliyuncs.com";
        if (string.Equals(endpoint, currentEndpoint, StringComparison.OrdinalIgnoreCase) ||
            !endpoint.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !endpoint.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        regionCode = endpoint[prefix.Length..^suffix.Length];
        return regionCode.Length > 0;
    }

    private static string GetRelativeName(string key, string prefix)
        => key.StartsWith(prefix, StringComparison.Ordinal) ? key[prefix.Length..] : key;

    private static string FormatSize(long size)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, size);
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{value:0} {units[unitIndex]}" : $"{value:0.##} {units[unitIndex]}";
    }

    private static string GetTextContentType(string key)
    {
        return Path.GetExtension(key).ToLowerInvariant() switch
        {
            ".json" => "application/json; charset=utf-8",
            ".xml" => "application/xml; charset=utf-8",
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" or ".mjs" or ".cjs" => "text/javascript; charset=utf-8",
            ".csv" => "text/csv; charset=utf-8",
            ".svg" => "image/svg+xml; charset=utf-8",
            _ => "text/plain; charset=utf-8"
        };
    }
}

public sealed record OssObjectListResult(
    List<ObjectEntry> Objects,
    string Endpoint,
    string RegionCode,
    bool EndpointCorrected);
