using Calcpad.Core;
using static Calcpad.Server.Services.WorksheetReferences;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Decides what a <c>#write</c>/<c>#append</c> target should be rewritten to when a
    /// worksheet is exported to run somewhere else, renaming one that would otherwise collide
    /// instead of refusing the export. Shared by <see cref="PortablePackage"/> and
    /// <see cref="PortableWorksheet"/>.
    ///
    /// There is one rule: an absolute target is collapsed to its bare filename so the output
    /// lands beside the exported worksheet, while a relative target already does and is left
    /// exactly as written. A token target is resolved to the author's local absolute path
    /// first, then collapsed like any other absolute one.
    /// </summary>
    internal sealed class OutputTargets(
        string rootDirectory,
        List<string> errors,
        PathRoots pathRoots)
    {
        // Windows is case-insensitive for the same reason PortablePackage's own comparer is:
        // two paths differing only in case are the same file there, but not elsewhere.
        private static readonly StringComparer PathComparer =
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        // Every occurrence's final bare filename, decided by Prepare before any rewriting starts
        // — see Prepare's own comment for why that has to happen up front rather than live.
        private readonly Dictionary<(string Owner, int Line, string Raw), string> _finalNames = new();

        /// <summary>
        /// Decides, for every <c>#write</c>/<c>#append</c> target in the document, the bare
        /// filename <see cref="Rewrite"/> will later collapse it to, renaming collisions the
        /// way <c>PortablePackage</c> renames bundled files: <c>name-1.ext</c> onwards, in
        /// occurrence order. This has to run as one batch before <see cref="Rewrite"/> streams
        /// through the document, because a rename can affect an <em>earlier</em> occurrence
        /// whose text would already be final by then.
        ///
        /// A relative target is never renamed but still reserves its name, and two occurrences
        /// resolving to the very same file are not a collision at all.
        /// <paramref name="roots"/> is a parameter rather than the instance's own
        /// <c>pathRoots</c> so a caller whose real pass declares roots progressively (as
        /// <see cref="PortableWorksheet"/> does) can still hand this every declaration.
        /// </summary>
        internal void Prepare(IEnumerable<(Reference Reference, string Owner, int Line)> outputReferences, PathRoots roots)
        {
            var groups = new Dictionary<string, List<(string Owner, int Line, string Raw, string Resolved, bool CanRename)>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var (reference, owner, line) in outputReferences)
            {
                var raw = reference.Raw;
                // An undeclared token never reaches a bare name at all (Rewrite leaves it exactly
                // as written), so there is nothing to batch here — silently, since Rewrite reports
                // the same undeclared-root error itself, later.
                if (!TryExpandToken(roots, ref raw, reportErrors: false, reference, owner, line, out _))
                    continue;

                if (!TryResolveTarget(raw, out var isAbsolute, out var effective, out var resolved))
                    continue;

                if (!groups.TryGetValue(effective, out var group))
                    groups[effective] = group = [];
                group.Add((owner, line, reference.Raw, resolved, isAbsolute));
            }

            foreach (var (name, group) in groups)
            {
                var distinctResolved = new HashSet<string>(group.Select(g => g.Resolved), PathComparer);
                if (distinctResolved.Count == 1)
                {
                    foreach (var occurrence in group)
                        _finalNames[(occurrence.Owner, occurrence.Line, occurrence.Raw)] = name;
                    continue;
                }

                var stem = Path.GetFileNameWithoutExtension(name);
                var extension = Path.GetExtension(name);
                var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var byResolved = new Dictionary<string, string>(PathComparer);
                var index = 0;
                foreach (var occurrence in group)
                {
                    if (!occurrence.CanRename)
                    {
                        // A relative target stays exactly as written and reserves `name`, even
                        // though it never goes through _finalNames (Rewrite always leaves it
                        // alone). Any renameable occurrence below still avoids it.
                        taken.Add(name);
                        continue;
                    }
                    if (!byResolved.TryGetValue(occurrence.Resolved, out var candidate))
                    {
                        do
                            candidate = $"{stem}-{++index}{extension}";
                        while (!taken.Add(candidate));
                        byResolved[occurrence.Resolved] = candidate;
                    }
                    _finalNames[(occurrence.Owner, occurrence.Line, occurrence.Raw)] = candidate;
                }
            }
        }

        /// <summary>
        /// The rewritten target, or <c>null</c> to leave <paramref name="reference"/> exactly as
        /// written. Call <see cref="Prepare"/> with every output reference in the document first —
        /// this only looks up what it decided.
        /// </summary>
        internal string? Rewrite(Reference reference, string owner, int line)
        {
            var raw = reference.Raw;
            if (!TryExpandToken(pathRoots, ref raw, reportErrors: true, reference, owner, line, out var isToken))
                return null; // error already recorded

            if (!TryResolveTarget(raw, out var isAbsolute, out _, out _))
                return isToken ? raw : null;

            if (!isAbsolute)
                return null;

            // Decided by Prepare, over every occurrence in the document at once — see its own
            // comment for why this can't be decided live, one reference at a time, here.
            return _finalNames.TryGetValue((owner, line, reference.Raw), out var finalName) ? finalName : null;
        }

        /// <summary>
        /// Expands a <c>&lt;project&gt;</c>/<c>&lt;library&gt;</c>/<c>&lt;user&gt;</c> token
        /// target in place. False only on an undeclared root, optionally reporting it — a
        /// caller's own pre-scan does not, since the real <see cref="Rewrite"/> pass reports the
        /// same failure itself later.
        /// </summary>
        private bool TryExpandToken(
            PathRoots roots, ref string raw, bool reportErrors, Reference reference, string owner, int line,
            out bool isToken)
        {
            isToken = PathRoots.IsUserToken(raw.AsSpan()) || PathRoots.HasToken(raw.AsSpan());
            if (!isToken)
                return true;

            if (roots.TryExpand(raw, out raw, out var error))
                return true;

            if (reportErrors)
                errors.Add($"{owner}, line {line}: {reference.Directive} {reference.Raw} — {error}");
            return false;
        }

        /// <summary>
        /// Resolves an already token-expanded target to the bare filename it collapses to and the
        /// full path it names. False on a target that turns out not to be a usable path at all
        /// (an invalid character, say) — the caller already has its own fallback for that.
        /// </summary>
        private bool TryResolveTarget(string raw, out bool isAbsolute, out string effective, out string resolved)
        {
            isAbsolute = PathRoots.HasToken(raw.AsSpan()) || PathRoots.IsUserToken(raw.AsSpan()) || IsAbsoluteTarget(raw);
            effective = raw;
            resolved = raw;
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(raw);
                effective = isAbsolute ? Path.GetFileName(expanded) : raw;
                resolved = isAbsolute || rootDirectory.Length == 0
                    ? Path.GetFullPath(expanded)
                    : Path.GetFullPath(expanded, rootDirectory);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
