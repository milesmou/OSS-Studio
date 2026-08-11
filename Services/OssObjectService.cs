using OSS = AlibabaCloud.OSS.V2;
using OSSStudio.Models;
using System.Text;

namespace OSSStudio.Services;

public sealed class OssObjectService
{
    public async Task<int> UploadPathsAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string prefix,
        IReadOnlyList<string> paths,
        IProgress<(int Current, int Total, string Name)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPrefix = string.IsNullOrEmpty(prefix) || prefix.EndsWith('/') ? prefix : $"{prefix}/";
        var uploads = new List<(string FilePath, string Key)>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
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
                var relativePath = Path.GetRelativePath(directory, filePath).Replace(Path.DirectorySeparatorChar, '/');
                uploads.Add((filePath, $"{normalizedPrefix}{directoryName}/{relativePath}"));
            }
        }

        using var client = CreateClient(bucket, credential);
        for (var index = 0; index < uploads.Count; index++)
        {
            var upload = uploads[index];
            progress?.Report((index + 1, uploads.Count, upload.Key));
            var request = new OSS.Models.PutObjectRequest
            {
                Bucket = bucket.Name,
                Key = upload.Key
            };
            AddRequesterPaysHeader(request, bucket);
            await client.PutObjectFromFileAsync(
                request,
                upload.FilePath,
                cancellationToken: cancellationToken);
        }

        return uploads.Count;
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
    {
        using var client = CreateClient(bucket, credential);
        var objects = await ListObjectSummariesAsync(client, bucket, prefix, cancellationToken);
        var folderName = SanitizePathSegment(prefix.TrimEnd('/').Split('/').LastOrDefault() ?? bucket.Name);
        var targetRoot = Path.GetFullPath(Path.Combine(destinationDirectory, folderName));
        Directory.CreateDirectory(targetRoot);

        var downloaded = 0;
        foreach (var item in objects)
        {
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

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var request = new OSS.Models.GetObjectRequest { Bucket = bucket.Name, Key = key };
            AddRequesterPaysHeader(request, bucket);
            await client.GetObjectToFileAsync(request, filePath, cancellationToken: cancellationToken);
            downloaded++;
            progress?.Report((downloaded, objects.Count, relativeKey));
        }

        return downloaded;
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
