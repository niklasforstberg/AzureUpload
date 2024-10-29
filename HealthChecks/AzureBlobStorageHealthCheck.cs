using Microsoft.Extensions.Diagnostics.HealthChecks;
using Azure.Storage.Blobs;

namespace AzureUpload.HealthChecks;

public class AzureBlobStorageHealthCheck : IHealthCheck
{
    private readonly BlobServiceClient _blobServiceClient;

    public AzureBlobStorageHealthCheck(BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var accountInfo = await _blobServiceClient.GetAccountInfoAsync(cancellationToken);
            return HealthCheckResult.Healthy($"Connected to Azure Storage. Account Kind: {accountInfo.Value.AccountKind}, SKU Name: {accountInfo.Value.SkuName}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Failed to connect to Azure Storage: {ex.Message}");
        }
    }
}