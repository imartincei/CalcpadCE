namespace Calcpad.S3Server.Models
{
    public enum BucketKind
    {
        Library = 1,
        Project = 2
    }

    public class Bucket
    {
        public string Name { get; set; } = string.Empty;
        public BucketKind Kind { get; set; } = BucketKind.Library;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Overrides a user's global role on one bucket.</summary>
    public class BucketGrant
    {
        public string UserId { get; set; } = string.Empty;
        public string BucketName { get; set; } = string.Empty;
        public UserRole Role { get; set; }
    }
}
