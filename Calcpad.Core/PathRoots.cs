using System;
using System.IO;

namespace Calcpad.Core
{
    public sealed class PathRoots
    {
        private const string ProjectToken = "{project}";
        private const string LibraryToken = "{library}";
        private const string UserToken = "{user}";
        private const string S3TokenPrefix = "{s3:";
        private const string ProjectKeyword = "#projectpath";
        private const string LibraryKeyword = "#librarypath";

        public string Project { get; private set; }
        public string Library { get; private set; }

        public static bool HasToken(ReadOnlySpan<char> path) =>
            path.StartsWith(ProjectToken, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(LibraryToken, StringComparison.OrdinalIgnoreCase);

        public static bool IsUserToken(ReadOnlySpan<char> path) =>
            path.StartsWith(UserToken, StringComparison.OrdinalIgnoreCase);

        public static bool IsS3Token(ReadOnlySpan<char> path) =>
            path.StartsWith(S3TokenPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>Any token that expands to an absolute path: {project}, {library}, {user} or {s3:bucket}.</summary>
        public static bool IsRootToken(ReadOnlySpan<char> path) =>
            HasToken(path) || IsUserToken(path) || IsS3Token(path);

        private static string StripLeadingSeparator(string rest) =>
            rest.Length > 0 && (rest[0] == '/' || rest[0] == '\\') ? rest[1..] : rest;

        /// <summary>S3 bucket naming: 3-63 lowercase letters, digits, dots or hyphens.</summary>
        public static bool IsValidBucketName(string name)
        {
            if (name.Length is < 3 or > 63 || !char.IsAsciiLetterOrDigit(name[0]) || !char.IsAsciiLetterOrDigit(name[^1]))
                return false;
            foreach (var c in name)
                if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-' || c == '.'))
                    return false;
            return !name.Contains("..");
        }

        /// <summary>{s3:bucket}/rest → {CacheRoot}/bucket/rest, the provider's local mirror of the bucket.</summary>
        private static bool TryExpandS3Token(string raw, out string expanded, out string error)
        {
            expanded = raw;
            error = null;
            var close = raw.IndexOf('}');
            var bucket = close > S3TokenPrefix.Length ? raw[S3TokenPrefix.Length..close].Trim() : "";
            if (!IsValidBucketName(bucket))
            {
                error = string.Format(Messages.Invalid_S3_path_root_0, raw);
                return false;
            }
            var cacheRoot = FileReferences.RemoteProvider?.CacheRoot;
            if (string.IsNullOrEmpty(cacheRoot))
            {
                error = string.Format(Messages.S3_not_connected_0, raw[..(close + 1)]);
                return false;
            }
            var bucketDir = Path.Combine(cacheRoot, bucket);
            Directory.CreateDirectory(bucketDir);
            var rest = StripLeadingSeparator(raw[(close + 1)..]);
            expanded = rest.Length == 0 ? bucketDir : Path.Combine(bucketDir, rest);
            return true;
        }

        private static string ExpandUserToken(string raw)
        {
            var rest = raw[UserToken.Length..];
            if (rest.Length > 0 && (rest[0] == '/' || rest[0] == '\\'))
                rest = rest[1..];

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return rest.Length == 0 ? home : Path.Combine(home, rest);
        }
        public static bool TryGetTokenKind(ReadOnlySpan<char> path, out bool isProject)
        {
            if (path.StartsWith(ProjectToken, StringComparison.OrdinalIgnoreCase))
            {
                isProject = true;
                return true;
            }
            if (path.StartsWith(LibraryToken, StringComparison.OrdinalIgnoreCase))
            {
                isProject = false;
                return true;
            }
            isProject = false;
            return false;
        }

        public static bool IsDeclaration(ReadOnlySpan<char> line, out bool isProject, out int start, out int length)
        {
            isProject = false;
            start = 0;
            length = 0;

            if (line.StartsWith(ProjectKeyword, StringComparison.OrdinalIgnoreCase))
                isProject = true;
            else if (line.StartsWith(LibraryKeyword, StringComparison.OrdinalIgnoreCase))
                isProject = false;
            else
                return false;

            var keywordLength = isProject ? ProjectKeyword.Length : LibraryKeyword.Length;
            var rest = line[keywordLength..];
            var trimmedStart = rest.Length - rest.TrimStart().Length;
            var value = rest[trimmedStart..];
            var commentIndex = value.IndexOfAny('\'', '"');
            if (commentIndex >= 0)
                value = value[..commentIndex];
            value = value.TrimEnd();

            start = keywordLength + trimmedStart;
            length = value.Length;
            return true;
        }

        private static readonly StringComparer PathComparer =
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        public bool TryDeclare(bool isProject, string rawValue, string declaringDirectory, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                error = string.Format(Messages.Missing_path_value_0,
                    isProject ? "#ProjectPath" : "#LibraryPath");
                return false;
            }

            string resolved;
            try
            {
                var expanded = rawValue;
                if (IsUserToken(rawValue.AsSpan()))
                    expanded = ExpandUserToken(rawValue);
                else if (IsS3Token(rawValue.AsSpan()) && !TryExpandS3Token(rawValue, out expanded, out error))
                    return false;
                expanded = Environment.ExpandEnvironmentVariables(expanded);
                resolved = string.IsNullOrEmpty(declaringDirectory)
                    ? Path.GetFullPath(expanded)
                    : Path.GetFullPath(expanded, declaringDirectory);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (FileReferences.IsRemote(resolved))
                Directory.CreateDirectory(resolved);

            if (!Directory.Exists(resolved))
            {
                error = string.Format(Messages.Path_root_folder_not_found_0_1,
                    isProject ? "#ProjectPath" : "#LibraryPath", resolved);
                return false;
            }

            var existing = isProject ? Project : Library;
            if (existing is not null)
            {
                if (PathComparer.Equals(existing, resolved))
                    return true;

                error = string.Format(Messages.Duplicate_path_declaration_0,
                    isProject ? "#ProjectPath" : "#LibraryPath");
                return false;
            }

            if (isProject)
                Project = resolved;
            else
                Library = resolved;
            return true;
        }

        public bool TryExpand(string raw, out string expanded, out string error)
        {
            error = null;
            expanded = raw;
            if (raw is null)
                return true;

            if (IsUserToken(raw.AsSpan()))
            {
                expanded = ExpandUserToken(raw);
                return true;
            }

            if (IsS3Token(raw.AsSpan()))
                return TryExpandS3Token(raw, out expanded, out error);

            if (!TryGetTokenKind(raw.AsSpan(), out var isProject))
                return true;

            var tokenLength = (isProject ? ProjectToken : LibraryToken).Length;
            var root = isProject ? Project : Library;
            if (root is null)
            {
                error = string.Format(Messages.Path_root_not_declared_0_1,
                    isProject ? ProjectToken : LibraryToken,
                    isProject ? "#ProjectPath" : "#LibraryPath");
                return false;
            }

            var rest = raw[tokenLength..];
            if (rest.Length > 0 && (rest[0] == '/' || rest[0] == '\\'))
                rest = rest[1..];

            expanded = rest.Length == 0 ? root : Path.Combine(root, rest);
            return true;
        }
    }
}
