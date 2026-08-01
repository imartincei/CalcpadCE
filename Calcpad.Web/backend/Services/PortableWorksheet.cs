using System.Text;
using Calcpad.Core;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Rewrites a worksheet into the self-contained form a compiled <c>.cpdz</c> needs. It is
    /// handed out to be filled in, so nothing it depends on can be left behind: macros and
    /// <c>#include</c>d files are expanded in place, and every <c>#read</c> becomes the data
    /// it imports. Images are the exception — they stay as <c>src</c> paths for the host to
    /// embed, which is why an included file's relative paths are made absolute on the way
    /// through: once its text sits in the parent worksheet, the folder they were relative to
    /// is gone.
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
            var outputs = new OutputTargets(nextToWorksheet, rootDirectory, errors);
            var rewritten = RewriteDirectives(expanded, sourceFilePath, outputs, errors);
            return new Result(rewritten, errors);
        }

        /// <summary>
        /// Replaces every <c>#read</c> with the data it imports, and rewrites every
        /// <c>#write</c>/<c>#append</c> target <paramref name="outputs"/> asks for. The read
        /// assignment goes between <c>#hide</c> and <c>#end hide</c>, which leaves the
        /// surrounding visibility as it was and keeps a bundled data set out of the report —
        /// where the directive itself only ever printed a line naming the file.
        /// </summary>
        private static string RewriteDirectives(
            string text, string? sourceFilePath, OutputTargets outputs, List<string> errors)
        {
            var sb = new StringBuilder(text.Length);
            var lineNumber = 0;
            foreach (var rawLine in text.Split('\n'))
            {
                ++lineNumber;
                var line = rawLine.TrimEnd('\r');
                var code = line.TrimStart();
                if (code.StartsWith("#read", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var assignment = ExpressionParser.InlineReadDirective(code, sourceFilePath);
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
                sb.Append(references.Exists(r => r.IsOutput)
                    ? WorksheetReferences.Rewrite(line, references,
                        r => outputs.Rewrite(r, "the worksheet", lineNumber))
                    : line)
                  .Append('\n');
            }
            return sb.ToString();
        }

        private static readonly Func<string, Queue<string>, string> ReadInclude =
            CalcpadService.CreateIncludeDelegate();

        private static string ReadIncludeWithAbsoluteImages(string fileName, Queue<string> fields) =>
            AbsoluteImagePaths(ReadInclude(fileName, fields), Path.GetDirectoryName(fileName)!);

        private static string AbsoluteImagePaths(string content, string directory) =>
            WorksheetReferences.ImageSource.Replace(content, match =>
            {
                var src = match.Groups[2].Value;
                if (WorksheetReferences.IsExternalSource(src))
                    return match.Value;

                var full = Path.GetFullPath(src, directory).Replace('\\', '/');
                return $"{match.Groups[1].Value}{full}{match.Groups[3].Value}";
            });
    }
}
