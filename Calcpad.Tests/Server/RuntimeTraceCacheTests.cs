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
        using var traces = new RuntimeTraceCache();
        var cache = new ContentResolutionCache(memoryCache, traces);

        var before = cache.GetOrResolve(Document, null);
        Assert.Null(before.Runtime);

        traces.Store(null, Document, new CalcpadService().Convert(Document, debug: true).Trace!);
        var after = cache.GetOrResolve(Document, null, useTrace: true);
        Assert.NotNull(after.Runtime);
        Assert.Equal(CalcpadType.Vector, after.Stage3.TypeTracker.Variables["y"].Type);
        Assert.Same(after, cache.GetOrResolve(Document, null, useTrace: true));

        // The base entry stays static, so it can take a newer trace later.
        Assert.Null(before.Runtime);
        Assert.Null(cache.GetOrResolve(Document + "z = 3\n", null, useTrace: true).Runtime);
    }

    [Fact]
    public void StoredTrace_IsIgnoredUnlessRequested()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        using var traces = new RuntimeTraceCache();
        var cache = new ContentResolutionCache(memoryCache, traces);

        traces.Store(null, Document, new CalcpadService().Convert(Document, debug: true).Trace!);
        Assert.Null(cache.GetOrResolve(Document, null).Runtime);
        Assert.NotNull(cache.GetOrResolve(Document, null, useTrace: true).Runtime);
    }

    [Fact]
    public void EarlierVersions_KeepTheirTraces()
    {
        using var traces = new RuntimeTraceCache();
        var first = Version(1);
        var second = Version(2);
        traces.Store("main.cpd", first, Run(first));
        traces.Store("main.cpd", second, Run(second));

        Assert.NotNull(traces.Get(ContentKey.For("main.cpd", first)));
        Assert.NotNull(traces.Get(ContentKey.For("main.cpd", second)));
        Assert.Null(traces.Get(ContentKey.For("other.cpd", first)));
    }

    [Fact]
    public void FullBudget_EvictsTheLeastRecentlyUsed_AndKeepsTheNewest()
    {
        var size = Run(Version(1)).Lines.Count;
        using var traces = new RuntimeTraceCache(2 * size);
        string a = Version(1), b = Version(2), c = Version(3);
        traces.Store(null, a, Run(a));
        Thread.Sleep(20);
        traces.Store(null, b, Run(b));
        Thread.Sleep(20);
        Assert.NotNull(traces.Get(ContentKey.For(null, a)));
        Thread.Sleep(20);
        traces.Store(null, c, Run(c));

        Assert.NotNull(traces.Get(ContentKey.For(null, a)));
        Assert.Null(traces.Get(ContentKey.For(null, b)));
        Assert.NotNull(traces.Get(ContentKey.For(null, c)));
    }

    private static string Version(int n) => Document.Replace("c = 1", $"c = {n}");

    private static Calcpad.Core.RuntimeTrace Run(string content) =>
        new CalcpadService().Convert(content, debug: true).Trace!;
}
