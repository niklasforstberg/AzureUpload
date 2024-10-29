namespace AzureUpload.Models.DTOs;

public record UserResponse(
    Guid Id,
    string Username,
    string Role
); 