using Amazon.S3;
using Calcpad.Core;
using Calcpad.S3Server.Data;
using Calcpad.S3Server.Models;
using Calcpad.S3Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Calcpad.S3Server.Controllers
{
    [ApiController]
    [Route("api/buckets")]
    [Authorize]
    public class BucketsController(S3ServerDbContext db, IObjectStore store, BucketAccess access) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> List()
        {
            var result = new List<BucketDto>();
            foreach (var bucket in await db.Buckets.OrderBy(b => b.Name).ToListAsync())
            {
                var role = await access.GetRoleAsync(User, bucket.Name);
                if (role >= UserRole.Viewer)
                    result.Add(new BucketDto(bucket.Name, bucket.Kind, bucket.CreatedBy, bucket.CreatedAt.ToString("O"), role));
            }
            return Ok(result);
        }

        [HttpPost]
        [Authorize(Policy = Program.AdminOnly)]
        public async Task<IActionResult> Create([FromBody] CreateBucketRequest request)
        {
            if (!PathRoots.IsValidBucketName(request.Name))
                return BadRequest(new { error = "Bucket names are 3-63 lowercase letters, digits, dots or hyphens." });
            if (!Enum.IsDefined(request.Kind))
                return BadRequest(new { error = "Invalid bucket kind" });
            if (await db.Buckets.FindAsync(request.Name) is not null)
                return Conflict(new { error = "Bucket already exists" });

            try
            {
                await store.CreateBucketAsync(request.Name);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
            {
                // Re-registering a bucket that exists in Garage but not in the database.
            }

            var bucket = new Bucket
            {
                Name = request.Name,
                Kind = request.Kind,
                CreatedBy = User.FindFirst("username")?.Value ?? string.Empty
            };
            db.Buckets.Add(bucket);
            await db.SaveChangesAsync();
            return Created($"/api/buckets/{bucket.Name}",
                new BucketDto(bucket.Name, bucket.Kind, bucket.CreatedBy, bucket.CreatedAt.ToString("O"), UserRole.Admin));
        }

        [HttpDelete("{bucket}")]
        [Authorize(Policy = Program.AdminOnly)]
        public async Task<IActionResult> Delete(string bucket)
        {
            var entry = await db.Buckets.FindAsync(bucket);
            if (entry is null)
                return NotFound(new { error = "Bucket not found" });

            try
            {
                await store.DeleteBucketAsync(bucket);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "BucketNotEmpty")
            {
                return Conflict(new { error = "Bucket is not empty" });
            }
            db.Buckets.Remove(entry);
            db.BucketGrants.RemoveRange(db.BucketGrants.Where(g => g.BucketName == bucket));
            await db.SaveChangesAsync();
            return NoContent();
        }

        [HttpGet("{bucket}/grants")]
        [Authorize(Policy = Program.AdminOnly)]
        public async Task<IActionResult> Grants(string bucket) =>
            Ok(await db.BucketGrants.Where(g => g.BucketName == bucket).ToListAsync());

        [HttpPut("{bucket}/grants")]
        [Authorize(Policy = Program.AdminOnly)]
        public async Task<IActionResult> SetGrant(string bucket, [FromBody] GrantRequest request)
        {
            if (await db.Buckets.FindAsync(bucket) is null)
                return NotFound(new { error = "Bucket not found" });
            if (await db.Users.FindAsync(request.UserId) is null)
                return NotFound(new { error = "User not found" });
            if (!Enum.IsDefined(request.Role))
                return BadRequest(new { error = "Invalid role" });

            var grant = await db.BucketGrants.FindAsync(request.UserId, bucket);
            if (grant is null)
                db.BucketGrants.Add(new BucketGrant { UserId = request.UserId, BucketName = bucket, Role = request.Role });
            else
                grant.Role = request.Role;
            await db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{bucket}/grants/{userId}")]
        [Authorize(Policy = Program.AdminOnly)]
        public async Task<IActionResult> RemoveGrant(string bucket, string userId)
        {
            var grant = await db.BucketGrants.FindAsync(userId, bucket);
            if (grant is null)
                return NotFound();

            db.BucketGrants.Remove(grant);
            await db.SaveChangesAsync();
            return NoContent();
        }
    }
}
