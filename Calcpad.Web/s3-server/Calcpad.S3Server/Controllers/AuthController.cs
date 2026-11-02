using Calcpad.S3Server.Models;
using Calcpad.S3Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Calcpad.S3Server.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController(AuthService auth) : ControllerBase
    {
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { error = "Username and password are required" });

            var result = await auth.LoginAsync(request);
            return result == null ? Unauthorized(new { error = "Invalid username or password" }) : Ok(result);
        }

        [HttpPost("register")]
        [Authorize(Policy = Program.AdminOnly)]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length is < 3 or > 30)
                return BadRequest(new { error = "Username must be 3-30 characters" });
            if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
                return BadRequest(new { error = "Valid email is required" });
            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
                return BadRequest(new { error = "Password must be at least 8 characters" });
            if (request.Role is { } role && (!Enum.IsDefined(role) || role == UserRole.None))
                return BadRequest(new { error = "Invalid role" });

            var user = await auth.RegisterAsync(request);
            return user == null
                ? Conflict(new { error = "Username or email already exists" })
                : Created($"/api/users/{user.Id}", user);
        }

        [HttpGet("profile")]
        [Authorize]
        public async Task<IActionResult> GetProfile()
        {
            var userId = User.FindFirst(AuthService.UserIdClaim)?.Value;
            if (userId == null)
                return Unauthorized();

            var user = await auth.GetUserByIdAsync(userId);
            return user == null ? NotFound(new { error = "User not found" }) : Ok(user);
        }
    }
}
