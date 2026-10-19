using Calcpad.Highlighter.ContentResolution;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Server.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Calcpad.Tests;

/// <summary>
/// A debug /convert leaves its runtime trace behind for lint and definitions of the same content.
/// </summary>
public class RuntimeTraceCacheTests
{
    private const string Document = "c = 1\n#if c > 5\ny = 10\n#else\ny = [1; 2]\n#end if\n";

    [Fact]
    public void Convert_ReturnsATraceOnlyInDebugMode()
    {
        Assert.NotNull(new CalcpadService().Convert(Document, debug: true).Trace);
        Assert.Null(new CalcpadService().Convert(Document, debug: false).Trace);
    }

    [Fact]
    public void StoredTrace_IsAppliedToTheSameContentOnly()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var traces = new RuntimeTraceCache(memoryCache);
        var cache = new ContentResolutionCache(memoryCache, traces);

        var before = cache.GetOrResolve(Document, null);
        Assert.Null(before.Runtime);

        traces.Store(null, null, Document, new CalcpadService().Convert(Document, debug: true).Trace!);
        var after = cache.GetOrResolve(Document, null);
        Assert.NotNull(after.Runtime);
        Assert.Equal(CalcpadType.Vector, after.Stage3.TypeTracker.Variables["y"].Type);
        Assert.Same(after, cache.GetOrResolve(Document, null));

        // The base entry stays static, so it can take a newer trace later.
        Assert.Null(before.Runtime);
        Assert.Null(cache.GetOrResolve(Document + "z = 3\n", null).Runtime);
    }

    [Fact]
    public void StoredNames_ArePerDocumentWhateverTheContent()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var traces = new RuntimeTraceCache(memoryCache);
        var cache = new ContentResolutionCache(memoryCache, traces);
        var names = RuntimeVariableNames.From(
            new CalcpadService().Convert(Document, debug: true).Trace!, cache.GetOrResolve(Document, "/a.cpd"));

        traces.StoreNames("/a.cpd", null, names);
        Assert.Same(names, traces.GetNames("/a.cpd", null));
        Assert.Null(traces.GetNames("/b.cpd", null));
        Assert.Null(traces.Get("/a.cpd", null, "unrelated"));
    }

    [Fact]
    public void StoredTraces_KeepUntitledDocumentsApartById()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var traces = new RuntimeTraceCache(memoryCache);
        var cache = new ContentResolutionCache(memoryCache, traces);
        const string other = "q = 1\n";

        traces.Store(null, "untitled-1", Document, new CalcpadService().Convert(Document, debug: true).Trace!);
        traces.Store(null, "untitled-2", other, new CalcpadService().Convert(other, debug: true).Trace!);
        Assert.NotNull(cache.GetOrResolve(Document, null, "untitled-1").Runtime);
        Assert.NotNull(cache.GetOrResolve(other, null, "untitled-2").Runtime);
        Assert.Null(cache.GetOrResolve(Document, null, "untitled-2").Runtime);
        Assert.Null(cache.GetOrResolve(Document, null).Runtime);
    }

    [Fact]
    public void StoredNames_KeepUntitledDocumentsApartById()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var traces = new RuntimeTraceCache(memoryCache);
        var names = new RuntimeVariableNames(["a"], [], true);

        traces.StoreNames(null, "untitled-1", names);
        traces.StoreNames(null, null, new RuntimeVariableNames(["b"], [], true));
        Assert.Same(names, traces.GetNames(null, "untitled-1"));
        Assert.Null(traces.GetNames(null, "untitled-2"));
        Assert.Null(traces.GetNames(null, null));
    }
}
