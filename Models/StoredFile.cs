namespace AzureUpload.Models;

public class StoredFile
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string AzureUri { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime UploadDate { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedDate { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
