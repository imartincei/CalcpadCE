using Calcpad.S3Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Calcpad.S3Server.Data
{
    public class S3ServerDbContext(DbContextOptions<S3ServerDbContext> options) : DbContext(options)
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Bucket> Buckets => Set<Bucket>();
        public DbSet<BucketGrant> BucketGrants => Set<BucketGrant>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("Users");
                entity.HasKey(u => u.Id);
                entity.HasIndex(u => u.Username).IsUnique();
                entity.HasIndex(u => u.Email).IsUnique();
            });

            modelBuilder.Entity<Bucket>(entity =>
            {
                entity.ToTable("Buckets");
                entity.HasKey(b => b.Name);
            });

            modelBuilder.Entity<BucketGrant>(entity =>
            {
                entity.ToTable("BucketGrants");
                entity.HasKey(g => new { g.UserId, g.BucketName });
            });
        }
    }
}
