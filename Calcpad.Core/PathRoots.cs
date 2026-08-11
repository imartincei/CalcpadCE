using System;
using System.IO;

namespace Calcpad.Core
{
    public sealed class PathRoots
    {
        private const string ProjectToken = "{project}";
        private const string LibraryToken = "{library}";
        private const string UserToken = "{user}";
        private const string ProjectKeyword = "#projectpath";
        private const string LibraryKeyword = "#librarypath";

        public string Project { get; private set; }
        public string Library { get; private set; }

        public static bool HasToken(ReadOnlySpan<char> path) =>
            path.StartsWith(ProjectToken, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(LibraryToken, StringComparison.OrdinalIgnoreCase);

        public static bool IsUserToken(ReadOnlySpan<char> path) =>
            path.StartsWith(UserToken, StringComparison.OrdinalIgnoreCase);

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
                var expanded = IsUserToken(rawValue.AsSpan()) ? ExpandUserToken(rawValue) : rawValue;
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
