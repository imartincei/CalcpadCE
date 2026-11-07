using System.Security.Cryptography;
using System.Text;

namespace Calcpad.Server.Services
{
    /// <summary>Identifies one version of a document across the endpoints that receive it.</summary>
    public static class ContentKey
    {
        public static string For(string? sourceFilePath, string content) =>
            $"{sourceFilePath ?? string.Empty}|{Sha256Hex(content)}";

        public static string Sha256Hex(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
