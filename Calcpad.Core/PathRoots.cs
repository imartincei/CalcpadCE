using System;
using System.IO;

namespace Calcpad.Core
{
    /// <summary>
    /// Tracks a document's <c>#ProjectPath</c>/<c>#LibraryPath</c> declarations and expands a
    /// leading <c>&lt;project&gt;</c>/<c>&lt;library&gt;</c> token in a reference against them.
    /// One instance follows a single consumer's own top-to-bottom walk of the document, so
    /// "declared before first use" falls out of the order lines are fed in rather than needing
    /// a separate check — a token reached before its declaration simply finds the root unset.
    /// A third token, <c>&lt;user&gt;</c>, needs no declaration at all: it always expands to the
    /// current OS user's home directory (see <see cref="IsUserToken"/>).
    /// </summary>
    public sealed class PathRoots
    {
        private const string ProjectToken = "<project>";
        private const string LibraryToken = "<library>";
        private const string UserToken = "<user>";
        private const string ProjectKeyword = "#projectpath";
        private const string LibraryKeyword = "#librarypath";

        public string Project { get; private set; }
        public string Library { get; private set; }

        /// <summary>Whether <paramref name="path"/> starts with a root token.</summary>
        public static bool HasToken(ReadOnlySpan<char> path) =>
            path.StartsWith(ProjectToken, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(LibraryToken, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether <paramref name="path"/> starts with the <c>&lt;user&gt;</c> token — the
        /// current OS user's home directory. Unlike <see cref="HasToken"/>'s two roots, this one
        /// needs no <c>#ProjectPath</c>/<c>#LibraryPath</c>-style declaration and always
        /// resolves, so it is checked and expanded separately from them rather than folded into
        /// <see cref="TryGetTokenKind"/>/<see cref="TryExpand"/>'s declared-root machinery.
        /// </summary>
        public static bool IsUserToken(ReadOnlySpan<char> path) =>
            path.StartsWith(UserToken, StringComparison.OrdinalIgnoreCase);

        // Expands the <user> token itself. Kept separate from Environment.ExpandEnvironmentVariables
        // (called on the result afterward by every caller) because that API only recognizes
        // %VAR%-style references on every platform — it never expands $HOME on Linux/macOS despite
        // looking like it should.
        private static string ExpandUserToken(string raw)
        {
            var rest = raw[UserToken.Length..];
            if (rest.Length > 0 && (rest[0] == '/' || rest[0] == '\\'))
                rest = rest[1..];

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return rest.Length == 0 ? home : Path.Combine(home, rest);
        }

        /// <summary>
        /// Which root <paramref name="path"/> starts with, for a caller that treats the two
        /// roots differently — e.g. a separate "bundle this root's references" choice for each.
        /// </summary>
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

        /// <summary>
        /// Whether <paramref name="line"/> is a <c>#ProjectPath</c>/<c>#LibraryPath</c>
        /// declaration. <paramref name="start"/>/<paramref name="length"/> locate the value —
        /// up to a trailing comment, the same convention <see cref="MacroParser.TryGetIncludePath"/>
        /// uses. A missing <c>=</c> or an empty value still reports <c>true</c>, with
        /// <paramref name="length"/> zero, so the caller can report the specific error.
        /// </summary>
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
            var eq = rest.IndexOf('=');
            if (eq < 0)
                return true;

            var afterEq = rest[(eq + 1)..];
            var trimmedStart = afterEq.Length - afterEq.TrimStart().Length;
            var value = afterEq[trimmedStart..];
            var commentIndex = value.IndexOfAny('\'', '"');
            if (commentIndex >= 0)
                value = value[..commentIndex];
            value = value.TrimEnd();

            start = keywordLength + eq + 1 + trimmedStart;
            length = value.Length;
            return true;
        }

        // Matches the host OS the way PortablePackage's own comparer does, so a redeclaration
        // that resolves to the same file — most notably the same #ProjectPath/#LibraryPath line
        // revisited by a #for/#while/#repeat loop it happens to sit inside — is recognized as
        // the harmless no-op it is rather than a conflicting second declaration.
        private static readonly StringComparer PathComparer =
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        /// <summary>
        /// Records a declaration read at <paramref name="declaringDirectory"/> — the folder of
        /// the file the line was written in, so a relative value resolves the way any other
        /// path in that file does. False and <paramref name="error"/> on an empty value or a
        /// second declaration of the same root that resolves to a different path.
        /// </summary>
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

        /// <summary>
        /// Expands a leading root token in <paramref name="raw"/>. Leaves anything else
        /// untouched and returns true, so a caller can run this unconditionally before its own
        /// environment-variable expansion. False and <paramref name="error"/> only when the
        /// token's root was never declared above this point in the walk.
        /// </summary>
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
