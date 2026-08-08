using System;
using System.IO;
using Calcpad.Server.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Calcpad.Tests;

/// <summary>
/// <c>ContentResolutionCache</c> lets lint/highlight/definitions/symbol-at-position share one
/// <c>ContentResolver.GetStagedContent</c> result per document instead of each redoing include
/// resolution and macro expansion when the frontend fires them concurrently for the same content.
/// </summary>
public class ContentResolutionCacheTests
{
    [Fact]
    public void GetOrResolve_ReturnsTheSameCachedInstanceForUnchangedContent()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new ContentResolutionCache(memoryCache);

        var first = cache.GetOrResolve("a = 1\n", null);
        var second = cache.GetOrResolve("a = 1\n", null);

        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrResolve_EditedContentEvictsTheOldEntryForThatSourceFile()
    {
        using var temp = new TempDir();
        var mainPath = temp.At("main.cpd");
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new ContentResolutionCache(memoryCache);

        var original = cache.GetOrResolve("a = 1\n", mainPath);
        cache.GetOrResolve("a = 2\n", mainPath); // new content for the same file

        // The old entry was evicted rather than left to expire, so resolving the original
        // content again must redo the work instead of handing back the stale cached object.
        var reResolvedOriginal = cache.GetOrResolve("a = 1\n", mainPath);

        Assert.NotSame(original, reResolvedOriginal);
    }

    [Fact]
    public void GetOrResolve_EditingAnIncludedFileInvalidatesTheCacheWithoutChangingMainContent()
    {
        using var temp = new TempDir();
        temp.Write("inc.cpd", "b = 1\n");
        var mainPath = temp.At("main.cpd");
        var mainContent = "#include inc.cpd\na = b + 1\n";

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new ContentResolutionCache(memoryCache);

        var first = cache.GetOrResolve(mainContent, mainPath);
        var cachedAgain = cache.GetOrResolve(mainContent, mainPath);
        Assert.Same(first, cachedAgain); // no change yet - still a cache hit

        temp.Write("inc.cpd", "b = 2\n");
        var afterIncludeEdit = cache.GetOrResolve(mainContent, mainPath);

        Assert.NotSame(first, afterIncludeEdit);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public TempDir() => Directory.CreateDirectory(Path);

        public string At(string relative) => System.IO.Path.Combine(Path, relative);

        public string Write(string relative, string content)
        {
            var p = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
            File.WriteAllText(p, content);
            return p;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
