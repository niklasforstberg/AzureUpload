namespace AzureUpload.Models.DTOs;

public record BlobItemResponse(
    string Name,
    string ContentType,
    long Size,
    string LastModified,
    string Uri
);
