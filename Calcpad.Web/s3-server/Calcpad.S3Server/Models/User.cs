namespace Calcpad.S3Server.Models
{
    public enum UserRole
    {
        /// <summary>Only meaningful as a bucket grant, where it denies access.</summary>
        None = 0,
        Viewer = 1,
        Contributor = 2,
        Admin = 3
    }

    public class User
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public UserRole Role { get; set; } = UserRole.Contributor;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
