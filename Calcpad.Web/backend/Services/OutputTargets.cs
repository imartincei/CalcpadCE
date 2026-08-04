using Calcpad.Core;
using static Calcpad.Server.Services.WorksheetReferences;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Decides what a <c>#write</c>/<c>#append</c> target should be rewritten to when a
    /// worksheet is exported to run somewhere else, renaming one that would otherwise collide
    /// with another instead of refusing the export. Shared by <see cref="PortablePackage"/> and
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
    ///
    /// A <c>&lt;user&gt;</c> target always resolves this same way — it is not gated by
    /// <paramref name="bundleProject"/>/<paramref name="bundleLibrary"/>, since there is no
    /// recipient-side declaration for it to wait for either way — and then follows
    /// <paramref name="nextToWorksheet"/> exactly like any other resolved target.
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

        // Every occurrence's final bare filename, decided by Prepare before any rewriting starts
        // — see Prepare's own comment for why that has to happen up front rather than live.
        // Populated only when nextToWorksheet is on; empty (and unused) otherwise.
        private readonly Dictionary<(string Owner, int Line, string Raw), string> _finalNames = new();

        /// <summary>
        /// Decides, for every <c>#write</c>/<c>#append</c> target in the document, the bare
        /// filename <see cref="Rewrite"/> will later collapse it to — renaming any that would
        /// otherwise collide with another the same way <c>PortablePackage</c>'s own bundled-file
        /// renaming does: <c>name-1.ext</c>, <c>name-2.ext</c> and so on, in occurrence order.
        ///
        /// This has to run as one batch, before <see cref="Rewrite"/> streams through the
        /// document rewriting each line in turn, because a rename can affect an <em>earlier</em>
        /// occurrence: two targets sharing a name are both renamed once the collision is found,
        /// even though the first of them looked fine in isolation. By the time <c>Rewrite</c>
        /// reaches it, that line's text is already final — there is no going back to rename it
        /// once a later collision surfaces. A relative target is never renamed (it stays exactly
        /// as written, the same as when this is off), but still reserves its name: an absolute
        /// target that would otherwise collapse onto it is renamed away instead. Two occurrences
        /// that resolve to the very same file — a <c>#write</c> and a later <c>#append</c> to it,
        /// say — are not a collision at all, and share the name unrenamed.
        ///
        /// <paramref name="roots"/> is deliberately a parameter rather than always the instance's
        /// own <c>pathRoots</c>: a caller whose real, line-by-line pass declares
        /// <c>#ProjectPath</c>/<c>#LibraryPath</c> progressively (as <see cref="PortableWorksheet"/>
        /// does) needs this batch to see every declaration in the document regardless of where in
        /// it a <c>#write</c> happens to sit — unlike the live pass, there is no "declared before
        /// use" ordering to preserve here, only a final name to compute. A caller that already
        /// walks the whole tree before rewriting anything (<see cref="PortablePackage"/>) can just
        /// pass its own, already fully-declared instance.
        ///
        /// A no-op when <paramref name="nextToWorksheet"/> is off, since nothing is ever
        /// collapsed to a bare name in that case.
        /// </summary>
        internal void Prepare(IEnumerable<(Reference Reference, string Owner, int Line)> outputReferences, PathRoots roots)
        {
            if (!nextToWorksheet)
                return;

            var groups = new Dictionary<string, List<(string Owner, int Line, string Raw, string Resolved, bool CanRename)>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var (reference, owner, line) in outputReferences)
            {
                var raw = reference.Raw;
                // An unbundled or undeclared token never reaches a bare name at all (Rewrite
                // leaves it exactly as written either way), so there is nothing to batch here —
                // silently, since Rewrite reports the same undeclared-root error itself, later.
                if (!TryExpandToken(roots, ref raw, reportErrors: false, reference, owner, line, out _, out var leaveAsWritten)
                    || leaveAsWritten)
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
        /// written. When <c>nextToWorksheet</c> is on, call <see cref="Prepare"/> with every
        /// output reference in the document first — this only looks up what it decided.
        /// </summary>
        internal string? Rewrite(Reference reference, string owner, int line)
        {
            var raw = reference.Raw;
            if (!TryExpandToken(pathRoots, ref raw, reportErrors: true, reference, owner, line,
                    out var isToken, out var leaveAsWritten))
                return null; // error already recorded

            if (leaveAsWritten)
                return raw; // isToken is true here; raw is still the original token text

            if (!nextToWorksheet)
                return isToken ? raw : null;

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
        /// same failure itself later. <paramref name="leaveAsWritten"/> is the unbundled-token
        /// case: <paramref name="isToken"/> is true but <paramref name="raw"/> is left as the
        /// original token text, to return immediately regardless of <c>nextToWorksheet</c> — the
        /// recipient's own declaration is what is meant to resolve it, not this export.
        /// </summary>
        private bool TryExpandToken(
            PathRoots roots, ref string raw, bool reportErrors, Reference reference, string owner, int line,
            out bool isToken, out bool leaveAsWritten)
        {
            leaveAsWritten = false;
            // {user} always resolves and is never gated by bundleProject/bundleLibrary — there is
            // no recipient-side declaration it could otherwise wait for — so it takes the same
            // "resolve now" path a bundled {project}/{library} target does.
            var isUserToken = PathRoots.IsUserToken(raw.AsSpan());
            var isProject = false;
            var isRootToken = !isUserToken && PathRoots.TryGetTokenKind(raw.AsSpan(), out isProject);
            isToken = isUserToken || isRootToken;
            if (!isToken)
                return true;

            if (isRootToken && (isProject ? !bundleProject : !bundleLibrary))
            {
                leaveAsWritten = true;
                return true;
            }

            if (roots.TryExpand(raw, out raw, out var error))
                return true;

            if (reportErrors)
                errors.Add($"{owner}, line {line}: {reference.Directive} {reference.Raw} — {error}");
            return false;
        }

        /// <summary>
        /// Resolves an already token-expanded target to its bare filename and full path, the way
        /// <c>nextToWorksheet</c> would collapse it. False on a target that turns out not to be a
        /// usable path at all (an invalid character, say) — the caller already has its own
        /// fallback for that.
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
