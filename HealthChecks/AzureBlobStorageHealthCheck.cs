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
            // Get account info
            var accountInfo = await _blobServiceClient.GetAccountInfoAsync(cancellationToken);
            
            // Get service properties
            var properties = await _blobServiceClient.GetPropertiesAsync(cancellationToken);
            
            // List first few containers (limited to 5 for performance)
            var containers = _blobServiceClient.GetBlobContainers()
                .Take(5)
                .Select(c => c.Name)
                .ToList();

            var data = new Dictionary<string, object>
            {
                { "AccountKind", accountInfo.Value.AccountKind },
                { "SkuName", accountInfo.Value.SkuName },
                { "ApiVersion", properties.Value.DefaultServiceVersion },
                { "StaticWebsiteEnabled", properties.Value.StaticWebsite?.Enabled ?? false },
                { "AvailableContainers", containers },
                { "ConnectionTestTime", DateTime.UtcNow }
            };

            return HealthCheckResult.Healthy("Azure Blob Storage connection is healthy", data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Failed to connect to Azure Storage", 
                exception: ex,
                data: new Dictionary<string, object>
                {
                    { "ErrorTime", DateTime.UtcNow },
                    { "ErrorType", ex.GetType().Name },
                    { "ErrorDetails", ex.Message }
                });
        }
    }
}