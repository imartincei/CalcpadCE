using Calcpad.Core;
using static Calcpad.Server.Services.WorksheetReferences;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Decides what a <c>#write</c>/<c>#append</c> target should be rewritten to when a
    /// worksheet is exported to run somewhere else, and refuses an export that would make two
    /// of them write the same file. Shared by <see cref="PortablePackage"/> and
    /// <see cref="PortableWorksheet"/>.
    ///
    /// Two independent choices feed into the rewrite. <paramref name="nextToWorksheet"/> collapses
    /// an absolute target to its bare filename, so the output lands beside the exported worksheet
    /// instead of wherever it was written against on the author's machine — a relative target
    /// already does that and is left alone either way. <paramref name="bundleProject"/>/
    /// <paramref name="bundleLibrary"/> decide what a <c>&lt;project&gt;</c>/<c>&lt;library&gt;</c>
    /// token target does: left alone when off — the recipient's own declaration resolves it later
    /// — or resolved to the author's local absolute path when on, exactly as though that had been
    /// written instead of the token, and then subject to <paramref name="nextToWorksheet"/> like
    /// any other absolute target.
    /// </summary>
    internal sealed class OutputTargets(
        bool nextToWorksheet,
        string rootDirectory,
        List<string> errors,
        PathRoots pathRoots,
        bool bundleProject,
        bool bundleLibrary)
    {
        // Windows is case-insensitive for the same reason PortablePackage's own comparer is:
        // two paths differing only in case are the same file there, but not elsewhere.
        private static readonly StringComparer PathComparer =
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        private readonly Dictionary<string, (string Resolved, string Raw, string Owner, int Line)> _seen =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The rewritten target, or <c>null</c> to leave <paramref name="reference"/> exactly as
        /// written.
        /// </summary>
        internal string? Rewrite(Reference reference, string owner, int line)
        {
            var raw = reference.Raw;
            var isToken = PathRoots.TryGetTokenKind(raw.AsSpan(), out var isProject);
            if (isToken)
            {
                if (isProject ? !bundleProject : !bundleLibrary)
                    return null;

                if (!pathRoots.TryExpand(raw, out raw, out var error))
                {
                    errors.Add($"{owner}, line {line}: {reference.Directive} {reference.Raw} — {error}");
                    return null;
                }
            }

            if (!nextToWorksheet)
                return isToken ? raw : null;

            var isAbsolute = isToken || IsAbsoluteTarget(raw);
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
                return isToken ? raw : null;
            }

            if (_seen.TryGetValue(effective, out var existing))
            {
                if (!PathComparer.Equals(existing.Resolved, resolved))
                    errors.Add($"{owner}, line {line}: {reference.Directive} {reference.Raw} and "
                        + $"{existing.Owner}, line {existing.Line}: {existing.Raw} would both write "
                        + $"\"{effective}\" once rewritten next to the worksheet. Rename one, or "
                        + "clear \"Write outputs next to the worksheet\".");
            }
            else
                _seen[effective] = (resolved, reference.Raw, owner, line);

            return isAbsolute ? effective : null;
        }
    }
}
