using Calcpad.S3Server.Data;
using Calcpad.S3Server.Models;
using Calcpad.S3Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Calcpad.S3Server.Controllers
{
    [ApiController]
    [Route("api/buckets/{bucket}")]
    [Authorize]
    public class ObjectsController(S3ServerDbContext db, ObjectService objects, BucketAccess access) : ControllerBase
    {
        private const long MaxUploadBytes = 100 * 1024 * 1024;

        [HttpGet("objects")]
        public async Task<IActionResult> List(string bucket, [FromQuery] string? prefix, [FromQuery] bool versions = false) =>
            await Authorize(bucket, UserRole.Viewer)
            ?? Ok(await objects.ListAsync(bucket, prefix, versions, HttpContext.RequestAborted));

        [HttpGet("meta/{**key}")]
        public async Task<IActionResult> Meta(string bucket, string key) =>
            await Authorize(bucket, UserRole.Viewer)
            ?? await Guarded(async () => await objects.HeadAsync(bucket, key) is { } info ? Ok(info) : NotFound());

        /// <summary>The latest version; <c>If-None-Match: {id}</c> returns 304 when it hasn't changed.</summary>
        [HttpGet("objects/{**key}")]
        public async Task<IActionResult> Get(string bucket, string key) =>
            await Authorize(bucket, UserRole.Viewer)
            ?? await Guarded(async () =>
            {
                var info = await objects.HeadAsync(bucket, key, HttpContext.RequestAborted);
                if (info is null)
                    return NotFound();
                if (Request.Headers.IfNoneMatch.ToString().Trim('"') is { Length: > 0 } seen
                    && string.Equals(seen, info.Id, StringComparison.OrdinalIgnoreCase))
                {
                    WriteHeaders(info);
                    return StatusCode(StatusCodes.Status304NotModified);
                }
                return await Content(bucket, info);
            });

        /// <summary>
        /// Stores a new version. <c>If-Match: {id}</c> names the version the upload is based on
        /// (<c>*</c> overwrites); without it the id in a worksheet's footer is used.
        /// </summary>
        [HttpPut("objects/{**key}")]
        [RequestSizeLimit(MaxUploadBytes)]
        public async Task<IActionResult> Put(string bucket, string key) =>
            await Authorize(bucket, UserRole.Contributor)
            ?? await Guarded(async () =>
            {
                using var ms = new MemoryStream();
                await Request.Body.CopyToAsync(ms, HttpContext.RequestAborted);
                var expected = Request.Headers.IfMatch.ToString().Trim('"');
                var user = User.FindFirst("username")?.Value ?? "unknown";
                try
                {
                    var result = await objects.UploadAsync(bucket, key, ms.ToArray(),
                        expected.Length == 0 ? null : expected, user, HttpContext.RequestAborted);
                    return Ok(result.Info);
                }
                catch (UploadConflictException ex)
                {
                    return Conflict(new { error = ex.Message });
                }
            });

        [HttpDelete("objects/{**key}")]
        public async Task<IActionResult> Delete(string bucket, string key) =>
            await Authorize(bucket, UserRole.Contributor)
            ?? await Guarded(async () => await objects.DeleteAsync(bucket, key) ? NoContent() : NotFound());

        /// <summary>The version an id or baseId names, searched under <paramref name="prefix"/>.</summary>
        [HttpGet("ids/{id:guid}")]
        public async Task<IActionResult> FindById(string bucket, Guid id, [FromQuery] string? prefix) =>
            await Authorize(bucket, UserRole.Viewer)
            ?? (await objects.FindByIdAsync(bucket, id, prefix, HttpContext.RequestAborted) is { } info ? Ok(info) : NotFound());

        [HttpGet("ids/{id:guid}/content")]
        public async Task<IActionResult> ContentById(string bucket, Guid id, [FromQuery] string? prefix) =>
            await Authorize(bucket, UserRole.Viewer)
            ?? (await objects.FindByIdAsync(bucket, id, prefix, HttpContext.RequestAborted) is { } info
                ? await Content(bucket, info)
                : NotFound());

        [HttpGet("ids/{baseId:guid}/versions")]
        public async Task<IActionResult> Versions(string bucket, Guid baseId) =>
            await Authorize(bucket, UserRole.Viewer)
            ?? Ok(await objects.ListVersionsAsync(bucket, baseId, HttpContext.RequestAborted));

        private async Task<IActionResult> Content(string bucket, ObjectInfo info)
        {
            var bytes = await objects.GetContentAsync(bucket, info, HttpContext.RequestAborted);
            if (bytes is null)
                return NotFound();
            WriteHeaders(info);
            return File(bytes, "application/octet-stream");
        }

        private void WriteHeaders(ObjectInfo info)
        {
            Response.Headers.ETag = $"\"{info.Id}\"";
            Response.Headers["X-Calcpad-Key"] = Uri.EscapeDataString(info.Key);
            Response.Headers["X-Calcpad-Id"] = info.Id;
            Response.Headers["X-Calcpad-Base-Id"] = info.BaseId;
            Response.Headers["X-Calcpad-Version"] = info.Version.ToString();
            Response.Headers["X-Calcpad-Latest"] = info.Latest ? "true" : "false";
        }

        private async Task<IActionResult?> Authorize(string bucket, UserRole required)
        {
            if (await db.Buckets.FindAsync(bucket) is null)
                return NotFound(new { error = $"Bucket '{bucket}' not found" });
            return await access.CanAsync(User, bucket, required) ? null : Forbid();
        }

        private async Task<IActionResult> Guarded(Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
