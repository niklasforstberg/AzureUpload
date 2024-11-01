public record AdminChangeUsernameRequest(
    Guid UserId,
    string NewUsername
);
