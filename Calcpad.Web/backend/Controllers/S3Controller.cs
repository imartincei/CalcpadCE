using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Calcpad.Core;
using Calcpad.Server.Services.S3;
using Microsoft.AspNetCore.Mvc;

namespace Calcpad.Server.Controllers
{
    /// <summary>
    /// The frontend's way to the S3 server: sign-in, uploads, and a pass-through for everything
    /// else, so the session token stays in this process.
    /// </summary>
    [ApiController]
    [Route("api/s3")]
    public class S3Controller(S3Session session, S3RemoteFileProvider provider) : ControllerBase
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public sealed record SessionRequest(string Url, string Username, string Password);
        public sealed record UploadRequest(string Bucket, string Key, string Content, string? SourceFilePath, bool Force);

        [HttpGet("session")]
        public IActionResult GetSession() => Ok(SessionInfo());

        [HttpPost("session")]
        public async Task<IActionResult> StartSession([FromBody] SessionRequest request)
        {
            Uri baseUrl;
            try
            {
                baseUrl = S3Session.NormalizeBaseUrl(request.Url);
            }
            catch (Exception ex) when (ex is UriFormatException or ArgumentException)
            {
                return BadRequest(new { error = ex.Message });
            }

            try
            {
                using var response = await S3Session.Http.PostAsJsonAsync(new Uri(baseUrl, "api/auth/login"),
                    new { request.Username, request.Password }, HttpContext.RequestAborted);
                if (!response.IsSuccessStatusCode)
                    return await Relay(response);

                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(HttpContext.RequestAborted));
                var token = json.RootElement.GetProperty("token").GetString();
                if (string.IsNullOrEmpty(token))
                    return StatusCode(StatusCodes.Status502BadGateway, new { error = "The S3 server returned no token." });

                session.Set(baseUrl, token, json.RootElement.GetProperty("user"));
                return Ok(SessionInfo());
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = $"Could not reach {baseUrl}: {ex.Message}" });
            }
        }

        [HttpDelete("session")]
        public IActionResult EndSession()
        {
            session.Clear();
            return Ok(SessionInfo());
        }

        /// <summary>
        /// Uploads worksheet text as a new version and returns it with the footer the server stamped,
        /// so the editor can take the new baseId/id/version.
        /// </summary>
        [HttpPost("upload")]
        public async Task<IActionResult> Upload([FromBody] UploadRequest request)
        {
            if (!session.IsConnected)
                return NotSignedIn();

            byte[]? original = null;
            if (FileMetadata.IsCompiled(request.Key) && request.SourceFilePath is { } source
                && FileMetadata.IsCompiled(source) && System.IO.File.Exists(source))
                original = await System.IO.File.ReadAllBytesAsync(source, HttpContext.RequestAborted);
            var body = FileMetadata.EncodeText(request.Content, request.Key, original);

            using var upload = session.CreateRequest(HttpMethod.Put, $"buckets/{request.Bucket}/objects/{S3Session.EscapeKey(request.Key)}");
            upload.Content = new ByteArrayContent(body);
            if (request.Force)
                upload.Headers.TryAddWithoutValidation("If-Match", "*");

            try
            {
                using var response = await S3Session.Http.SendAsync(upload, HttpContext.RequestAborted);
                if (!response.IsSuccessStatusCode)
                    return await Relay(response);

                var info = await response.Content.ReadFromJsonAsync<S3RemoteFileProvider.RemoteObject>(JsonOptions, HttpContext.RequestAborted);
                if (info?.Id is null || info.BaseId is null)
                    return StatusCode(StatusCodes.Status502BadGateway, new { error = "The S3 server returned no version info." });

                provider.Invalidate(request.Bucket, request.Key);
                var stamped = FileMetadata.IsWorksheet(request.Key)
                    ? FileMetadata.WithFileInfo(request.Content, new Dictionary<string, string>
                    {
                        [FileMetadata.BaseIdKey] = info.BaseId,
                        [FileMetadata.IdKey] = info.Id,
                        [FileMetadata.VersionKey] = info.Version.ToString(),
                        ["lastUpdatedBy"] = info.UpdatedBy ?? string.Empty,
                        ["lastUpdatedAt"] = info.UpdatedAt ?? string.Empty
                    })
                    : request.Content;
                return Ok(new { info, content = stamped });
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
            }
        }

        /// <summary>Forwards <c>/api/s3/remote/{path}</c> to <c>{server}/api/{path}</c> with the session token.</summary>
        [AcceptVerbs("GET", "POST", "PUT", "DELETE")]
        [Route("remote/{**path}")]
        public async Task<IActionResult> Remote(string path)
        {
            if (!session.IsConnected)
                return NotSignedIn();

            using var forward = session.CreateRequest(new HttpMethod(Request.Method), path + Request.QueryString);
            if (Request.ContentLength > 0 || Request.Headers.TransferEncoding.Count > 0)
            {
                using var reader = new StreamReader(Request.Body, Encoding.UTF8);
                forward.Content = new StringContent(await reader.ReadToEndAsync(HttpContext.RequestAborted), Encoding.UTF8,
                    Request.ContentType ?? "application/json");
            }

            try
            {
                using var response = await S3Session.Http.SendAsync(forward, HttpContext.RequestAborted);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    session.Clear();
                return await Relay(response);
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
            }
        }

        private object SessionInfo() => new
        {
            connected = session.IsConnected,
            url = session.BaseUrl?.ToString(),
            user = session.User
        };

        private ObjectResult NotSignedIn() =>
            StatusCode(StatusCodes.Status401Unauthorized, new { error = "Not signed in to an S3 server." });

        private static async Task<IActionResult> Relay(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                Content = body,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json"
            };
        }
    }
}
