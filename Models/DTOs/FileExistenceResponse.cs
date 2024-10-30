namespace AzureUpload.Models.DTOs;

public record FileExistenceResponse(
    string FileName,
    bool ExistsInDatabase,
    bool ExistsInStorage,
    string ContentType,
    long Size,
    DateTime UploadDate,
    string? Uri
);
