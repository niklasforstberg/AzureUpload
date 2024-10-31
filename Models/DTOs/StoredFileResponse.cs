namespace AzureUpload.Models.DTOs;

public record StoredFileResponse(
    Guid Id,
    string FileName,
    string BlobName,
    string ContentType,
    long Size,
    DateTime UploadDate
);
