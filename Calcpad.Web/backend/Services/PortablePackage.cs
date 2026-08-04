using System.IO.Compression;
using System.Text;
using Calcpad.Core;
using static Calcpad.Server.Services.WorksheetReferences;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Packs a worksheet and everything it references into a ZIP that runs wherever it is
    /// unpacked, while staying the text it was: the document keeps its <c>#include</c>s, its
    /// <c>#read</c>s and its images, and only their paths change — each one rewritten to point
    /// into a folder beside the document carrying the file it named.
    ///
    /// <code>
    /// calc.zip
    ///   calc.cpd
    ///   calc.cpd.refs/  image.png  data/loads.csv  lib.cpd
    /// </code>
    ///
    /// This is the middle ground between a worksheet that only runs on the machine it was
    /// written on and a compiled <c>.cpdz</c>, which travels anywhere but cannot be read
    /// (see <see cref="PortableWorksheet"/>). The recipient can open, read and edit it.
    ///
    /// A reference that sits under the document's own folder keeps that structure inside the
    /// refs folder — <c>./data/loads.csv</c> lands at <c>calc.cpd.refs/data/loads.csv</c> — since
    /// nothing else could collide with it there. One that reaches outside that folder, whether
    /// written as an absolute path or via a leading <c>..</c>, is flattened to its bare name
    /// instead, because there is no tree left to mirror; if two of those bare names collide, the
    /// second and any further one are renamed <c>name-1.ext</c>, <c>name-2.ext</c> and so on, and
    /// every path that pointed at the original is rewritten to match.
    ///
    /// A <c>&lt;project&gt;</c>/<c>&lt;library&gt;</c>/<c>&lt;user&gt;</c> reference is resolved
    /// against this exporting machine's own roots and bundled like any other: the token names a
    /// folder there is no reason to expect the recipient has, and a package that still depended on
    /// one would not be portable. The recipient who does have their own <c>#ProjectPath</c>/
    /// <c>#LibraryPath</c> is better served by the document itself than by a package.
    /// </summary>
    internal static class PortablePackage
    {
        internal sealed record Result(
            byte[]? Zip,
            string Name,
            string RefsFolder,
            IReadOnlyList<string> Bundled,
            IReadOnlyList<string> Errors);

        private const string RefsSuffix = ".refs";
        /// <summary>Matches the include depth the highlighter's content resolver allows.</summary>
        private const int MaxDepth = 20;

        // Whether two paths name the same file is the host's business, as it is for the circular
        // include check in MacroParser. Entry names, however, are compared case-insensitively
        // whatever the host: the archive may well be unpacked on a system where a folder cannot
        // hold both Loads.csv and loads.csv.
        private static readonly StringComparer PathComparer =
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        /// <summary>
        /// Builds the archive for <paramref name="content"/>, whose references resolve against the
        /// folder of <paramref name="sourceFilePath"/> — and, for a nested include, against the
        /// folder of the file holding the line, exactly as they do when the worksheet runs.
        /// <paramref name="content"/> is taken as given rather than read back from disk, so an
        /// unsaved edit is packed as it stands.
        /// </summary>
        /// <returns>
        /// The archive, or <see cref="Result.Errors"/> saying why there is none. A reference that
        /// cannot be read, and two outputs that would collapse onto the same file, are both
        /// refusals: a package that fails, or overwrites one output with another, for whoever
        /// receives it is the one thing this exists to prevent. Two references sharing a name is
        /// not — the later one is renamed instead.
        /// </returns>
        public static Result Build(string content, string? sourceFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
                return Unpackable("A portable package resolves references against the folder of the "
                    + "document holding them, so the document has to be saved first.");

            string rootPath;
            try
            {
                rootPath = Path.GetFullPath(sourceFilePath);
            }
            catch (Exception ex)
            {
                return Unpackable($"{sourceFilePath} is not a usable path: {ex.Message}");
            }

            var innerName = Path.GetFileName(rootPath);
            var rootDirectory = Path.GetDirectoryName(rootPath) ?? string.Empty;
            var refsFolder = innerName + RefsSuffix;
            var zipName = Path.GetFileNameWithoutExtension(innerName) + ".zip";
            var errors = new List<string>();

            // Stripped for the scan, since a byte order mark is not whitespace and would hide a
            // reference on the first line, and put back on the way out. Included files are read
            // as text, which drops theirs.
            var bom = content.StartsWith('﻿');
            var pathRoots = new PathRoots();
            var documents = Walk(rootPath, bom ? content[1..] : content, errors, pathRoots);
            var dataFiles = documents.DataFiles;
            if (errors.Count > 0)
                return Failed(errors);

            var members = FlatMembers(documents.Texts.Keys.Where(p => !PathComparer.Equals(p, rootPath)),
                dataFiles, rootDirectory);
            var zipPaths = new Dictionary<string, string>(PathComparer) { [rootPath] = innerName };
            foreach (var (path, name) in members)
                zipPaths[path] = $"{refsFolder}/{name}";

            var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            var rewritten = new Dictionary<string, string>(PathComparer);
            var outputs = new OutputTargets(rootDirectory, errors, pathRoots);
            // pathRoots is already fully declared — Walk, above, has already read every
            // #ProjectPath/#LibraryPath in the tree — so Prepare sees exactly what Rewrite,
            // below, will.
            outputs.Prepare(EnumerateOutputReferences(documents.Texts), pathRoots);
            foreach (var path in documents.Texts.Keys.OrderBy(p => p, StringComparer.Ordinal))
                rewritten[path] = RewriteDocument(documents.Texts[path], path, rootDirectory, zipPaths,
                    outputs, pathRoots, errors);
            if (errors.Count > 0)
                return Failed(errors);

            entries[innerName] = Utf8.GetBytes(bom ? '﻿' + rewritten[rootPath] : rewritten[rootPath]);
            foreach (var (path, name) in members)
            {
                var entryName = zipPaths[path];
                if (rewritten.TryGetValue(path, out var text))
                {
                    // Reached as an #include as well as a #read: the rewrite would hand the reader
                    // of the data a file that is no longer the one it read.
                    if (dataFiles.Contains(path) && text != documents.Texts[path])
                        errors.Add($"{name} is both included and read as data, and its paths have to "
                            + "be rewritten for the first — which would change the data the second "
                            + "reads. Keep the two apart.");

                    entries[entryName] = Utf8.GetBytes(text);
                    continue;
                }
                try
                {
                    entries[entryName] = File.ReadAllBytes(path);
                }
                catch (Exception ex)
                {
                    errors.Add($"{path} could not be read: {ex.Message}");
                }
            }
            if (errors.Count > 0)
                return Failed(errors);

            return new Result(Pack(entries), zipName, refsFolder,
                [.. entries.Keys.Where(name => name != innerName)], errors);

            Result Failed(IReadOnlyList<string> messages) => new(null, zipName, refsFolder, [], messages);
        }

        private static Result Unpackable(string message) => new(null, string.Empty, string.Empty, [], [message]);

        private static readonly UTF8Encoding Utf8 = new(false);

        private sealed record Walked(
            Dictionary<string, string> Texts,
            HashSet<string> DataFiles);

        /// <summary>
        /// Follows every reference from the root outwards, collecting the text of each document
        /// and the path of each data file and image, and records every <c>#ProjectPath</c>/
        /// <c>#LibraryPath</c> declaration into <paramref name="pathRoots"/> along the way — the
        /// same instance <see cref="RewriteDocument"/> later reads from, once every declaration
        /// in the tree has been seen. Reading an included file is what makes it a document, so a
        /// file only referenced by <c>#read</c> is never parsed for references of its own.
        /// </summary>
        /// <remarks>
        /// A declaration inside a <c>#local</c>...<c>#global</c> section of a file reached by
        /// <c>#include</c> is skipped, mirroring <c>CalcpadService.ProcessIncludedContent</c> —
        /// the include delegate the real pipeline reads through — which drops that section
        /// entirely before the includer ever sees it. Without this, a module that follows the
        /// documented pattern of scoping its own roots to <c>#local</c> would still collide with
        /// the including document's here, even though it never actually would at render time.
        /// The root's own <c>#local</c> is not gated: opening it directly never goes through the
        /// include delegate, so its declarations are live exactly as written.
        /// </remarks>
        private static Walked Walk(
            string rootPath,
            string rootText,
            List<string> errors,
            PathRoots pathRoots)
        {
            var texts = new Dictionary<string, string>(PathComparer) { [rootPath] = rootText };
            var dataFiles = new HashSet<string>(PathComparer);
            var rootDirectory = Path.GetDirectoryName(rootPath) ?? string.Empty;
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((rootPath, 0));

            while (queue.Count > 0)
            {
                var (path, depth) = queue.Dequeue();
                var directory = Path.GetDirectoryName(path) ?? string.Empty;
                var owner = Path.GetFileName(path);
                var isRootFile = PathComparer.Equals(path, rootPath);
                var isLocal = false;
                var lineNumber = 0;
                foreach (var (line, _) in Lines(texts[path]))
                {
                    ++lineNumber;
                    var trimmedLine = line.TrimStart();
                    if (PathRoots.IsDeclaration(trimmedLine.AsSpan(), out var isProject, out var declStart, out var declLength))
                    {
                        if (isRootFile || !isLocal)
                        {
                            if (declLength == 0)
                                errors.Add($"{owner}, line {lineNumber}: "
                                    + $"{(isProject ? "#ProjectPath" : "#LibraryPath")} requires a path.");
                            else if (!pathRoots.TryDeclare(isProject, trimmedLine.Substring(declStart, declLength),
                                directory, out var declError))
                                errors.Add($"{owner}, line {lineNumber}: {declError}");
                        }
                        continue;
                    }
                    if (Validator.IsKeyword(line, "#local"))
                    {
                        isLocal = true;
                        continue;
                    }
                    if (Validator.IsKeyword(line, "#global"))
                    {
                        isLocal = false;
                        continue;
                    }

                    foreach (var reference in Scan(line))
                    {
                        if (reference.IsOutput)
                            continue;
                        if (reference.Kind == ReferenceKind.Image && IsExternalSource(reference.Raw))
                            continue;

                        // A no-op on a path that carries no token at all, so it runs unconditionally.
                        var raw = reference.Raw;
                        if (!pathRoots.TryExpand(raw, out raw, out var tokenError))
                        {
                            errors.Add($"{owner}, line {lineNumber}: {reference.Directive} "
                                + $"{reference.Raw} — {tokenError}");
                            continue;
                        }

                        if (!TryResolve(raw, ResolveDirectory(reference, directory), out var resolved))
                        {
                            errors.Add(Unreadable(owner, lineNumber, reference, reference.Raw));
                            continue;
                        }
                        if (!File.Exists(resolved))
                        {
                            errors.Add(Unreadable(owner, lineNumber, reference, resolved));
                            continue;
                        }
                        if (reference.Kind != ReferenceKind.Include)
                        {
                            dataFiles.Add(resolved);
                            continue;
                        }
                        // Already walked: a second reference to the same file, or a cycle. Its
                        // lines are rewritten either way, so there is nothing left to do here.
                        if (texts.ContainsKey(resolved))
                            continue;
                        if (depth + 1 > MaxDepth)
                        {
                            errors.Add($"{owner}, line {lineNumber}: #include {reference.Raw} is "
                                + $"nested more than {MaxDepth} levels deep.");
                            continue;
                        }
                        try
                        {
                            texts[resolved] = File.ReadAllText(resolved);
                            queue.Enqueue((resolved, depth + 1));
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"{owner}, line {lineNumber}: #include {reference.Raw} — "
                                + $"{resolved} could not be read: {ex.Message}");
                        }
                    }
                }
            }
            return new Walked(texts, dataFiles);

            /// <summary>
            /// The folder a reference is taken as relative to. Only <c>#include</c> resolves
            /// against the file holding it: the macro parser expands includes into one worksheet
            /// before anything else runs, so by the time a <c>#read</c> is evaluated — or an
            /// image src is fetched — the folder its line was written in is gone, and the root
            /// document's is what is left. That is the same asymmetry PortableWorksheet works
            /// around by making an included file's image paths absolute.
            /// </summary>
            string ResolveDirectory(Reference reference, string owningDirectory) =>
                reference.Kind == ReferenceKind.Include ? owningDirectory : rootDirectory;
        }

        private static string Unreadable(string owner, int line, Reference reference, string resolved) =>
            $"{owner}, line {line}: {reference.Directive} {reference.Raw} — {resolved} could not be read.";

        /// <summary>
        /// The path each bundled file takes inside the refs folder, relative to it: the same path
        /// it has under <paramref name="rootDirectory"/> when it sits there, since nothing else can
        /// collide with a mirrored tree; otherwise its bare name, renamed <c>name-1.ext</c>,
        /// <c>name-2.ext</c> and so on when another bare name — mirrored or not — already claims
        /// it, in path order.
        /// </summary>
        private static SortedDictionary<string, string> FlatMembers(
            IEnumerable<string> includes, IEnumerable<string> dataFiles, string rootDirectory)
        {
            var members = new SortedDictionary<string, string>(PathComparer);
            var seen = new HashSet<string>(PathComparer);
            var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in includes.Concat(dataFiles).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!seen.Add(path))
                    continue;

                if (TryNestedPath(path, rootDirectory, out var nested))
                {
                    members[path] = nested;
                    continue;
                }

                var name = Path.GetFileName(path);
                if (!byName.TryGetValue(name, out var group))
                    byName[name] = group = new();
                group.Add(path);
            }

            // A bare name only goes to a single claimant once nothing already sitting in the
            // mirrored tree wants it too — otherwise it joins the multi-claimant files below,
            // if it hadn't already, and is renamed like them.
            var taken = new HashSet<string>(members.Values, StringComparer.OrdinalIgnoreCase);
            var renaming = new List<List<string>>();
            foreach (var (name, group) in byName)
            {
                if (group.Count == 1 && taken.Add(name))
                    members[group[0]] = name;
                else
                    renaming.Add(group);
            }

            foreach (var group in renaming)
            {
                var name = Path.GetFileName(group[0]);
                var stem = Path.GetFileNameWithoutExtension(name);
                var extension = Path.GetExtension(name);
                var index = 0;
                foreach (var path in group)
                {
                    string candidate;
                    do
                        candidate = $"{stem}-{++index}{extension}";
                    while (!taken.Add(candidate));
                    members[path] = candidate;
                }
            }
            return members;
        }

        /// <summary>
        /// Whether <paramref name="path"/> sits under <paramref name="rootDirectory"/> — as
        /// opposed to reaching it only through a leading <c>..</c>, or an absolute path elsewhere
        /// entirely — and if so, its path relative to it, with forward slashes for the zip entry.
        /// </summary>
        private static bool TryNestedPath(string path, string rootDirectory, out string nested)
        {
            var relative = Path.GetRelativePath(rootDirectory, path);
            if (Path.IsPathRooted(relative) || relative == ".."
                || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                nested = string.Empty;
                return false;
            }
            nested = relative.Replace(Path.DirectorySeparatorChar, '/');
            return true;
        }

        /// <summary>
        /// Every <c>#write</c>/<c>#append</c> reference across the whole tree, with the owner
        /// name and line number <see cref="RewriteDocument"/> will later look each one up by —
        /// same file-name-then-line-order walk, so <see cref="OutputTargets.Prepare"/> sees
        /// occurrences in exactly the order <see cref="OutputTargets.Rewrite"/> will reach them.
        /// </summary>
        private static IEnumerable<(WorksheetReferences.Reference Reference, string Owner, int Line)>
            EnumerateOutputReferences(Dictionary<string, string> texts)
        {
            foreach (var path in texts.Keys.OrderBy(p => p, StringComparer.Ordinal))
            {
                var owner = Path.GetFileName(path);
                var lineNumber = 0;
                foreach (var (line, _) in Lines(texts[path]))
                {
                    ++lineNumber;
                    foreach (var reference in Scan(line))
                        if (reference.IsOutput)
                            yield return (reference, owner, lineNumber);
                }
            }
        }

        /// <summary>
        /// Points every reference in <paramref name="text"/> at its bundled copy. An
        /// <c>#include</c> is reached from wherever <paramref name="path"/> itself landed in the
        /// zip. Everything else is reached from the root document's own location regardless of
        /// where <paramref name="path"/> landed, because that is where it is resolved from once
        /// the includes have been expanded — so a bundled include several folders deep can still
        /// name a reference several folders back up, which reads oddly but is what resolves.
        /// </summary>
        private static string RewriteDocument(
            string text,
            string path,
            string rootDirectory,
            IReadOnlyDictionary<string, string> zipPaths,
            OutputTargets outputs,
            PathRoots pathRoots,
            List<string> errors)
        {
            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            var owner = Path.GetFileName(path);
            var sb = new StringBuilder(text.Length);
            var lineNumber = 0;
            foreach (var (line, terminator) in Lines(text))
            {
                ++lineNumber;
                var references = Scan(line);
                sb.Append(references.Count == 0 ? line : Rewrite(line, references, Target)).Append(terminator);
            }
            return sb.ToString();

            string? Target(Reference reference)
            {
                if (reference.IsOutput)
                    return outputs.Rewrite(reference, owner, lineNumber);
                if (reference.Kind == ReferenceKind.Image && IsExternalSource(reference.Raw))
                    return null;

                // Walk already validated every token reference in the tree, so a failure here
                // would mean Build reached this rewrite pass despite Walk reporting an error —
                // which Build's own error check does not allow. Reported defensively rather than
                // silently leaving the token in place.
                var raw = reference.Raw;
                if (!pathRoots.TryExpand(raw, out raw, out var tokenError))
                {
                    errors.Add($"{owner}, line {lineNumber}: {reference.Directive} "
                        + $"{reference.Raw} — {tokenError}");
                    return null;
                }

                var isInclude = reference.Kind == ReferenceKind.Include;
                if (!TryResolve(raw, isInclude ? directory : rootDirectory, out var resolved))
                    return null;

                var fromDirectory = isInclude ? ZipDirectory(zipPaths[path]) : string.Empty;
                return ZipRelative(fromDirectory, zipPaths[resolved]);
            }
        }

        private static string ZipDirectory(string zipPath)
        {
            var slash = zipPath.LastIndexOf('/');
            return slash < 0 ? string.Empty : zipPath[..slash];
        }

        /// <summary>
        /// The path from <paramref name="fromDirectory"/> to <paramref name="toPath"/> within the
        /// zip, forward-slashed regardless of host: these are archive entry names, not filesystem
        /// paths, so they never go through <see cref="Path"/>.
        /// </summary>
        private static string ZipRelative(string fromDirectory, string toPath)
        {
            var from = fromDirectory.Length == 0 ? [] : fromDirectory.Split('/');
            var to = toPath.Split('/');
            var common = 0;
            while (common < from.Length && common < to.Length - 1 && from[common] == to[common])
                ++common;

            return string.Join('/', Enumerable.Repeat("..", from.Length - common).Concat(to.Skip(common)));
        }

        /// <summary>
        /// Resolves a written reference the way the directive that carries it does: environment
        /// variables expanded, then taken relative to the folder of the file it was written in.
        /// </summary>
        private static bool TryResolve(string raw, string directory, out string resolved)
        {
            resolved = string.Empty;
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(raw);
                resolved = directory.Length > 0
                    ? Path.GetFullPath(expanded, directory)
                    : Path.GetFullPath(expanded);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The lines of <paramref name="text"/> with their terminators kept, so a document that
        /// arrives with CRLF endings — or without a final newline — leaves with them intact.
        /// </summary>
        private static IEnumerable<(string Line, string Terminator)> Lines(string text)
        {
            var start = 0;
            for (var i = 0; i < text.Length; ++i)
            {
                var c = text[i];
                if (c is not ('\n' or '\r'))
                    continue;

                var terminator = c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : text[i].ToString();
                yield return (text[start..i], terminator);
                i += terminator.Length - 1;
                start = i + 1;
            }
            if (start < text.Length)
                yield return (text[start..], string.Empty);
        }

        private static byte[] Pack(SortedDictionary<string, byte[]> entries)
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                foreach (var (name, bytes) in entries)
                {
                    using var target = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
                    target.Write(bytes);
                }

            return stream.ToArray();
        }
    }
}
