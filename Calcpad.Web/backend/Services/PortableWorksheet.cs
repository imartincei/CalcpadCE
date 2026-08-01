using System.Text;
using Calcpad.Core;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Rewrites a worksheet into the self-contained form a compiled <c>.cpdz</c> needs. It is
    /// handed out to be filled in, so nothing it depends on can be left behind: macros and
    /// <c>#include</c>d files are expanded in place, every <c>#read</c> becomes the data it
    /// imports, and a <c>&lt;project&gt;</c>/<c>&lt;library&gt;</c> reference is resolved to the
    /// author's local path rather than left for a <c>#ProjectPath</c>/<c>#LibraryPath</c> the
    /// recipient has no way to add — a compiled worksheet's source is locked. Images are the
    /// exception to "expanded in place" — they stay as <c>src</c> paths for the host to embed,
    /// which is why an included file's relative paths are made absolute on the way through: once
    /// its text sits in the parent worksheet, the folder they were relative to is gone.
    /// </summary>
    internal static class PortableWorksheet
    {
        internal sealed record Result(string Content, IReadOnlyList<string> Errors);

        /// <summary>
        /// Bundles <paramref name="content"/>, whose <c>#include</c> and <c>#read</c> paths
        /// resolve against the folder of <paramref name="sourceFilePath"/> — an unsaved
        /// worksheet therefore only resolves absolute ones. Anything standing in the way is
        /// reported rather than skipped: a worksheet that still reads a file beside it is not
        /// portable, so it is better not to write one at all.
        /// </summary>
        /// <param name="nextToWorksheet">
        /// Collapses an absolute <c>#write</c>/<c>#append</c> target to its bare filename, so the
        /// output lands beside wherever the compiled worksheet runs instead of a folder that may
        /// not exist there. A relative target already does that and is untouched either way.
        /// </param>
        public static Result Build(string content, string? sourceFilePath, bool nextToWorksheet = false)
        {
            var errors = new List<string>();
            var macroParser = new MacroParser
            {
                Include = ReadIncludeWithAbsoluteImages,
                SourceFilePath = sourceFilePath,
            };
            macroParser.Parse(content, out var expanded, null, 0, false);
            foreach (var error in macroParser.Errors)
                errors.Add($"Line {error.SourceLine}: {error.Message}");

            if (errors.Count > 0)
                return new Result(content, errors);

            var rootDirectory = string.IsNullOrEmpty(sourceFilePath)
                ? string.Empty : Path.GetDirectoryName(sourceFilePath) ?? string.Empty;
            // A compiled worksheet's source is locked, so there is no "leave the token" option
            // here the way a portable package offers: both roots always resolve.
            var pathRoots = new PathRoots();
            var outputs = new OutputTargets(nextToWorksheet, rootDirectory, errors, pathRoots,
                bundleProject: true, bundleLibrary: true);
            var rewritten = RewriteDirectives(expanded, sourceFilePath, outputs, pathRoots, errors);
            return new Result(rewritten, errors);
        }

        /// <summary>
        /// Replaces every <c>#read</c> with the data it imports, rewrites every
        /// <c>#write</c>/<c>#append</c> target <paramref name="outputs"/> asks for, and resolves
        /// any <c>&lt;project&gt;</c>/<c>&lt;library&gt;</c> image reference left unresolved by
        /// <see cref="AbsoluteImagePaths"/>. The read assignment goes between <c>#hide</c> and
        /// <c>#end hide</c>, which leaves the surrounding visibility as it was and keeps a
        /// bundled data set out of the report — where the directive itself only ever printed a
        /// line naming the file.
        /// </summary>
        /// <remarks>
        /// <paramref name="text"/> is already macro-expanded, so a <c>#ProjectPath</c>/
        /// <c>#LibraryPath</c> declared inside a <c>#local</c> section of an included file is
        /// already gone — <see cref="CalcpadService.CreateIncludeDelegate"/> strips it before
        /// this ever sees the text, the same as it does for the real render path. A relative
        /// declaration resolves against <paramref name="sourceFilePath"/>'s folder regardless of
        /// which file wrote it, matching the existing <c>#read</c>/<c>#write</c> asymmetry: once
        /// everything is flattened into one string, the folder a line was originally written in
        /// is gone, and the root document's is what is left.
        /// </remarks>
        private static string RewriteDirectives(
            string text, string? sourceFilePath, OutputTargets outputs, PathRoots pathRoots, List<string> errors)
        {
            var declaringDirectory = string.IsNullOrEmpty(sourceFilePath)
                ? null : Path.GetDirectoryName(sourceFilePath);
            var sb = new StringBuilder(text.Length);
            var lineNumber = 0;
            foreach (var rawLine in text.Split('\n'))
            {
                ++lineNumber;
                var line = rawLine.TrimEnd('\r');
                var code = line.TrimStart();

                if (PathRoots.IsDeclaration(code.AsSpan(), out var isProject, out var declStart, out var declLength))
                {
                    if (declLength == 0)
                        errors.Add($"Line {lineNumber}: "
                            + $"{(isProject ? "#ProjectPath" : "#LibraryPath")} requires a path.");
                    else if (!pathRoots.TryDeclare(isProject, code.Substring(declStart, declLength),
                        declaringDirectory, out var declError))
                        errors.Add($"Line {lineNumber}: {declError}");
                    continue;
                }

                if (code.StartsWith("#read", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var assignment = ExpressionParser.InlineReadDirective(code, sourceFilePath, pathRoots);
                        if (assignment is null)
                            continue;

                        var indent = line[..(line.Length - code.Length)];
                        sb.Append(indent).Append("#hide").Append('\n')
                          .Append(indent).Append(assignment).Append('\n')
                          .Append(indent).Append("#end hide").Append('\n');
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Line {lineNumber}: {ex.Message}");
                    }
                    continue;
                }

                var references = WorksheetReferences.Scan(line);
                var needsRewrite = references.Exists(r => r.IsOutput
                    || r.Kind == WorksheetReferences.ReferenceKind.Image && PathRoots.HasToken(r.Raw));
                sb.Append(needsRewrite ? WorksheetReferences.Rewrite(line, references, Target) : line)
                  .Append('\n');
            }
            return sb.ToString();

            string? Target(WorksheetReferences.Reference r)
            {
                if (r.IsOutput)
                    return outputs.Rewrite(r, "the worksheet", lineNumber);
                if (r.Kind != WorksheetReferences.ReferenceKind.Image || !PathRoots.HasToken(r.Raw))
                    return null;

                if (!pathRoots.TryExpand(r.Raw, out var expandedSrc, out var tokenError))
                {
                    errors.Add($"Line {lineNumber}: <img src> {r.Raw} — {tokenError}");
                    return null;
                }
                try
                {
                    return Path.GetFullPath(Environment.ExpandEnvironmentVariables(expandedSrc))
                        .Replace('\\', '/');
                }
                catch
                {
                    return null;
                }
            }
        }

        private static readonly Func<string, Queue<string>, string> ReadInclude =
            CalcpadService.CreateIncludeDelegate();

        private static string ReadIncludeWithAbsoluteImages(string fileName, Queue<string> fields) =>
            AbsoluteImagePaths(ReadInclude(fileName, fields), Path.GetDirectoryName(fileName)!);

        /// <summary>
        /// Makes a relative image path absolute against <paramref name="directory"/>, with
        /// environment variables expanded first — the same rule any other reference resolves by.
        /// A <c>&lt;project&gt;</c>/<c>&lt;library&gt;</c> reference is left exactly as written:
        /// it names a document-wide root, not one relative to whichever included file happens to
        /// hold the line, so it is resolved once, later, by <see cref="RewriteDirectives"/> once
        /// every declaration in the document is known.
        /// </summary>
        private static string AbsoluteImagePaths(string content, string directory) =>
            WorksheetReferences.ImageSource.Replace(content, match =>
            {
                var src = match.Groups[2].Value;
                if (WorksheetReferences.IsExternalSource(src) || PathRoots.HasToken(src))
                    return match.Value;

                try
                {
                    var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(src), directory)
                        .Replace('\\', '/');
                    return $"{match.Groups[1].Value}{full}{match.Groups[3].Value}";
                }
                catch
                {
                    return match.Value;
                }
            });
    }
}
