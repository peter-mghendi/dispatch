using System.Collections.Concurrent;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Caching.Memory;
using static System.Net.Mime.MediaTypeNames;

namespace Dispatch.Server.Services;

public sealed class HandlerStorage(IAmazonS3 s3, IMemoryCache cache)
{
    private const string BucketName = "dispatch";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _downloads = new();

    public async Task StoreAsync(string hash, Stream input, CancellationToken cancellation = default)
    {
        var request = new PutObjectRequest {
            BucketName = BucketName,
            ContentType = Application.Wasm,
            InputStream = input,
            Key = Key(hash)
        };

        await s3.PutObjectAsync(request, cancellation).ConfigureAwait(false);
    }

    public async Task<byte[]> FetchAsync(string hash, CancellationToken cancellation = default)
    {
        if (cache.TryGetValue(Key(hash), out byte[]? cached) && cached is not null) return cached;

        // The shared download ignores any one caller's cancellation; each caller only stops waiting.
        return await _downloads.GetOrAdd(hash, key => new(() => DownloadAsync(key))).Value.WaitAsync(cancellation);
    }

    public async Task DeleteAsync(string hash, CancellationToken cancellation = default)
    {
        await s3.DeleteObjectAsync(BucketName, Key(hash), cancellation);
        cache.Remove(Key(hash));
    }

    private async Task<byte[]> DownloadAsync(string hash)
    {
        try
        {
            using var response = await s3.GetObjectAsync(BucketName, Key(hash));
            using var memory = new MemoryStream(capacity: (int)response.ContentLength);
            await response.ResponseStream.CopyToAsync(memory);
            var bytes = memory.ToArray();

            cache.Set(Key(hash), bytes, new MemoryCacheEntryOptions { Size = bytes.Length, SlidingExpiration = CacheLifetime });
            return bytes;
        }
        finally
        {
            _downloads.TryRemove(hash, out _);
        }
    }

    private static string Key(string hash) => $"handlers/{hash}.wasm";
}
