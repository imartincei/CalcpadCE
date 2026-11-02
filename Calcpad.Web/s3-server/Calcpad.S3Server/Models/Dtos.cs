namespace Calcpad.S3Server.Models
{
    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class RegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public UserRole? Role { get; set; }
    }

    public class UpdateUserRequest
    {
        public UserRole? Role { get; set; }
        public bool? IsActive { get; set; }
        public string? Password { get; set; }
    }

    public class AuthResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new();
        public string ExpiresAt { get; set; } = string.Empty;
    }

    public class UserDto
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string? LastLoginAt { get; set; }
        public bool IsActive { get; set; }

        public static UserDto FromUser(User user) => new()
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role,
            CreatedAt = user.CreatedAt.ToString("O"),
            LastLoginAt = user.LastLoginAt?.ToString("O"),
            IsActive = user.IsActive
        };
    }

    public class CreateBucketRequest
    {
        public string Name { get; set; } = string.Empty;
        public BucketKind Kind { get; set; } = BucketKind.Library;
    }

    public record BucketDto(string Name, BucketKind Kind, string CreatedBy, string CreatedAt, UserRole Access);

    public class GrantRequest
    {
        public string UserId { get; set; } = string.Empty;
        public UserRole Role { get; set; }
    }

    /// <summary>
    /// One stored version. <see cref="Key"/> is the logical path; archived versions live at
    /// <see cref="StorageKey"/> under <c>.versions/</c>.
    /// </summary>
    public record ObjectInfo(
        string Key,
        string StorageKey,
        bool Latest,
        string? BaseId,
        string? Id,
        int Version,
        long Size,
        string? Sha256,
        string? Title,
        string? Author,
        string? UpdatedBy,
        string? UpdatedAt);
}
