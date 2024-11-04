namespace AzureUpload.Controllers;

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AzureUpload.Data;
using AzureUpload.Models.DTOs;
using AzureUpload.Models;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

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

    [HttpGet("azure-files")]
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
                    blobItem.Name,
                    properties.Value.ContentType ?? "application/octet-stream",
                    blobItem.Properties.ContentLength ?? 0,
                    properties.Value.LastModified.ToString("yyyy-MM-dd HH:mm:ss"),
                    blobClient.Uri.ToString()
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

    [HttpPost("upload")]
    public async Task<ActionResult<BlobItemResponse>> UploadFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file was provided");
        }

        // Check for video file types
        var videoContentTypes = new[] 
        {
            "video/mp4",
            "video/mpeg",
            "video/ogg",
            "video/webm",
            "video/x-msvideo",
            "video/quicktime"
        };

        if (videoContentTypes.Contains(file.ContentType.ToLower()))
        {
            return BadRequest("Video files are not allowed");
        }

        try
        {
            // Get current user ID from claims
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
            {
                return BadRequest("Invalid user ID");
            }

            // Sanitize filename and upload to blob storage
            var sanitizedFileName = SanitizeFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(sanitizedFileName))
            {
                return BadRequest("Invalid filename");
            }

            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(sanitizedFileName);

            // Check if blob already exists
            if (await blobClient.ExistsAsync())
            {
                return Conflict($"A file with name '{file.FileName}' already exists");
            }

            // Upload the file
            var blobHttpHeaders = new BlobHttpHeaders
            {
                ContentType = file.ContentType
            };

            await using var stream = file.OpenReadStream();
            await blobClient.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = blobHttpHeaders
            });

            // Get blob properties
            var properties = await blobClient.GetPropertiesAsync();

            // Create database record
            var storedFile = new StoredFile
            {
                FileName = file.FileName,
                BlobName = sanitizedFileName,
                ContentType = file.ContentType,
                Size = file.Length,
                UploadDate = DateTime.UtcNow,
                UserId = userId,
                AzureUri = blobClient.Uri.ToString()
            };

            await _context.Files.AddAsync(storedFile);
            await _context.SaveChangesAsync();
            
            return Ok(new BlobItemResponse(
                sanitizedFileName,
                properties.Value.ContentType,
                properties.Value.ContentLength,
                properties.Value.LastModified.ToString("yyyy-MM-dd HH:mm:ss"),
                blobClient.Uri.ToString()
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {FileName}", file.FileName);
            return StatusCode(500, "An unexpected error occurred while uploading the file");
        }
    }

    [HttpGet("my-files")]
    public async Task<ActionResult<IEnumerable<StoredFileResponse>>> GetMyFiles()
    {
        try
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
            {
                return BadRequest("Invalid user ID");
            }

            var files = await _context.Files
                .Where(f => f.UserId == userId && !f.IsDeleted)
                .OrderByDescending(f => f.UploadDate)
                .Select(f => new StoredFileResponse(
                    f.Id,
                    f.FileName,
                    f.BlobName,
                    f.ContentType,
                    f.Size,
                    f.UploadDate,
                    f.AzureUri
                ))
                .ToListAsync();

            return Ok(files);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user files");
            return StatusCode(500, "An error occurred while retrieving files");
        }
    }

    [HttpGet("audit-files")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<FileAuditResponse>> AuditFiles([FromQuery] bool cleanup = false)
    {
        try
        {
            var orphanedFiles = new List<OrphanedFileInfo>();
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            
            // Get all non-deleted files from database
            var dbFiles = await _context.Files
                .Where(f => !f.IsDeleted) 
                .Select(f => new { f.Id, f.BlobName })
                .ToDictionaryAsync(f => f.BlobName, f => f.Id);

            // Get all blobs from Azure
            var azureFiles = new HashSet<string>();
            await foreach (var blob in containerClient.GetBlobsAsync())
            {
                azureFiles.Add(blob.Name);
                
                // Check if blob exists in DB
                if (!dbFiles.ContainsKey(blob.Name))
                {
                    orphanedFiles.Add(new OrphanedFileInfo(
                        blob.Name,
                        "Azure",
                        blob.Properties.ContentLength ?? 0,
                        blob.Properties.LastModified?.DateTime ?? DateTime.UtcNow
                    ));
                }
            }

            // Check for non-deleted files in DB that don't exist in Azure
            foreach (var dbFile in dbFiles)
            {
                if (!azureFiles.Contains(dbFile.Key))
                {
                    orphanedFiles.Add(new OrphanedFileInfo(
                        dbFile.Key,
                        "Database",
                        0,
                        DateTime.UtcNow
                    ));
                }
            }

            // Cleanup if requested
            if (cleanup && orphanedFiles.Any())
            {
                foreach (var orphanedFile in orphanedFiles)
                {
                    if (orphanedFile.Location == "Azure")
                    {
                        // Delete from Azure
                        var blobClient = containerClient.GetBlobClient(orphanedFile.FileName);
                        await blobClient.DeleteIfExistsAsync();
                    }
                    else
                    {
                        // Delete from Database
                        var fileToDelete = await _context.Files
                            .FirstOrDefaultAsync(f => f.BlobName == orphanedFile.FileName);
                        if (fileToDelete != null)
                        {
                            _context.Files.Remove(fileToDelete);
                        }
                    }
                }

                await _context.SaveChangesAsync();
            }

            return Ok(new FileAuditResponse(
                orphanedFiles,
                orphanedFiles.Count,
                cleanup,
                DateTime.UtcNow
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during file audit");
            return StatusCode(500, "An error occurred while auditing files");
        }
    }

    [HttpDelete("files/{fileName}")]
    public async Task<IActionResult> DeleteFile(string fileName)
    {
        try
        {
            // Get current user ID from claims
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
            {
                return BadRequest("Invalid user ID");
            }

            // Find the file in database
            var storedFile = await _context.Files
                .FirstOrDefaultAsync(f => f.BlobName == fileName && f.UserId == userId);

            if (storedFile == null)
            {
                return NotFound("File not found");
            }

            if (storedFile.IsDeleted)
            {
                return BadRequest("File is already marked as deleted");
            }

            // Delete from Azure
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(fileName);

            if (await blobClient.ExistsAsync())
            {
                await blobClient.DeleteAsync();
            }

            // Mark as deleted in database
            storedFile.IsDeleted = true;
            storedFile.DeletedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "File {FileName} deleted by user {UserId} at {DeletedDate}", 
                fileName, userId, storedFile.DeletedDate);

            return Ok(new
            {
                Message = "File deleted successfully",
                FileName = storedFile.FileName,
                DeletedDate = storedFile.DeletedDate
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FileName}", fileName);
            return StatusCode(500, "An error occurred while deleting the file");
        }
    }

    [HttpPost("transfer-ownership")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<FileTransferResponse>> TransferFileOwnership(FileTransferRequest request)
    {
        try
        {
            if (request.Files.Count == 0)
            {
                return BadRequest("No files specified for transfer");
            }

            // Verify target user exists
            var targetUser = await _context.Users.FindAsync(request.NewUserId);
            if (targetUser == null)
            {
                return NotFound($"Target user with ID {request.NewUserId} not found");
            }

            // Find all specified files
            var filesToTransfer = await _context.Files
                .Where(f => request.Files.Contains(f.BlobName) && !f.IsDeleted)
                .ToListAsync();

            if (!filesToTransfer.Any())
            {
                return NotFound("None of the specified files were found");
            }

            // Track results
            var results = new List<FileTransferResult>();
            foreach (var fileName in request.Files)
            {
                var file = filesToTransfer.FirstOrDefault(f => f.BlobName == fileName);
                if (file == null)
                {
                    results.Add(new FileTransferResult(fileName, false, "File not found"));
                    continue;
                }

                // Transfer ownership
                file.UserId = request.NewUserId;
                results.Add(new FileTransferResult(fileName, true, "Ownership transferred successfully"));
            }

            await _context.SaveChangesAsync();

            return Ok(new FileTransferResponse(
                results,
                results.Count(r => r.Success),
                request.NewUserId,
                DateTime.UtcNow
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transferring file ownership to user {UserId}", request.NewUserId);
            return StatusCode(500, "An error occurred while transferring file ownership");
        }
    }

    [HttpGet("admin/file-inventory")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<FileInventoryResponse>> GetFileInventory()
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var inventory = new List<FileInventoryItem>();

            // Get all files from database, grouped by BlobName
            var dbFiles = await _context.Files
                .Include(f => f.User)
                .GroupBy(f => f.BlobName)
                .ToDictionaryAsync(
                    g => g.Key,
                    g => g.OrderByDescending(f => f.UploadDate).ToList() // Most recent first
                );

            // Track which files we've processed from Azure
            var processedBlobNames = new HashSet<string>();

            // Enumerate all blobs in Azure
            await foreach (var blobItem in containerClient.GetBlobsAsync())
            {
                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                var properties = await blobClient.GetPropertiesAsync();

                // Try to find matching database records
                dbFiles.TryGetValue(blobItem.Name, out var fileVersions);
                var currentVersion = fileVersions?.FirstOrDefault(); // Most recent version

                inventory.Add(new FileInventoryItem(
                    BlobName: blobItem.Name,
                    AzureUri: blobClient.Uri.ToString(),
                    ContentType: properties.Value.ContentType,
                    Size: properties.Value.ContentLength,
                    LastModifiedInAzure: properties.Value.LastModified.DateTime,
                    UploadName: currentVersion?.FileName,
                    UploadDate: currentVersion?.UploadDate,
                    IsDeleted: currentVersion?.IsDeleted ?? false,
                    DeletedDate: currentVersion?.DeletedDate,
                    Owner: currentVersion?.User == null ? null : new FileOwner(
                        currentVersion.User.Id,
                        currentVersion.User.Username
                    ),
                    Status: DetermineFileStatus(currentVersion, true),
                    VersionCount: fileVersions?.Count ?? 0,
                    HasDeletedVersions: fileVersions?.Any(f => f.IsDeleted) ?? false
                ));

                processedBlobNames.Add(blobItem.Name);
            }

            // Add files that exist in database but not in Azure
            foreach (var dbFile in dbFiles)
            {
                if (!processedBlobNames.Contains(dbFile.Key))
                {
                    var currentVersion = dbFile.Value.First(); // Most recent version
                    
                    // Skip if file is marked as deleted and not in Azure
                    if (currentVersion.IsDeleted)
                    {
                        continue;
                    }

                    inventory.Add(new FileInventoryItem(
                        BlobName: dbFile.Key,
                        AzureUri: currentVersion.AzureUri,
                        ContentType: currentVersion.ContentType,
                        Size: currentVersion.Size,
                        LastModifiedInAzure: null,
                        UploadName: currentVersion.FileName,
                        UploadDate: currentVersion.UploadDate,
                        IsDeleted: currentVersion.IsDeleted,
                        DeletedDate: currentVersion.DeletedDate,
                        Owner: new FileOwner(
                            currentVersion.User.Id,
                            currentVersion.User.Username
                        ),
                        Status: "Missing from Azure", 
                        VersionCount: dbFile.Value.Count,
                        HasDeletedVersions: dbFile.Value.Any(f => f.IsDeleted)
                    ));
                }
            }

            return Ok(new FileInventoryResponse(
                Files: inventory,
                TotalCount: inventory.Count,
                TotalSize: inventory.Sum(f => f.Size),
                ScanTime: DateTime.UtcNow
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file inventory");
            return StatusCode(500, "An error occurred while retrieving the file inventory");
        }
    }

    private static string DetermineFileStatus(StoredFile? currentVersion, bool existsInAzure)
    {
        if (currentVersion == null)
            return "Orphaned in Azure";
        if (!existsInAzure)
            return "Missing from Azure";
        if (currentVersion.IsDeleted)
            return "Marked as Deleted";
        return "Active";
    }

    private static string SanitizeFileName(string fileName)
    {
        // Remove invalid characters
        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat(new[] { '#', '%', '&', '{', '}', '\\', '<', '>', '*', '?', '/', ' ', '$', '!' })
            .ToArray();

        var sanitizedName = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        
        // Replace multiple consecutive underscores with a single one
        sanitizedName = string.Join("_", sanitizedName.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries));
        
        return sanitizedName.Trim('.');
    }
}
