using System.Security.Cryptography;
using System.Text;

namespace Calcpad.Server.Services
{
    /// <summary>Identifies one version of a document across the endpoints that receive it.</summary>
    public static class ContentKey
    {
        public static string For(string? sourceFilePath, string content) =>
            $"{sourceFilePath ?? string.Empty}|{Sha256Hex(content)}";

        /// <summary>
        /// The document a request is about: its path, else the client's id for an untitled one,
        /// else the key all other untitled documents share.
        /// </summary>
        public static string Document(string? sourceFilePath, string? documentId) =>
            !string.IsNullOrEmpty(sourceFilePath) ? sourceFilePath
            : string.IsNullOrEmpty(documentId) ? string.Empty
            : "id:" + documentId;

        public static string Sha256Hex(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
