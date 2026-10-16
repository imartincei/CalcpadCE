using System.IO.Compression;
using System.Text;
using Calcpad.Core;
using static Calcpad.Server.Services.WorksheetReferences;

namespace Calcpad.Server.Services
{
    /// <summary>
    /// Packs a worksheet and everything it references into a ZIP that runs wherever it is
    /// unpacked while staying the text it was: only the reference paths change, each rewritten
    /// to point into a refs folder beside the document. References are flattened into that
    /// folder by bare file name, collisions renamed <c>name-1.ext</c> onwards in path order,
    /// and <c>&lt;project&gt;</c>/<c>&lt;library&gt;</c>/<c>&lt;user&gt;</c> tokens resolved
    /// against the exporting machine and bundled like any other.
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
        /// The archive, or <see cref="Result.Errors"/> saying why there is none. A reference
        /// that cannot be read, and two outputs that would collapse onto the same file, are
        /// both refusals; two references sharing a name is not — the later one is renamed.
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
                dataFiles);
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
        /// <c>#include</c> is skipped, mirroring <c>CalcpadService.ProcessIncludedContent</c>,
        /// which drops that section before the includer sees it. The root's own <c>#local</c>
        /// is not gated: opening it directly never goes through the include delegate.
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
            /// first, so by the time a <c>#read</c> or an image src is evaluated the root
            /// document's folder is what is left — the same asymmetry PortableWorksheet works
            /// around by making an included file's image paths absolute.
            /// </summary>
            string ResolveDirectory(Reference reference, string owningDirectory) =>
                reference.Kind == ReferenceKind.Include ? owningDirectory : rootDirectory;
        }

        private static string Unreadable(string owner, int line, Reference reference, string resolved) =>
            $"{owner}, line {line}: {reference.Directive} {reference.Raw} — {resolved} could not be read.";

        /// <summary>
        /// The bare file name each bundled file takes inside the refs folder — nothing preserves
        /// a subfolder, so two references that sat in different folders outside are always flat
        /// siblings inside. When two distinct files would take the same bare name, the second and
        /// any further one are renamed <c>name-1.ext</c>, <c>name-2.ext</c> and so on, in path
        /// order.
        /// </summary>
        private static SortedDictionary<string, string> FlatMembers(
            IEnumerable<string> includes, IEnumerable<string> dataFiles)
        {
            var members = new SortedDictionary<string, string>(PathComparer);
            var seen = new HashSet<string>(PathComparer);
            var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in includes.Concat(dataFiles).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!seen.Add(path))
                    continue;

                var name = Path.GetFileName(path);
                if (!byName.TryGetValue(name, out var group))
                    byName[name] = group = new();
                group.Add(path);
            }

            // Shared across every basename so a renamed candidate can never land on a name some
            // other, unrelated file already claims outright.
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        /// <c>#include</c> is reached from wherever <paramref name="path"/> itself landed in
        /// the zip, while everything else is reached from the root document's location, since
        /// that is where it resolves once the includes have been expanded.
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
