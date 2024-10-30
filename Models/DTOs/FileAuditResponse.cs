namespace AzureUpload.Models.DTOs;

public record FileAuditResponse(
    IEnumerable<OrphanedFileInfo> OrphanedFiles,
    int TotalOrphaned,
    bool CleanupPerformed,
    DateTime AuditTime
);

public record OrphanedFileInfo(
    string FileName,
    string Location, // "Azure" or "Database"
    long Size,
    DateTime LastModified
);
