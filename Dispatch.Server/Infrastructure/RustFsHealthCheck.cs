using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Dispatch.Server.Infrastructure;

public sealed class RustFsHealthCheck(IAmazonS3 s3) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = "dispatch", MaxKeys = 1 }, cts.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("RustFS probe failed.", ex);
        }
    }
}