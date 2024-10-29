namespace AzureUpload.Controllers;

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AzureUpload.Data;
using AzureUpload.Models.DTOs;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "UserAccess")]
public class StorageController : ControllerBase
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<StorageController> _logger;
    private readonly string _containerName;

    public StorageController(
        BlobServiceClient blobServiceClient,
        ApplicationDbContext context,
        ILogger<StorageController> logger,
        IConfiguration configuration)
    {
        _blobServiceClient = blobServiceClient;
        _context = context;
        _logger = logger;
        _containerName = configuration["AzureStorage:ContainerName"] 
            ?? throw new ArgumentNullException("AzureStorage:ContainerName configuration is required");
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BlobItemResponse>>> ListFiles()
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            
            // Ensure container exists
            await containerClient.CreateIfNotExistsAsync();

            var blobs = new List<BlobItemResponse>();
            await foreach (var blobItem in containerClient.GetBlobsAsync())
            {
                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                var properties = await blobClient.GetPropertiesAsync();
                
                blobs.Add(new BlobItemResponse(
                    Name: blobItem.Name,
                    ContentType: properties.Value.ContentType ?? "application/octet-stream",
                    Size: blobItem.Properties.ContentLength ?? 0,
                    LastModified: properties.Value.LastModified.ToString("yyyy-MM-dd HH:mm:ss"),
                    Uri: blobClient.Uri.ToString()
                ));
            }

            return Ok(blobs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files from blob storage");
            return StatusCode(500, "An error occurred while retrieving the file list");
        }
    }
}
