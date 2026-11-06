using System.Collections.Concurrent;
using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter;
using Calcpad.Highlighter.Tokenizer;
using Xunit;

namespace HighlighterExamplesTest
{
    /// <summary>
    /// Holds the local lint baseline for the repo's Examples, Tests and Calcpad.Tests folders.
    /// The first run, or any run with CALCPAD_UPDATE_BASELINE=1, records instead of compares, and
    /// the baseline is rewritten for the files that ran once the class finishes.
    /// </summary>
    public sealed class HighlighterExamplesFixture : IDisposable
    {
        public static readonly string[] Folders = ["Examples", "Tests", "Calcpad.Tests"];

        public static string RepoRoot { get; } = FindRepoRoot();

        public string BaselinePath { get; } =
            Path.Combine(RepoRoot, "tools", "highlighter-examples-test", "highlighter-examples-test.txt");

        public bool Updating { get; }

        public IReadOnlyDictionary<string, List<string>> Baseline { get; }

        private readonly ConcurrentDictionary<string, List<string>> _recorded = new(StringComparer.Ordinal);

        public HighlighterExamplesFixture()
        {
            var exists = File.Exists(BaselinePath);
            Updating = !exists || Environment.GetEnvironmentVariable("CALCPAD_UPDATE_BASELINE") == "1";
            Baseline = exists
                ? Parse(File.ReadAllLines(BaselinePath))
                : new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }

        public static IEnumerable<string> Files() =>
            Folders
                .SelectMany(folder => Directory.GetFiles(Path.Combine(RepoRoot, folder), "*.cpd", SearchOption.AllDirectories))
                .Select(path => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/'))
                .Where(path => !path.Split('/').Any(part => part is "bin" or "obj"))
                .OrderBy(path => path, StringComparer.Ordinal);

        public void Record(string file, List<string> entries) => _recorded[file] = entries;

        public void Dispose()
        {
            if (!Updating || _recorded.IsEmpty)
                return;

            var existing = Files().ToHashSet(StringComparer.Ordinal);
            var merged = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (file, entries) in Baseline)
            {
                if (existing.Contains(file))
                    merged[file] = entries;
            }
            foreach (var (file, entries) in _recorded)
                merged[file] = entries;

            var lines = new List<string>
            {
                "# Lint diagnostics for every .cpd file under " + string.Join(", ", Folders) + ".",
                "# Regenerate: CALCPAD_UPDATE_BASELINE=1 dotnet test tools/highlighter-examples-test",
            };
            foreach (var (file, entries) in merged)
            {
                if (entries.Count == 0)
                    continue;

                lines.Add(file);
                lines.AddRange(entries.Select(e => "  " + e));
            }
            File.WriteAllLines(BaselinePath, lines);
        }

        private static Dictionary<string, List<string>> Parse(string[] lines)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            List<string> current = null;
            foreach (var line in lines)
            {
                if (line.Length == 0 || line[0] == '#')
                    continue;

                if (line.StartsWith("  ", StringComparison.Ordinal))
                    current?.Add(line[2..]);
                else
                    result[line] = current = [];
            }
            return result;
        }

        private static string FindRepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (Folders.All(f => Directory.Exists(Path.Combine(dir.FullName, f))) &&
                    Directory.Exists(Path.Combine(dir.FullName, "Calcpad.Highlighter")))
                    return dir.FullName;
            }
            throw new DirectoryNotFoundException("Repo root with " + string.Join(", ", Folders) + " not found above " + AppContext.BaseDirectory);
        }
    }

    /// <summary>
    /// Lints and highlights every .cpd file in Examples, Tests and Calcpad.Tests, and fails on any
    /// diagnostic that differs from highlighter-examples-test.txt.
    /// </summary>
    public class HighlighterExamplesTests : IClassFixture<HighlighterExamplesFixture>
    {
        private readonly HighlighterExamplesFixture _fixture;

        public HighlighterExamplesTests(HighlighterExamplesFixture fixture)
        {
            _fixture = fixture;
        }

        public static IEnumerable<object[]> Files() =>
            HighlighterExamplesFixture.Files().Select(file => new object[] { file });

        [Theory]
        [MemberData(nameof(Files))]
        public void Diagnostics_MatchBaseline(string file)
        {
            var path = Path.Combine(HighlighterExamplesFixture.RepoRoot, file);
            var content = File.ReadAllText(path);
            var staged = new ContentResolver().GetStagedContent(content, sourceFilePath: path);
            var lint = new CalcpadLinter().Lint(staged, new LintIgnoreRegionParser().ExtractRegions(content));

            var tokenizer = new CalcpadTokenizer();
            tokenizer.SetResolvedContent(staged);
            tokenizer.Tokenize(content);

            var actual = lint.Diagnostics
                .OrderBy(d => d.Line).ThenBy(d => d.Column)
                .ThenBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.Message, StringComparer.Ordinal)
                .Select(d => $"{d.Line + 1}:{d.Column + 1} {d.Severity} {d.Code} {d.Message.ReplaceLineEndings(" ")}")
                .ToList();

            if (_fixture.Updating)
            {
                _fixture.Record(file, actual);
                return;
            }

            var expected = _fixture.Baseline.GetValueOrDefault(file) ?? [];
            var added = Except(actual, expected);
            var removed = Except(expected, actual);
            Assert.True(added.Count == 0 && removed.Count == 0,
                $"{file} differs from {Path.GetFileName(_fixture.BaselinePath)}." +
                string.Concat(added.Select(e => "\n  + " + e)) +
                string.Concat(removed.Select(e => "\n  - " + e)) +
                "\nIf the change is intended, rerun with CALCPAD_UPDATE_BASELINE=1.");
        }

        private static List<string> Except(List<string> items, List<string> remove)
        {
            var remaining = new List<string>(items);
            foreach (var item in remove)
                remaining.Remove(item);
            return remaining;
        }
    }
}
