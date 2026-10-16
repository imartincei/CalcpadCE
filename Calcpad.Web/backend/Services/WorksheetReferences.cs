using System.Text.RegularExpressions;
using Calcpad.Core;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// What a worksheet references, and where in a line each reference sits. Every path grammar
    /// belongs to Core — <see cref="MacroParser.TryGetIncludePath"/>,
    /// <see cref="ExpressionParser.TryGetDataPath"/> and <see cref="ImageReferences"/> — so that
    /// the two callers bundling a worksheet agree with the renderer on what it depends on.
    /// </summary>
    internal static class WorksheetReferences
    {
        internal enum ReferenceKind { Include, Data, Image }

        /// <param name="Start">Offset of <paramref name="Raw"/> within the line it was found on.</param>
        /// <param name="IsOutput">
        /// A <c>#write</c>/<c>#append</c> target. It is where the worksheet puts its results, not
        /// something it depends on, so bundling leaves it exactly as written.
        /// </param>
        internal readonly record struct Reference(
            ReferenceKind Kind,
            string Raw,
            int Start,
            int Length,
            bool IsOutput)
        {
            internal string Directive => Kind switch
            {
                ReferenceKind.Include => "#include",
                ReferenceKind.Data => IsOutput ? "#write" : "#read",
                _ => "<img src>",
            };
        }

        internal static Regex ImageSource => ImageReferences.Source;

        internal static bool IsExternalSource(string src) => ImageReferences.IsExternal(src);

        /// <summary>
        /// Whether a <c>#write</c>/<c>#append</c> target names a fixed location rather than one
        /// beside wherever the worksheet ends up — rooted once environment variables are
        /// expanded, the same test the directive itself would resolve against.
        /// </summary>
        internal static bool IsAbsoluteTarget(string raw)
        {
            try
            {
                return Path.IsPathRooted(Environment.ExpandEnvironmentVariables(raw));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Every file <paramref name="line"/> refers to, in the order they appear. A directive
        /// carries at most one; an image comment may carry several. Offsets are into
        /// <paramref name="line"/> as given, indent included.
        /// </summary>
        internal static List<Reference> Scan(string line)
        {
            var references = new List<Reference>();
            var code = line.AsSpan().TrimStart();
            var indent = line.Length - code.Length;
            if (!code.IsEmpty && code[0] == '#')
            {
                if (MacroParser.TryGetIncludePath(code, out var start, out var length))
                    references.Add(new Reference(ReferenceKind.Include,
                        code.Slice(start, length).ToString(), indent + start, length, false));
                else if (ExpressionParser.TryGetDataPath(code, out start, out length))
                    references.Add(new Reference(ReferenceKind.Data,
                        code.Slice(start, length).ToString(), indent + start, length,
                        !code.StartsWith("#read", StringComparison.OrdinalIgnoreCase)));

                return references;
            }

            foreach (var match in ImageSource.Matches(line).Cast<Match>())
            {
                var src = match.Groups[2];
                references.Add(new Reference(ReferenceKind.Image, src.Value, src.Index, src.Length, false));
            }
            return references;
        }

        /// <summary>
        /// Replaces the spans of <paramref name="references"/> in <paramref name="line"/> with
        /// what <paramref name="replacement"/> returns, right to left so the offsets of the ones
        /// still to come stay valid. Everything around them — a trailing comment, an
        /// <c>#{field}</c> block, a sheet, a range, <c>type=</c>, <c>sep=</c> — is untouched.
        /// </summary>
        internal static string Rewrite(
            string line,
            List<Reference> references,
            Func<Reference, string?> replacement)
        {
            for (var i = references.Count - 1; i >= 0; --i)
            {
                var reference = references[i];
                var value = replacement(reference);
                if (value is not null)
                    line = string.Concat(
                        line.AsSpan(0, reference.Start),
                        value,
                        line.AsSpan(reference.Start + reference.Length));
            }
            return line;
        }
    }
}
