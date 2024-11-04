namespace AzureUpload.Models.DTOs;

public record FileTransferRequest(
    Guid NewUserId,
    List<string> Files
);
