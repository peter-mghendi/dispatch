using Amazon.S3;
using Amazon.S3.Model;
using static System.Net.Mime.MediaTypeNames;

namespace Dispatch.Server.Services;

public class HandlerStorage(IAmazonS3 s3)
{
    private const string BucketName = "dispatch";

    public async Task UploadAsync(string hash, Stream input, CancellationToken cancellation = default)
    {
        var request = new PutObjectRequest {
            BucketName = BucketName,
            ContentType = Application.Wasm,
            InputStream = input,
            Key = Key(hash)
        };

        await s3.PutObjectAsync(request, cancellation).ConfigureAwait(false);
    }

    public async Task<Stream> DownloadAsync(string hash, CancellationToken cancellation = default)
    {
        var request = new GetObjectRequest { BucketName = BucketName, Key = Key(hash) };
        var response = await s3.GetObjectAsync(request, cancellation);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(string hash, CancellationToken cancellation = default)
    {
        await s3.DeleteObjectAsync(BucketName, Key(hash), cancellation);
    }

    private static string Key(string hash) => $"handlers/{hash}.wasm";
}