namespace AzureUpload.Models;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    // Role should always be uppercase ("ADMIN" or "USER")
    public string Role { get; set; } = "USER";
    public List<StoredFile> Files { get; set; } = new();
}
