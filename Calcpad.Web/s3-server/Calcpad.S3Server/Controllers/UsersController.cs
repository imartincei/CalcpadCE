using Calcpad.S3Server.Models;
using Calcpad.S3Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Calcpad.S3Server.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize(Policy = Program.AdminOnly)]
    public class UsersController(AuthService auth) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await auth.GetAllUsersAsync());

        [HttpGet("{userId}")]
        public async Task<IActionResult> Get(string userId)
        {
            var user = await auth.GetUserByIdAsync(userId);
            return user == null ? NotFound(new { error = "User not found" }) : Ok(user);
        }

        [HttpPut("{userId}")]
        public async Task<IActionResult> Update(string userId, [FromBody] UpdateUserRequest request)
        {
            if (request.Role is { } role && (!Enum.IsDefined(role) || role == UserRole.None))
                return BadRequest(new { error = "Invalid role" });
            if (request.Password is { Length: > 0 and < 8 })
                return BadRequest(new { error = "Password must be at least 8 characters" });

            return await auth.UpdateUserAsync(userId, request)
                ? Ok(await auth.GetUserByIdAsync(userId))
                : NotFound(new { error = "User not found" });
        }

        [HttpDelete("{userId}")]
        public async Task<IActionResult> Delete(string userId)
        {
            if (userId == User.FindFirst(AuthService.UserIdClaim)?.Value)
                return BadRequest(new { error = "You cannot delete your own account" });

            return await auth.DeleteUserAsync(userId) ? NoContent() : NotFound(new { error = "User not found" });
        }
    }
}
