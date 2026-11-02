using Calcpad.Core;

namespace Calcpad.Tests;

[CollectionDefinition(nameof(RemoteProviderCollection), DisableParallelization = true)]
public class RemoteProviderCollection;

/// <summary>Extension-less GUID references and <c>{s3:bucket}</c> roots.</summary>
[Collection(nameof(RemoteProviderCollection))]
public sealed class FileReferenceTests : IDisposable
{
    private const string BaseId = "aaaaaaaa-0000-0000-0000-000000000000";
    private const string V1 = "11111111-0000-0000-0000-000000000000";
    private const string V2 = "22222222-0000-0000-0000-000000000000";

    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public FileReferenceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        FileReferences.RemoteProvider = null;
        Directory.Delete(_root, recursive: true);
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Worksheet(string body, string id, int version) =>
        FileMetadata.WithFileInfo(body, new Dictionary<string, string>
        {
            [FileMetadata.BaseIdKey] = BaseId,
            [FileMetadata.IdKey] = id,
            [FileMetadata.VersionKey] = version.ToString()
        });

    private string Flatten(string source)
    {
        var macroParser = new MacroParser { Include = (f, _) => File.ReadAllText(f), SourceFilePath = Path.Combine(_root, "main.cpd") };
        macroParser.Parse(source, out var expanded, null, 0, false);
        return expanded;
    }

    [Fact]
    public void Include_ById_PicksThatExactVersion()
    {
        Write("lib/beam-v1.cpd", Worksheet("v = 1\n", V1, 1));
        Write("lib/beam-v2.cpd", Worksheet("v = 2\n", V2, 2));

        var expanded = Flatten($"#include ./lib/{V1}\n");

        Assert.Contains("v = 1", expanded);
        Assert.DoesNotContain("v = 2", expanded);
    }

    [Fact]
    public void Include_ByBaseId_PicksTheLatestVersion()
    {
        Write("lib/beam-v1.cpd", Worksheet("v = 1\n", V1, 1));
        Write("lib/beam-v2.cpd", Worksheet("v = 2\n", V2, 2));

        var expanded = Flatten($"#include ./lib/{BaseId}\n");

        Assert.Contains("v = 2", expanded);
        Assert.DoesNotContain("v = 1", expanded);
    }

    [Fact]
    public void Highlighter_ResolvesIncludeById()
    {
        Write("lib/beam.cpd", Worksheet("v = 7\n", V1, 1));

        var staged = new Calcpad.Highlighter.ContentResolution.ContentResolver()
            .GetStagedContent($"#include ./lib/{BaseId}\n", sourceFilePath: Path.Combine(_root, "main.cpd"));

        Assert.Contains("v = 7", string.Join('\n', staged.Stage2.Lines));
    }

    [Fact]
    public void Include_ByUnknownId_IsNotFound()
    {
        Write("lib/beam.cpd", Worksheet("v = 1\n", V1, 1));
        Assert.Contains("not found", Flatten($"#include ./lib/{Guid.NewGuid()}\n"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsIdReference_RequiresAGuidWithoutExtension()
    {
        Assert.True(FileReferences.IsIdReference($"/a/{V1}", out _));
        Assert.False(FileReferences.IsIdReference($"/a/{V1}.cpd", out _));
        Assert.False(FileReferences.IsIdReference("/a/beam", out _));
    }

    [Fact]
    public void S3Token_WithoutAProvider_ReportsNotConnected()
    {
        Assert.False(new PathRoots().TryExpand("{s3:lib}/beam.cpd", out _, out var error));
        Assert.Contains("S3", error);
    }

    [Fact]
    public void S3Token_WithAnInvalidBucket_Fails()
    {
        FileReferences.RemoteProvider = new FakeProvider(Path.Combine(_root, "cache"));
        Assert.False(new PathRoots().TryExpand("{s3:Bad_Name}/beam.cpd", out _, out var error));
        Assert.Contains("S3", error);
    }

    [Fact]
    public void LibraryPath_OnS3_FetchesIncludesThroughTheProvider()
    {
        var provider = new FakeProvider(Path.Combine(_root, "cache"));
        provider.Objects["lib/steel/beam.cpd"] = "#include ./section.cpd\nb = 1\n";
        provider.Objects["lib/steel/section.cpd"] = "s = 2\n";
        FileReferences.RemoteProvider = provider;

        var expanded = Flatten("#LibraryPath {s3:lib}\n#include {library}/steel/beam.cpd\n");

        Assert.Contains("b = 1", expanded);
        Assert.Contains("s = 2", expanded);
    }

    [Fact]
    public void Include_ByIdOnS3_UsesTheProviderLookup()
    {
        var provider = new FakeProvider(Path.Combine(_root, "cache"));
        provider.Objects["lib/beam.cpd"] = "b = 1\n";
        provider.Ids[(Path.Combine(provider.CacheRoot, "lib"), Guid.Parse(V1))] = "lib/beam.cpd";
        FileReferences.RemoteProvider = provider;

        Assert.Contains("b = 1", Flatten($"#include {{s3:lib}}/{V1}\n"));
    }

    [Fact]
    public void Read_ByIdOnS3_TakesTheExtensionFromTheResolvedFile()
    {
        var provider = new FakeProvider(Path.Combine(_root, "cache"));
        provider.Objects["data/loads.csv"] = "1,2\n";
        provider.Ids[(Path.Combine(provider.CacheRoot, "data"), Guid.Parse(V1))] = "data/loads.csv";
        FileReferences.RemoteProvider = provider;

        var directive = ExpressionParser.EmbedReadDirective(
            $"#read M from {{s3:data}}/{V1}", Path.Combine(_root, "main.cpd"), new PathRoots(), out _);

        Assert.Equal("#read M from data:text/csv;base64,MSwyCg==", directive);
    }

    private sealed class FakeProvider(string cacheRoot) : IRemoteFileProvider
    {
        public Dictionary<string, string> Objects { get; } = [];
        public Dictionary<(string Folder, Guid Id), string> Ids { get; } = [];
        public string CacheRoot => cacheRoot;

        public bool TryEnsureLocal(string localPath)
        {
            var key = Path.GetRelativePath(cacheRoot, localPath).Replace('\\', '/');
            if (!Objects.TryGetValue(key, out var content))
                return false;
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            File.WriteAllText(localPath, content);
            return true;
        }

        public string ResolveId(string folderPath, Guid id)
        {
            if (!Ids.TryGetValue((folderPath, id), out var key))
                return null;
            var path = Path.Combine(cacheRoot, key);
            return TryEnsureLocal(path) ? path : null;
        }
    }
}
