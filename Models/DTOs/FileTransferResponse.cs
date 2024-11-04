namespace AzureUpload.Models.DTOs;

public record FileTransferResponse(
    List<FileTransferResult> Results,
    int SuccessCount,
    Guid NewUserId,
    DateTime TransferDate
);

public record FileTransferResult(
    string FileName,
    bool Success,
    string Message
);
