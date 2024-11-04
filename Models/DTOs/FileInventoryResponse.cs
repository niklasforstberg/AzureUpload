namespace AzureUpload.Models.DTOs;

public record FileInventoryResponse(
    List<FileInventoryItem> Files,
    int TotalCount,
    long TotalSize,
    DateTime ScanTime
);

public record FileInventoryItem(
    string BlobName,
    string AzureUri,
    string ContentType,
    long Size,
    DateTime? LastModifiedInAzure,
    string? UploadName,
    DateTime? UploadDate,
    bool IsDeleted,
    DateTime? DeletedDate,
    FileOwner? Owner,
    string Status,
    int VersionCount,
    bool HasDeletedVersions
);

public record FileOwner(
    Guid Id,
    string Username
);
