public record AdminChangePasswordRequest(
    Guid UserId,
    string NewPassword
);
