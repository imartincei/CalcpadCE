using System.Security.Claims;
using Calcpad.S3Server.Data;
using Calcpad.S3Server.Models;

namespace Calcpad.S3Server.Services
{
    /// <summary>
    /// A user's role on a bucket: global admins are always admins, otherwise a bucket grant
    /// overrides the global role.
    /// </summary>
    public class BucketAccess(S3ServerDbContext db)
    {
        public async Task<UserRole> GetRoleAsync(ClaimsPrincipal principal, string bucket)
        {
            var userId = principal.FindFirst(AuthService.UserIdClaim)?.Value;
            if (userId is null)
                return UserRole.None;

            var user = await db.Users.FindAsync(userId);
            if (user is null || !user.IsActive)
                return UserRole.None;

            return Resolve(user.Role, await db.BucketGrants.FindAsync(userId, bucket));
        }

        public static UserRole Resolve(UserRole globalRole, BucketGrant? grant) =>
            globalRole == UserRole.Admin ? UserRole.Admin : grant?.Role ?? globalRole;

        public async Task<bool> CanAsync(ClaimsPrincipal principal, string bucket, UserRole required) =>
            await GetRoleAsync(principal, bucket) >= required;
    }
}
