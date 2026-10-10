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

        traces.Store(null, Document, new CalcpadService().Convert(Document, debug: true).Trace!);
        var after = cache.GetOrResolve(Document, null);
        Assert.NotNull(after.Runtime);
        Assert.Equal(CalcpadType.Vector, after.Stage3.TypeTracker.Variables["y"].Type);
        Assert.Same(after, cache.GetOrResolve(Document, null));

        // The base entry stays static, so it can take a newer trace later.
        Assert.Null(before.Runtime);
        Assert.Null(cache.GetOrResolve(Document + "z = 3\n", null).Runtime);
    }
}
