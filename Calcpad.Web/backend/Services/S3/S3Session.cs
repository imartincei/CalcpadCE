using System.Net.Http.Headers;
using System.Text.Json;

namespace Calcpad.Server.Services.S3
{
    /// <summary>The signed-in connection to a Calcpad S3 server; the token never leaves this process.</summary>
    public sealed class S3Session
    {
        private record State(Uri BaseUrl, string Token, JsonElement User);

        private volatile State? _state;

        public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

        public bool IsConnected => _state is not null;
        public Uri? BaseUrl => _state?.BaseUrl;
        public JsonElement? User => _state?.User;

        public void Set(Uri baseUrl, string token, JsonElement user) => _state = new State(baseUrl, token, user.Clone());

        public void Clear() => _state = null;

        /// <summary>A request to <c>{BaseUrl}/api/{path}</c> carrying the session token.</summary>
        public HttpRequestMessage CreateRequest(HttpMethod method, string path)
        {
            var state = _state ?? throw new InvalidOperationException("Not signed in to an S3 server.");
            var request = new HttpRequestMessage(method, new Uri(state.BaseUrl, "api/" + path.TrimStart('/')));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.Token);
            return request;
        }

        public static string EscapeKey(string key) =>
            string.Join('/', key.Split('/').Select(Uri.EscapeDataString));

        public static Uri NormalizeBaseUrl(string url)
        {
            var uri = new Uri(url.Trim(), UriKind.Absolute);
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("The S3 server URL must be http or https.");
            return new Uri(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
        }
    }
}
