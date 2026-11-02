using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Calcpad.S3Server.Data;
using Calcpad.S3Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Calcpad.S3Server.Services
{
    public class JwtOptions
    {
        public string Secret { get; set; } = string.Empty;
        public string Issuer { get; set; } = "CalcpadS3";
        public string Audience { get; set; } = "CalcpadS3Client";
        public int ExpiryHours { get; set; } = 24;

        public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(Secret));
    }

    public class AuthService(S3ServerDbContext db, JwtOptions jwt)
    {
        public const string UserIdClaim = "userId";
        public const string RoleClaim = "role";

        public async Task<AuthResponse?> LoginAsync(LoginRequest request)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
            if (user == null || !user.IsActive || !BCrypt.Net.BCrypt.EnhancedVerify(request.Password, user.PasswordHash))
                return null;

            user.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return CreateAuthResponse(user);
        }

        public async Task<UserDto?> RegisterAsync(RegisterRequest request)
        {
            if (await db.Users.AnyAsync(u => u.Username == request.Username || u.Email == request.Email))
                return null;

            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = HashPassword(request.Password),
                Role = request.Role ?? UserRole.Contributor
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return UserDto.FromUser(user);
        }

        /// <summary>Creates the first admin from config when the store is empty.</summary>
        public async Task SeedAdminAsync(string? username, string? password, ILogger logger)
        {
            if (await db.Users.AnyAsync())
                return;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("No users exist and Admin__Username/Admin__Password are not set, so nobody can sign in.");
                return;
            }
            db.Users.Add(new User
            {
                Username = username,
                Email = $"{username}@localhost",
                PasswordHash = HashPassword(password),
                Role = UserRole.Admin
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded admin user {Username}", username);
        }

        public async Task<UserDto?> GetUserByIdAsync(string id)
        {
            var user = await db.Users.FindAsync(id);
            return user == null ? null : UserDto.FromUser(user);
        }

        public async Task<List<UserDto>> GetAllUsersAsync() =>
            (await db.Users.OrderBy(u => u.Username).ToListAsync()).ConvertAll(UserDto.FromUser);

        public async Task<bool> UpdateUserAsync(string id, UpdateUserRequest request)
        {
            var user = await db.Users.FindAsync(id);
            if (user == null)
                return false;

            if (request.Role.HasValue)
                user.Role = request.Role.Value;
            if (request.IsActive.HasValue)
                user.IsActive = request.IsActive.Value;
            if (!string.IsNullOrEmpty(request.Password))
                user.PasswordHash = HashPassword(request.Password);
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteUserAsync(string id)
        {
            var user = await db.Users.FindAsync(id);
            if (user == null)
                return false;

            db.Users.Remove(user);
            db.BucketGrants.RemoveRange(db.BucketGrants.Where(g => g.UserId == id));
            await db.SaveChangesAsync();
            return true;
        }

        private static string HashPassword(string password) =>
            BCrypt.Net.BCrypt.EnhancedHashPassword(password, 12);

        private AuthResponse CreateAuthResponse(User user)
        {
            var expiresAt = DateTime.UtcNow.AddHours(jwt.ExpiryHours);
            var token = new JwtSecurityToken(
                issuer: jwt.Issuer,
                audience: jwt.Audience,
                claims:
                [
                    new Claim(UserIdClaim, user.Id),
                    new Claim("username", user.Username),
                    new Claim(RoleClaim, ((int)user.Role).ToString())
                ],
                expires: expiresAt,
                signingCredentials: new SigningCredentials(jwt.SigningKey, SecurityAlgorithms.HmacSha256));

            return new AuthResponse
            {
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                User = UserDto.FromUser(user),
                ExpiresAt = expiresAt.ToString("O")
            };
        }
    }
}
