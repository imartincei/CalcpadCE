using System.IO.Compression;
using System.Text;
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
    ///   calc.cpd.refs/  image.png  include.cpd  loads.csv
    /// </code>
    ///
    /// This is the middle ground between a worksheet that only runs on the machine it was
    /// written on and a compiled <c>.cpdz</c>, which travels anywhere but cannot be read
    /// (see <see cref="PortableWorksheet"/>). The recipient can open, read and edit it.
    ///
    /// The folder is flat, so no two referenced files may share a name — including through
    /// nested includes, whose own references land in the same folder. That is refused rather
    /// than worked around: any scheme for renaming the duplicates would leave the recipient
    /// with a worksheet whose paths no longer match the ones its author wrote.
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
        /// <param name="nextToWorksheet">
        /// Collapses an absolute <c>#write</c>/<c>#append</c> target to its bare filename, so the
        /// output lands beside wherever the package is unpacked instead of a folder that may not
        /// exist there. A relative target already does that and is untouched either way.
        /// </param>
        /// <returns>
        /// The archive, or <see cref="Result.Errors"/> saying why there is none. A reference that
        /// cannot be read, two that share a name, and two outputs that would collapse onto the
        /// same file are all refusals: a package that fails, or overwrites one output with
        /// another, for whoever receives it is the one thing this exists to prevent.
        /// </returns>
        public static Result Build(string content, string? sourceFilePath, bool nextToWorksheet = false)
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
            var documents = Walk(rootPath, bom ? content[1..] : content, errors);
            var dataFiles = documents.DataFiles;
            if (errors.Count > 0)
                return Failed(errors);

            var members = FlatMembers(documents.Texts.Keys.Where(p => !PathComparer.Equals(p, rootPath)),
                dataFiles, refsFolder, errors);
            if (errors.Count > 0)
                return Failed(errors);

            var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            var rewritten = new Dictionary<string, string>(PathComparer);
            var outputs = new OutputTargets(nextToWorksheet, rootDirectory, errors);
            foreach (var path in documents.Texts.Keys.OrderBy(p => p, StringComparer.Ordinal))
                rewritten[path] = RewriteDocument(documents.Texts[path], path, rootPath, innerName,
                    refsFolder, outputs);
            if (errors.Count > 0)
                return Failed(errors);

            entries[innerName] = Utf8.GetBytes(bom ? '﻿' + rewritten[rootPath] : rewritten[rootPath]);
            foreach (var (path, name) in members)
            {
                var entryName = $"{refsFolder}/{name}";
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
        /// and the path of each data file and image. Reading an included file is what makes it a
        /// document, so a file only referenced by <c>#read</c> is never parsed for references of
        /// its own.
        /// </summary>
        private static Walked Walk(string rootPath, string rootText, List<string> errors)
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
                var lineNumber = 0;
                foreach (var (line, _) in Lines(texts[path]))
                {
                    ++lineNumber;
                    foreach (var reference in Scan(line))
                    {
                        if (reference.IsOutput)
                            continue;
                        if (reference.Kind == ReferenceKind.Image && IsExternalSource(reference.Raw))
                            continue;

                        if (!TryResolve(reference.Raw, ResolveDirectory(reference, directory), out var resolved))
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
        /// The name each bundled file takes in the refs folder: its own, since the folder is flat.
        /// Two files wanting the same one is the format's single limitation, and is reported with
        /// both paths — the author is the only one who can decide which to rename.
        /// </summary>
        private static SortedDictionary<string, string> FlatMembers(
            IEnumerable<string> includes,
            IEnumerable<string> dataFiles,
            string refsFolder,
            List<string> errors)
        {
            var members = new SortedDictionary<string, string>(PathComparer);
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in includes.Concat(dataFiles).OrderBy(p => p, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(path);
                if (!members.TryAdd(path, name))
                    continue;

                if (byName.TryGetValue(name, out var taken))
                    errors.Add($"Two of the referenced files are both named \"{name}\": {taken} and "
                        + $"{path}. They are bundled together into {refsFolder}, so one of them has "
                        + "to be renamed or the reference dropped.");
                else
                    byName[name] = path;
            }
            return members;
        }

        /// <summary>
        /// Points every reference in <paramref name="text"/> at its bundled copy. An
        /// <c>#include</c> is reached from the file holding it — through the refs folder from the
        /// root, as a sibling from inside it, and back out of the folder for the root itself.
        /// Everything else is reached from the root document wherever its line sits, because that
        /// is where it is resolved from once the includes have been expanded: a bundled include
        /// therefore names the refs folder too, which reads oddly but is what resolves.
        /// </summary>
        private static string RewriteDocument(
            string text,
            string path,
            string rootPath,
            string innerName,
            string refsFolder,
            OutputTargets outputs)
        {
            var isRoot = PathComparer.Equals(path, rootPath);
            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            var rootDirectory = Path.GetDirectoryName(rootPath) ?? string.Empty;
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

                var isInclude = reference.Kind == ReferenceKind.Include;
                if (!TryResolve(reference.Raw, isInclude ? directory : rootDirectory, out var resolved))
                    return null;

                if (PathComparer.Equals(resolved, rootPath))
                    return isRoot || !isInclude ? innerName : $"../{innerName}";

                var name = Path.GetFileName(resolved);
                return isRoot || !isInclude ? $"{refsFolder}/{name}" : name;
            }
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
