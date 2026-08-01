using static Calcpad.Server.Services.WorksheetReferences;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Decides what a <c>#write</c>/<c>#append</c> target should be rewritten to when a
    /// worksheet is exported to run somewhere else, and refuses an export that would make two
    /// of them write the same file. Shared by <see cref="PortablePackage"/> and
    /// <see cref="PortableWorksheet"/>, since both offer the same option: collapse an absolute
    /// target to its bare filename so the output lands beside the exported worksheet instead of
    /// wherever it was written against on the author's machine. A relative target already does
    /// that and is left alone either way.
    /// </summary>
    internal sealed class OutputTargets(bool nextToWorksheet, string rootDirectory, List<string> errors)
    {
        // Windows is case-insensitive for the same reason PortablePackage's own comparer is:
        // two paths differing only in case are the same file there, but not elsewhere.
        private static readonly StringComparer PathComparer =
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        private readonly Dictionary<string, (string Resolved, string Raw, string Owner, int Line)> _seen =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The rewritten target, or <c>null</c> to leave <paramref name="reference"/> exactly as
        /// written — always when the option is off, and always for a target that is not rooted,
        /// since it already lands beside wherever the worksheet ends up.
        /// </summary>
        internal string? Rewrite(Reference reference, string owner, int line)
        {
            if (!nextToWorksheet)
                return null;

            var raw = reference.Raw;
            var isAbsolute = IsAbsoluteTarget(raw);
            string effective, resolved;
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(raw);
                effective = isAbsolute ? Path.GetFileName(expanded) : raw;
                resolved = isAbsolute || rootDirectory.Length == 0
                    ? Path.GetFullPath(expanded)
                    : Path.GetFullPath(expanded, rootDirectory);
            }
            catch
            {
                return null;
            }

            if (_seen.TryGetValue(effective, out var existing))
            {
                if (!PathComparer.Equals(existing.Resolved, resolved))
                    errors.Add($"{owner}, line {line}: {reference.Directive} {raw} and "
                        + $"{existing.Owner}, line {existing.Line}: {existing.Raw} would both write "
                        + $"\"{effective}\" once rewritten next to the worksheet. Rename one, or "
                        + "clear \"Write outputs next to the worksheet\".");
            }
            else
                _seen[effective] = (resolved, raw, owner, line);

            return isAbsolute ? effective : null;
        }
    }
}
