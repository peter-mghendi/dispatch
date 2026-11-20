using System.Collections.Concurrent;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Caching.Memory;
using static System.Net.Mime.MediaTypeNames;

namespace Dispatch.Server.Services;

public sealed class HandlerStorage(IAmazonS3 store, IMemoryCache cache)
{
    private const string BucketName = "dispatch";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _downloads = new();

    public async Task StoreAsync(byte[] bytes, CancellationToken cancellation = default)
    {
        using var input = new MemoryStream(bytes, writable: false);
        var hash = Hash(bytes);
        var request = new PutObjectRequest {
            BucketName = BucketName,
            ContentType = Application.Wasm,
            InputStream = input,
            Key = Key(hash)
        };

        await store.PutObjectAsync(request, cancellation).ConfigureAwait(false);
    }

    public async Task<byte[]> FetchAsync(string hash, CancellationToken cancellation = default)
    {
        if (cache.TryGetValue(Key(hash), out byte[]? cached) && cached is not null) return cached;

        // The shared download ignores any one caller's cancellation; each caller only stops waiting.
        return await _downloads.GetOrAdd(hash, key => new(() => DownloadAsync(key))).Value.WaitAsync(cancellation);
    }

    public async Task DeleteAsync(string hash, CancellationToken cancellation = default)
    {
        await store.DeleteObjectAsync(BucketName, Key(hash), cancellation);
        cache.Remove(Key(hash));
    }

    private async Task<byte[]> DownloadAsync(string hash)
    {
        var key = Key(hash);

        try
        {
            using var response = await store.GetObjectAsync(BucketName, key);
            using var memory = new MemoryStream(capacity: (int)response.ContentLength);

            await response.ResponseStream.CopyToAsync(memory);
            var bytes = memory.ToArray();

            if (Hash(bytes) != hash) throw new InvalidDataException($"Stored module '{key}' does not match its hash.");

            var options = new MemoryCacheEntryOptions { Size = bytes.Length, SlidingExpiration = CacheLifetime };
            return cache.Set(key, bytes, options);
        }
        finally
        {
            _downloads.TryRemove(hash, out _);
        }
    }

    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA3_256.HashData(bytes));

    private static string Key(string hash) => $"handlers/{hash}.wasm";
}
